using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using UnityEngine;

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
using BDArmory.Bullets;

namespace BDArmory.Control
{
    public class MissileFire : PartModule
    {
        #region Declarations

        //weapons
        private List<IBDWeapon> weaponTypes = new List<IBDWeapon>();
        private Dictionary<string, List<float>> weaponRanges = new Dictionary<string, List<float>>();
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
            if (!gunRippleIndex.ContainsKey(weaponname))
            {
                UpdateList();
                if (!gunRippleIndex.ContainsKey(weaponname))
                {
                    Debug.LogError($"[BDArmory.MissileFire]: Weapon {weaponname} on {vessel.vesselName} does not exist in the gunRippleIndex!");
                    return;
                }
            }
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
                // Debug.Log("[BDArmory.MissileFire]: Ripple data: " + rippleData);
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
                        rippleData += $"{wpnName.Current},{rippleDictionary[wpnName.Current].rippleFire},{rippleDictionary[wpnName.Current].rpm};";
                    }
                node.SetValue("RippleData", rippleData, true);
            }
            //Debug.Log("[BDArmory.MissileFire]: Saved ripple data");
        }

        public float barrageStagger = 0f;
        public bool hasSingleFired;

        public bool engageAir = true;
        public bool engageMissile = true;
        public bool engageSrf = true;
        public bool engageSLW = true;
        public bool weaponsListNeedsUpdating = false;

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
        bool unguidedWeapon = false;
        public Vector3 bombAimerPosition = Vector3.zero;
        List<Vector3> bombAimerTrajectory = [];
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
        public Vessel incomingMissileVessel
        {
            get
            {
                if (_incomingMissileVessel != null && !_incomingMissileVessel.gameObject.activeInHierarchy) _incomingMissileVessel = null;
                return _incomingMissileVessel;
            }
            set { _incomingMissileVessel = value; }
        }
        Vessel _incomingMissileVessel;

        //guard mode vars
        float targetScanTimer;
        float PDScanTimer = 0;
        Vessel guardTarget;
        Vessel missileTarget;
        public TargetInfo currentTarget;
        public int engagedTargets = 0;
        public List<TargetInfo> targetsAssigned; //secondary targets list
        public List<TargetInfo> missilesAssigned; //secondary missile targets list
        public List<TargetInfo> PDMslTgts; //pointDefense/APS targets list
        public List<PooledBullet> PDBulletTgts; //pointDefense/APS targets list
        public List<PooledRocket> PDRktTgts; //pointDefense/APS targets list
        public List<MissileTurret> MslTurrets; //list of turrets holding interceptor missiles
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
        float bombFlightTime;

        //targeting pods
        public ModuleTargetingCamera mainTGP = null;
        public List<ModuleTargetingCamera> targetingPods { get { if (modulesNeedRefreshing) RefreshModules(); return _targetingPods; } }
        List<ModuleTargetingCamera> _targetingPods = new List<ModuleTargetingCamera>();

        //radar
        public List<ModuleRadar> radars { get { if (modulesNeedRefreshing) RefreshModules(); return _radars; } }
        public List<ModuleRadar> _radars = new List<ModuleRadar>();
        public int MaxradarLocks = 0;
        public VesselRadarData vesselRadarData;
        public bool _radarsEnabled = false;
        public float GpsUpdateMax = -1;
        public List<ModuleIRST> irsts { get { if (modulesNeedRefreshing) RefreshModules(); return _irsts; } }
        public List<ModuleIRST> _irsts = new List<ModuleIRST>();
        public bool _irstsEnabled = false;
        public bool _sonarsEnabled = false;
        //jammers
        public List<ModuleECMJammer> jammers { get { if (modulesNeedRefreshing) RefreshModules(); return _jammers; } }
        public List<ModuleECMJammer> _jammers = new List<ModuleECMJammer>();

        //cloak generators
        public List<ModuleCloakingDevice> cloaks { get { if (modulesNeedRefreshing) RefreshModules(); return _cloaks; } }
        public List<ModuleCloakingDevice> _cloaks = new List<ModuleCloakingDevice>();

        //other modules
        public List<IBDWMModule> wmModules { get { if (modulesNeedRefreshing) RefreshModules(); return _wmModules; } }
        List<IBDWMModule> _wmModules = new List<IBDWMModule>();

        bool modulesNeedRefreshing = true; // Refresh modules as needed — avoids excessive calling due to events.
        bool cmPrioritiesNeedRefreshing = true; // Refresh CM priorities as needed.

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
        public int designatedGPSCoordsIndex = -1;

        Vector3d designatedINSCoords = Vector3d.zero;
        public void SelectNextGPSTarget()
        {
            var targets = BDATargetManager.GPSTargetList(Team);
            if (targets.Count == 0) return;
            if (++designatedGPSCoordsIndex >= targets.Count) designatedGPSCoordsIndex = 0;
            designatedGPSInfo = targets[designatedGPSCoordsIndex];
        }

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
                    return selectedWeapon.GetWeaponModule();
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
                    return previousSelectedWeapon.GetWeaponModule();
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
        HashSet<WeaponClasses> recentlyFiringWeaponClasses = new HashSet<WeaponClasses> { WeaponClasses.Gun, WeaponClasses.Rocket, WeaponClasses.DefenseLaser };
        public bool recentlyFiring // Recently firing property for CameraTools.
        {
            get
            {
                if (guardFiringMissile) return true; // Fired a missile recently.
                foreach (var weaponCandidate in weaponArray)
                {
                    if (weaponCandidate == null || !recentlyFiringWeaponClasses.Contains(weaponCandidate.GetWeaponClass())) continue;
                    var weapon = (ModuleWeapon)weaponCandidate;
                    if (weapon == null) continue;
                    if (weapon.timeSinceFired < BDArmorySettings.CAMERA_SWITCH_FREQUENCY / 2f) return true; // Fired a gun recently.
                }
                return false;
            }
        }

        public Vector3 incomingThreatPosition;
        public Vessel incomingThreatVessel;
        public float incomingMissDistance;
        public float incomingMissTime;
        public float incomingThreatDistanceSqr;
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
        public float AutoFireCosAngleAdjustment = 1.0f; //tune Autofire angle in WM GUI

        public float adjustedAutoFireCosAngle = 0.99970f; //increased to 3 deg from 1, max increased to v1.3.8 default of 4

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_FieldOfView"),//Field of View
            UI_FloatRange(minValue = 10f, maxValue = 360f, stepIncrement = 10f, scene = UI_Scene.All)]
        public float
            guardAngle = 360;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_VisualRange"),//Visual Range
            UI_FloatSemiLogRange(minValue = 100f, maxValue = 200000f, sigFig = 1, scene = UI_Scene.All)]
        public float
            guardRange = 200000f;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_GunsRange"),//Guns Range
            UI_FloatPowerRange(minValue = 0f, maxValue = 10000f, power = 2f, sigFig = 2, scene = UI_Scene.All)]
        public float gunRange = 2500f;
        public float maxGunRange = 10f;
        public float maxVisualGunRangeSqr;

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

        [KSPField(isPersistant = true)]
        public bool targetRandom = false;

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_targetSetting")]//Target Setting
        public string targetingString = StringUtils.Localize("#LOC_BDArmory_TargetCOM");
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

        private string targetBiasLabel = StringUtils.Localize("#LOC_BDArmory_TargetPriority_CurrentTargetBias");
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority_CurrentTargetBias", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true),//Current target bias
            UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float targetBias = 1.1f;

        private string targetPreferenceLabel = StringUtils.Localize("#LOC_BDArmory_TargetPriority_AirVsGround");
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority_AirVsGround", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true),//Target Preference
            UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float targetWeightAirPreference = 0f;

        private string targetRangeLabel = StringUtils.Localize("#LOC_BDArmory_TargetPriority_TargetProximity");
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority_TargetProximity", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true),//Target Range
            UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float targetWeightRange = 1f;

        private string targetATALabel = StringUtils.Localize("#LOC_BDArmory_TargetPriority_CloserAngleToTarget");
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority_CloserAngleToTarget", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true),//Antenna Train Angle
            UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float targetWeightATA = 1f;

        private string targetAoDLabel = StringUtils.Localize("#LOC_BDArmory_TargetPriority_AngleOverDistance");
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority_AngleOverDistance", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true),//Angle/Distance
            UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float targetWeightAoD = 0f;

        private string targetAccelLabel = StringUtils.Localize("#LOC_BDArmory_TargetPriority_TargetAcceleration");
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority_TargetAcceleration", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true),//Target Acceleration
            UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float targetWeightAccel = 0;

        private string targetClosureTimeLabel = StringUtils.Localize("#LOC_BDArmory_TargetPriority_ShorterClosingTime");
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority_ShorterClosingTime", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true),//Target Closure Time
            UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float targetWeightClosureTime = 0f;

        private string targetWeaponNumberLabel = StringUtils.Localize("#LOC_BDArmory_TargetPriority_TargetWeaponNumber");
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority_TargetWeaponNumber", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true),//Target Weapon Number
            UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float targetWeightWeaponNumber = 0;

        private string targetMassLabel = StringUtils.Localize("#LOC_BDArmory_TargetPriority_TargetMass");
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority_TargetMass", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true),//Target Mass
            UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float targetWeightMass = 0;

        private string targetDmgLabel = StringUtils.Localize("#LOC_BDArmory_TargetPriority_TargetDmg");
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority_TargetDmg", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true),//Target Damage
            UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float targetWeightDamage = -1;

        private string targetFriendliesEngagingLabel = StringUtils.Localize("#LOC_BDArmory_TargetPriority_FewerTeammatesEngaging");
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority_FewerTeammatesEngaging", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true),//Number Friendlies Engaging
            UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float targetWeightFriendliesEngaging = 1f;

        private string targetThreatLabel = StringUtils.Localize("#LOC_BDArmory_TargetPriority_TargetThreat");
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority_TargetThreat", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true),//Target threat
            UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float targetWeightThreat = 1f;

        private string targetProtectTeammateLabel = StringUtils.Localize("#LOC_BDArmory_TargetPriority_TargetProtectTeammate");
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority_TargetProtectTeammate", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true),//Protect Teammates
            UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float targetWeightProtectTeammate = 0f;

        private string targetProtectVIPLabel = StringUtils.Localize("#LOC_BDArmory_TargetPriority_TargetProtectVIP");
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority_TargetProtectVIP", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true),//Protect VIPs
            UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float targetWeightProtectVIP = 0f;

        private string targetAttackVIPLabel = StringUtils.Localize("#LOC_BDArmory_TargetPriority_TargetAttackVIP");
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority_TargetAttackVIP", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true),//Attack Enemy VIPs
            UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float targetWeightAttackVIP = 0f;
        #endregion

        #region Countermeasure Settings
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_EvadeThreshold", advancedTweakable = true, groupName = "cmSettings", groupDisplayName = "#LOC_BDArmory_Countermeasure_Settings", groupStartCollapsed = true),// Evade time threshold
            UI_FloatRange(minValue = 1f, maxValue = 60f, stepIncrement = 0.5f, scene = UI_Scene.All)]
        public float evadeThreshold = 5f; // Works well

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_CMThreshold", advancedTweakable = true, groupName = "cmSettings", groupDisplayName = "#LOC_BDArmory_Countermeasure_Settings", groupStartCollapsed = true),// Countermeasure dispensing time threshold
            UI_FloatRange(minValue = 1f, maxValue = 60f, stepIncrement = 0.5f, scene = UI_Scene.All)]
        public float cmThreshold = 5f; // Works well

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_CMRepetition", advancedTweakable = true, groupName = "cmSettings", groupDisplayName = "#LOC_BDArmory_Countermeasure_Settings", groupStartCollapsed = true),// Flare dispensing repetition
            UI_FloatRange(minValue = 1f, maxValue = 20f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float cmRepetition = 3f; // Prior default was 4

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_CMInterval", advancedTweakable = true, groupName = "cmSettings", groupDisplayName = "#LOC_BDArmory_Countermeasure_Settings", groupStartCollapsed = true),// Flare dispensing interval
            UI_FloatRange(minValue = 0.01f, maxValue = 1f, stepIncrement = 0.01f, scene = UI_Scene.All)]
        public float cmInterval = 0.2f; // Prior default was 0.6

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_CMWaitTime", advancedTweakable = true, groupName = "cmSettings", groupDisplayName = "#LOC_BDArmory_Countermeasure_Settings", groupStartCollapsed = true),// Flare dispensing wait time
            UI_FloatSemiLogRange(minValue = 0.01f, maxValue = 10f, reducedPrecisionAtMin = true, scene = UI_Scene.All)]
        public float cmWaitTime = 0.7f; // Works well

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_ChaffRepetition", advancedTweakable = true, groupName = "cmSettings", groupDisplayName = "#LOC_BDArmory_Countermeasure_Settings", groupStartCollapsed = true),// Chaff dispensing repetition
            UI_FloatRange(minValue = 1f, maxValue = 20f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float chaffRepetition = 2f; // Prior default was 4

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_ChaffInterval", advancedTweakable = true, groupName = "cmSettings", groupDisplayName = "#LOC_BDArmory_Countermeasure_Settings", groupStartCollapsed = true),// Chaff dispensing interval
            UI_FloatRange(minValue = 0.01f, maxValue = 1f, stepIncrement = 0.01f, scene = UI_Scene.All)]
        public float chaffInterval = 0.5f; // Prior default was 0.6

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_ChaffWaitTime", advancedTweakable = true, groupName = "cmSettings", groupDisplayName = "#LOC_BDArmory_Countermeasure_Settings", groupStartCollapsed = true),// Chaff dispensing wait time
            UI_FloatSemiLogRange(minValue = 0.01f, maxValue = 10f, reducedPrecisionAtMin = true, scene = UI_Scene.All)]
        public float chaffWaitTime = 0.6f; // Works well

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_SmokeRepetition", advancedTweakable = true, groupName = "cmSettings", groupDisplayName = "#LOC_BDArmory_Countermeasure_Settings", groupStartCollapsed = true),// Chaff dispensing repetition
            UI_FloatRange(minValue = 1f, maxValue = 10, stepIncrement = 1f, scene = UI_Scene.All)]
        public float smokeRepetition = 1f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_SmokeInterval", advancedTweakable = true, groupName = "cmSettings", groupDisplayName = "#LOC_BDArmory_Countermeasure_Settings", groupStartCollapsed = true),// Chaff dispensing interval
            UI_FloatSemiLogRange(minValue = 0.01f, maxValue = 4f, reducedPrecisionAtMin = true, scene = UI_Scene.All)]
        public float smokeInterval = 1;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_SmokeWaitTime", advancedTweakable = true, groupName = "cmSettings", groupDisplayName = "#LOC_BDArmory_Countermeasure_Settings", groupStartCollapsed = true),// Chaff dispensing wait time
            UI_FloatSemiLogRange(minValue = 0.01f, maxValue = 10f, reducedPrecisionAtMin = true, scene = UI_Scene.All)]
        public float smokeWaitTime = 1f; // Works well

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_NonGuardModeCMs", advancedTweakable = true, groupName = "cmSettings", groupDisplayName = "#LOC_BDArmory_Countermeasure_Settings", groupStartCollapsed = true), // Non-guard mode CMs.
            UI_Toggle(enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.All)]
        public bool nonGuardModeCMs = false; // Allows for manually flying the craft while still auto-deploying CMs.

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_DynamicRadar", advancedTweakable = true, groupName = "cmSettings", groupDisplayName = "#LOC_BDArmory_Countermeasure_Settings", groupStartCollapsed = true), // Disable Radar vs ARMs
            UI_Toggle(enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.All)]
        public bool DynamicRadarOverride = false; // toggle AI toggling radar when incoming ARMs
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
                        if (!weapon.Current.isAPS) weapon.Current.aiControlled = false;
                        if (weapon.Current.dualModeAPS) weapon.Current.isAPS = true;
                    }
                if (vesselRadarData) vesselRadarData.UnslaveTurrets(); // Unslave the turrets so that manual firing works.
                weaponIndex = 0;
                selectedWeapon = null;
            }
            else
            {
                using (var weapon = VesselModuleRegistry.GetModules<ModuleWeapon>(vessel).GetEnumerator())
                    while (weapon.MoveNext())
                    {
                        if (weapon.Current == null) continue;
                        weapon.Current.aiControlled = true;
                        if (weapon.Current.isAPS) weapon.Current.EnableWeapon();
                    }
                if (radars.Count > 0)
                {
                    using (List<ModuleRadar>.Enumerator rd = radars.GetEnumerator())
                        while (rd.MoveNext())
                        {
                            if (rd.Current != null || rd.Current.canLock)
                            {
                                if (rd.Current.sonarMode == ModuleRadar.SonarModes.None)
                                {
                                    rd.Current.EnableRadar();
                                    float scanSpeed = rd.Current.directionalFieldOfView / rd.Current.scanRotationSpeed * 2;
                                    if (GpsUpdateMax > 0 && scanSpeed < GpsUpdateMax) GpsUpdateMax = scanSpeed;
                                    _radarsEnabled = true;
                                }
                                else if (rd.Current.sonarMode == ModuleRadar.SonarModes.passive)
                                // Only enable passive sonar, wouldn't want to ping the enemy
                                {
                                    rd.Current.EnableRadar();
                                    float scanSpeed = rd.Current.directionalFieldOfView / rd.Current.scanRotationSpeed * 2;
                                    if (GpsUpdateMax > 0 && scanSpeed < GpsUpdateMax) GpsUpdateMax = scanSpeed;
                                    //_sonarsEnabled = true;
                                }
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
                                float scanSpeed = rd.Current.directionalFieldOfView / rd.Current.scanRotationSpeed * 2;
                                if (GpsUpdateMax > 0 && scanSpeed < GpsUpdateMax) GpsUpdateMax = scanSpeed;
                                _irstsEnabled = true;
                            }
                            _irstsEnabled = true;
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

        [KSPAction("Deploy Kerbals' Parachutes")] // If there's an EVAing kerbal.
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

        [KSPAction("Remove Kerbals' Helmets")] // Note: removing helmets only works for the active vessel, so this waits until the vessel is active before doing so.
        public void AGRemoveKerbalsHelmets(KSPActionParam param)
        {
            if (vessel.isActiveVessel)
            {
                foreach (var kerbal in VesselModuleRegistry.GetModules<KerbalEVA>(vessel).Where(k => k != null)) kerbal.ToggleHelmetAndNeckRing(false, false);
                waitingToRemoveHelmets = false;
            }
            else if (!waitingToRemoveHelmets) StartCoroutine(RemoveKerbalsHelmetsWhenActiveVessel());
        }

        bool waitingToRemoveHelmets = false;
        IEnumerator RemoveKerbalsHelmetsWhenActiveVessel()
        {
            waitingToRemoveHelmets = true;
            yield return new WaitUntil(() => (vessel == null || vessel.isActiveVessel));
            if (vessel == null) yield break;
            foreach (var kerbal in VesselModuleRegistry.GetModules<KerbalEVA>(vessel))
            {
                if (kerbal == null) continue;
                if (kerbal.CanSafelyRemoveHelmet())
                {
                    kerbal.ToggleHelmetAndNeckRing(false, false);
                }
            }
            waitingToRemoveHelmets = false;
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
            foreach (var tnt in VesselModuleRegistry.GetModules<BDExplosivePart>(vessel))
            {
                if (tnt == null) continue;
                tnt.ArmAG(null);
                tnt.DetonateIfPossible();
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
                alliesString = string.Join("; ", value.Allies);
                team = value.Serialize();
            }
        }

        // Team name
        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_Team")]//Team
        public string teamString = "Neutral";

        // Team name
        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_Allies")]//Team
        public string alliesString = "None";

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
        public string selectedWeaponString = "None";

        /* //global toggle moved to BDASetup; decide if we need a per-WM toggle instead later for selective usage of DLZ
        [KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_MissilesRange")]//Toggle DLZ
        public void ToggleDLZ()
        {
            BDArmorySettings.USE_DLZ_LAUNCH_RANGE = !BDArmorySettings.USE_DLZ_LAUNCH_RANGE;
            Events["ToggleDLZ"].guiName = $" {StringUtils.Localize("#LOC_BDArmory_MissilesRange")}: {(BDArmorySettings.USE_DLZ_LAUNCH_RANGE ? StringUtils.Localize("#LOC_BDArmory_true") : StringUtils.Localize("#LOC_BDArmory_false"))}";//"Use Dynamic Launch Range: True/False
            GUIUtils.RefreshAssociatedWindows(part);
        }
        */
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
                            var gun = weapon.Current.GetWeaponModule();
                            sw = weapon.Current; //check against salvofiring turrets - if all guns overheat at the same time, turrets get stuck in standby mode
                            if (gun.isReloading || gun.isOverheated || gun.pointingAtSelf || !(gun.ammoCount > 0 || BDArmorySettings.INFINITE_AMMO)) continue; //instead of returning the first weapon in a weapon group, return the first weapon in a group that actually can fire
                            //no check for if all weapons in the group are reloading/overheated...
                            //Doc also was floating the idea of a 'use this gun' button for aiming, though that would be more a PilotAi thing...
                        }
                        if (weapon.Current.GetWeaponClass() == WeaponClasses.Missile || weapon.Current.GetWeaponClass() == WeaponClasses.Bomb || weapon.Current.GetWeaponClass() == WeaponClasses.SLW)
                        {
                            var msl = weapon.Current.GetPart().FindModuleImplementing<MissileLauncher>();
                            if (msl == null) continue;

                            if (msl.launched || msl.HasFired) continue; //return first missile that is ready to fire
                            if (msl.GetEngageRange() != selectedWeaponsEngageRangeMax) continue;
                            sw = weapon.Current;
                        }
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
                selectedWeaponsEngageRangeMax = GetWeaponRange(value);
                UpdateSelectedWeaponState();
            }
        }

        IBDWeapon previousSelectedWeapon { get; set; }

        public float selectedWeaponsEngageRangeMax { get; private set; } = 0;

        [KSPAction("Fire Missile")]
        public void AGFire(KSPActionParam param)
        {
            FireMissileManually(false);
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

        RaycastHit[] clearanceHits = new RaycastHit[10];

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
            clickSound = SoundUtils.GetAudioClip("BDArmory/Sounds/click");
            warningSound = SoundUtils.GetAudioClip("BDArmory/Sounds/warning");
            armOnSound = SoundUtils.GetAudioClip("BDArmory/Sounds/armOn");
            armOffSound = SoundUtils.GetAudioClip("BDArmory/Sounds/armOff");
            heatGrowlSound = SoundUtils.GetAudioClip("BDArmory/Sounds/heatGrowl");

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
                if (guardMode) ToggleGuardMode();
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

                // Update the max visual gun range (sqr) whenever the gun range or guard range changes.
                {
                    ((UI_FloatSemiLogRange)Fields["guardRange"].uiControlFlight).onFieldChanged = UpdateVisualGunRangeSqr;
                    ((UI_FloatPowerRange)Fields["gunRange"].uiControlFlight).onFieldChanged = UpdateVisualGunRangeSqr;
                    UpdateVisualGunRangeSqr(null, null);
                }

                AI = VesselModuleRegistry.GetIBDAIControl(vessel, true);

                modulesNeedRefreshing = true;
                cmPrioritiesNeedRefreshing = true;
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
            targetingString = (targetCoM ? StringUtils.Localize("#LOC_BDArmory_TargetCOM") + "; " : "")
                + (targetMass ? StringUtils.Localize("#LOC_BDArmory_Mass") + "; " : "")
                + (targetCommand ? StringUtils.Localize("#LOC_BDArmory_Command") + "; " : "")
                + (targetEngine ? StringUtils.Localize("#LOC_BDArmory_Engines") + "; " : "")
                + (targetWeapon ? StringUtils.Localize("#LOC_BDArmory_Weapons") + "; " : "")
                + (targetRandom ? StringUtils.Localize("#LOC_BDArmory_Random") + "; " : "");

            if (HighLogic.LoadedSceneIsFlight) TimingManager.FixedUpdateAdd(TimingManager.TimingStage.Earlyish, PointDefence); // Perform point defence checks before bullets get moved to avoid order of operation issues.
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
            modulesNeedRefreshing = true;
            weaponsListNeedsUpdating = true;
            cmPrioritiesNeedRefreshing = true;
            // UpdateList();
            if (vessel != null)
            {
                var TI = vessel.gameObject.GetComponent<TargetInfo>();
                if (TI != null)
                {
                    TI.targetPartListNeedsUpdating = true;
                }
            }
        }

        void OnVesselCreate(Vessel v)
        {
            if (v == null) return;
            modulesNeedRefreshing = true;
            cmPrioritiesNeedRefreshing = true;
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

            if (HighLogic.LoadedSceneIsFlight && ((j.Parent && j.Parent.vessel == vessel) || (j.Child && j.Child.vessel == vessel)))
            {
                modulesNeedRefreshing = true;
                weaponsListNeedsUpdating = true;
                cmPrioritiesNeedRefreshing = true;
                // UpdateList();
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

        public override void OnUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight)
            {
                return;
            }

            base.OnUpdate();

            UpdateTargetingAudio();

            if (vessel.isActiveVessel && !guardMode) // Manual firing.
            {
                bool missileTriggerHeld = false;
                if (!CheckMouseIsOnGui() && isArmed && BDInputUtils.GetKey(BDInputSettingsFields.WEAP_FIRE_KEY))
                {
                    triggerTimer += Time.fixedDeltaTime;
                    missileTriggerHeld = true;
                }
                else
                {
                    triggerTimer = 0;
                }
                if (BDInputUtils.GetKey(BDInputSettingsFields.WEAP_FIRE_MISSILE_KEY))
                {
                    FireMissileManually(false);
                    missileTriggerHeld = true;
                }
                if (hasSingleFired && !missileTriggerHeld)
                {
                    hasSingleFired = false;
                }
                if (BDInputUtils.GetKeyDown(BDInputSettingsFields.WEAP_NEXT_KEY)) CycleWeapon(true);
                if (BDInputUtils.GetKeyDown(BDInputSettingsFields.WEAP_PREV_KEY)) CycleWeapon(false);
                if (BDInputUtils.GetKeyDown(BDInputSettingsFields.WEAP_TOGGLE_ARMED_KEY)) ToggleArm();
                if (BDInputUtils.GetKeyDown(BDInputSettingsFields.TGP_SELECT_NEXT_GPS_TARGET)) SelectNextGPSTarget();

                //firing missiles and rockets===
                if (selectedWeapon != null &&
                    (selectedWeapon.GetWeaponClass() == WeaponClasses.Missile
                     || selectedWeapon.GetWeaponClass() == WeaponClasses.Bomb
                     || selectedWeapon.GetWeaponClass() == WeaponClasses.SLW
                    ))
                {
                    canRipple = true;
                    FireMissileManually(true);
                }
                else if (selectedWeapon != null &&
                         ((selectedWeapon.GetWeaponClass() == WeaponClasses.Gun
                         || selectedWeapon.GetWeaponClass() == WeaponClasses.Rocket
                         || selectedWeapon.GetWeaponClass() == WeaponClasses.DefenseLaser) && currentGun.canRippleFire))//&& currentGun.roundsPerMinute < 1500)) //set this based on if the WG can ripple vs if first weapon in the WG happens to be > 1500 RPM
                {
                    canRipple = true;
                }
                else
                {
                    canRipple = false; // Disable the ripple options in the WM gui.
                }
            }
            else
            {
                canRipple = false; // Disable the ripple options in the WM gui.
                triggerTimer = 0;
                hasSingleFired = false; // The AI uses this as part of it's authorisation check for guns!
            }
        }

        void UpdateWeaponIndex()
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
        }

        void UpdateGuidanceTargets()
        {
            if (weaponIndex > 0 &&
                   (selectedWeapon.GetWeaponClass() == WeaponClasses.Missile ||
                   selectedWeapon.GetWeaponClass() == WeaponClasses.SLW ||
                    selectedWeapon.GetWeaponClass() == WeaponClasses.Bomb))
            {
                SearchForLaserPoint();
                SearchForHeatTarget(CurrentMissile);
                SearchForRadarSource();
            }
            CalculateMissilesAway();
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
                    if ((missileBase.HasFired || missileBase.launched) && !missileBase.HasMissed && !missileBase.HasExploded || missileBase.GetWeaponClass() == WeaponClasses.Bomb) //culling post-thrust missiles makes AGMs get cleared almost immediately after launch
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
            if (vessel == null || !vessel.gameObject.activeInHierarchy) return;
            if (weaponsListNeedsUpdating) UpdateList();

            if (!vessel.packed)
            {
                UpdateWeaponIndex();
                UpdateGuidanceTargets();
            }

            if (guardMode && vessel.IsControllable) //isControllable returns false if Commsnet is enabled and probecore craft has no antenna
            {
                GuardMode();
            }
            else
            {
                if (nonGuardModeCMs && vessel.IsControllable) UpdateGuardViewScan(); // Scan for missiles and automatically deploy CMs / enable RWR.
                targetScanTimer = -100;
            }
            bombFlightTime = BombAimer();
        }

        void PointDefence()
        {
            if (vessel.IsControllable)
            {
                if (Time.time - PDScanTimer > 0.1f)
                {
                    PointDefenseTurretFiring();
                    PDScanTimer = Time.time;
                }
            }
            else PDScanTimer = -100;
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
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.Earlyish, PointDefence);
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
                    if (guardTarget != null)
                        GUIUtils.DrawLineBetweenWorldPositions(guardTarget.LandedOrSplashed ? guardTarget.CoM + ((guardTarget.vesselSize.y / 2) * VectorUtils.GetUpDirection(transform.position)) : guardTarget.CoM,
                        ((vessel.LandedOrSplashed && (guardTarget.transform.position - transform.position).sqrMagnitude > 2250000f) ?
                        transform.position + (SurfaceVisionOffset.Evaluate((guardTarget.CoM - transform.position).magnitude) * VectorUtils.GetUpDirection(transform.position)) : transform.position), 3, Color.yellow);
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
                    switch (missile.TargetingMode)
                    {
                        case MissileBase.TargetingModes.Laser:
                            {
                                if (laserPointDetected && foundCam)
                                {
                                    GUIUtils.DrawTextureOnWorldPos(foundCam.groundTargetPosition, BDArmorySetup.Instance.greenCircleTexture, new Vector2(48, 48), 1);
                                }
                                else
                                {
                                    GUIUtils.DrawTextureOnWorldPos(missile.MissileReferenceTransform.position + (2000 * missile.GetForwardTransform()), BDArmorySetup.Instance.largeGreenCircleTexture, new Vector2(96, 96), 0);
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
                                break;
                            }
                        case MissileBase.TargetingModes.Heat:
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
                                break;
                            }
                        case MissileBase.TargetingModes.Radar:
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
                                else
                                {
                                    GUIUtils.DrawTextureOnWorldPos(missile.MissileReferenceTransform.position + (2000 * missile.GetForwardTransform()), BDArmorySetup.Instance.largeGreenCircleTexture, new Vector2(96, 96), 0);
                                }
                                break;
                            }
                        case MissileBase.TargetingModes.AntiRad:
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
                                break;
                            }
                        case MissileBase.TargetingModes.Inertial:
                            {
                                MissileBase ml = CurrentMissile;
                                float distanceToTarget = 0;

                                if (vesselRadarData)
                                {
                                    TargetSignatureData targetData = TargetSignatureData.noTarget;
                                    if (_radarsEnabled || ml.GetWeaponClass() == WeaponClasses.SLW && _sonarsEnabled)
                                    {
                                        if (vesselRadarData.locked)
                                            targetData = vesselRadarData.lockedTargetData.targetData;
                                        else
                                            targetData = vesselRadarData.detectedRadarTarget(guardTarget != null ? guardTarget : null, this);
                                    }
                                    else if (_irstsEnabled)
                                        targetData = vesselRadarData.activeIRTarget(null, this);

                                    if (targetData.exists)
                                    {
                                        distanceToTarget = Vector3.Distance(targetData.predictedPosition, ml.MissileReferenceTransform.position);
                                        Vector3 fireSolution = MissileGuidance.GetAirToAirFireSolution(ml, targetData.predictedPosition, targetData.velocity);
                                        Vector3 fsDirection = (fireSolution - ml.MissileReferenceTransform.position).normalized;
                                        if (vesselRadarData.locked)
                                            GUIUtils.DrawTextureOnWorldPos(ml.MissileReferenceTransform.position + (distanceToTarget * fsDirection), BDArmorySetup.Instance.greenDotTexture, new Vector2(6, 6), 0);
                                        else
                                            GUIUtils.DrawTextureOnWorldPos(ml.MissileReferenceTransform.position + (distanceToTarget * fsDirection), BDArmorySetup.Instance.greenCircleTexture, new Vector2(36, 36), 5);
                                        GUIUtils.DrawTextureOnWorldPos(ml.MissileReferenceTransform.position + (distanceToTarget * ml.GetForwardTransform()), BDArmorySetup.Instance.dottedLargeGreenCircle, new Vector2(128, 128), 0);
                                    }
                                    else
                                    {
                                        GUIUtils.DrawTextureOnWorldPos(missile.MissileReferenceTransform.position + (2000 * missile.GetForwardTransform()), BDArmorySetup.Instance.largeGreenCircleTexture, new Vector2(156, 156), 0);
                                        break;
                                    }
                                }
                                else
                                    GUIUtils.DrawTextureOnWorldPos(missile.MissileReferenceTransform.position + (2000 * missile.GetForwardTransform()), BDArmorySetup.Instance.largeGreenCircleTexture, new Vector2(156, 156), 0);
                                break;
                            }
                        case MissileBase.TargetingModes.None:
                            {
                                if (selectedWeapon.GetWeaponClass() != WeaponClasses.Bomb)
                                {
                                    GUIUtils.DrawTextureOnWorldPos(missile.MissileReferenceTransform.position + (1250 * missile.GetForwardTransform()), BDArmorySetup.Instance.largeGreenCircleTexture, new Vector2(96, 96), 0);
                                }
                                break;
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
                    debugString.AppendLine($"Missiles away: {firedMissiles}; targeted vessels: {engagedTargets}");

                    if (missileIsIncoming)
                    {
                        foreach (var incomingMissile in results.incomingMissiles)
                            debugString.AppendLine($"Incoming missile: {(incomingMissile.vessel != null ? incomingMissile.vessel.vesselName + $" @ {incomingMissile.distance:0} m ({incomingMissile.time:0.0}s)" : null)}");
                    }
                    if (underAttack) debugString.AppendLine($"Under attack from {(incomingThreatVessel != null ? incomingThreatVessel.vesselName : null)}");
                    if (underFire) debugString.AppendLine($"Under fire from {(priorGunThreatVessel != null ? priorGunThreatVessel.vesselName : null)}");
                    if (isChaffing) debugString.AppendLine("Chaffing");
                    if (isFlaring) debugString.AppendLine("Flaring");
                    if (isSmoking) debugString.AppendLine("Dropping Smoke");
                    if (isECMJamming) debugString.AppendLine("ECMJamming");
                    if (isCloaking) debugString.AppendLine("Cloaking");
                }
                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_WEAPONS)
                {
                    if (weaponArray != null) // Heat debugging
                    {
                        List<string> weaponHeatDebugStrings = new List<string>();
                        List<string> weaponAimDebugStrings = new List<string>();
                        HashSet<WeaponClasses> validClasses = new HashSet<WeaponClasses> { WeaponClasses.Gun, WeaponClasses.Rocket, WeaponClasses.DefenseLaser };
                        foreach (var weaponCandidate in VesselModuleRegistry.GetModules<IBDWeapon>(vessel)) // Show each weapon, not each weapon group (which might contain multiple weapon types).
                        {
                            if (weaponCandidate == null || !validClasses.Contains(weaponCandidate.GetWeaponClass())) continue;
                            var weapon = (ModuleWeapon)weaponCandidate;
                            if (weapon is null) continue;
                            weaponHeatDebugStrings.Add(string.Format(" - {0}: heat: {1,6:F1}, max: {2}, overheated: {3}", weapon.shortName, weapon.heat, weapon.maxHeat, weapon.isOverheated));

                            weaponAimDebugStrings.Add($" - Target: {(weapon.visualTargetPart != null ? weapon.visualTargetPart.name : weapon.visualTargetVessel != null ? weapon.visualTargetVessel.vesselName : weapon.GPSTarget ? "GPS" : weapon.slaved ? "slaved" : weapon.radarTarget ? "radar" : weapon.atprAcquired ? "atpr" : "none")}, Lead Offset: {weapon.GetLeadOffset()}, FinalAimTgt: {weapon.finalAimTarget}, tgt Position: {weapon.targetPosition}, pointingAtSelf: {weapon.pointingAtSelf}, safeToFire: {weapon.safeToFire}, tgt CosAngle {weapon.targetCosAngle}, wpn CosAngle {weapon.targetAdjustedMaxCosAngle}, Wpn Autofire {weapon.autoFire}, target Radius {weapon.targetRadius}, RoF {weapon.roundsPerMinute}, MaxRoF {weapon.baseRPM}");

                            // weaponAimDebugStrings.Add($" - Target pos: {weapon.targetPosition.ToString("G3")}, vel: {weapon.targetVelocity.ToString("G4")}, acc: {weapon.targetAcceleration.ToString("G6")}");
                            // weaponAimDebugStrings.Add($" - Target rel pos: {(weapon.targetPosition - weapon.fireTransforms[0].position).ToString("G3")} ({(weapon.targetPosition - weapon.fireTransforms[0].position).magnitude:F1}), rel vel: {(weapon.targetVelocity - weapon.part.rb.velocity).ToString("G4")}, rel acc: {((Vector3)(weapon.targetAcceleration - weapon.vessel.acceleration)).ToString("G6")}");
#if DEBUG
                            if (weapon.visualTargetVessel != null && weapon.visualTargetVessel.loaded) weaponAimDebugStrings.Add($" - Visual target {(weapon.visualTargetPart != null ? weapon.visualTargetPart.name : "CoM")} on {weapon.visualTargetVessel.vesselName}, distance: {(weapon.fireTransforms[0] != null ? (weapon.finalAimTarget - weapon.fireTransforms[0].position).magnitude : 0):F1}, radius: {weapon.targetRadius:F1} ({weapon.visualTargetVessel.GetBounds()}), max deviation: {weapon.maxDeviation}, firing tolerance: {weapon.FiringTolerance}");
                            if (weapon.turret) weaponAimDebugStrings.Add($" - Turret: pitch: {weapon.turret.Pitch:F3}° ({weapon.turret.minPitch}°—{weapon.turret.maxPitch}°), yaw: {weapon.turret.Yaw:F3}° ({-weapon.turret.yawRange / 2f}°—{weapon.turret.yawRange / 2f}°)");
#endif
                        }
                        float shots = 0;
                        float hits = 0;
                        float accuracy = 0;
                        if (BDACompetitionMode.Instance.Scores.ScoreData.ContainsKey(vessel.vesselName))
                        {
                            hits = BDACompetitionMode.Instance.Scores.ScoreData[vessel.vesselName].hits;
                            shots = BDACompetitionMode.Instance.Scores.ScoreData[vessel.vesselName].shotsFired;
                            if (shots > 0) accuracy = hits / shots;
                        }
                        weaponHeatDebugStrings.Add($" - Shots Fired: {shots}, Shots Hit: {hits}, Accuracy: {accuracy:F3}");

                        if (weaponHeatDebugStrings.Count > 0)
                        {
                            debugString.AppendLine("Weapon Heat:\n" + string.Join("\n", weaponHeatDebugStrings));
                            debugString.AppendLine("Aim debugging:\n" + string.Join("\n", weaponAimDebugStrings));
                        }
                    }
                    GUI.Label(new Rect(200, Screen.height - 700, Screen.width / 2 - 200, 16 * debugString.Length), debugString.ToString());
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
            while (!FlightGlobals.ready || (vessel is not null && (vessel.packed || !vessel.loaded)))
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
                yield return new WaitUntilFixed(() => missileIsIncoming); // Wait until missile is incoming.
                if (BDArmorySettings.DEBUG_AI) { Debug.Log($"[BDArmory.MissileFire]: Triggering missile warning on {vessel.vesselName}"); }
                yield return new WaitUntilFixed(() => Time.time - incomingMissileLastDetected > 1f); // Wait until 1s after no missiles are detected.
                if (BDArmorySettings.DEBUG_AI) { Debug.Log($"[BDArmory.MissileFire]: Silencing missile warning on {vessel.vesselName}"); }
                missileIsIncoming = false;
            }
        }

        IEnumerator UnderFireRoutine()
        {
            underFireLastNotified = Time.time; // Update the last notification.
            if (underFire) yield break; // Already under fire, we only want 1 timer.
            underFire = true;
            if (BDArmorySettings.DEBUG_AI) { Debug.Log($"[BDArmory.MissileFire]: Triggering under fire warning on {vessel.vesselName} by {priorGunThreatVessel.vesselName}"); }
            yield return new WaitUntilFixed(() => Time.time - underFireLastNotified > 1f); // Wait until 1s after being under fire.
            if (BDArmorySettings.DEBUG_AI) { Debug.Log($"[BDArmory.MissileFire]: Silencing under fire warning on {vessel.vesselName}"); }
            underFire = false;
            priorGunThreatVessel = null;
        }

        IEnumerator UnderAttackRoutine()
        {
            underAttackLastNotified = Time.time; // Update the last notification.
            if (underAttack) yield break; // Already under attack, we only want 1 timer.
            underAttack = true;
            if (BDArmorySettings.DEBUG_AI) { Debug.Log($"[BDArmory.MissileFire]: Triggering under attack warning on {vessel.vesselName} by {incomingThreatVessel.vesselName}"); }
            yield return new WaitUntilFixed(() => Time.time - underAttackLastNotified > 1f); // Wait until 3s after being under attack.
            if (BDArmorySettings.DEBUG_AI) { Debug.Log($"[BDArmory.MissileFire]: Silencing under attack warning on {vessel.vesselName}"); }
            underAttack = false;
        }

        IEnumerator GuardTurretRoutine()
        {
            if (SetDeployableWeapons())
            {
                yield return new WaitForSecondsFixed(2f);
            }

            if (gameObject.activeInHierarchy)
            //target is out of visual range, try using sensors
            {
                if (guardTarget.LandedOrSplashed)
                {
                    if (targetingPods.Count > 0)
                    {
                        float scaledDistance = Mathf.Max(400f, 0.004f * (float)guardTarget.srfSpeed * (float)guardTarget.srfSpeed);
                        using (List<ModuleTargetingCamera>.Enumerator tgp = targetingPods.GetEnumerator())
                            while (tgp.MoveNext())
                            {
                                if (tgp.Current == null) continue;
                                if (!tgp.Current.enabled || (tgp.Current.cameraEnabled && tgp.Current.groundStabilized &&
                                                             !((tgp.Current.groundTargetPosition - guardTarget.transform.position).sqrMagnitude > scaledDistance))) continue;
                                tgp.Current.EnableCamera();
                                yield return StartCoroutine(tgp.Current.PointToPositionRoutine(guardTarget.CoM, guardTarget));
                                //yield return StartCoroutine(tgp.Current.PointToPositionRoutine(TargetInfo.TargetCOMDispersion(guardTarget)));
                                if (!tgp.Current) continue;
                                if (tgp.Current.groundStabilized && guardTarget &&
                                    (tgp.Current.groundTargetPosition - guardTarget.transform.position).sqrMagnitude < scaledDistance)
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
                                if ((rd.Current != null || rd.Current.canLock) && rd.Current.sonarMode == ModuleRadar.SonarModes.None)
                                {
                                    rd.Current.EnableRadar();
                                    _radarsEnabled = true;
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
                        yield return new WaitForSecondsFixed(0.5f);
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
            yield return new WaitForSecondsFixed(8);
            incomingMissileDistance = float.MaxValue;
            incomingMissileTime = float.MaxValue;
        }

        IEnumerator GuardMissileRoutine(Vessel targetVessel, MissileBase ml)
        {
            if (ml && !guardFiringMissile)
            {
                bool dumbfiring = false;
                guardFiringMissile = true;
                var wait = new WaitForFixedUpdate();
                float tryLockTime = targetVessel.IsMissile() ? 0.05f : 0.25f; // More urgency for incoming missiles
                switch (ml.TargetingMode)
                {
                    case MissileBase.TargetingModes.Radar:
                        {
                            if (vesselRadarData) //no check for radar present, but off/out of juice
                            {
                                float BayTriggerTime = -1;
                                if (SetCargoBays())
                                {
                                    BayTriggerTime = Time.time;
                                    //yield return new WaitForSecondsFixed(2f); //so this doesn't delay radar targeting stuff below
                                }
                                float attemptLockTime = Time.time;
                                while (ml && (!vesselRadarData.locked || (vesselRadarData.lockedTargetData.vessel != targetVessel)) && Time.time - attemptLockTime < 2)
                                {
                                    if (vesselRadarData.locked)
                                    {
                                        List<TargetSignatureData> possibleTargets = vesselRadarData.GetLockedTargets();
                                        bool existingLock = false;
                                        for (int i = 0; i < possibleTargets.Count; i++)
                                        {
                                            if (possibleTargets[i].vessel == targetVessel)
                                            {
                                                existingLock = true;
                                                break;
                                            }
                                        }
                                        if (existingLock)
                                        {
                                            vesselRadarData.SwitchActiveLockedTarget(targetVessel);
                                            yield return wait;
                                        }
                                        else
                                        {
                                            // if a low lock capacity radar , and it already has a lock on another target, TLT will return false, because the radar already at lock cap
                                            // end result: radar lock stuck on wrong target; need unlock, then lock if lock num = max locks
                                            //if availableLocks, tryLocktarget, else, unlock target -> try locktarget
                                            if (MaxradarLocks <= vesselRadarData.GetLockedTargets().Count) //not currently checking if available radar locks are viable, e.g. a rear-facing radar w/ lock capability
                                            {
                                                if (PreviousMissile == null || (PreviousMissile.TargetingMode != MissileBase.TargetingModes.Radar && PreviousMissile.TargetingMode != MissileBase.TargetingModes.Inertial && PreviousMissile.TargetingMode != MissileBase.TargetingModes.Gps))
                                                    vesselRadarData.UnlockAllTargets();
                                                else if (PreviousMissile.TargetingMode == MissileBase.TargetingModes.Radar)
                                                {
                                                    if (PreviousMissile.ActiveRadar)
                                                        vesselRadarData.UnlockSelectedTarget(PreviousMissile.targetVessel.Vessel); //no longer need that lock for guidance, remove
                                                    else
                                                    {
                                                        if (MaxradarLocks > 1) //guiding a SARH, but he have a spare lock...
                                                        {
                                                            vesselRadarData.UnlockAllTargets(); //clear everything...
                                                            vesselRadarData.TryLockTarget(PreviousMissile.targetVessel.Vessel); //and immediately relock the SARH target vessel as a work around for only having unlock everything, and unlockselected
                                                        }
                                                        else
                                                        {
                                                            if (!CurrentMissile.radarLOAL) //LOAL missiles at least can be dumbfired...
                                                                if (BDArmorySettings.DEBUG_MISSILES)
                                                                    Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} cannot fire radar missile, all locks in use! Aborting loaunch!");
                                                            break; //we need single lock for our previous SARH against the previous target, break; current radar missile will have to wait.
                                                        }
                                                    }
                                                }
                                                vesselRadarData.TryLockTarget(targetVessel);
                                            }
                                        }
                                    }
                                    else
                                        vesselRadarData.TryLockTarget(targetVessel);

                                    yield return new WaitForSecondsFixed(tryLockTime);
                                }
                                // if (ml && AIMightDirectFire() && vesselRadarData.locked)
                                // {
                                //     SetCargoBays();
                                //     float LAstartTime = Time.time;
                                //     while (AIMightDirectFire() && Time.time - LAstartTime < 3 && !GetLaunchAuthorization(guardTarget, this))
                                //     {
                                //         yield return new WaitForFixedUpdate();
                                //     }
                                //     // yield return new WaitForSecondsFixed(0.5f);
                                // }

                                //wait for missile turret to point at target
                                //TODO BDModularGuidance: add turret
                                float attemptStartTime = Time.time;
                                MissileLauncher mlauncher = ml as MissileLauncher;
                                if (targetVessel && mlauncher)
                                {
                                    float angle = 999;
                                    float turretStartTime = attemptStartTime;
                                    if (mlauncher.missileTurret && vesselRadarData.locked)
                                    {
                                        vesselRadarData.SlaveTurrets();
                                        while (Time.time - turretStartTime < Mathf.Max(targetScanInterval / 2f, 2) && targetVessel && mlauncher && angle > mlauncher.missileTurret.fireFOV)
                                        {
                                            angle = Vector3.Angle(mlauncher.missileTurret.finalTransform.forward, mlauncher.missileTurret.slavedTargetPosition - mlauncher.missileTurret.finalTransform.position);
                                            mlauncher.missileTurret.slaved = true;
                                            mlauncher.missileTurret.slavedTargetPosition = MissileGuidance.GetAirToAirFireSolution(mlauncher, targetVessel.CoM, targetVessel.Velocity());
                                            mlauncher.missileTurret.SlavedAim();
                                            //Debug.Log($"[PD Missile Debug - {vessel.GetName()}] bringing radarMsl turret to bear...");
                                            yield return wait;
                                        }
                                    }
                                    if (mlauncher.multiLauncher && mlauncher.multiLauncher.turret && vesselRadarData.locked)
                                    {
                                        vesselRadarData.SlaveTurrets();
                                        while (Time.time - turretStartTime < Mathf.Max(targetScanInterval / 2f, 2) && targetVessel && mlauncher && angle > mlauncher.multiLauncher.turret.fireFOV)
                                        {
                                            angle = Vector3.Angle(mlauncher.multiLauncher.turret.finalTransform.forward, mlauncher.multiLauncher.turret.slavedTargetPosition - mlauncher.multiLauncher.turret.finalTransform.position);
                                            mlauncher.multiLauncher.turret.slaved = true;
                                            mlauncher.multiLauncher.turret.slavedTargetPosition = MissileGuidance.GetAirToAirFireSolution(mlauncher, targetVessel.CoM, targetVessel.Velocity());
                                            mlauncher.multiLauncher.turret.SlavedAim();
                                            //Debug.Log($"[PD Missile Debug - {vessel.GetName()}] bringing radarMsl turret to bear...");
                                            yield return wait;
                                        }
                                    }
                                    if (mlauncher.multiLauncher && !mlauncher.multiLauncher.turret)
                                        yield return new WaitForSecondsFixed(mlauncher.multiLauncher.deploySpeed);
                                }
                                yield return wait;

                                // if (ml && guardTarget && vesselRadarData.locked && (!AIMightDirectFire() || GetLaunchAuthorization(guardTarget, this)))
                                //no check if only non-locking scanning radars on craft
                                //if (ml && guardTarget && ((vesselRadarData.locked && vesselRadarData.lockedTargetData.vessel == guardTarget) || ml.radarLOAL) && GetLaunchAuthorization(guardTarget, this)) //allow lock on after launch missiles to fire of target scanned by not locked?
                                if (ml && targetVessel)
                                {
                                    if (vesselRadarData.locked && vesselRadarData.lockedTargetData.vessel == targetVessel)
                                    {
                                        if (GetLaunchAuthorization(targetVessel, this, ml))
                                        {
                                            if (BDArmorySettings.DEBUG_MISSILES)
                                            {
                                                Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} firing on target {targetVessel.GetName()}");
                                            }
                                            if (BayTriggerTime > 0 && (Time.time - BayTriggerTime < 2)) //if bays opening, see if 2 sec for the bays to open have elapsed, if not, wait remaining time needed
                                            {
                                                yield return new WaitForSecondsFixed(2 - (Time.time - BayTriggerTime));
                                            }
                                            FireCurrentMissile(ml, true);
                                            //StartCoroutine(MissileAwayRoutine(mlauncher));
                                        }
                                    }
                                    else
                                    {
                                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName}'s {(CurrentMissile ? CurrentMissile.name : "null missile")} could not lock, attempting unguided fire.");
                                        dumbfiring = true; //so let them be used as unguided ordinance
                                    }
                                }
                            }
                            else //no radar, missiles now expensive unguided ordinance
                            {
                                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName}'s {(CurrentMissile ? CurrentMissile.name : "null missile")} has no radar, attempting unguided fire.");
                                dumbfiring = true; //so let them be used as unguided ordinance
                            }
                            break;
                        }
                    case MissileBase.TargetingModes.Heat:
                        {
                            if (vesselRadarData && vesselRadarData.locked) // FIXME This wipes radar guided missiles' targeting data when switching to a heat guided missile. Radar is used to allow heat seeking missiles with allAspect = true to lock on target and fire when the target is not within sensor FOV
                            {
                                //vesselRadarData.UnlockAllTargets() //maybe use vrd.UnlockCurrentTarget() instead? //Or check if previousMissile isn't a SARH that needs that lock...
                                vesselRadarData.UnslaveTurrets();
                            }

                            if (SetCargoBays())
                            {
                                yield return new WaitForSecondsFixed(2f);
                            }

                            float attemptStartTime = Time.time;
                            float attemptDuration = Mathf.Max(targetScanInterval * 0.75f, 5f);
                            MissileLauncher mlauncher;
                            while (ml && targetVessel && Time.time - attemptStartTime < attemptDuration && (!heatTarget.exists || (heatTarget.predictedPosition - targetVessel.transform.position).sqrMagnitude > 40 * 40))
                                yield return wait;
                            if (BDArmorySettings.DEBUG_MISSILES && CurrentMissile) Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName}'s {CurrentMissile.GetShortName()} has heatTarget: {heatTarget.exists}");
                            //try uncaged IR lock with radar
                            if (ml.activeRadarRange > 0) //defaults to 6k for non-radar missiles, using negative value for differentiating passive acoustic vs heater
                            {
                                if (targetVessel && !heatTarget.exists && vesselRadarData)
                                {
                                    if (_radarsEnabled)
                                    {
                                        if (!vesselRadarData.locked ||
                                            (vesselRadarData.lockedTargetData.targetData.predictedPosition -
                                             targetVessel.transform.position).sqrMagnitude > 40 * 40)
                                        {
                                            //vesselRadarData.TryLockTarget(guardTarget.transform.position);
                                            if (PreviousMissile != null && PreviousMissile.ActiveRadar && PreviousMissile.targetVessel != null) //previous missile has gone active, don't need that lock anymore
                                            {
                                                vesselRadarData.UnlockSelectedTarget(PreviousMissile.targetVessel.Vessel);
                                            }
                                            vesselRadarData.TryLockTarget(targetVessel);
                                            yield return new WaitForSecondsFixed(Mathf.Min(1, (targetScanInterval * tryLockTime)));
                                        }
                                    }
                                    else if (_irstsEnabled)
                                    {
                                        heatTarget = vesselRadarData.activeIRTarget(targetVessel, this);
                                        yield return new WaitForSecondsFixed(Mathf.Min(1, (targetScanInterval * tryLockTime)));
                                    }
                                }
                            }
                            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName}'s heatTarget locked");
                            // if (AIMightDirectFire() && ml && heatTarget.exists)
                            // {
                            //     float LAstartTime = Time.time;
                            //     while (Time.time - LAstartTime < 3 && AIMightDirectFire() && GetLaunchAuthorization(guardTarget, this))
                            //     {
                            //         yield return new WaitForFixedUpdate();
                            //     }
                            //     yield return new WaitForSecondsFixed(0.5f);
                            // }

                            //wait for missile turret to point at target
                            attemptStartTime = Time.time;
                            mlauncher = ml as MissileLauncher;
                            if (targetVessel && mlauncher)
                            {
                                float angle = 999;
                                float turretStartTime = attemptStartTime;
                                if (mlauncher.missileTurret && heatTarget.exists)
                                {
                                    while (heatTarget.exists && Time.time - turretStartTime < Mathf.Max(targetScanInterval / 2f, 2) && targetVessel && mlauncher && angle > mlauncher.missileTurret.fireFOV)
                                    {
                                        angle = Vector3.Angle(mlauncher.missileTurret.finalTransform.forward, mlauncher.missileTurret.slavedTargetPosition - mlauncher.missileTurret.finalTransform.position);
                                        mlauncher.missileTurret.slaved = true;
                                        mlauncher.missileTurret.slavedTargetPosition = MissileGuidance.GetAirToAirFireSolution(mlauncher, heatTarget.predictedPosition, heatTarget.velocity);
                                        mlauncher.missileTurret.SlavedAim();
                                        yield return wait;
                                    }
                                }
                                if (mlauncher.multiLauncher && mlauncher.multiLauncher.turret && heatTarget.exists)
                                {
                                    while (heatTarget.exists && Time.time - turretStartTime < Mathf.Max(targetScanInterval / 2f, 2) && targetVessel && mlauncher && angle > mlauncher.multiLauncher.turret.fireFOV)
                                    {
                                        angle = Vector3.Angle(mlauncher.multiLauncher.turret.finalTransform.forward, mlauncher.multiLauncher.turret.slavedTargetPosition - mlauncher.multiLauncher.turret.finalTransform.position);
                                        mlauncher.multiLauncher.turret.slaved = true;
                                        mlauncher.multiLauncher.turret.slavedTargetPosition = MissileGuidance.GetAirToAirFireSolution(mlauncher, heatTarget.predictedPosition, heatTarget.velocity);
                                        mlauncher.multiLauncher.turret.SlavedAim();
                                        yield return wait;
                                    }
                                }
                                if (mlauncher.multiLauncher && !mlauncher.multiLauncher.turret)
                                    yield return new WaitForSecondsFixed(mlauncher.multiLauncher.deploySpeed);
                            }

                            yield return wait;

                            if (heatTarget.exists && heatTarget.signalStrength * ((BDArmorySettings.ASPECTED_IR_SEEKERS && Vector3.Dot(guardTarget.vesselTransform.up, ml.transform.forward) > 0.25f) ? ml.frontAspectHeatModifier : 1) < ml.heatThreshold)
                            {
                                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire]: Heatseeker heat threashold not met, aborting launch attempt.");
                                break; //in case rearAspect missile doesn't have a heatTarget, then GMR creates a heatTarget via radar lock in the 'if (targetVessel && !heatTarget.exists && vesselRadarData)' codeblock above
                            }
                            // if (guardTarget && ml && heatTarget.exists && (!AIMightDirectFire() || GetLaunchAuthorization(guardTarget, this)))
                            if (targetVessel && ml && heatTarget.exists && heatTarget.vessel == targetVessel && GetLaunchAuthorization(targetVessel, this, ml))
                            {
                                if (BDArmorySettings.DEBUG_MISSILES)
                                {
                                    Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} firing on target {targetVessel.GetName()}");
                                }

                                FireCurrentMissile(ml, true);
                                //StartCoroutine(MissileAwayRoutine(mlauncher));
                            }
                            //else //event that heatTarget.exists && heatTarget != guardtarget?
                            break;
                        }
                    case MissileBase.TargetingModes.Gps:
                        {
                            if (SetCargoBays())
                            {
                                yield return new WaitForSecondsFixed(2f);
                                if (vessel == null || targetVessel == null) break;
                            }
                            //have GPS missiles require a targeting cam for coords? GPS bombs require one.
                            float attemptStartTime;
                            bool foundTargetInDatabase = false;
                            using (List<GPSTargetInfo>.Enumerator gps = BDATargetManager.GPSTargetList(Team).GetEnumerator())
                                while (gps.MoveNext())
                                {
                                    if ((gps.Current.worldPos - guardTarget.CoM).sqrMagnitude > 100) continue;
                                    designatedGPSInfo = gps.Current;
                                    foundTargetInDatabase = true;
                                    break;
                                }
                            if (!foundTargetInDatabase)
                            {
                                if (targetingPods.Count > 0) //if targeting pods are available, slew them onto target and lock.
                                {
                                    using (List<ModuleTargetingCamera>.Enumerator tgp = targetingPods.GetEnumerator())
                                        while (tgp.MoveNext())
                                        {
                                            if (tgp.Current == null) continue;
                                            if (tgp.Current.maxRayDistance * tgp.Current.maxRayDistance < (tgp.Current.cameraParentTransform.position - targetVessel.CoM).sqrMagnitude) continue; //target further than max camera range (def ~15.5km)
                                            tgp.Current.EnableCamera();
                                            tgp.Current.CoMLock = true;
                                            yield return StartCoroutine(tgp.Current.PointToPositionRoutine(targetVessel.CoM, targetVessel));
                                        }

                                    //search for a laser point that corresponds with target vessel
                                    attemptStartTime = Time.time;
                                    float attemptDuration = targetScanInterval * 0.75f;
                                    while (Time.time - attemptStartTime < attemptDuration && (!laserPointDetected || (foundCam && (foundCam.groundTargetPosition - targetVessel.CoM).sqrMagnitude > Mathf.Max(400, 0.013f * (float)guardTarget.srfSpeed * (float)guardTarget.srfSpeed))))
                                    {
                                        yield return wait;
                                    }
                                    designatedGPSInfo = new GPSTargetInfo(VectorUtils.WorldPositionToGeoCoords(foundCam.groundTargetPosition, vessel.mainBody), targetVessel.vesselName.Substring(0, Mathf.Min(12, targetVessel.vesselName.Length)));
                                }
                                else //no cam, get ranging from radar lock? Limit to aerial targets only to not obsolete the tgtCam? Or see if the speed improvements to camera tracking speed permit cams to now be able to track planes
                                {
                                    // unguidedWeapon = true; //so let them be used as unguided ordinance
                                    //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire]: No targeting cam! Available cams: {targetingPods.Count}; switching to unguided firing");
                                    //break;

                                    //comment out section below, and uncomment above, if we don't want radar locks to provide GPS ranging/coord data
                                    float attemptLockTime = Time.time;
                                    while (ml && vesselRadarData && (!vesselRadarData.locked || (vesselRadarData.lockedTargetData.vessel != targetVessel)) && Time.time - attemptLockTime < 2)
                                    {
                                        if (vesselRadarData.locked)
                                        {
                                            vesselRadarData.SwitchActiveLockedTarget(targetVessel);
                                            yield return wait;
                                        }
                                        else
                                        {
                                            vesselRadarData.TryLockTarget(targetVessel);
                                        }
                                        yield return new WaitForSecondsFixed(tryLockTime);
                                    }
                                    if (vesselRadarData && vesselRadarData.locked && vesselRadarData.lockedTargetData.vessel == targetVessel) //no GPS coords, missile is now expensive rocket
                                    {
                                        designatedGPSInfo = new GPSTargetInfo(VectorUtils.WorldPositionToGeoCoords(targetVessel.CoM, vessel.mainBody), targetVessel.vesselName.Substring(0, Mathf.Min(12, targetVessel.vesselName.Length)));
                                    }
                                    else
                                    {
                                        dumbfiring = true; //so let them be used as unguided ordinance
                                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire]: No Laser target! Available cams: {targetingPods.Count}; switching to unguided firing");
                                        break;
                                    }
                                }
                            }
                            attemptStartTime = Time.time;
                            MissileLauncher mlauncher;
                            mlauncher = ml as MissileLauncher;
                            if (mlauncher)
                            {
                                float angle = 999;
                                float turretStartTime = attemptStartTime;

                                if (mlauncher.missileTurret)
                                {
                                    while (Time.time - turretStartTime < Mathf.Max(targetScanInterval / 2f, 2) && targetVessel && mlauncher && angle > mlauncher.missileTurret.fireFOV)
                                    {
                                        angle = Vector3.Angle(mlauncher.missileTurret.finalTransform.forward, mlauncher.missileTurret.slavedTargetPosition - mlauncher.missileTurret.finalTransform.position);
                                        mlauncher.missileTurret.slaved = true;
                                        mlauncher.missileTurret.slavedTargetPosition = MissileGuidance.GetAirToAirFireSolution(mlauncher, designatedGPSInfo.worldPos, designatedGPSInfo.gpsVessel.Velocity());
                                        mlauncher.missileTurret.SlavedAim();
                                        yield return wait;
                                    }
                                }
                                if (mlauncher.multiLauncher && mlauncher.multiLauncher.turret)
                                {
                                    while (Time.time - turretStartTime < Mathf.Max(targetScanInterval / 2f, 2) && targetVessel && mlauncher && angle > mlauncher.multiLauncher.turret.fireFOV)
                                    {
                                        angle = Vector3.Angle(mlauncher.multiLauncher.turret.finalTransform.forward, mlauncher.multiLauncher.turret.slavedTargetPosition - mlauncher.multiLauncher.turret.finalTransform.position);
                                        mlauncher.multiLauncher.turret.slaved = true;
                                        mlauncher.multiLauncher.turret.slavedTargetPosition = MissileGuidance.GetAirToAirFireSolution(mlauncher, designatedGPSInfo.worldPos, designatedGPSInfo.gpsVessel.Velocity());
                                        mlauncher.multiLauncher.turret.SlavedAim();
                                        yield return wait;
                                    }
                                }
                                if (mlauncher.multiLauncher && !mlauncher.multiLauncher.turret)
                                    yield return new WaitForSecondsFixed(mlauncher.multiLauncher.deploySpeed);
                            }
                            yield return wait;
                            if (vessel && targetVessel)
                                designatedGPSInfo = new GPSTargetInfo(VectorUtils.WorldPositionToGeoCoords(targetVessel.CoM, vessel.mainBody), targetVessel.vesselName.Substring(0, Mathf.Min(12, targetVessel.vesselName.Length)));

                            if (BDArmorySettings.DEBUG_MISSILES)
                            {
                                Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} firing GPS missile at {designatedGPSInfo.worldPos}");
                            }
                            FireCurrentMissile(ml, true);
                            //if (FireCurrentMissile(true))
                            //    StartCoroutine(MissileAwayRoutine(ml)); //NEW: try to prevent launching all missile complements at once...
                            break;
                        }
                    case MissileBase.TargetingModes.AntiRad:
                        {
                            if (rwr)
                            {
                                if (!rwr.rwrEnabled) rwr.EnableRWR();
                                if (rwr.rwrEnabled && !rwr.displayRWR) rwr.displayRWR = true;
                            }

                            if (SetCargoBays())
                            {
                                yield return new WaitForSecondsFixed(2f);
                            }

                            float attemptStartTime = Time.time;
                            float attemptDuration = targetScanInterval * 0.75f;
                            while (Time.time - attemptStartTime < attemptDuration && (!antiRadTargetAcquired || !AntiRadDistanceCheck()))
                                yield return wait;

                            attemptStartTime = Time.time;
                            MissileLauncher mlauncher = ml as MissileLauncher;
                            if (targetVessel && mlauncher)
                            {
                                float angle = 999;
                                float turretStartTime = attemptStartTime;
                                if (mlauncher.missileTurret && antiRadTargetAcquired)
                                {
                                    while (antiRadTargetAcquired && Time.time - turretStartTime < Mathf.Max(targetScanInterval / 2f, 2) && targetVessel && mlauncher && antiRadTargetAcquired && angle > mlauncher.missileTurret.fireFOV)
                                    {
                                        angle = Vector3.Angle(mlauncher.missileTurret.finalTransform.forward, mlauncher.missileTurret.slavedTargetPosition - mlauncher.missileTurret.finalTransform.position);
                                        mlauncher.missileTurret.slaved = true;
                                        mlauncher.missileTurret.slavedTargetPosition = MissileGuidance.GetAirToAirFireSolution(mlauncher, antiRadiationTarget, targetVessel.Velocity());
                                        mlauncher.missileTurret.SlavedAim();
                                        yield return wait;
                                    }
                                }
                                if (mlauncher.multiLauncher && mlauncher.multiLauncher.turret && antiRadTargetAcquired)
                                {
                                    while (antiRadTargetAcquired && Time.time - turretStartTime < Mathf.Max(targetScanInterval / 2f, 2) && targetVessel && mlauncher && antiRadTargetAcquired && angle > mlauncher.multiLauncher.turret.fireFOV)
                                    {
                                        angle = Vector3.Angle(mlauncher.multiLauncher.turret.finalTransform.forward, mlauncher.multiLauncher.turret.slavedTargetPosition - mlauncher.multiLauncher.turret.finalTransform.position);
                                        mlauncher.multiLauncher.turret.slaved = true;
                                        mlauncher.multiLauncher.turret.slavedTargetPosition = MissileGuidance.GetAirToAirFireSolution(mlauncher, antiRadiationTarget, targetVessel.Velocity());
                                        mlauncher.multiLauncher.turret.SlavedAim();
                                        yield return wait;
                                    }
                                }
                                if (mlauncher.multiLauncher && !mlauncher.multiLauncher.turret)
                                    yield return new WaitForSecondsFixed(mlauncher.multiLauncher.deploySpeed);
                            }

                            yield return wait;
                            if (ml && antiRadTargetAcquired && AntiRadDistanceCheck())
                            {
                                FireCurrentMissile(ml, true);
                                //StartCoroutine(MissileAwayRoutine(ml));
                            }
                            break;
                        }
                    case MissileBase.TargetingModes.Laser:
                        {
                            if (SetCargoBays())
                            {
                                yield return new WaitForSecondsFixed(2f);
                            }

                            if (targetingPods.Count > 0) //if targeting pods are available, slew them onto target and lock.
                            {
                                using (List<ModuleTargetingCamera>.Enumerator tgp = targetingPods.GetEnumerator())
                                    while (tgp.MoveNext())
                                    {
                                        if (tgp.Current == null) continue;
                                        if (tgp.Current.maxRayDistance * tgp.Current.maxRayDistance < (tgp.Current.cameraParentTransform.position - targetVessel.CoM).sqrMagnitude) continue; //target further than max camera range (def ~15.5km)
                                        tgp.Current.CoMLock = true;
                                        yield return StartCoroutine(tgp.Current.PointToPositionRoutine(targetVessel.CoM, targetVessel));
                                        //if (tgp.Current.groundStabilized && (tgp.Current.GroundtargetPosition - guardTarget.transform.position).sqrMagnitude < 20 * 20) 
                                        //if ((tgp.Current.groundTargetPosition - guardTarget.transform.position).sqrMagnitude < 10 * 10) 
                                        //{
                                        //    tgp.Current.CoMLock = true; // make the designator continue to paint target
                                        //    break;
                                        //}
                                    }
                            }
                            else //no cam, laser missiles now expensive unguided ordinance
                            {
                                dumbfiring = true; //so let them be used as unguided ordinance
                                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire]: No Laser target! Available cams: {targetingPods.Count}; switching to unguided firing");
                                break;
                            }
                            //search for a laser point that corresponds with target vessel
                            float attemptStartTime = Time.time;
                            float attemptDuration = targetScanInterval * 0.75f;
                            while (Time.time - attemptStartTime < attemptDuration && (!laserPointDetected || (foundCam && (foundCam.groundTargetPosition - targetVessel.CoM).sqrMagnitude > 100)))
                            {
                                yield return wait;
                            }
                            MissileLauncher mlauncher = ml as MissileLauncher;
                            if (targetVessel && mlauncher && foundCam)
                            {
                                float angle = 999;
                                float turretStartTime = attemptStartTime;
                                if (mlauncher.missileTurret)
                                {
                                    while (Time.time - turretStartTime < Mathf.Max(targetScanInterval / 2f, 2) && mlauncher && mlauncher.isActiveAndEnabled && foundCam && angle > mlauncher.missileTurret.fireFOV)
                                    {
                                        angle = Vector3.Angle(mlauncher.missileTurret.finalTransform.forward, mlauncher.missileTurret.slavedTargetPosition - mlauncher.missileTurret.finalTransform.position);
                                        mlauncher.missileTurret.slaved = true;
                                        mlauncher.missileTurret.slavedTargetPosition = foundCam.groundTargetPosition;
                                        mlauncher.missileTurret.SlavedAim();
                                        yield return wait;
                                    }
                                }
                                if (mlauncher.multiLauncher && mlauncher.multiLauncher.turret)
                                {
                                    while (Time.time - turretStartTime < Mathf.Max(targetScanInterval / 2f, 2) && mlauncher && mlauncher.isActiveAndEnabled && foundCam && angle > mlauncher.multiLauncher.turret.fireFOV)
                                    {
                                        angle = Vector3.Angle(mlauncher.multiLauncher.turret.finalTransform.forward, mlauncher.multiLauncher.turret.slavedTargetPosition - mlauncher.multiLauncher.turret.finalTransform.position);
                                        mlauncher.multiLauncher.turret.slaved = true;
                                        mlauncher.multiLauncher.turret.slavedTargetPosition = foundCam.groundTargetPosition;
                                        mlauncher.multiLauncher.turret.SlavedAim();
                                        yield return wait;
                                    }
                                }
                                if (mlauncher.multiLauncher && !mlauncher.multiLauncher.turret)
                                    yield return new WaitForSecondsFixed(mlauncher.multiLauncher.deploySpeed);
                            }
                            yield return wait;
                            //Debug.Log($"[GMR_Debug] waiting... laspoint: {laserPointDetected}; foundCam: {foundCam != null}; targetVessel: {targetVessel != null}");
                            if (ml && laserPointDetected && foundCam && (foundCam.groundTargetPosition - targetVessel.CoM).sqrMagnitude < Mathf.Max(100, 0.013f * (float)targetVessel.srfSpeed * (float)targetVessel.srfSpeed))
                            {
                                FireCurrentMissile(ml, true);
                                //StartCoroutine(MissileAwayRoutine(ml));
                            }
                            else
                            {
                                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire]: Laser Target Error: laserdot:{laserPointDetected}, cam:{(foundCam != null ? foundCam : "null")}, pointingatTgt:{(foundCam != null ? (foundCam.groundTargetPosition - targetVessel.CoM).sqrMagnitude < Mathf.Max(100, 0.013f * (float)targetVessel.srfSpeed * (float)targetVessel.srfSpeed) : "null")}");
                                //remember to check if the values you're debugging actually exist before referencing them...
                            }
                            break;
                        }
                    case MissileBase.TargetingModes.Inertial:
                        {
                            TargetSignatureData INSTarget = TargetSignatureData.noTarget;
                            if (vesselRadarData)
                            {
                                float BayTriggerTime = -1;
                                if (SetCargoBays())
                                {
                                    BayTriggerTime = Time.time;
                                    //yield return new WaitForSecondsFixed(2f);
                                    if (vessel == null || targetVessel == null) break;
                                }
                                if (ml.GetWeaponClass() == WeaponClasses.SLW)
                                {
                                    if (_sonarsEnabled)
                                        INSTarget = vesselRadarData.detectedRadarTarget(targetVessel, this); //detected by radar scan?
                                }
                                else
                                {
                                    if (_radarsEnabled)
                                        INSTarget = vesselRadarData.detectedRadarTarget(targetVessel, this); //detected by radar scan?
                                    if (!INSTarget.exists && _irstsEnabled)
                                        INSTarget = vesselRadarData.activeIRTarget(null, this); //how about IRST?
                                }

                                float attemptStartTime = Time.time;
                                float attemptLockTime = Time.time;
                                while (ml && vesselRadarData && (!vesselRadarData.locked || (vesselRadarData.lockedTargetData.vessel != targetVessel)) && Time.time - attemptLockTime < 2f)
                                {
                                    if (vesselRadarData.locked) //we got radar, can we get a lock for better datalink update rate?
                                    {
                                        vesselRadarData.SwitchActiveLockedTarget(targetVessel);
                                        yield return wait;
                                    }
                                    else
                                    {
                                        vesselRadarData.TryLockTarget(targetVessel);
                                    }
                                    yield return new WaitForSecondsFixed(tryLockTime);
                                }
                                if (vessel && INSTarget.exists && INSTarget.vessel == targetVessel)
                                {
                                    designatedINSCoords = VectorUtils.WorldPositionToGeoCoords(MissileGuidance.GetAirToAirFireSolution(ml, targetVessel.CoM, targetVessel.Velocity()), targetVessel.mainBody);
                                }
                                else
                                {
                                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire]: No radar or IRST target! Switching to unguided firing.");
                                    dumbfiring = true; //so let them be used as unguided ordinance
                                    break;
                                }
                                MissileLauncher mlauncher = ml as MissileLauncher;
                                if (mlauncher)
                                {
                                    float angle = 999;
                                    float turretStartTime = attemptStartTime;
                                    if (mlauncher.missileTurret)
                                    {
                                        while (Time.time - turretStartTime < Mathf.Max(targetScanInterval / 2f, 2) && mlauncher && targetVessel && angle > mlauncher.missileTurret.fireFOV)
                                        {
                                            angle = Vector3.Angle(mlauncher.missileTurret.finalTransform.forward, mlauncher.missileTurret.slavedTargetPosition - mlauncher.missileTurret.finalTransform.position);
                                            mlauncher.missileTurret.slaved = true;
                                            mlauncher.missileTurret.slavedTargetPosition = MissileGuidance.GetAirToAirFireSolution(mlauncher, targetVessel.CoM, targetVessel.Velocity());
                                            mlauncher.missileTurret.SlavedAim();
                                            yield return wait;
                                        }
                                    }
                                    if (mlauncher.multiLauncher && mlauncher.multiLauncher.turret)
                                    {
                                        while (Time.time - turretStartTime < Mathf.Max(targetScanInterval / 2f, 2) && mlauncher && targetVessel && angle > mlauncher.multiLauncher.turret.fireFOV)
                                        {
                                            angle = Vector3.Angle(mlauncher.multiLauncher.turret.finalTransform.forward, mlauncher.multiLauncher.turret.slavedTargetPosition - mlauncher.multiLauncher.turret.finalTransform.position);
                                            mlauncher.multiLauncher.turret.slaved = true;
                                            mlauncher.multiLauncher.turret.slavedTargetPosition = MissileGuidance.GetAirToAirFireSolution(mlauncher, targetVessel.CoM, targetVessel.Velocity());
                                            mlauncher.multiLauncher.turret.SlavedAim();
                                            yield return wait;
                                        }
                                    }
                                    if (mlauncher.multiLauncher && !mlauncher.multiLauncher.turret)
                                        yield return new WaitForSecondsFixed(mlauncher.multiLauncher.deploySpeed);
                                }
                                yield return wait;

                                if (vessel && ml && targetVessel)
                                {
                                    if (BayTriggerTime > 0 && (Time.time - BayTriggerTime < 2)) //if bays opening, see if 2 sec for the bays to open have elapsed, if not, wait remaining time needed
                                    {
                                        yield return new WaitForSecondsFixed(2 - (Time.time - BayTriggerTime));
                                    }
                                    if (INSTarget.exists && GetLaunchAuthorization(targetVessel, this, ml))
                                    {
                                        designatedINSCoords = VectorUtils.WorldPositionToGeoCoords(MissileGuidance.GetAirToAirFireSolution(ml, INSTarget.vessel.CoM, INSTarget.vessel.Velocity()), INSTarget.vessel.mainBody);

                                        FireCurrentMissile(ml, true);
                                        //StartCoroutine(MissileAwayRoutine(ml));
                                        break;
                                    }
                                    else
                                    {
                                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire]: No radar or IRST target! Switching to unguided firing.");
                                        dumbfiring = true; //so let them be used as unguided ordinance

                                    }
                                }
                            }
                            else
                            {
                                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire]: No radar or IRST target! Switching to unguided firing.");
                                dumbfiring = true; //so let them be used as unguided ordinance
                            }
                            break;
                        }
                    case MissileBase.TargetingModes.None:
                        {
                            dumbfiring = true;
                            break;
                        }
                }
                if (dumbfiring) //unguidedWeapon
                {
                    MissileLauncher mlauncher = ml as MissileLauncher;
                    if (mlauncher && mlauncher.multiLauncher && mlauncher.multiLauncher.overrideReferenceTransform)
                    {
                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName}'s {(CurrentMissile ? CurrentMissile.name : "null missile")} launched from MML, aborting unguided launch.");
                    }
                    else
                    {
                        if ((ml.TargetingMode == MissileBase.TargetingModes.None) || ((targetVessel.CoM - ml.vessel.CoM).sqrMagnitude < 0.01f * ml.maxStaticLaunchRange * ml.maxStaticLaunchRange))
                        {
                            if (SetCargoBays())
                            {
                                yield return new WaitForSecondsFixed(2f);
                            }
                            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} attempting to fire unguided missile on target {targetVessel.GetName()}");

                            float attemptStartTime = Time.time;
                            if (targetVessel && mlauncher)
                            {
                                float angle = 999;
                                float turretStartTime = attemptStartTime;
                                float dumbfireFOV = 1f; // Match firing conditions for dumbfired weapons in GetLaunchAuthorization
                                if (mlauncher.missileTurret)
                                {
                                    while (Time.time - turretStartTime < Mathf.Max(targetScanInterval / 2f, 2) && mlauncher && targetVessel && angle > dumbfireFOV)
                                    {
                                        angle = Vector3.Angle(mlauncher.missileTurret.finalTransform.forward, mlauncher.missileTurret.slavedTargetPosition - mlauncher.missileTurret.finalTransform.position);
                                        mlauncher.missileTurret.slaved = true;
                                        mlauncher.missileTurret.slavedTargetPosition = MissileGuidance.GetAirToAirFireSolution(ml, targetVessel);
                                        mlauncher.missileTurret.SlavedAim();
                                        yield return wait;
                                    }
                                }
                                if (mlauncher.multiLauncher && mlauncher.multiLauncher.turret)
                                {
                                    while (Time.time - turretStartTime < Mathf.Max(targetScanInterval / 2f, 2) && mlauncher && targetVessel && angle > dumbfireFOV)
                                    {
                                        angle = Vector3.Angle(mlauncher.multiLauncher.turret.finalTransform.forward, mlauncher.multiLauncher.turret.slavedTargetPosition - mlauncher.multiLauncher.turret.finalTransform.position);
                                        mlauncher.multiLauncher.turret.slaved = true;
                                        mlauncher.multiLauncher.turret.slavedTargetPosition = MissileGuidance.GetAirToAirFireSolution(ml, targetVessel);
                                        mlauncher.multiLauncher.turret.SlavedAim();
                                        yield return wait;
                                    }
                                }
                            }
                            yield return wait;
                            if (ml && targetVessel && GetLaunchAuthorization(targetVessel, this, ml))
                            {
                                FireCurrentMissile(ml, true);
                            }
                        }
                    }
                    dumbfiring = false;
                }
                guardFiringMissile = false;
            }
        }
        IEnumerator GuardBombRoutine()
        {
            guardFiringMissile = true;
            float bombStartTime = Time.time;
            float bombAttemptDuration = Mathf.Max(targetScanInterval, 12f);
            float radius = CurrentMissile.GetBlastRadius() * Mathf.Max(0.68f * CurrentMissile.clusterbomb, 1f) * Mathf.Min(0.68f + 1.4f * (maxMissilesOnTarget - 1f), 1.5f);
            radius = Mathf.Min(radius, 150f);
            float targetToleranceSqr = Mathf.Max(100, 0.013f * (float)guardTarget.srfSpeed * (float)guardTarget.srfSpeed);

            bool doProxyCheck = true;

            float prevDist = 2 * radius;
            var wait = new WaitForFixedUpdate();

            while (guardTarget && Time.time - bombStartTime < bombAttemptDuration && weaponIndex > 0 &&
                 weaponArray[weaponIndex].GetWeaponClass() == WeaponClasses.Bomb && firedMissiles < maxMissilesOnTarget)
            {
                Vector3 leadTarget = Vector3.zero;
                if (bombFlightTime > 0)
                {
                    leadTarget = AIUtils.PredictPosition(guardTarget, bombFlightTime);//lead moving ground target to properly line up bombing run; bombs fire solution already plotted in missileFire, torps more or less hit top speed instantly, so simplified fire solution can be used
                }
                float targetDist = Vector3.Distance(bombAimerPosition, leadTarget);
                if (targetDist < (radius * 20f))
                {
                    if (SetCargoBays())
                        yield return new WaitForSecondsFixed(2f);
                    MissileLauncher mlauncher = CurrentMissile as MissileLauncher;
                    if (mlauncher.multiLauncher && !mlauncher.multiLauncher.turret)
                        yield return new WaitForSecondsFixed(mlauncher.multiLauncher.deploySpeed);
                }
                if ((CurrentMissile.TargetingMode == MissileBase.TargetingModes.Gps && (designatedGPSInfo.worldPos - guardTarget.CoM).sqrMagnitude > targetToleranceSqr) //Was blastRadius, but these are precision guided munitions. Let's use a little precision here
               || (CurrentMissile.TargetingMode == MissileBase.TargetingModes.Laser && (!laserPointDetected || (foundCam && (foundCam.groundTargetPosition - guardTarget.CoM).sqrMagnitude > targetToleranceSqr))))
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
                                    yield return StartCoroutine(tgp.Current.PointToPositionRoutine(guardTarget.CoM, guardTarget));
                                }
                        }
                        float attemptStartTime = Time.time;
                        float attemptDuration = targetScanInterval * 0.75f;
                        while (Time.time - attemptStartTime < attemptDuration && (!laserPointDetected || (foundCam && (foundCam.groundTargetPosition - guardTarget.CoM).sqrMagnitude > targetToleranceSqr)))
                        {
                            yield return new WaitForFixedUpdate();
                        }

                        if (guardTarget && (foundCam && (foundCam.groundTargetPosition - guardTarget.transform.position).sqrMagnitude <= targetToleranceSqr))
                        {
                            radius = 500;
                        }
                        else //no coords, treat as standard unguided bomb
                        {
                            if (foundCam) foundCam.DisableCamera();
                            //designatedGPSInfo = new GPSTargetInfo();
                        }
                    }
                }

                if (targetDist > radius
                    || Vector3.Dot(vessel.up, vessel.transform.forward) > 0) // roll check
                {
                    if (targetDist < Mathf.Max(radius * 2, 800f) &&
                        Vector3.Dot(guardTarget.CoM - bombAimerPosition, guardTarget.CoM - transform.position) < 0)
                    {
                        pilotAI.RequestExtend("too close to bomb", guardTarget, ignoreCooldown: true); // Extend from target vessel.
                        break;
                    }
                    yield return wait;
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
                        if (guardTarget && (foundCam && (foundCam.groundTargetPosition - guardTarget.transform.position).sqrMagnitude <= targetToleranceSqr)) //was tgp.groundtargetposition
                        {
                            designatedGPSInfo = new GPSTargetInfo(foundCam.bodyRelativeGTP, "Guard Target");
                        }
                        FireCurrentMissile(CurrentMissile, true);
                        timeBombReleased = Time.time;
                        yield return new WaitForSecondsFixed(rippleFire ? 60f / rippleRPM : 0.06f);
                        if (firedMissiles >= maxMissilesOnTarget)
                        {
                            yield return new WaitForSecondsFixed(1f);
                            if (pilotAI)
                            {
                                pilotAI.RequestExtend("bombs away!", null, radius, guardTarget.CoM, ignoreCooldown: true); // Extend from the place the bomb is expected to fall.
                            }   //maybe something similar should be adapted for any missiles with nuke warheards...?
                        }
                    }
                    else
                    {
                        yield return wait;
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
                if (ml == null)
                {
                    if (targetingAudioSource.isPlaying)
                    {
                        targetingAudioSource.Stop();
                    }
                    return;
                }
                if (ml.TargetingMode == MissileBase.TargetingModes.Heat)
                {
                    if (targetingAudioSource.clip != heatGrowlSound)
                    {
                        targetingAudioSource.clip = heatGrowlSound;
                    }

                    if (heatTarget.exists && CurrentMissile && CurrentMissile.heatThreshold < heatTarget.signalStrength)
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
            bool detectedLaunch = false;
            if (rwr && (rwr.omniDetection || (!rwr.omniDetection && ml.TargetingMode == MissileBase.TargetingModes.Radar && ml.ActiveRadar) || _irstsEnabled)) //omni RWR detection, radar spike from lock, or IR spike from launch
                detectedLaunch = true;

            if (distance < (detectedLaunch ? this.guardRange : this.guardRange / 3))
            {
                warningSounding = true;
                BDArmorySetup.Instance.missileWarningTime = Time.time;
                BDArmorySetup.Instance.missileWarning = true;
                warningAudioSource.pitch = distance < 800 ? 1.45f : 1f;
                warningAudioSource.PlayOneShot(warningSound);

                float waitTime = distance < 800 ? .25f : 1.5f;

                yield return new WaitForSecondsFixed(waitTime);

                if (ml && ml.vessel && CanSeeTarget(ml))
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
        public bool isSmoking;
        public bool isDecoying;
        public bool isBubbling;
        public bool isECMJamming;
        public bool isCloaking;

        bool isLegacyCMing;

        int cmCounter;
        int cmAmount = 5;

        public void FireAllCountermeasures(int count)
        {
            if (!isChaffing && !isFlaring && !isSmoking && ThreatClosingTime(incomingMissileVessel) > cmThreshold)
            {
                StartCoroutine(AllCMRoutine(count));
            }
        }

        public void FireECM(float duration)
        {
            if (!isECMJamming)
            {
                StartCoroutine(ECMRoutine(duration));
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

        public void FireSmoke()
        {
            if (!isSmoking && ThreatClosingTime(incomingMissileVessel) <= cmThreshold)
            {
                StartCoroutine(SmokeRoutine((int)smokeRepetition, smokeInterval));
            }
        }

        public void FireDecoys()
        {
            if (!isDecoying && ThreatClosingTime(incomingMissileVessel) <= cmThreshold * 5) //because torpedoes substantially slower than missiles; 5s before impact for something moving 50m/s is 250m away
            {
                StartCoroutine(DecoyRoutine((int)cmRepetition, cmInterval));
            }
        }

        public void FireBubbles()
        {
            if (!isBubbling && ThreatClosingTime(incomingMissileVessel) <= cmThreshold * 5)
            {
                StartCoroutine(BubbleRoutine((int)chaffRepetition, chaffInterval));
            }
        }

        IEnumerator ECMRoutine(float duration)
        {
            isECMJamming = true;
            //yield return new WaitForSecondsFixed(UnityEngine.Random.Range(0.2f, 1f));
            if (duration > 0)
            {
                using (var ecm = VesselModuleRegistry.GetModules<ModuleECMJammer>(vessel).GetEnumerator())
                    while (ecm.MoveNext())
                    {
                        if (ecm.Current == null) continue;
                        if (ecm.Current.manuallyEnabled) continue;
                        if (ecm.Current.jammerEnabled)
                        {
                            ecm.Current.manuallyEnabled = true;
                            continue;
                        }
                        ecm.Current.EnableJammer();
                    }
                yield return new WaitForSecondsFixed(duration);
            }
            isECMJamming = false;

            using (var ecm1 = VesselModuleRegistry.GetModules<ModuleECMJammer>(vessel).GetEnumerator())
                while (ecm1.MoveNext())
                {
                    if (ecm1.Current == null) continue;
                    if (!ecm1.Current.manuallyEnabled)
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
            yield return new WaitForSecondsFixed(10.0f);
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
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} starting chaff routine");
            // yield return new WaitForSecondsFixed(0.2f); // Reaction time delay
            for (int i = 0; i < repetition; ++i)
            {
                DropCM(CMDropper.CountermeasureTypes.Chaff);
                if (i < repetition - 1) // Don't wait on the last one.
                    yield return new WaitForSecondsFixed(interval);
            }
            yield return new WaitForSecondsFixed(chaffWaitTime);
            isChaffing = false;
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} ending chaff routine");
        }

        IEnumerator FlareRoutine(int repetition, float interval)
        {
            isFlaring = true;
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} starting flare routine");
            // yield return new WaitForSecondsFixed(0.2f); // Reaction time delay
            for (int i = 0; i < repetition; ++i)
            {
                DropCM(CMDropper.CountermeasureTypes.Flare);
                if (i < repetition - 1) // Don't wait on the last one.
                    yield return new WaitForSecondsFixed(interval);
            }
            yield return new WaitForSecondsFixed(cmWaitTime);
            isFlaring = false;
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} ending flare routine");
        }
        IEnumerator SmokeRoutine(int repetition, float interval)
        {
            isSmoking = true;
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} starting smoke routine");
            // yield return new WaitForSecondsFixed(0.2f); // Reaction time delay
            for (int i = 0; i < repetition; ++i)
            {
                DropCM(CMDropper.CountermeasureTypes.Smoke);
                if (i < repetition - 1) // Don't wait on the last one.
                    yield return new WaitForSecondsFixed(interval);
            }
            yield return new WaitForSecondsFixed(smokeWaitTime);
            isSmoking = false;
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} ending smoke routine");
        }
        IEnumerator DecoyRoutine(int repetition, float interval)
        {
            isDecoying = true;
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} starting decoy routine");
            // yield return new WaitForSecondsFixed(0.2f); // Reaction time delay
            for (int i = 0; i < repetition; ++i)
            {
                DropCM(CMDropper.CountermeasureTypes.Decoy);
                if (i < repetition - 1) // Don't wait on the last one.
                    yield return new WaitForSecondsFixed(interval);
            }
            yield return new WaitForSecondsFixed(cmWaitTime);
            isDecoying = false;
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} ending decoy routine");
        }
        IEnumerator BubbleRoutine(int repetition, float interval)
        {
            isBubbling = true;
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} starting bubblescreen routine");
            // yield return new WaitForSecondsFixed(0.2f); // Reaction time delay
            for (int i = 0; i < repetition; ++i)
            {
                DropCM(CMDropper.CountermeasureTypes.Bubbles);
                if (i < repetition - 1) // Don't wait on the last one.
                    yield return new WaitForSecondsFixed(interval);
            }
            yield return new WaitForSecondsFixed(cmWaitTime);
            isBubbling = false;
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} ending bubble routine");
        }
        IEnumerator AllCMRoutine(int count)
        {
            // Use this routine for missile threats that are outside of the cmThreshold
            isFlaring = true;
            isChaffing = true;
            isSmoking = true;
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} starting All CM routine");
            for (int i = 0; i < count; ++i)
            {
                DropCMs((int)(CMDropper.CountermeasureTypes.Flare | CMDropper.CountermeasureTypes.Chaff | CMDropper.CountermeasureTypes.Smoke));
                if (i < count - 1) // Don't wait on the last one.
                    yield return new WaitForSecondsFixed(1f);
            }
            isFlaring = false;
            isChaffing = false;
            isSmoking = false;
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} ending All CM routine");
        }

        IEnumerator LegacyCMRoutine()
        {
            isLegacyCMing = true;
            yield return new WaitForSecondsFixed(UnityEngine.Random.Range(.2f, 1f));
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
                    yield return new WaitForSecondsFixed(0.15f);
                }
                else
                {
                    cmCounter = 0;
                    yield return new WaitForSecondsFixed(UnityEngine.Random.Range(.5f, 1f));
                }
            }
            isLegacyCMing = false;
        }

        Dictionary<CMDropper.CountermeasureTypes, int> cmCurrentPriorities = new Dictionary<CMDropper.CountermeasureTypes, int>();
        void RefreshCMPriorities()
        {
            cmCurrentPriorities.Clear();
            foreach (var cm in VesselModuleRegistry.GetModules<CMDropper>(vessel))
            {
                if (cm == null) continue;
                if (!cmCurrentPriorities.ContainsKey(cm.cmType) || cm.Priority > cmCurrentPriorities[cm.cmType])
                    cmCurrentPriorities[cm.cmType] = cm.Priority;
            }
            cmPrioritiesNeedRefreshing = false;
        }

        void DropCM(CMDropper.CountermeasureTypes cmType) => DropCMs((int)cmType);
        void DropCMs(int cmTypes)
        {
            if (cmPrioritiesNeedRefreshing) RefreshCMPriorities(); // Refresh highest priorities if needed.
            var cmDropped = cmCurrentPriorities.ToDictionary(kvp => kvp.Key, kvp => false);
            // Drop the appropriate CMs.
            foreach (var cm in VesselModuleRegistry.GetModules<CMDropper>(vessel))
            {
                if (cm == null) continue;
                if (((int)cm.cmType & cmTypes) != 0 && cm.Priority == cmCurrentPriorities[cm.cmType])
                {
                    if (cm.DropCM())
                        cmDropped[cm.cmType] = true;
                }
            }
            // Check for not having dropped any of the current priority.
            foreach (var cmType in cmDropped.Keys)
            {
                if (((int)cmType & cmTypes) == 0) continue; // This type wasn't requested.
                if (cmDropped[cmType]) continue; // Successfully dropped something of this type.
                if (cmCurrentPriorities[cmType] > -1)
                {
                    --cmCurrentPriorities[cmType]; // Lower the priority.
                    if (cmCurrentPriorities[cmType] > -1) // Still some left?
                        DropCMs((int)cmType); // Fire some of the next priority.
                }
            }
        }

        [KSPAction("#LOC_BDArmory_FireCountermeasure")]//Fire Countermeasure
        public void AGDropCMs(KSPActionParam param)
        { DropCMs((int)(CMDropper.CountermeasureTypes.Flare | CMDropper.CountermeasureTypes.Chaff | CMDropper.CountermeasureTypes.Smoke)); }

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

        bool FireCurrentMissile(MissileBase missile, bool checkClearance)
        {
            if (missile == null) return false;
            bool DisengageAfterFiring = false;
            if (missile is MissileBase)
            {
                MissileBase ml = missile;
                if (checkClearance && (!CheckBombClearance(ml) || (ml is MissileLauncher && ((MissileLauncher)ml).rotaryRail && !((MissileLauncher)ml).rotaryRail.readyMissile == ml) || ml.launched))
                {
                    using (var otherMissile = VesselModuleRegistry.GetModules<MissileBase>(vessel).GetEnumerator())
                        while (otherMissile.MoveNext())
                        {
                            if (otherMissile.Current == null) continue;
                            if (otherMissile.Current == ml || otherMissile.Current.GetShortName() != ml.GetShortName() ||
                                !CheckBombClearance(otherMissile.Current)) continue;
                            if (otherMissile.Current.GetEngagementRangeMax() != selectedWeaponsEngageRangeMax) continue;
                            if (otherMissile.Current.launched) continue;
                            CurrentMissile = otherMissile.Current;
                            selectedWeapon = otherMissile.Current;
                            FireCurrentMissile(otherMissile.Current, false);
                            return true;
                        }
                    CurrentMissile = ml;
                    selectedWeapon = ml;
                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire]: No Clearance! Cannot fire {CurrentMissile.GetShortName()}");
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
                        float timeToImpact = AIUtils.TimeToCPA(guardTarget, vessel.CoM, vessel.Velocity(), (thrust / missile.part.mass) * missile.GetForwardTransform(), 16);
                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire]: Blast standoff dist: {ml.StandOffDistance}; time2Impact: {timeToImpact}");
                        if (ml.StandOffDistance > 0 && (transform.position + (timeToImpact * (Vector3)vessel.Velocity())).CloserToThan(currentTarget.position + (timeToImpact * currentTarget.velocity), ml.StandOffDistance)) //if predicted craft position will be within blast radius when missile arrives, break off
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
                    pilotAI.RequestExtend("Nuke away!", guardTarget, missile.StandOffDistance * 1.25f, guardTarget.CoM, ignoreCooldown: true); // Extend from projected detonation site if within blast radius
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
                FireCurrentMissile(CurrentMissile, true);
            }
            UpdateList();
        }

        /// <summary>
        /// Fire a missile via trigger, action group or hotkey.
        /// </summary>
        void FireMissileManually(bool mainTrigger)
        {
            if (!MapView.MapIsEnabled && !hasSingleFired && ((mainTrigger && triggerTimer > BDArmorySettings.TRIGGER_HOLD_TIME) || !mainTrigger))
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

        #endregion Fire

        #region Weapon Info

        void DisplaySelectedWeaponMessage()
        {
            if (BDArmorySetup.GAME_UI_ENABLED && vessel == FlightGlobals.ActiveVessel)
            {
                ScreenMessages.RemoveMessage(selectionMessage);
                selectionMessage.textInstance = null;

                selectionText = $"Selected Weapon: {(GetWeaponName(weaponArray[weaponIndex])).ToString()}";
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
        float GetWeaponRange(IBDWeapon weapon)
        {
            if (weapon == null)
            {
                return -1;
            }
            else
            {
                return weapon.GetEngageRange();
            }
        }
        public void UpdateList()
        {
            weaponsListNeedsUpdating = false;
            weaponTypes.Clear();
            weaponRanges.Clear();
            // extension for feature_engagementenvelope: also clear engagement specific weapon lists
            weaponTypesAir.Clear();
            weaponTypesMissile.Clear();
            targetMissiles = false;
            weaponTypesGround.Clear();
            weaponTypesSLW.Clear();
            //gunRippleIndex.Clear(); //since there keeps being issues with the more limited ripple dict, lets just make it perisitant for all weapons on the craft
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
                                if (weapon.Current.GetWeaponClass() == WeaponClasses.Missile || weapon.Current.GetWeaponClass() == WeaponClasses.Bomb || weapon.Current.GetWeaponClass() == WeaponClasses.SLW)
                                {
                                    float range = weapon.Current.GetPart().FindModuleImplementing<MissileBase>().engageRangeMax;

                                    if (weaponRanges.TryGetValue(weaponName, out var registeredRanges))
                                    {
                                        if (registeredRanges.Contains(range))
                                            alreadyAdded = true;
                                    }
                                }
                                else
                                    alreadyAdded = true;
                                //break;
                            }
                        }

                    if (weapon.Current.GetWeaponClass() == WeaponClasses.Gun || weapon.Current.GetWeaponClass() == WeaponClasses.Rocket || weapon.Current.GetWeaponClass() == WeaponClasses.DefenseLaser)
                    {
                        if (!gunRippleIndex.ContainsKey(weapon.Current.GetPartName())) //I think the empty rocketpod? contine might have been tripping up the ripple dict and not adding the hydra
                            gunRippleIndex.Add(weapon.Current.GetPartName(), 0);
                        var gun = weapon.Current.GetWeaponModule();
                        //dont add empty rocket pods
                        if ((gun.rocketPod && !gun.externalAmmo) && gun.GetRocketResource().amount < 1 && !BDArmorySettings.INFINITE_AMMO)
                        {
                            continue;
                        }
                        //dont add APS
                        if (gun.isAPS && !gun.dualModeAPS)
                        {
                            continue;
                        }
                    }
                    if (!alreadyAdded)
                    {
                        weaponTypes.Add(weapon.Current);
                        if (weapon.Current.GetWeaponClass() == WeaponClasses.Missile || weapon.Current.GetWeaponClass() == WeaponClasses.Bomb || weapon.Current.GetWeaponClass() == WeaponClasses.SLW)
                        {
                            float range = weapon.Current.GetPart().FindModuleImplementing<MissileBase>().engageRangeMax;

                            if (weaponRanges.TryGetValue(weaponName, out var registeredRanges))
                            {
                                registeredRanges.Add(range);
                            }
                            else
                                weaponRanges.Add(weaponName, new List<float> { range });
                        }
                    }
                    EngageableWeapon engageableWeapon = weapon.Current as EngageableWeapon;

                    if (engageableWeapon != null)
                    {
                        if (engageableWeapon.GetEngageAirTargets()) weaponTypesAir.Add(weapon.Current);
                        if (engageableWeapon.GetEngageMissileTargets())
                        {
                            weaponTypesMissile.Add(weapon.Current);
                            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire] Adding {weapon.Current.GetShortName()}; {weapon.Current.GetPart().persistentId} to engageMissiles list...");
                            targetMissiles = true;
                        }
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
                if (!CurrentMissile || CurrentMissile.GetPartName() != selectedWeapon.GetPartName() || CurrentMissile.engageRangeMax != selectedWeaponsEngageRangeMax)
                {
                    using (var Missile = VesselModuleRegistry.GetModules<MissileBase>(vessel).GetEnumerator())
                        while (Missile.MoveNext())
                        {
                            if (Missile.Current == null) continue;
                            if (Missile.Current.GetPartName() != selectedWeapon.GetPartName()) continue;
                            if (Missile.Current.launched) continue;
                            if (Missile.Current.engageRangeMax != selectedWeaponsEngageRangeMax) continue;
                            CurrentMissile = Missile.Current;
                        }
                    //CurrentMissile = selectedWeapon.GetPart().FindModuleImplementing<MissileBase>();
                }
            }
            else
            {
                CurrentMissile = null;
            }
            //selectedWeapon = weaponArray[weaponIndex];

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
                        weapon.Current.initialFireDelay = 60 / (weapon.Current.roundsPerMinute * rippleGunCount[weapon.Current.WeaponName]);
                        //Debug.Log("[RIPPLEDEBUG]" + weapon.Current.WeaponName + " rippleIndex: " + weapon.Current.rippleIndex + "; initialfiredelay: " + weapon.Current.initialFireDelay);
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

                        if (rotRail.Current.readyMissile.GetPartName() != cm.GetPartName())
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
                        else if (rotRail.Current.nextMissile.GetPartName() != cm.GetPartName())
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
                    if (weaponIndex > 0 && cm)
                    {
                        if (mt.Current.ContainsMissileOfType(cm) && (!mt.Current.activeMissileOnly || cm.missileTurret == mt.Current))
                        {
                            mt.Current.EnableTurret(CurrentMissile);
                        }
                    }
                    else
                    {
                        if (MslTurrets.Contains(mt.Current)) continue;
                        mt.Current.DisableTurret();
                    }
                }
            if (weaponIndex > 0 && cm && cm.multiLauncher && cm.multiLauncher.turret)
            {
                cm.multiLauncher.turret.EnableTurret(CurrentMissile);
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
                    if (weaponIndex > 0 && cm && mt.Current.ContainsMissileOfType(cm) && cm.deployableRail == mt.Current && !cm.launched)
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
                            if (weaponArray[weaponIndex].GetPart() == null || launcher.GetPartName() != weaponArray[weaponIndex].GetPartName()) continue;
                            if (launcher.launched) continue;
                            if (launcher.engageRangeMax != selectedWeaponsEngageRangeMax) continue;
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
                if (weaponArray[weaponIndex].GetPart() != null && missile.GetPartName() == weaponArray[weaponIndex].GetPartName())
                {
                    if (!missile.rotaryRail)
                    {
                        return missile;
                    }
                    if (missile.rotaryRail.readyToFire && missile.rotaryRail.readyMissile == CurrentMissile && !missile.launched)
                    {
                        return missile;
                    }
                }
                using (var ml = VesselModuleRegistry.GetModules<MissileLauncher>(vessel).GetEnumerator())
                    while (ml.MoveNext())
                    {
                        if (ml.Current == null) continue;
                        if (weaponArray[weaponIndex].GetPart() == null || ml.Current.GetPartName() != weaponArray[weaponIndex].GetPartName()) continue;
                        if (ml.Current.launched) continue;
                        if (!ml.Current.rotaryRail)
                        {
                            return ml.Current;
                        }
                        if (ml.Current.rotaryRail.readyMissile == null || ml.Current.rotaryRail.readyMissile.part == null) continue;
                        if (ml.Current.rotaryRail.readyToFire && ml.Current.rotaryRail.readyMissile.GetPartName() == weaponArray[weaponIndex].GetPartName())
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
                Transform referenceTransform = (launcher.multiLauncher != null && launcher.multiLauncher.overrideReferenceTransform) ? launcher.part.FindModelTransform(launcher.multiLauncher.launchTransformName).GetChild(0) : launcher.MissileReferenceTransform;
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
                        ? referenceTransform.forward
                        : -referenceTransform.up) * launcher.decoupleSpeed * time) +
                                        ((FlightGlobals.getGeeForceAtPosition(transform.position) - vessel.acceleration) *
                                         0.5f * time * time);
                    Vector3 crossAxis = Vector3.Cross(direction, referenceTransform.transform.right).normalized;

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
                        new Ray(referenceTransform.position - (radius*crossAxis), direction),
                        new Ray(referenceTransform.position + (radius*crossAxis), direction),
                        new Ray(referenceTransform.position, direction)
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
                            var hitCount = Physics.RaycastNonAlloc(rt.Current, clearanceHits, rayDistance, layerMask);
                            if (hitCount == clearanceHits.Length) // If there's a whole bunch of stuff in the way (unlikely), then we need to increase the size of our hits buffer.
                            {
                                clearanceHits = Physics.RaycastAll(rt.Current, rayDistance, layerMask);
                                hitCount = clearanceHits.Length;
                            }
                            using (var t = clearanceHits.Take(hitCount).GetEnumerator())
                                while (t.MoveNext())
                                {
                                    Part p = t.Current.collider.GetComponentInParent<Part>();

                                    if ((p == null || p == ml.part) && p != null) continue;
                                    if (BDArmorySettings.DEBUG_MISSILES)
                                        Debug.Log($"[BDArmory.MissileFire]: RAYCAST HIT, clearance is FALSE! part={(p != null ? p.name : null)}, collider+{(p != null ? p.collider : null)}");
                                    return false;
                                }
                        }
                    return true;
                }

                { //forward check for no-drop missiles
                    var ray = new Ray(ml.MissileReferenceTransform.position, ml.MissileReferenceTransform.forward);
                    var hitCount = Physics.RaycastNonAlloc(ray, clearanceHits, 50, layerMask);
                    if (hitCount == clearanceHits.Length) // If there's a whole bunch of stuff in the way (unlikely), then we need to increase the size of our hits buffer.
                    {
                        clearanceHits = Physics.RaycastAll(ray, 50, layerMask);
                        hitCount = clearanceHits.Length;
                    }
                    using (var t = clearanceHits.Take(hitCount).GetEnumerator())
                        while (t.MoveNext())
                        {
                            Part p = t.Current.collider.GetComponentInParent<Part>();
                            if ((p == null || p == ml.part) && p != null) continue;
                            if (BDArmorySettings.DEBUG_MISSILES)
                                Debug.Log($"[BDArmory.MissileFire]: RAYCAST HIT, clearance is FALSE! part={(p != null ? p.name : null)}, collider={(p != null ? p.collider : null)}");
                            return false;
                        }
                }
            }
            return true;
        }

        void RefreshModules()
        {
            modulesNeedRefreshing = false;
            cmPrioritiesNeedRefreshing = true;
            VesselModuleRegistry.OnVesselModified(vessel); // Make sure the registry is up-to-date.
            _radars = VesselModuleRegistry.GetModules<ModuleRadar>(vessel);
            if (_radars != null)
            {
                // DISABLE RADARS
                /*
                List<ModuleRadar>.Enumerator rad = _radars.GetEnumerator();
                while (rad.MoveNext())
                {
                    if (rad.Current == null) continue;
                    rad.Current.EnsureVesselRadarData();
                    if (rad.Current.radarEnabled) rad.Current.EnableRadar();
                }
                rad.Dispose();
                */
                MaxradarLocks = 0;
                using (List<ModuleRadar>.Enumerator rd = _radars.GetEnumerator())
                    while (rd.MoveNext())
                    {
                        if (rd.Current != null && rd.Current.canLock)
                        {
                            if (rd.Current.maxLocks > 0) MaxradarLocks += rd.Current.maxLocks;
                        }
                    }
                using (List<ModuleRadar>.Enumerator rd = _radars.GetEnumerator()) //now refresh lock array size with new maxradarLock value
                    while (rd.MoveNext())
                    {
                        if (rd.Current != null && rd.Current.canLock)
                        {
                            rd.Current.RefreshLockArray();
                        }
                    }
            }
            _irsts = VesselModuleRegistry.GetModules<ModuleIRST>(vessel);
            _jammers = VesselModuleRegistry.GetModules<ModuleECMJammer>(vessel);
            _cloaks = VesselModuleRegistry.GetModules<ModuleCloakingDevice>(vessel);
            _targetingPods = VesselModuleRegistry.GetModules<ModuleTargetingCamera>(vessel);
            _wmModules = VesselModuleRegistry.GetModules<IBDWMModule>(vessel);
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
                        if ((transform.position + (Vector3)vessel.Velocity()).CloserToThan(currentTarget.position + currentTarget.velocity, gunRange * 0.75f)) //don't swap away from current target if about to enter gunrange
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
                                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire]: Adding {target.Current.Vessel.GetName()} to exclusion list; length: {targetsAssigned.Count}");
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
                        Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} is engaging an override target with {selectedWeapon}");
                    }
                    overrideTimer = 15f;
                    return;
                }
                else if (BDArmorySettings.DEBUG_AI)
                {
                    Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} is engaging an override target with failed to engage its override target!");
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
                            Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName}  is engaging incoming missile ({potentialTarget.Vessel.GetName()}:{potentialTarget.Vessel.parts[0].persistentId}) with {selectedWeapon}");
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
                            Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} is engaging unengaged missile ({potentialTarget.Vessel.GetName()}:{potentialTarget.Vessel.parts[0].persistentId}) with {selectedWeapon}");
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
                targetDebugText = $" is engaging highest priority target ({(potentialTarget != null ? potentialTarget.Vessel.vesselName : "null")}) with ";
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
                        Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName + targetDebugText + (selectedWeapon != null ? selectedWeapon.GetShortName() : "")}");
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
                    if (!HasWeaponsAndAmmo() && pilotAI != null && pilotAI.allowRamming && !(!pilotAI.allowRammingGroundTargets && potentialTarget.Vessel.LandedOrSplashed))
                    {
                        if (BDArmorySettings.DEBUG_AI)
                        {
                            Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName + targetDebugText} ramming.");
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
                        Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} is engaging the closest target ({potentialTarget.Vessel.vesselName}) with {selectedWeapon.GetShortName()}");
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
                            Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName}  is engaging the least engaged missile ({potentialTarget.Vessel.vesselName}) with {selectedWeapon.GetShortName()}");
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
                            Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} is engaging the closest hostile missile ({potentialTarget.Vessel.vesselName}) with {selectedWeapon.GetShortName()}");
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
                        Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} is engaging a final target with {selectedWeapon.GetShortName()}");
                    }
                    return;
                }

            //no valid targets found
            if (potentialTarget == null || selectedWeapon == null)
            {
                if (BDArmorySettings.DEBUG_AI)
                {
                    Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} is disengaging - no valid weapons - no valid targets");
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
            using (List<TargetInfo>.Enumerator secTgt = targetsAssigned.GetEnumerator())
                while (secTgt.MoveNext())
                {
                    if (secTgt.Current == null) continue;
                    if (secTgt.Current == currentTarget) continue;
                    secTgt.Current.Disengage(this);
                }
            targetsAssigned.Clear();
            using (List<TargetInfo>.Enumerator mslTgt = missilesAssigned.GetEnumerator())
                while (mslTgt.MoveNext())
                {
                    if (mslTgt.Current == null) continue;
                    if (mslTgt.Current == currentTarget) continue;
                    mslTgt.Current.Disengage(this);
                }
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
            if (targetMissiles)
            {
                for (int i = 0; i < Math.Max(multiTargetNum, multiMissileTgtNum) - 1; i++)
                {
                    TargetInfo potentialMissileTarget = null;
                    //=========MISSILES=============
                    //prioritize incoming missiles
                    potentialMissileTarget = BDATargetManager.GetMissileTarget(this, true);
                    if (potentialMissileTarget)
                    {
                        missilesAssigned.Add(potentialMissileTarget);
                        targetsTried.Add(potentialMissileTarget);
                        if (BDArmorySettings.DEBUG_AI)
                            Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} targeting missile {potentialMissileTarget.Vessel.GetName()}:{potentialMissileTarget.Vessel.parts[0].persistentId} as a secondary target");
                    }
                    //then provide point defense umbrella
                    potentialMissileTarget = BDATargetManager.GetClosestMissileTarget(this);
                    if (potentialMissileTarget)
                    {
                        missilesAssigned.Add(potentialMissileTarget);
                        targetsTried.Add(potentialMissileTarget);
                        if (BDArmorySettings.DEBUG_AI)
                            Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} targeting closest missile {potentialMissileTarget.Vessel.GetName()}:{potentialMissileTarget.Vessel.parts[0].persistentId} as a secondary target");
                    }
                    potentialMissileTarget = BDATargetManager.GetUnengagedMissileTarget(this);
                    if (potentialMissileTarget)
                    {
                        missilesAssigned.Add(potentialMissileTarget);
                        targetsTried.Add(potentialMissileTarget);
                        if (BDArmorySettings.DEBUG_AI)
                            Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} targeting free missile {potentialMissileTarget.Vessel.GetName()}:{potentialMissileTarget.Vessel.parts[0].persistentId} as a secondary target");
                    }
                }
            }

            for (int i = 0; i < Math.Max(multiTargetNum, multiMissileTgtNum) - 1; i++) //primary target already added, so subtract 1 from nultitargetnum
            {
                TargetInfo potentialTarget = null;
                //============VESSEL THREATS============

                //then engage the closest enemy
                potentialTarget = BDATargetManager.GetHighestPriorityTarget(this);
                if (potentialTarget)
                {
                    targetsAssigned.Add(potentialTarget);
                    targetsTried.Add(potentialTarget);
                    if (BDArmorySettings.DEBUG_AI)
                        Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} targeting priority target {potentialTarget.Vessel.GetName()} as secondary target {i}");
                }
                else
                {
                    potentialTarget = BDATargetManager.GetClosestTarget(this);
                    if (BDArmorySettings.DEFAULT_FFA_TARGETING)
                    {
                        potentialTarget = BDATargetManager.GetClosestTargetWithBiasAndHysteresis(this);
                    }
                    if (potentialTarget)
                    {
                        targetsAssigned.Add(potentialTarget);
                        targetsTried.Add(potentialTarget);
                        if (BDArmorySettings.DEBUG_AI)
                            Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} targeting bias target {potentialTarget.Vessel.GetName()} as secondary target {i}");
                    }
                    else
                    {
                        using (List<TargetInfo>.Enumerator target = BDATargetManager.TargetList(Team).GetEnumerator())
                            while (target.MoveNext())
                            {
                                if (target.Current == null) continue;
                                if (target.Current.weaponManager == null) continue;
                                if (target.Current && target.Current.Vessel && CanSeeTarget(target.Current) && !targetsTried.Contains(target.Current))
                                {
                                    targetsAssigned.Add(target.Current);
                                    targetsTried.Add(target.Current);
                                    if (BDArmorySettings.DEBUG_AI)
                                        Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} targeting first remaining target {target.Current.Vessel.GetName()} as secondary target {i}");
                                    break;
                                }
                            }
                    }
                }
            }
            if (targetsAssigned.Count + missilesAssigned.Count == 0)
            {
                if (BDArmorySettings.DEBUG_AI)
                    Debug.Log("[BDArmory.MissileFire]: No available secondary targets");
            }
        }

        // Update target priority UI
        public void UpdateTargetPriorityUI(TargetInfo target)
        {
            // Return if the UI isn't visible
            if (part.PartActionWindow == null || !part.PartActionWindow.isActiveAndEnabled) return;
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
            var TargetDamageFields = Fields["targetWeightDamage"];
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
            float targetDamageValue = target.TargetPriDmg(target.weaponManager);
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
                targetWeightDamage * targetDamageValue +
                targetWeightFriendliesEngaging * targetFriendliesEngagingValue +
                targetWeightThreat * targetThreatValue +
                targetWeightAoD * targetAoDValue +
                targetWeightProtectTeammate * targetProtectTeammateValue +
                targetWeightProtectVIP * targetProtectVIPValue +
                targetWeightAttackVIP * targetAttackVIPValue);

            // Update GUI
            TargetBiasFields.guiName = targetBiasLabel + $": {targetBiasValue:0.00}";
            TargetRangeFields.guiName = targetRangeLabel + $": {targetRangeValue:0.00}";
            TargetPreferenceFields.guiName = targetPreferenceLabel + $": {targetPreferencevalue:0.00}";
            TargetATAFields.guiName = targetATALabel + $": {targetATAValue:0.00}";
            TargetAoDFields.guiName = targetAoDLabel + $": {targetAoDValue:0.00}";
            TargetAccelFields.guiName = targetAccelLabel + $": {targetAccelValue:0.00}";
            TargetClosureTimeFields.guiName = targetClosureTimeLabel + $": {targetClosureTimeValue:0.00}";
            TargetWeaponNumberFields.guiName = targetWeaponNumberLabel + $": {targetWeaponNumberValue:0.00}";
            TargetMassFields.guiName = targetMassLabel + $": {targetMassValue:0.00}";
            TargetDamageFields.guiName = targetDmgLabel + $": {targetDamageValue:0.00}";
            TargetFriendliesEngagingFields.guiName = targetFriendliesEngagingLabel + $": {targetFriendliesEngagingValue:0.00}";
            TargetThreatFields.guiName = targetThreatLabel + $": {targetThreatValue:0.00}";
            TargetProtectTeammateFields.guiName = targetProtectTeammateLabel + $": {targetProtectTeammateValue:0.00}";
            TargetProtectVIPFields.guiName = targetProtectVIPLabel + $": {targetProtectVIPValue:0.00}";
            TargetAttackVIPFields.guiName = targetAttackVIPLabel + $": {targetAttackVIPValue:0.00}";

            TargetScoreLabel = targetScore.ToString("0.00");
            TargetLabel = target.Vessel.GetName();
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
            var surfaceAI = VesselModuleRegistry.GetModule<BDModuleSurfaceAI>(vessel); // Get the surface AI if the vessel has one.
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
                        if (!CheckEngagementEnvelope(item.Current, distance, guardTarget)) continue;
                        // weapon usable, if missile continue looking for lasers/guns, else take it
                        WeaponClasses candidateClass = item.Current.GetWeaponClass();
                        switch (candidateClass)
                        {
                            case (WeaponClasses.DefenseLaser):
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
                                        ModuleTurret turret = Laser.turret;
                                        if (!TargetInTurretRange(turret, 15, default, Laser)) continue; // weight selection towards turrets that can fire on missile
                                        targetWeapon = item.Current;
                                        break;
                                    }
                                    targetWeapon = item.Current; // then any laser
                                    break;
                                }

                            case (WeaponClasses.Gun):
                                {
                                    // For point defense, favor turrets and RoF
                                    ModuleWeapon Gun = item.Current as ModuleWeapon;
                                    float candidateRPM = Gun.roundsPerMinute;
                                    float candidateYTraverse = Gun.yawRange;
                                    float candidatePTraverse = Gun.maxPitch;
                                    float candidateMinrange = Gun.engageRangeMin;
                                    float candidateMaxRange = Gun.engageRangeMax;
                                    bool candidatePFuzed = Gun.eFuzeType == PooledBullet.BulletFuzeTypes.Proximity || Gun.eFuzeType == PooledBullet.BulletFuzeTypes.Flak;
                                    bool candidateVTFuzed = Gun.eFuzeType == PooledBullet.BulletFuzeTypes.Timed || Gun.eFuzeType == PooledBullet.BulletFuzeTypes.Flak;
                                    float Cannistershot = Gun.ProjectileCount;

                                    Transform fireTransform = Gun.fireTransforms[0];

                                    if (vessel.Splashed && (BDArmorySettings.BULLET_WATER_DRAG && FlightGlobals.getAltitudeAtPos(fireTransform.position) < 0)) continue;
                                    if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 41)
                                    {
                                        candidateRPM = BDArmorySettings.FIRE_RATE_OVERRIDE;
                                    }
                                    if (candidateYTraverse > 0 || candidatePTraverse > 0)
                                    {
                                        ModuleTurret turret = Gun.turret;
                                        candidateRPM *= TargetInTurretRange(turret, 5, default, Gun) ? 2.0f : 0.01f; // weight selection towards turrets that can fire on missile
                                    }
                                    if (candidatePFuzed || candidateVTFuzed)
                                    {
                                        candidateRPM *= 1.5f; // weight selection towards flak ammo
                                    }
                                    if (Cannistershot > 1)
                                    {
                                        candidateRPM *= (1 + ((Cannistershot / 2) / 100)); // weight selection towards cluster ammo based on submunition count
                                    }
                                    if (candidateMinrange > distance || distance > candidateMaxRange)
                                    {
                                        candidateRPM *= .01f; //if within min range, massively negatively weight weapon - allows weapon to still be selected if all others lost/out of ammo
                                    }
                                    if ((targetWeapon != null) && (targetWeaponRPM > candidateRPM))
                                        continue; //dont replace better guns (but do replace missiles)

                                    targetWeapon = item.Current;
                                    targetWeaponRPM = candidateRPM;
                                    break;
                                }

                            case (WeaponClasses.Rocket):
                                {
                                    // For point defense, favor turrets and RoF
                                    ModuleWeapon Rocket = item.Current as ModuleWeapon;
                                    float candidateRocketAccel = Rocket.thrust / Rocket.rocketMass;
                                    float candidateRPM = Rocket.roundsPerMinute / 2;
                                    bool candidatePFuzed = Rocket.proximityDetonation;
                                    float candidateYTraverse = Rocket.yawRange;
                                    float candidatePTraverse = Rocket.maxPitch;
                                    float candidateMinrange = Rocket.engageRangeMin;
                                    float candidateMaxRange = Rocket.engageRangeMax;
                                    Transform fireTransform = Rocket.fireTransforms[0];

                                    if (vessel.Splashed && (BDArmorySettings.BULLET_WATER_DRAG && FlightGlobals.getAltitudeAtPos(fireTransform.position) < 0)) continue;
                                    if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 41)
                                    {
                                        candidateRPM = BDArmorySettings.FIRE_RATE_OVERRIDE / 2;
                                    }
                                    bool compareRocketRPM = false;

                                    if (candidateYTraverse > 0 || candidatePTraverse > 0)
                                    {
                                        ModuleTurret turret = Rocket.turret;
                                        candidateRPM *= TargetInTurretRange(turret, 5, default, Rocket) ? 2.0f : 0.01f; // weight selection towards turrets that can fire on missile
                                    }
                                    if (targetRocketAccel < candidateRocketAccel)
                                    {
                                        candidateRPM *= 1.5f; //weight towards faster rockets
                                    }
                                    if (!candidatePFuzed)
                                    {
                                        candidateRPM *= 0.01f; //negatively weight against contact-fuze rockets
                                    }
                                    if (candidateMinrange > distance || distance > candidateMaxRange)
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
                                    break;
                                }
                        }
                        /*
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
                                if (mlauncher.TargetingMode == MissileBase.TargetingModes.Radar && (!_radarsEnabled && !mlauncher.radarLOAL)) continue; //dont select RH missiles when no radar aboard
                                if (mlauncher.TargetingMode == MissileBase.TargetingModes.Laser && targetingPods.Count <= 0) continue; //don't select LH missiles when no FLIR aboard
                                if (mlauncher.reloadableRail != null && (mlauncher.reloadableRail.ammoCount < 1 && !BDArmorySettings.INFINITE_ORDINANCE)) continue; //don't select when out of ordinance
                                candidateDetDist = mlauncher.DetonationDistance;
                                candidateAccel = mlauncher.thrust / mlauncher.part.mass; //for anti-missile, prioritize proxidetonation and accel
                                bool EMP = mlauncher.warheadType == MissileBase.WarheadTypes.EMP;
                                candidatePriority = Mathf.RoundToInt(mlauncher.priority);

                                if (EMP) continue;
                                if (vessel.Splashed && FlightGlobals.getAltitudeAtPos(mlauncher.transform.position) < -10) continue; //we aren't going to surface in time (and are under no threat from the missile while underwater0 so don't baother
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

                                if (vessel.Splashed && FlightGlobals.getAltitudeAtPos(mlauncher.transform.position) < -5) continue;
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
                        */
                    }
            }

            //else if (!target.isLanded)
            else if (target.isFlying && !target.isMissile)
            {
                // iterate over weaponTypesAir and pick suitable one based on engagementRange (and dynamic launch zone for missiles)
                // Prioritize by:
                // 1. AA missiles (if we're flying, otherwise use guns if we're within gun range)
                // 1. Lasers
                // 2. Guns
                // 3. rockets
                // 4. unguided missiles
                using (List<IBDWeapon>.Enumerator item = weaponTypesAir.GetEnumerator())
                    while (item.MoveNext())
                    {
                        if (item.Current == null) continue;

                        // candidate, check engagement envelope
                        if (!CheckEngagementEnvelope(item.Current, distance, guardTarget)) continue;
                        // weapon usable, if missile continue looking for lasers/guns, else take it
                        WeaponClasses candidateClass = item.Current.GetWeaponClass();
                        // any rocketpods work?
                        switch (candidateClass)
                        {
                            case (WeaponClasses.Bomb): //hardly ideal, but if it's the only thing you have, then just maybe...
                                {
                                    if (!vessel.Splashed || (vessel.Splashed && vessel.altitude > currentTarget.Vessel.altitude))
                                    {
                                        MissileLauncher Bomb = item.Current as MissileLauncher;

                                        if (Bomb.reloadableRail != null && (Bomb.reloadableRail.ammoCount < 1 && !BDArmorySettings.INFINITE_ORDINANCE)) continue; //don't select when out of ordinance
                                                                                                                                                                  //if (firedMissiles >= maxMissilesOnTarget) continue;// Max missiles are fired, try another weapon
                                                                                                                                                                  // only useful if we are flying
                                        float candidateYield = Bomb.GetBlastRadius();
                                        int candidateCluster = Bomb.clusterbomb;
                                        bool EMP = Bomb.warheadType == MissileBase.WarheadTypes.EMP;
                                        int candidatePriority = Mathf.RoundToInt(Bomb.priority);

                                        if (EMP && target.isDebilitated) continue;
                                        if (targetWeapon != null && targetWeaponPriority > candidatePriority)
                                            continue; //keep higher priority weapon
                                        if (distance < candidateYield)
                                            continue;// don't drop bombs when within blast radius
                                        bool candidateUnguided = false;
                                        if (!vessel.LandedOrSplashed)
                                        {
                                            if (Bomb.GuidanceMode != MissileBase.GuidanceModes.AGMBallistic) //If you're targeting a massive flying sky cruiser or zeppelin, and you have *nothing else*...
                                            {
                                                candidateYield /= (candidateCluster * 2); //clusterbombs are altitude fuzed, not proximity
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
                                            if (Bomb.GuidanceMode == MissileBase.GuidanceModes.AGMBallistic) //There is at least precedent for A2A JDAM kills, so thats something
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
                                    break;
                                }
                            case (WeaponClasses.Rocket):
                                {
                                    //for AA, favor higher accel and proxifuze
                                    ModuleWeapon Rocket = item.Current as ModuleWeapon;
                                    float candidateRocketAccel = Rocket.thrust / Rocket.rocketMass;
                                    float candidateRPM = Rocket.roundsPerMinute;
                                    bool candidatePFuzed = Rocket.proximityDetonation;
                                    int candidatePriority = Mathf.RoundToInt(Rocket.priority);
                                    float candidateYTraverse = Rocket.yawRange;
                                    float candidatePTraverse = Rocket.maxPitch;
                                    float candidateMaxRange = Rocket.engageRangeMax;
                                    float candidateMinrange = Rocket.engageRangeMin;
                                    Transform fireTransform = Rocket.fireTransforms[0];
                                    if (vessel.LandedOrSplashed && candidatePTraverse <= 0) continue; //not going to hit a flier with fixed guns
                                    if (vessel.Splashed && BDArmorySettings.BULLET_WATER_DRAG && (!surfaceAI || surfaceAI.SurfaceType != AIUtils.VehicleMovementType.Submarine) && FlightGlobals.getAltitudeAtPos(fireTransform.position) < 0) continue;
                                    Vector3 aimDirection = fireTransform.forward;
                                    float targetCosAngle = Rocket.FiringSolutionVector != null ? Vector3.Dot(aimDirection, (Vector3)Rocket.FiringSolutionVector) : Vector3.Dot(aimDirection, (vessel.vesselTransform.position - fireTransform.position).normalized);
                                    bool outsideFiringCosAngle = targetCosAngle < Rocket.targetAdjustedMaxCosAngle;

                                    if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 41)
                                    {
                                        candidateRPM = BDArmorySettings.FIRE_RATE_OVERRIDE;
                                    }

                                    if (targetWeapon != null && targetWeaponPriority > candidatePriority)
                                        continue; //dont replace a higher priority weapon with a lower priority one

                                    if (candidateYTraverse > 0 || candidatePTraverse > 0)
                                    {
                                        ModuleTurret turret = Rocket.turret;
                                        candidateRPM *= TargetInTurretRange(turret, 15, default, Rocket) ? 2.0f : 0.01f; // weight selection towards turrets that can face in the right direction
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
                                    if (candidateMinrange > distance || distance > candidateMaxRange)
                                    {
                                        candidateRPM *= .01f; //if within min range massively negatively weight weapon - allows weapon to still be selected if all others lost/out of ammo
                                    }
                                    if (Rocket.dualModeAPS) candidateRPM /= 4; //disincentivise selecting dual mode APS turrets if something else is available to maintain Point Defense umbrella
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
                                        if (targetWeapon != null && (targetWeapon.GetWeaponClass() == WeaponClasses.Missile) && (targetWeaponTDPS > 0))
                                            continue; //dont replace missiles within their engage range
                                        if (targetWeaponRPM < candidateRPM) //or best gun
                                        {
                                            targetWeapon = item.Current;
                                            targetWeaponRPM = candidateRPM;
                                            targetRocketAccel = candidateRocketAccel;
                                            targetWeaponPriority = candidatePriority;
                                        }
                                    }
                                    break;
                                }
                            //Guns have higher priority than rockets; selected gun will override rocket selection
                            case (WeaponClasses.Gun):
                                {
                                    // For AtA, generally favour higher RPM and turrets
                                    //prioritize weapons with priority, then:
                                    //if shooting fighter-sized targets, prioritize RPM
                                    //if shooting larger targets - bombers/zeppelins/Ace Combat Wunderwaffen - prioritize biggest caliber
                                    ModuleWeapon Gun = item.Current as ModuleWeapon;
                                    float candidateRPM = Gun.roundsPerMinute;
                                    bool candidateGimbal = Gun.turret;
                                    float candidateTraverse = Gun.yawRange;
                                    bool candidatePFuzed = Gun.eFuzeType == PooledBullet.BulletFuzeTypes.Proximity || Gun.eFuzeType == PooledBullet.BulletFuzeTypes.Flak;
                                    bool candidateVTFuzed = Gun.eFuzeType == PooledBullet.BulletFuzeTypes.Timed || Gun.eFuzeType == PooledBullet.BulletFuzeTypes.Flak;
                                    float Cannistershot = Gun.ProjectileCount;
                                    float candidateMinrange = Gun.engageRangeMin;
                                    float candidateMaxRange = Gun.engageRangeMax;
                                    int candidatePriority = Mathf.RoundToInt(Gun.priority);
                                    float candidateRadius = currentTarget.Vessel.GetRadius(Gun.fireTransforms[0].forward, target.bounds);
                                    float candidateCaliber = Gun.caliber;
                                    if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 41)
                                    {
                                        candidateRPM = BDArmorySettings.FIRE_RATE_OVERRIDE;
                                    }
                                    Transform fireTransform = Gun.fireTransforms[0];
                                    if ((vessel.situation == Vessel.Situations.LANDED || vessel.situation == Vessel.Situations.SPLASHED) && !candidateGimbal) continue; //not going to hit fliers with fixed guns
                                    if ((!surfaceAI || surfaceAI.SurfaceType != AIUtils.VehicleMovementType.Submarine) && vessel.Splashed && (BDArmorySettings.BULLET_WATER_DRAG && FlightGlobals.getAltitudeAtPos(fireTransform.position) < 0)) continue; //don't select guns on sinking ships, but allow gun selection on subs

                                    Vector3 aimDirection = fireTransform.forward;
                                    float targetCosAngle = Gun.FiringSolutionVector != null ? Vector3.Dot(aimDirection, (Vector3)Gun.FiringSolutionVector) : Vector3.Dot(aimDirection, (vessel.vesselTransform.position - fireTransform.position).normalized);
                                    bool outsideFiringCosAngle = targetCosAngle < Gun.targetAdjustedMaxCosAngle;

                                    if (targetWeapon != null && targetWeaponPriority > candidatePriority) continue; //keep higher priority weapon

                                    if (candidateRadius > 8) //most fighters are, what, at most 15m in their largest dimension? That said, maybe make this configurable in the weapon PAW...
                                    {//weight selection towards larger caliber bullets, modified by turrets/fuzes/range settings when shooting bombers
                                        if (candidateGimbal = true && candidateTraverse > 0)
                                        {
                                            ModuleTurret turret = Gun.turret;
                                            candidateCaliber *= TargetInTurretRange(turret, 15, default, Gun) ? 1.5f : 0.01f; // weight selection towards turrets that can face the right direction
                                        }
                                        if (candidatePFuzed || candidateVTFuzed)
                                        {
                                            candidateCaliber *= 1.5f; // weight selection towards flak ammo
                                        }
                                        if (outsideFiringCosAngle)
                                        {
                                            candidateCaliber *= .01f; //if outside firing angle, massively negatively weight weapon - allows weapon to still be selected if all others lost/out of ammo
                                        }
                                        if (candidateMinrange > distance || distance > candidateMaxRange)
                                        {
                                            candidateCaliber *= .01f; //if within min range massively negatively weight weapon - allows weapon to still be selected if all others lost/out of ammo
                                        }
                                        candidateRPM = candidateCaliber * 10;
                                    }
                                    else //weight selection towards RoF, modified by turrets/fuzes/shot quantity/range
                                    {
                                        if (candidateGimbal = true && candidateTraverse > 0)
                                        {
                                            ModuleTurret turret = Gun.turret;
                                            candidateRPM *= TargetInTurretRange(turret, 15, default, Gun) ? 1.5f : 0.01f; // weight selection towards turrets that can face the right direction
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
                                        if (candidateMinrange > distance || distance > candidateMaxRange)
                                        {
                                            candidateRPM *= .01f; //if within min range massively negatively weight weapon - allows weapon to still be selected if all others lost/out of ammo
                                        }
                                    }
                                    if (Gun.dualModeAPS) candidateRPM /= 4; //disincentivise selecting dual mode APS turrets if something else is available to maintain Point Defense umbrella

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
                                            if ((targetWeapon != null) && (targetWeapon.GetWeaponClass() == WeaponClasses.Missile) && (targetWeaponTDPS > 0))
                                                continue; //dont replace missiles within their engage range
                                            targetWeapon = item.Current;
                                            targetWeaponRPM = candidateRPM;
                                            targetWeaponPriority = candidatePriority;
                                        }
                                    }
                                    break;
                                }
                            //if lasers, lasers will override gun selection
                            case (WeaponClasses.DefenseLaser):
                                {
                                    // For AA, favour higher power/turreted
                                    ModuleWeapon Laser = item.Current as ModuleWeapon;
                                    float candidateRPM = Laser.roundsPerMinute;
                                    bool candidateGimbal = Laser.turret;
                                    float candidateTraverse = Laser.yawRange;
                                    float candidateMinrange = Laser.engageRangeMin;
                                    float candidateMaxRange = Laser.engageRangeMax;
                                    int candidatePriority = Mathf.RoundToInt(Laser.priority);
                                    bool electrolaser = Laser.electroLaser;
                                    bool pulseLaser = Laser.pulseLaser;
                                    float candidatePower = electrolaser ? Laser.ECPerShot / (pulseLaser ? 50 : 1) : Laser.laserDamage / (pulseLaser ? 50 : 1);

                                    Transform fireTransform = Laser.fireTransforms[0];

                                    if ((vessel.situation == Vessel.Situations.LANDED || vessel.situation == Vessel.Situations.SPLASHED) && !candidateGimbal) continue; //not going to hit fliers with fixed lasers
                                    if ((!surfaceAI || surfaceAI.SurfaceType != AIUtils.VehicleMovementType.Submarine) && vessel.Splashed && BDArmorySettings.BULLET_WATER_DRAG && FlightGlobals.getAltitudeAtPos(fireTransform.position) < 0) continue;
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
                                        ModuleTurret turret = Laser.turret;
                                        candidateRPM *= TargetInTurretRange(turret, 15, default, Laser) ? 1.5f : 0.01f; // weight selection towards turrets that can point in the right direction
                                    }
                                    if (candidateMinrange > distance || distance > candidateMaxRange)
                                    {
                                        candidateRPM *= .00001f; //if within min range massively negatively weight weapon - allows weapon to still be selected if all others lost/out of ammo
                                    }
                                    if (Laser.dualModeAPS) candidateRPM /= 4; //disincentivise selecting dual mode APS turrets if something else is available to maintain Point Defense umbrella

                                    if (targetWeaponPriority < candidatePriority) //use priority gun
                                    {
                                        targetWeapon = item.Current;
                                        targetWeaponRPM = candidateRPM;
                                        targetWeaponPriority = candidatePriority;
                                    }
                                    else //if equal priority, use standard weighting
                                    {
                                        if (targetWeapon != null && (targetWeapon.GetWeaponClass() == WeaponClasses.Missile) && (targetWeaponTDPS > 0))
                                            continue; //dont replace missiles within their engage range
                                        if (targetWeaponRPM < candidateRPM)
                                        {
                                            targetWeapon = item.Current;
                                            targetWeaponRPM = candidateRPM;
                                            targetWeaponPriority = candidatePriority;
                                        }
                                    }
                                    break;
                                }
                            //projectile weapon selected, any missiles that take precedence?
                            case (WeaponClasses.Missile):
                                {
                                    //if (firedMissiles >= maxMissilesOnTarget) continue;// Max missiles are fired, try another weapon
                                    float candidateDetDist = 0;
                                    float candidateTurning = 0;
                                    int candidatePriority = 0;
                                    float candidateTDPS = 0f;

                                    MissileLauncher mlauncher = item.Current as MissileLauncher;
                                    if (mlauncher != null)
                                    {
                                        if (mlauncher.reloadableRail != null && (mlauncher.reloadableRail.ammoCount < 1 && !BDArmorySettings.INFINITE_ORDINANCE)) continue; //don't select when out of ordinance
                                        candidateDetDist = mlauncher.DetonationDistance;
                                        candidateTurning = mlauncher.maxTurnRateDPS; //for anti-aircraft, prioritize detonation dist and turn capability
                                        candidatePriority = Mathf.RoundToInt(mlauncher.priority);
                                        bool EMP = mlauncher.warheadType == MissileBase.WarheadTypes.EMP;
                                        bool heat = mlauncher.TargetingMode == MissileBase.TargetingModes.Heat;
                                        bool radar = mlauncher.TargetingMode == MissileBase.TargetingModes.Radar;
                                        bool inertial = mlauncher.TargetingMode == MissileBase.TargetingModes.Inertial;
                                        float heatThresh = mlauncher.heatThreshold;
                                        if (EMP && target.isDebilitated) continue;
                                        if (vessel.Splashed && (!surfaceAI || surfaceAI.SurfaceType != AIUtils.VehicleMovementType.Submarine) && (BDArmorySettings.BULLET_WATER_DRAG && FlightGlobals.getAltitudeAtPos(mlauncher.transform.position) < -10)) continue; //allow submarine-mounted missiles; new launch depth check in launchAuth 
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
                                        //if (heat && heatTarget.exists && heatTarget.signalStrength *
                                        //       ((BDArmorySettings.ASPECTED_IR_SEEKERS && Vector3.Dot(guardTarget.vesselTransform.up, mlauncher.transform.forward) > 0.25f) ?
                                        //        mlauncher.frontAspectHeatModifier : 1) < heatThresh) //heatTarget doesn't get found until *after* a heater is selected
                                        if (heat && BDArmorySettings.ASPECTED_IR_SEEKERS && Vector3.Dot(guardTarget.vesselTransform.up, mlauncher.transform.forward) > 0.25f)
                                        {
                                            //candidateTDPS *= 0.0001f; //Heatseeker, but IR sig is below missile threshold, skip to something else unless nothing else available
                                            if (mlauncher.frontAspectHeatModifier < 0.15f) continue;
                                            candidateTDPS *= mlauncher.frontAspectHeatModifier / 100;
                                        }
                                        if (radar)
                                        {
                                            if (!_radarsEnabled || (vesselRadarData != null && !vesselRadarData.locked))
                                            {
                                                if (!mlauncher.radarLOAL) candidateTDPS *= 0.001f; //no radar lock, skip to something else unless nothing else available
                                                else
                                                {
                                                    if (mlauncher.radarTimeout < ((distance - mlauncher.activeRadarRange) / mlauncher.optimumAirspeed)) candidateTDPS *= 0.5f; //outside missile self-lock zone 
                                                }
                                            }
                                        }
                                        if (inertial)
                                        {
                                            if (!(_radarsEnabled || _irstsEnabled) || vesselRadarData == null)
                                            {
                                                candidateTDPS *= 0.001f; //no radar/IRST, skip to something else unless nothing else available
                                            }
                                            else if (!vesselRadarData.detectedRadarTarget(target.Vessel, this).exists || !vesselRadarData.activeIRTarget(target.Vessel, this).exists)
                                            {
                                                candidateTDPS *= 0.001f;
                                            }
                                        }
                                        if (mlauncher.TargetingMode == MissileBase.TargetingModes.Laser && targetingPods.Count <= 0)
                                        {
                                            candidateTDPS *= 0.001f;  //no laserdot, skip to something else unless nothing else available
                                        }
                                    }
                                    else
                                    { //is modular missile
                                        BDModularGuidance mm = item.Current as BDModularGuidance; //need to work out priority stuff for MMGs
                                        candidateTDPS = 5000;
                                        candidateDetDist = mm.warheadYield;
                                        //candidateTurning = ((MissileLauncher)item.Current).maxTurnRateDPS; //for anti-aircraft, prioritize detonation dist and turn capability
                                        candidatePriority = Mathf.RoundToInt(mm.priority);

                                        if ((!surfaceAI || surfaceAI.SurfaceType != AIUtils.VehicleMovementType.Submarine) && vessel.Splashed && (BDArmorySettings.BULLET_WATER_DRAG && FlightGlobals.getAltitudeAtPos(mlauncher.transform.position) < 0)) continue;
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
                                    if (distance < ((EngageableWeapon)item.Current).engageRangeMin || firedMissiles >= maxMissilesOnTarget || (unguidedWeapon && distance > ((EngageableWeapon)item.Current).engageRangeMax / 10))
                                        candidateTDPS *= -1f; // if within min range, negatively weight weapon - allows weapon to still be selected if all others lost/out of ammo
                                    if ((!vessel.LandedOrSplashed) || ((distance > gunRange) && (vessel.LandedOrSplashed))) // If we're not airborne, we want to prioritize guns
                                    {
                                        if (distance <= gunRange && candidateTDPS < 1 && targetWeapon != null) continue; //missiles are within min range/can't lock, don't replace existing gun if in gun range
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
                                    break;
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
                        if (!CheckEngagementEnvelope(item.Current, distance, guardTarget)) continue;
                        // weapon usable, if missile continue looking for lasers/guns, else take it
                        WeaponClasses candidateClass = item.Current.GetWeaponClass();
                        switch (candidateClass)
                        {
                            case (WeaponClasses.DefenseLaser): //lasers would be a suboptimal choice for strafing attacks, but if nothing else available...
                                {
                                    // For Atg, favour higher power/turreted
                                    ModuleWeapon Laser = item.Current as ModuleWeapon;
                                    float candidateRPM = Laser.roundsPerMinute;
                                    bool candidateGimbal = Laser.turret;
                                    float candidateTraverse = Laser.yawRange;
                                    float candidateMinrange = Laser.engageRangeMin;
                                    float candidateMaxRange = Laser.engageRangeMax;
                                    int candidatePriority = Mathf.RoundToInt(Laser.priority);
                                    bool electrolaser = Laser.electroLaser;
                                    bool pulseLaser = Laser.pulseLaser;
                                    bool HEpulses = Laser.HEpulses;
                                    float candidatePower = electrolaser ? Laser.ECPerShot / (pulseLaser ? 50 : 1) : Laser.laserDamage / (pulseLaser ? 50 : 1);

                                    Transform fireTransform = Laser.fireTransforms[0];

                                    if ((!surfaceAI || surfaceAI.SurfaceType != AIUtils.VehicleMovementType.Submarine) && vessel.Splashed && (BDArmorySettings.BULLET_WATER_DRAG && FlightGlobals.getAltitudeAtPos(fireTransform.position) < 0)) continue; //new ModuleWeapon depth check for sub-mounted rockets

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
                                        ModuleTurret turret = Laser.turret;
                                        candidateRPM *= TargetInTurretRange(turret, 0, default, Laser) ? 1.5f : 0.01f; // weight selection towards turrets that can point in the right direction
                                    }
                                    if (HEpulses)
                                    {
                                        candidateRPM *= 1.5f; // weight selection towards lasers that can do blast damage
                                    }
                                    if (candidateMinrange > distance || distance > candidateMaxRange)
                                    {
                                        candidateRPM *= .00001f; //if within min range massively negatively weight weapon - allows weapon to still be selected if all others lost/out of ammo
                                    }
                                    if (Laser.dualModeAPS) candidateRPM /= 4; //disincentivise selecting dual mode APS turrets if something else is available to maintain Point Defense umbrella

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
                                    break;
                                }

                            case (WeaponClasses.Gun): //iterate through guns, if nothing else, use found gun
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
                                    float candidateMaxRange = Gun.engageRangeMax;
                                    float candidateTraverse = Gun.yawRange * Gun.maxPitch;
                                    float candidateRadius = currentTarget.Vessel.GetRadius(Gun.fireTransforms[0].forward, target.bounds);
                                    float candidateCaliber = Gun.caliber;
                                    Transform fireTransform = Gun.fireTransforms[0];

                                    if (BDArmorySettings.BULLET_WATER_DRAG)
                                    {
                                        if ((!surfaceAI || surfaceAI.SurfaceType != AIUtils.VehicleMovementType.Submarine) && vessel.Splashed && FlightGlobals.getAltitudeAtPos(fireTransform.position) < 0) continue;
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
                                        ModuleTurret turret = Gun.turret;
                                        candidateRPM *= TargetInTurretRange(turret, 0, default, Gun) ? 1.5f : 0.01f; // weight selection towards turrets that can point in the right direction
                                    }
                                    if (candidateMinrange > distance || distance > candidateMaxRange)
                                    {
                                        candidateRPM *= .01f; //if within min range massively negatively weight weapon - allows weapon to still be selected if all others lost/out of ammo
                                    }
                                    if (Gun.dualModeAPS) candidateRPM /= 4; //disincentivise selecting dual mode APS turrets if something else is available to maintain Point Defense umbrella

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
                                    break;
                                }
                            //Any rockets we can use instead of guns?
                            case (WeaponClasses.Rocket):
                                {
                                    ModuleWeapon Rocket = item.Current as ModuleWeapon;
                                    float candidateRocketPower = Rocket.blastRadius;
                                    float CandidateEndurance = Rocket.thrustTime;
                                    int candidateRanking = Mathf.RoundToInt(Rocket.priority);
                                    Transform fireTransform = Rocket.fireTransforms[0];

                                    if (vessel.Splashed && (BDArmorySettings.BULLET_WATER_DRAG && FlightGlobals.getAltitudeAtPos(fireTransform.position) < 0))
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
                                    break;
                                }
                            //Bombs are good. any of those we can use over rockets?
                            case (WeaponClasses.Bomb):
                                {
                                    if (vessel.Splashed && vessel.altitude < currentTarget.Vessel.altitude) continue; //I guess depth charges would sorta apply here, but those are SLW instead
                                    MissileLauncher Bomb = item.Current as MissileLauncher;
                                    if (Bomb.reloadableRail != null && (Bomb.reloadableRail.ammoCount < 1 && !BDArmorySettings.INFINITE_ORDINANCE)) continue; //don't select when out of ordinance
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

                                        if (Bomb.GuidanceMode != MissileBase.GuidanceModes.AGMBallistic)
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
                                    break;
                                }
                            //Missiles are the preferred method of ground attack. use if available over other options
                            case (WeaponClasses.Missile): //don't use missiles underwater. That's what torpedoes are for
                                {
                                    // Priority Sequence:
                                    // - Antiradiation
                                    // - guided missiles
                                    // - by blast strength
                                    // - add code to choose optimal missile based on target profile - i.e. use bigger bombs on large landcruisers, smaller bombs on small Vees that don't warrant that sort of overkill?
                                    int candidatePriority;
                                    float candidateYield;
                                    double srfSpeed = currentTarget.Vessel.horizontalSrfSpeed;
                                    MissileLauncher Missile = item.Current as MissileLauncher;
                                    if (Missile != null)
                                    {
                                        //if (Missile.TargetingMode == MissileBase.TargetingModes.Radar && radars.Count <= 0) continue; //dont select RH missiles when no radar aboard
                                        //if (Missile.TargetingMode == MissileBase.TargetingModes.Laser && targetingPods.Count <= 0) continue; //don't select LH missiles when no FLIR aboard
                                        if (Missile.reloadableRail != null && (Missile.reloadableRail.ammoCount < 1 && !BDArmorySettings.INFINITE_ORDINANCE)) continue; //don't select when out of ordinance
                                        if (vessel.Splashed && (!surfaceAI || surfaceAI.SurfaceType != AIUtils.VehicleMovementType.Submarine) && FlightGlobals.getAltitudeAtPos(item.Current.GetPart().transform.position) < -10) continue;
                                        //if (firedMissiles >= maxMissilesOnTarget) continue;// Max missiles are fired, try another weapon
                                        candidateYield = Missile.GetBlastRadius();
                                        bool EMP = Missile.warheadType == MissileBase.WarheadTypes.EMP;
                                        candidatePriority = Mathf.RoundToInt(Missile.priority);

                                        if (EMP && target.isDebilitated) continue;
                                        //if (targetWeapon != null && targetWeapon.GetWeaponClass() == WeaponClasses.Bomb) targetYield = -1; //reset targetyield so larger bomb yields don't supercede missiles
                                        if (targetWeapon != null && targetWeaponPriority > candidatePriority)
                                            continue; //keep higher priority weapon
                                        if (srfSpeed < 1) // set higher than 0 in case of physics jitteriness
                                        {
                                            if (Missile.TargetingMode == MissileBase.TargetingModes.Gps ||
                                                Missile.GuidanceMode == MissileBase.GuidanceModes.Cruise ||
                                                  Missile.GuidanceMode == MissileBase.GuidanceModes.AGMBallistic ||
                                                  Missile.TargetingMode == MissileBase.TargetingModes.Inertial ||
                                                  Missile.GuidanceMode == MissileBase.GuidanceModes.None)
                                            {
                                                if (targetWeapon != null && targetYield > candidateYield) continue; //prioritize biggest Boom
                                                if (distance < Missile.engageRangeMin) continue; //select missiles we can use now
                                                                                                 //targetYield = candidateYield;
                                                candidateAGM = true;
                                                //targetWeapon = item.Current;
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
                                                                                                                    //targetYield = candidateYield;
                                                                                                                    //targetWeapon = item.Current;
                                                                                                                    //targetWeaponPriority = candidatePriority;
                                                candidateAGM = true;
                                            }
                                        }
                                        else if (Missile.TargetingMode == MissileBase.TargetingModes.Laser)
                                        {
                                            if (candidateAntiRad) continue; //keep antirad missile;
                                            if (targetingPods.Count <= 0 || (foundCam && (foundCam.groundTargetPosition - guardTarget.CoM).sqrMagnitude > Mathf.Max(100, 0.013f * (float)guardTarget.srfSpeed * (float)guardTarget.srfSpeed))) candidateYield *= 0.1f;

                                            if (targetWeapon != null && targetYield > candidateYield) continue; //prioritize biggest Boom
                                            candidateAGM = true;
                                            //targetYield = candidateYield;
                                            //targetWeapon = item.Current;
                                            //targetWeaponPriority = candidatePriority;
                                        }
                                        else
                                        {
                                            if (!candidateAGM)
                                            {
                                                if (Missile.TargetingMode == MissileBase.TargetingModes.Radar && (!_radarsEnabled || (vesselRadarData != null && !vesselRadarData.locked)) && !Missile.radarLOAL) candidateYield *= 0.1f;
                                                if (Missile.TargetingMode == MissileBase.TargetingModes.Inertial && !(_radarsEnabled || _irstsEnabled)) candidateYield *= 0.1f;
                                                if (targetWeapon != null && targetYield > candidateYield) continue;
                                                //targetYield = candidateYield;
                                                //targetWeapon = item.Current;
                                                //targetWeaponPriority = candidatePriority;
                                            }
                                        }
                                        if (distance < ((EngageableWeapon)item.Current).engageRangeMin || firedMissiles >= maxMissilesOnTarget || (unguidedWeapon && distance > ((EngageableWeapon)item.Current).engageRangeMax / 10))
                                            candidateYield *= -1f; // if within min range, negatively weight weapon - allows weapon to still be selected if all others lost/out of ammo
                                        if (!vessel.LandedOrSplashed || (vessel.LandedOrSplashed && (distance > gunRange || targetWeapon == null || (distance <= gunRange && targetWeapon != null && (targetWeapon.GetWeaponClass() != WeaponClasses.Rocket || targetWeapon.GetWeaponClass() != WeaponClasses.Gun))))) // If we're not airborne, we want to prioritize guns
                                        {
                                            if (distance <= gunRange && candidateYield < 1 && targetWeapon != null) continue; //missiles are within min range/can't lock, don't replace existing gun if in gun range
                                            if (targetWeaponPriority < candidatePriority) //use priority gun
                                            {
                                                targetWeapon = item.Current;
                                                targetYield = candidateYield;
                                                targetWeaponPriority = candidatePriority;
                                            }
                                            else //if equal priority, use standard weighting
                                            {
                                                if (targetYield < candidateYield)
                                                {
                                                    targetWeapon = item.Current;
                                                    targetYield = candidateYield;
                                                    targetWeaponPriority = candidatePriority;
                                                }
                                            }
                                        }
                                    }
                                    else //modular missile
                                    {
                                        BDModularGuidance mm = item.Current as BDModularGuidance; //need to work out priority stuff for MMGs
                                        if (mm.GuidanceMode == MissileBase.GuidanceModes.SLW) continue;
                                        candidateYield = mm.warheadYield;
                                        //candidateTurning = ((MissileLauncher)item.Current).maxTurnRateDPS; //for anti-aircraft, prioritize detonation dist and turn capability
                                        candidatePriority = Mathf.RoundToInt(mm.priority);

                                        if ((!surfaceAI || surfaceAI.SurfaceType != AIUtils.VehicleMovementType.Submarine) && vessel.Splashed && (BDArmorySettings.BULLET_WATER_DRAG && FlightGlobals.getAltitudeAtPos(mm.transform.position) < -10)) continue;
                                        if (targetWeapon != null && targetWeaponPriority > candidatePriority) continue; //keep higher priority weapon
                                        if (srfSpeed < 1) // set higher than 0 in case of physics jitteriness
                                        {
                                            if (mm.TargetingMode == MissileBase.TargetingModes.Gps ||
                                                mm.TargetingMode == MissileBase.TargetingModes.Inertial ||
                                                mm.GuidanceMode == MissileBase.GuidanceModes.Cruise ||
                                                  mm.GuidanceMode == MissileBase.GuidanceModes.AGMBallistic ||
                                                  mm.GuidanceMode == MissileBase.GuidanceModes.None)
                                            {
                                                if (targetWeapon != null && targetYield > candidateYield) continue; //prioritize biggest Boom
                                                if (distance < mm.engageRangeMin) continue; //select missiles we can use now
                                                                                            //targetYield = candidateYield;
                                                candidateAGM = true;
                                                //targetWeapon = item.Current;
                                            }
                                        }
                                        if (mm.TargetingMode == MissileBase.TargetingModes.Laser)
                                        {
                                            if (candidateAntiRad) continue; //keep antirad missile;
                                            if (mm.TargetingMode == MissileBase.TargetingModes.Laser && targetingPods.Count <= 0) candidateYield *= 0.1f;

                                            if (targetWeapon != null && targetYield > candidateYield) continue; //prioritize biggest Boom
                                            candidateAGM = true;
                                            //targetYield = candidateYield;
                                            //targetWeapon = item.Current;
                                            //targetWeaponPriority = candidatePriority;
                                        }
                                        else
                                        {
                                            if (!candidateAGM)
                                            {
                                                if (mm.TargetingMode == MissileBase.TargetingModes.Radar && (!_radarsEnabled || (vesselRadarData != null && !vesselRadarData.locked)) && !mm.radarLOAL) candidateYield *= 0.1f;
                                                if (mm.TargetingMode == MissileBase.TargetingModes.Inertial && !(_radarsEnabled || _irstsEnabled)) candidateYield *= 0.1f;
                                                if (targetWeapon != null && targetYield > candidateYield) continue;
                                                //targetYield = candidateYield;
                                                //targetWeapon = item.Current;
                                                //targetWeaponPriority = candidatePriority;
                                            }
                                        }
                                        if (distance < ((EngageableWeapon)item.Current).engageRangeMin || firedMissiles >= maxMissilesOnTarget)
                                            candidateYield *= -1f; // if within min range, negatively weight weapon - allows weapon to still be selected if all others lost/out of ammo

                                        if (!vessel.LandedOrSplashed || (vessel.LandedOrSplashed && (distance > gunRange || targetWeapon == null || (distance <= gunRange && targetWeapon != null && (targetWeapon.GetWeaponClass() != WeaponClasses.Rocket || targetWeapon.GetWeaponClass() != WeaponClasses.Gun))))) // If we're not airborne, we want to prioritize guns
                                        {
                                            if (distance <= gunRange && candidateYield < 1 && targetWeapon != null) continue; //missiles are within min range/can't lock, don't replace existing gun if in gun range
                                            if (targetWeaponPriority < candidatePriority) //use priority gun
                                            {
                                                targetWeapon = item.Current;
                                                targetYield = candidateYield;
                                                targetWeaponPriority = candidatePriority;
                                            }
                                            else //if equal priority, use standard weighting
                                            {
                                                if (targetYield < candidateYield)
                                                {
                                                    targetWeapon = item.Current;
                                                    targetYield = candidateYield;
                                                    targetWeaponPriority = candidatePriority;
                                                }
                                            }
                                        }
                                    }
                                    break;
                                }

                            // TargetInfo.isLanded includes splashed but not underwater, for whatever reasons.
                            // If target is splashed, and we have torpedoes, use torpedoes, because, obviously,
                            // torpedoes are the best kind of sausage for splashed targets,
                            // almost as good as STS missiles, which we don't have.
                            case (WeaponClasses.SLW):
                                {
                                    if (!target.isSplashed) continue;
                                    //if (firedMissiles >= maxMissilesOnTarget) continue;// Max missiles are fired, try another weapon
                                    MissileLauncher SLW = item.Current as MissileLauncher;
                                    if (item.Current.GetMissileType().ToLower() == "depthcharge") continue; // don't use depth charges against surface ships
                                    if (SLW.reloadableRail != null && (SLW.reloadableRail.ammoCount < 1 && !BDArmorySettings.INFINITE_ORDINANCE)) continue; //don't select when out of ordinance
                                    float candidateYield = SLW.GetBlastRadius();
                                    bool EMP = SLW.warheadType == MissileBase.WarheadTypes.EMP;
                                    int candidatePriority = Mathf.RoundToInt(SLW.priority);

                                    if (EMP && target.isDebilitated) continue;
                                    // not sure on the desired selection priority algorithm, so placeholder By Yield for now

                                    if (SLW.TargetingMode == MissileBase.TargetingModes.Heat && SLW.activeRadarRange < 0 && (rwr && rwr.rwrEnabled)) //we have passive acoustic homing? see if anything has active sonar
                                    {
                                        for (int i = 0; i < rwr.pingsData.Length; i++)
                                        {
                                            if (rwr.pingsData[i].signalStrength == 6) //Sonar
                                            {
                                                if ((rwr.pingWorldPositions[i] - guardTarget.CoM).sqrMagnitude < 20 * 20) //is current target a hostile radar source?
                                                {
                                                    candidateYield *= 2; // Prioritize PAH Torps for hostile sonar sources
                                                    break;
                                                }
                                            }
                                        }
                                    }

                                    if (distance < ((EngageableWeapon)item.Current).engageRangeMin || firedMissiles >= maxMissilesOnTarget || (unguidedWeapon && distance > ((EngageableWeapon)item.Current).engageRangeMax / 10))
                                        candidateYield *= -1f; // if within min range, negatively weight weapon - allows weapon to still be selected if all others lost/out of ammo

                                    //if ((!vessel.LandedOrSplashed) || ((distance > gunRange) && (vessel.LandedOrSplashed))) 
                                    {
                                        //if ((distance <= gunRange || distance < candidateYield || candidateYield < 1) && targetWeapon != null) continue; //torp are within min range/can't lock, don't replace existing gun if in gun range
                                        if ((distance < candidateYield || candidateYield < 1) && targetWeapon != null) continue; //torp are within min range/can't lock, use something else; else, prioritize SLW, as those are the best option

                                        if (targetWeaponPriority < candidatePriority) //use priority gun
                                        {
                                            targetWeapon = item.Current;
                                            targetYield = candidateYield;
                                            targetWeaponPriority = candidatePriority;
                                        }
                                        else //if equal priority, use standard weighting
                                        {
                                            if (targetYield < candidateYield)
                                            {
                                                targetWeapon = item.Current;
                                                targetYield = candidateYield;
                                                targetWeaponPriority = candidatePriority;
                                            }
                                        }
                                    }
                                    break;
                                }
                        }
                    }
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
                        if (!CheckEngagementEnvelope(item.Current, distance, guardTarget)) continue;

                        WeaponClasses candidateClass = item.Current.GetWeaponClass();
                        switch (candidateClass)
                        {
                            case (WeaponClasses.SLW):
                                {
                                    MissileLauncher SLW = item.Current as MissileLauncher;
                                    if (SLW.TargetingMode == MissileBase.TargetingModes.Radar && (!_sonarsEnabled && !SLW.radarLOAL)) continue; //dont select RH missiles when no radar aboard
                                    if (SLW.TargetingMode == MissileBase.TargetingModes.Laser && targetingPods.Count <= 0) continue; //don't select LH missiles when no FLIR aboard
                                    if (SLW.reloadableRail != null && (SLW.reloadableRail.ammoCount < 1 && !BDArmorySettings.INFINITE_ORDINANCE)) continue; //don't select when out of ordinance
                                    float candidateYield = SLW.GetBlastRadius();
                                    float candidateTurning = SLW.maxTurnRateDPS;
                                    float candidateTDPS = 0f;
                                    bool EMP = SLW.warheadType == MissileBase.WarheadTypes.EMP;
                                    bool heat = SLW.TargetingMode == MissileBase.TargetingModes.Heat;
                                    bool radar = SLW.TargetingMode == MissileBase.TargetingModes.Radar;
                                    bool inertial = SLW.TargetingMode == MissileBase.TargetingModes.Inertial;
                                    float heatThresh = SLW.heatThreshold;
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

                                    if (item.Current.GetMissileType().ToLower() != "torpedo") continue;

                                    if (distance < candidateYield) continue; //don't use explosives within their blast radius
                                                                             //if(firedMissiles >= maxMissilesOnTarget) continue;// Max missiles are fired, try another weapon
                                    if (SLW.TargetingMode == MissileBase.TargetingModes.Heat && SLW.activeRadarRange < 0 && (rwr && rwr.rwrEnabled)) //we have passive acoustic homing? see if anything has active sonar
                                    {
                                        for (int i = 0; i < rwr.pingsData.Length; i++)
                                        {
                                            if (rwr.pingsData[i].signalStrength == 6) //Sonar
                                            {
                                                if ((rwr.pingWorldPositions[i] - guardTarget.CoM).sqrMagnitude < 20 * 20) //is current target a hostile radar source?
                                                {
                                                    candidateYield *= 1.5f; // Prioritize PAH Torps for hostile sonar sources
                                                    break;
                                                }
                                            }
                                        }
                                    }

                                    if (candidateTurning + candidateYield > targetWeaponTDPS)
                                    {
                                        candidateTDPS = candidateTurning + candidateYield; // weight selection towards more maneuverable missiles
                                    }
                                    //if (candidateDetDist > 0)
                                    //{
                                    //    candidateTDPS += candidateDetDist; // weight selection towards misiles with proximity warheads
                                    //}
                                    if (heat && heatTarget.exists && heatTarget.signalStrength < heatThresh)
                                    {
                                        candidateTDPS *= 0.001f; //Heatseeker, but IR sig is below missile threshold, skip to something else unless nutohine else available
                                    }
                                    if (radar)
                                    {
                                        if (!_sonarsEnabled || (vesselRadarData != null && !vesselRadarData.locked))
                                        {
                                            if (!SLW.radarLOAL) candidateTDPS *= 0.001f; //no radar/sonar lock, skip to something else unless nothing else available
                                            else
                                            {
                                                if (SLW.radarTimeout < ((distance - SLW.activeRadarRange) / SLW.optimumAirspeed)) candidateTDPS *= 0.5f; //outside missile self-lock zone 
                                            }
                                        }
                                    }
                                    if (inertial)
                                    {
                                        if (!_sonarsEnabled)
                                        {
                                            candidateTDPS *= 0.001f; //no radar/sonar, skip to something else unless nothing else available
                                        }
                                    }
                                    if (distance < ((EngageableWeapon)item.Current).engageRangeMin || firedMissiles >= maxMissilesOnTarget || (unguidedWeapon && distance > ((EngageableWeapon)item.Current).engageRangeMax / 10))
                                        candidateTDPS *= -1f; // if within min range, negatively weight weapon - allows weapon to still be selected if all others lost/out of ammo
                                    if ((!vessel.Splashed) || ((distance > gunRange) && (vessel.LandedOrSplashed))) // If we're not airborne, we want to prioritize guns
                                    {
                                        if ((distance <= 500 || distance < candidateYield || candidateTDPS < 1) && targetWeapon != null) continue; //torp are within min range/can't lock, don't replace existing gun if in gun range
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
                                    break;
                                    //MMG torpedo support... ?
                                }
                            case (WeaponClasses.Rocket):
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
                                            if ((distance > 500 * CandidateEndurance)) continue;
                                        }
                                        if (targetWeaponPriority > candidateRanking)
                                            continue; //don't select a lower priority weapon over a higher priority one

                                        if (targetWeaponPriority < candidateRanking) //use priority gun
                                        {
                                            if (distance < candidateRocketPower) continue;// don't fire rockets when within blast radius
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
                                    break;
                                }
                            case (WeaponClasses.DefenseLaser):
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
                                            ModuleTurret turret = Laser.turret;
                                            candidateRPM *= TargetInTurretRange(turret, 15, default, Laser) ? 1.5f : 0.01f; // weight selection towards turrets that can point in the right direction
                                        }
                                        if (candidateMinrange > distance || distance > candidateMaxrange / 10)
                                        {
                                            candidateRPM *= .00001f; //if within min range massively negatively weight weapon - allows weapon to still be selected if all others lost/out of ammo
                                        }
                                        if (Laser.dualModeAPS) candidateRPM /= 4; //disincentivise selecting dual mode APS turrets if something else is available to maintain Point Defense umbrella

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
                                    break;
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
                    if (selectedWeapon.GetShortName() == weaponArray[weaponIndex].GetShortName() && targetWeapon.GetEngageRange() == weaponArray[weaponIndex].GetEngageRange())
                    {
                        break;
                    }
                }

                if (BDArmorySettings.DEBUG_AI)
                {
                    Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} - Selected weapon {selectedWeapon.GetShortName()}");
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
                    Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} - No weapon selected for target {target.Vessel.vesselName}");
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
        bool CheckEngagementEnvelope(IBDWeapon weaponCandidate, float distanceToTarget, Vessel targetVessel)
        {
            EngageableWeapon engageableWeapon = weaponCandidate as EngageableWeapon;

            if (engageableWeapon == null) return true;
            if (!engageableWeapon.engageEnabled) return true;
            try
            {
                //if (distanceToTarget < engageableWeapon.GetEngagementRangeMin()) return false; //covered in weapon select logic
                //if (distanceToTarget > engageableWeapon.GetEngagementRangeMax()) return false;
                //if (distanceToTarget > (engageableWeapon.GetEngagementRangeMax() * 1.2f)) return false; //have Ai begin to preemptively lead target, instead of frantically doing so after weapon in range
                //if (distanceToTarget > (engageableWeapon.GetEngagementRangeMax() + (float)vessel.speed * 2)) return false; //have AI preemptively begin to lead 2s out from max weapon range
                //take target vel into account? //if you're going 250m/s, that's only an extra 500m to the maxRange; if the enemy is closing towards you at 250m/s, that's 250m addition
                //Max 1.5x engagement, or engagementRange + vel*4?
                //min 2x engagement, or engagement + 2000m?
                if (weaponCandidate.GetWeaponClass() != WeaponClasses.Missile || ((MissileBase)weaponCandidate).UseStaticMaxLaunchRange)
                    if (distanceToTarget > Mathf.Min(engageableWeapon.GetEngagementRangeMax() * 2, engageableWeapon.GetEngagementRangeMax() + Mathf.Max(1000, (float)vessel.speed * 2))) return false; //have AI preemptively begin to lead 2s out from max weapon range
                switch (weaponCandidate.GetWeaponClass())
                {
                    case WeaponClasses.DefenseLaser:
                        {
                            ModuleWeapon laser = (ModuleWeapon)weaponCandidate;
                            if (distanceToTarget < laser.minSafeDistance) return false;

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
                                    Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} - Firing possible with {weaponCandidate.GetShortName()}");
                                }
                                return true;
                            }
                            break;
                        }

                    case WeaponClasses.Gun:
                        {
                            ModuleWeapon gun = (ModuleWeapon)weaponCandidate;
                            if (distanceToTarget < gun.minSafeDistance) return false;

                            // check yaw range of turret
                            //ModuleTurret turret = gun.turret;
                            //float gimbalTolerance = vessel.LandedOrSplashed ? 0 : 15;
                            //if (turret != null)
                            //if (!TargetInTurretRange(turret, gimbalTolerance, default, gun))
                            //return false;

                            // check overheat, reloading, ability to fire soon
                            if (!gun.hasGunner)
                                return false;
                            if (gun.isReloading || gun.isOverheated)
                                return false;
                            if (!gun.CanFireSoon())
                                return false;
                            // check ammo
                            if (CheckAmmo(gun))
                            {
                                if (BDArmorySettings.DEBUG_WEAPONS)
                                {
                                    Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} - Firing possible with {weaponCandidate.GetShortName()}");
                                }
                                return true;
                            }
                            break;
                        }

                    case WeaponClasses.Missile:
                        {
                            MissileBase ml = (MissileBase)weaponCandidate;
                            //if (distanceToTarget < engageableWeapon.GetEngagementRangeMin()) return false; //handled by bool smartWeapon select

                            bool readyMissiles = false;
                            using (var msl = VesselModuleRegistry.GetModules<MissileBase>(vessel).GetEnumerator())
                                while (msl.MoveNext())
                                {
                                    if (msl.Current == null) continue;
                                    if (msl.Current.launched) continue;
                                    readyMissiles = true;
                                    break;
                                }
                            if (!readyMissiles) return false;
                            // lock radar if needed
                            if (ml.TargetingMode == MissileBase.TargetingModes.Radar)
                            {
                                if (results.foundAntiRadiationMissile && DynamicRadarOverride) return false; // Don't try to fire radar missiles while we have an incoming anti-rad missile
                                using (List<ModuleRadar>.Enumerator rd = radars.GetEnumerator())
                                    while (rd.MoveNext())
                                    {
                                        if (rd.Current != null && rd.Current.canLock && rd.Current.sonarMode == ModuleRadar.SonarModes.None)
                                        {
                                            if (results.foundAntiRadiationMissile && rd.Current.DynamicRadar) continue;
                                            rd.Current.EnableRadar();
                                            _radarsEnabled = true;
                                        }
                                    }
                            }
                            if (ml.TargetingMode == MissileBase.TargetingModes.Inertial)
                            {
                                if (!results.foundAntiRadiationMissile || !DynamicRadarOverride)
                                {
                                    using (List<ModuleRadar>.Enumerator rd = radars.GetEnumerator())
                                        while (rd.MoveNext())
                                        {
                                            if (rd.Current != null && rd.Current.sonarMode == ModuleRadar.SonarModes.None)
                                            {
                                                if (rd.Current.DynamicRadar && results.foundAntiRadiationMissile) continue; //don't enable radar if incoming HARM, unless radar is specifically set to be used regardless
                                                float scanSpeed = (rd.Current.locked && rd.Current.lockedTarget.vessel == targetVessel) ? rd.Current.multiLockFOV : rd.Current.directionalFieldOfView / rd.Current.scanRotationSpeed * 2;
                                                if (GpsUpdateMax > 0 && scanSpeed < GpsUpdateMax) GpsUpdateMax = scanSpeed;
                                                rd.Current.EnableRadar();
                                                if (ml.GetWeaponClass() != WeaponClasses.SLW) _radarsEnabled = true;
                                                else _sonarsEnabled = true;
                                            }
                                        }
                                }
                                if (!_radarsEnabled)
                                {
                                    using (List<ModuleIRST>.Enumerator rd = irsts.GetEnumerator())
                                        while (rd.MoveNext())
                                        {
                                            if (rd.Current != null)
                                            {
                                                float scanSpeed = rd.Current.directionalFieldOfView / rd.Current.scanRotationSpeed * 2;
                                                if (GpsUpdateMax > 0 && scanSpeed < GpsUpdateMax) GpsUpdateMax = scanSpeed;
                                                rd.Current.EnableIRST();
                                                _irstsEnabled = true;
                                            }
                                            _irstsEnabled = true;
                                        }
                                }

                            }
                            if (ml.TargetingMode == MissileBase.TargetingModes.Laser)
                            {
                                if (targetingPods.Count > 0) //if targeting pods are available, slew them onto target and lock.
                                {
                                    using (List<ModuleTargetingCamera>.Enumerator tgp = targetingPods.GetEnumerator())
                                        while (tgp.MoveNext())
                                        {
                                            if (tgp.Current == null) continue;
                                            tgp.Current.EnableCamera();
                                        }
                                }
                            }
                            MissileLauncher mlauncher = ml as MissileLauncher;

                            unguidedWeapon = UnguidedMissile(ml, distanceToTarget);

                            // check DLZ                            
                            float fireFOV = -1;

                            if (mlauncher != null) //else MMGs throw an Error here
                                fireFOV = mlauncher.missileTurret ? mlauncher.missileTurret.turret.yawRange : mlauncher.multiLauncher && mlauncher.multiLauncher.turret ? mlauncher.multiLauncher.turret.turret.yawRange : -1;
                            MissileLaunchParams dlz = MissileLaunchParams.GetDynamicLaunchParams(ml, targetVessel.Velocity(), targetVessel.transform.position, fireFOV, unguidedWeapon);
                            if (vessel.srfSpeed > ml.minLaunchSpeed && distanceToTarget < dlz.maxLaunchRange && distanceToTarget > dlz.minLaunchRange)
                            {
                                //old radar/ins special conditions would prevent these missile types from ever being dumbfired, now covered by above unguidedWeapon condition
                                if (BDArmorySettings.DEBUG_MISSILES)
                                {
                                    Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} - Firing possible with {weaponCandidate.GetShortName()}");
                                }
                                return true;
                            }
                            if (BDArmorySettings.DEBUG_MISSILES)
                            {
                                Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} - Failed DLZ test: {weaponCandidate.GetShortName()}, distance: {distanceToTarget}, DLZ min/max: {dlz.minLaunchRange}/{dlz.maxLaunchRange}");
                            }
                            break;
                        }

                    case WeaponClasses.Bomb:
                        if (distanceToTarget < engageableWeapon.GetEngagementRangeMin()) return false;
                        if (!vessel.LandedOrSplashed) // TODO: bomb always allowed?
                            using (var bomb = VesselModuleRegistry.GetModules<MissileBase>(vessel).GetEnumerator())
                                while (bomb.MoveNext())
                                {
                                    if (bomb.Current == null) continue;
                                    if (bomb.Current.launched) continue;
                                    return true;
                                }
                        break;

                    case WeaponClasses.Rocket:
                        {
                            ModuleWeapon rocket = (ModuleWeapon)weaponCandidate;
                            if (distanceToTarget < rocket.minSafeDistance) return false;

                            // check yaw range of turret
                            ModuleTurret turret = rocket.turret;
                            float gimbalTolerance = vessel.LandedOrSplashed ? 0 : 15;
                            if (turret != null)
                                if (!TargetInTurretRange(turret, gimbalTolerance, default, rocket))
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
                                    Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} - Firing possible with {weaponCandidate.GetShortName()}");
                                }
                                return true;
                            }
                            break;
                        }

                    case WeaponClasses.SLW:
                        {
                            MissileBase ml = (MissileBase)weaponCandidate;
                            if (distanceToTarget < engageableWeapon.GetEngagementRangeMin()) return false;

                            // Enable sonar, or radar, if no sonar is found.
                            if (((MissileBase)weaponCandidate).TargetingMode == MissileBase.TargetingModes.Radar)
                            {
                                if (results.foundTorpedo && results.foundHeatMissile && DynamicRadarOverride) return false; // Don't try to fire active sonar torps while we have an incoming passive sonar torp

                                using (List<ModuleRadar>.Enumerator rd = radars.GetEnumerator())
                                    while (rd.MoveNext())
                                    {
                                        if (rd.Current != null && rd.Current.sonarMode == ModuleRadar.SonarModes.Active)
                                        {
                                            if (results.foundTorpedo && results.foundHeatMissile && rd.Current.DynamicRadar) continue;
                                            rd.Current.EnableRadar();
                                            _sonarsEnabled = true;
                                        }

                                    }
                            }
                            if (((MissileBase)weaponCandidate).TargetingMode == MissileBase.TargetingModes.Inertial)
                            {
                                using (List<ModuleRadar>.Enumerator rd = radars.GetEnumerator())
                                    while (rd.MoveNext())
                                    {
                                        if (rd.Current != null && rd.Current.sonarMode != ModuleRadar.SonarModes.None)
                                        {
                                            if (rd.Current.sonarMode == ModuleRadar.SonarModes.Active && results.foundTorpedo && results.foundHeatMissile && rd. Current.DynamicRadar) continue;
                                            rd.Current.EnableRadar();
                                            _sonarsEnabled = true;
                                        }
                                        float scanSpeed = (rd.Current.locked && rd.Current.lockedTarget.vessel == targetVessel) ? rd.Current.multiLockFOV : rd.Current.directionalFieldOfView / rd.Current.scanRotationSpeed * 2;
                                        if (GpsUpdateMax > 0 && scanSpeed < GpsUpdateMax) GpsUpdateMax = scanSpeed;
                                    }
                            }
                            MissileLauncher mlauncher = ml as MissileLauncher;

                            unguidedWeapon = ml.GuidanceMode == MissileBase.GuidanceModes.None || UnguidedMissile(ml, distanceToTarget);
                            return true;
                        }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            catch (Exception e)
            {
                var excDetails = e.Message;
#if DEBUG
                excDetails += $"\n{e.StackTrace}"; // Add a stacktrace of the original exception for debug builds.
#endif
                Debug.LogError($"[BDArmory.MissileFire]: Exception thrown while checking the engagement envelope of {(weaponCandidate != null ? weaponCandidate.GetPartName() : "NULL weapon")} against {(targetVessel != null ? targetVessel.vesselName : "NULL vessel")}: {excDetails}");
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
                if (multiTargetNum > 1 || multiMissileTgtNum > 1)
                {
                    SmartFindSecondaryTargets();
                    using (List<TargetInfo>.Enumerator secTgt = targetsAssigned.GetEnumerator())
                        while (secTgt.MoveNext())
                        {
                            if (secTgt.Current == null) continue;
                            if (secTgt.Current == currentTarget) continue;
                            secTgt.Current.Engage(this);
                        }
                    using (List<TargetInfo>.Enumerator mslTgt = missilesAssigned.GetEnumerator())
                        while (mslTgt.MoveNext())
                        {
                            if (mslTgt.Current == null) continue;
                            if (mslTgt.Current == currentTarget) continue;
                            mslTgt.Current.Engage(this);
                        }
                }
                MissileBase ml = CurrentMissile;
                MissileBase pMl = PreviousMissile;
                if (!ml && pMl) ml = PreviousMissile; //if fired missile, then switched to guns or something

                if (vesselRadarData != null && (!vesselRadarData.locked || vesselRadarData.lockedTargetData.vessel != guardTarget))
                {
                    if (!vesselRadarData.locked)
                    {
                        vesselRadarData.TryLockTarget(guardTarget);
                    }
                    else
                    {
                        if (firedMissiles >= maxMissilesOnTarget && (multiMissileTgtNum > 1 && BDATargetManager.TargetList(Team).Count > 1)) //if there are multiple potential targets, see how many can be fired at with missiles
                        {
                            if (ml && !ml.radarLOAL) //switch active lock instead of clearing locks for SARH missiles
                            {
                                //vesselRadarData.UnlockCurrentTarget();
                                vesselRadarData.TryLockTarget(guardTarget);
                            }
                            else
                                vesselRadarData.SwitchActiveLockedTarget(guardTarget);
                        }
                        else
                        {
                            if (PreviousMissile != null && PreviousMissile.ActiveRadar && PreviousMissile.targetVessel != null) //previous missile has gone active, don't need that lock anymore
                            {
                                vesselRadarData.UnlockSelectedTarget(PreviousMissile.targetVessel.Vessel);
                            }
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
                staleTarget = false; //reset staletarget bool if no target
            }
        }

        #endregion Smart Targeting
        public float detectedTargetTimeout = 0;
        public bool staleTarget = false;

        FloatCurve SurfaceVisionOffset = null;

        public bool CanSeeTarget(TargetInfo target, bool checkForNonVisualDetection = true, bool checkForstaleTarget = true)
        {
            // fix cheating: we can see a target IF we either have a visual on it, OR it has been detected on radar/sonar/IRST
            // but to prevent AI from stopping an engagement just because a target dropped behind a small hill 5 seconds ago, clamp the timeout to 30 seconds
            // i.e. let's have at least some object permanence :)
            // If we can't directly see the target via sight or radar, AI will head to last known position of target, based on target's vector at time contact was lost,
            // with precision of estimated position degrading over time.

            //extend to allow teamamtes provide vision? Could count scouted tarets as stale to prevent precise targeting, but at least let AI know something is out there

            // can we get a visual sight of the target?

            if (SurfaceVisionOffset == null)
            {
                SurfaceVisionOffset = new FloatCurve();
                SurfaceVisionOffset.Add(1500, 1.88f);
                SurfaceVisionOffset.Add(2000, 3.35f);
                SurfaceVisionOffset.Add(3000, 7.5f);
                SurfaceVisionOffset.Add(4000, 13.35f);
                SurfaceVisionOffset.Add(5000, 20.85f);
                SurfaceVisionOffset.Add(6000, 30f);
                SurfaceVisionOffset.Add(8000, 53.4f);
                SurfaceVisionOffset.Add(10000, 83.4f);
            }
            if (target == null || target.Vessel == null) return false;
            VesselCloakInfo vesselcamo = target.Vessel.gameObject.GetComponent<VesselCloakInfo>();
            float viewModifier = 1;
            if (vesselcamo && vesselcamo.cloakEnabled)
            {
                viewModifier = vesselcamo.opticalReductionFactor;
            }
            //Can the target be seen?
            float visDistance = guardRange;
            if (BDArmorySettings.UNDERWATER_VISION && (this.vessel.IsUnderwater() || target.Vessel.IsUnderwater())) visDistance = 100;
            visDistance *= viewModifier;
            float objectPermanenceThreshold = (target.Vessel.LandedOrSplashed && target.Vessel.srfSpeed < 10) ? 30 * (10 - (float)target.Vessel.srfSpeed) : 30; //have slow/stationary targets have much longer timeouts since they are't going anywhere.
                                                                                                                                                                //needs to use lastGoodVesselVel, not current speed, since if we can't see it, we can't know how fast it's going
            if ((target.Vessel.transform.position - transform.position).sqrMagnitude < (visDistance * visDistance) &&
            Vector3.Angle(-vessel.ReferenceTransform.forward, target.Vessel.transform.position - vessel.CoM) < (guardAngle * 1.1f) / 2)
            {
                if ((target.Vessel.LandedOrSplashed && vessel.LandedOrSplashed) && ((target.Vessel.transform.position - transform.position).sqrMagnitude > 2250000f)) //land Vee vs land Vee will have a max of ~1.8km viewDist, due to curvature of Kerbin
                {
                    Vector3 targetDirection = (target.Vessel.transform.position - transform.position).ProjectOnPlanePreNormalized(VectorUtils.GetUpDirection(transform.position));
                    if (RadarUtils.TerrainCheck(target.Vessel.CoM + ((target.Vessel.vesselSize.y / 2) * VectorUtils.GetUpDirection(transform.position)), vessel.CoM + (SurfaceVisionOffset.Evaluate((target.Vessel.transform.position - transform.position).magnitude) * VectorUtils.GetUpDirection(transform.position)), FlightGlobals.currentMainBody)
                        || RadarUtils.TerrainCheck(targetDirection, vessel.CoM, FlightGlobals.currentMainBody)) ////target more than 1.5km away, do a paired raycast looking straight, and a raycast using an offset to adjust the horizonpoint to the target, should catch majority of intervening terrain. Clamps to 10km; beyond that, spotter (air)craft will be needed to share vision
                    {
                        if (target.detectedTime.TryGetValue(Team, out float detectedTime) && Time.time - detectedTime < Mathf.Max(objectPermanenceThreshold, targetScanInterval)) //intervening terrain, has an ally seen the target?
                        {
                            //Debug.Log($"[BDArmory.MissileFire]: {target.name} last seen {Time.time - detectedTime} seconds ago. Recalling last known position");
                            detectedTargetTimeout = Time.time - detectedTime;
                            staleTarget = true;
                            return true;
                        }
                        staleTarget = true;
                        return false;
                    }
                }
                else//target/vessel is flying, or ground Vees are within 1.5km of each other, standard LoS checks
                {
                    if (RadarUtils.TerrainCheck((vessel.LandedOrSplashed ? target.Vessel.CoM + (VectorUtils.GetUpDirection(transform.position) * (target.Vessel.vesselSize.y / 2)) : target.Vessel.CoM), transform.position, FlightGlobals.currentMainBody))
                    {
                        if (target.detectedTime.TryGetValue(Team, out float detectedTime) && Time.time - detectedTime < Mathf.Max(objectPermanenceThreshold, targetScanInterval))
                        {
                            //Debug.Log($"[BDArmory.MissileFire]: {target.name} last seen {Time.time - detectedTime} seconds ago. Recalling last known position");
                            detectedTargetTimeout = Time.time - detectedTime;
                            staleTarget = true;
                            return true;
                        }
                        staleTarget = true;
                        return false;
                    }
                }

                detectedTargetTimeout = 0;
                staleTarget = false;
                return true;
            }
            if (checkForNonVisualDetection)
            {
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
            }

            //can't see target, but did we see it recently?
            if (checkForstaleTarget) //merely look to see if a target was last detected within 30s
            {
                if (target.detectedTime.TryGetValue(Team, out float detectedTime) && Time.time - detectedTime < Mathf.Max(objectPermanenceThreshold, targetScanInterval))
                {
                    //Debug.Log($"[BDArmory.MissileFire]: {target.name} last seen {Time.time - detectedTime} seconds ago. Recalling last known position");
                    detectedTargetTimeout = Time.time - detectedTime;
                    staleTarget = true;
                    return true;
                }
                return false; //target long gone
            }
            return false;
        }

        /// <summary>
        /// Check to see if an incoming missile is visible based on guardRange and missile state
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        public bool CanSeeTarget(MissileBase target)
        {
            // can we get a visual sight of the target?
            float visrange = guardRange;
            if (BDArmorySettings.VARIABLE_MISSILE_VISIBILITY)
            {
                visrange *= target.MissileState == MissileBase.MissileStates.Boost ? 1 : (target.MissileState == MissileBase.MissileStates.Cruise ? 0.75f : 0.33f);
            }
            if ((target.transform.position - transform.position).sqrMagnitude < visrange * visrange)
            {
                if (RadarUtils.TerrainCheck(target.transform.position, transform.position, FlightGlobals.currentMainBody))
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
            if (!ml || !(ml.TargetingMode == MissileBase.TargetingModes.Laser || ml.TargetingMode == MissileBase.TargetingModes.Gps))
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

        void SearchForHeatTarget(MissileBase currMissile, TargetInfo targetMissile = null)
        {
            if (currMissile != null)
            {
                if (!currMissile || currMissile.TargetingMode != MissileBase.TargetingModes.Heat)
                {
                    return;
                }
                float scanRadius = currMissile.lockedSensorFOV * 0.5f;
                float maxOffBoresight = currMissile.maxOffBoresight * 0.85f;

                if (vesselRadarData) // && !currMissile.IndependantSeeker) //missile with independantSeeker can't get targetdata from radar/IRST
                {
                    if (currMissile.GuidanceMode != MissileBase.GuidanceModes.SLW || currMissile.GuidanceMode == MissileBase.GuidanceModes.SLW && currMissile.activeRadarRange > 0) //heatseeking missiles/torps
                    {
                        if (_irstsEnabled)
                        {
                            if (targetMissile == null)
                                heatTarget = vesselRadarData.activeIRTarget(guardTarget, this); //point seeker at active target's IR return
                            else
                                heatTarget = vesselRadarData.activeIRTarget(targetMissile.Vessel, this);
                        }
                        else
                        {
                            if (vesselRadarData.locked)
                            {
                                if (targetMissile == null) //uncaged radar lock
                                    heatTarget = vesselRadarData.lockedTargetData.targetData;
                                else //since it's probable that the Wm is locked to the current guardTarget, but not that incoming missile we're trying to acquire for intercept
                                {
                                    List<TargetSignatureData> possibleTargets = vesselRadarData.GetLockedTargets();
                                    for (int i = 0; i < possibleTargets.Count; i++)
                                    {
                                        if (possibleTargets[i].vessel == targetMissile.Vessel)
                                        {
                                            heatTarget = possibleTargets[i];
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else //active sonar torps
                    {
                        heatTarget = vesselRadarData.detectedRadarTarget(guardTarget, this); //get initial direction for passive sonar torps from passive/non-locking sonar return
                    }
                }
                Vector3 direction =
                    heatTarget.exists && Vector3.Angle(heatTarget.position - currMissile.MissileReferenceTransform.position, currMissile.GetForwardTransform()) < maxOffBoresight ?
                    heatTarget.predictedPosition - currMissile.MissileReferenceTransform.position
                    : currMissile.GetForwardTransform();
                // remove AI target check/move to a missile .cfg option to allow older gen heaters?
                if (currMissile.GuidanceMode != MissileBase.GuidanceModes.SLW || currMissile.GuidanceMode == MissileBase.GuidanceModes.SLW && currMissile.activeRadarRange > 0)
                    heatTarget = BDATargetManager.GetHeatTarget(vessel, vessel, new Ray(currMissile.MissileReferenceTransform.position + (50 * currMissile.GetForwardTransform()), direction), TargetSignatureData.noTarget, scanRadius, currMissile.heatThreshold, currMissile.frontAspectHeatModifier, currMissile.uncagedLock, currMissile.targetCoM, currMissile.lockedSensorFOVBias, currMissile.lockedSensorVelocityBias, this, targetMissile != null ? targetMissile : guardMode ? currentTarget : null);
                else heatTarget = BDATargetManager.GetAcousticTarget(vessel, vessel, new Ray(currMissile.MissileReferenceTransform.position + (50 * currMissile.GetForwardTransform()), direction), TargetSignatureData.noTarget, scanRadius, currMissile.heatThreshold, currMissile.targetCoM, currMissile.lockedSensorFOVBias, currMissile.lockedSensorVelocityBias, this, targetMissile != null ? targetMissile : guardMode ? currentTarget : null);
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

        public void SendTargetDataToMissile(MissileBase ml, bool clearHeat = true)
        { //TODO BDModularGuidance: implement all targetings on base
            bool dumbfire = false;
            bool validTarget = false;
            Vessel targetVessel = missileTarget != null ? missileTarget : (guardTarget != null ? guardTarget : null);
            switch (ml.TargetingMode)
            {
                case MissileBase.TargetingModes.Laser:
                    {
                        if (laserPointDetected)
                        {
                            ml.lockedCamera = foundCam;
                            if (guardMode && guardTarget != null && (foundCam.groundTargetPosition - guardTarget.CoM).sqrMagnitude < 10 * 10) validTarget = true; //*highly* unlikely laser-guided missiles used for missile interception, so leaving these guardTarget
                        }
                        else
                        {
                            dumbfire = true;
                            validTarget = true;
                        }
                        break;
                    }
                case MissileBase.TargetingModes.Gps:
                    {
                        if (designatedGPSCoords != Vector3d.zero)
                        {
                            ml.targetGPSCoords = designatedGPSCoords;
                            ml.TargetAcquired = true;
                            if (laserPointDetected)
                                ml.lockedCamera = foundCam;
                            if (guardMode && GPSDistanceCheck()) validTarget = true;
                        }
                        else if (ml.GetWeaponClass() == WeaponClasses.Bomb)
                        {
                            dumbfire = true;
                            validTarget = true;
                        }
                        break;
                    }
                case MissileBase.TargetingModes.Heat:
                    {
                        if (heatTarget.exists)
                        {
                            ml.heatTarget = heatTarget;
                            if (clearHeat) heatTarget = TargetSignatureData.noTarget;

                            var heatTgtVessel = ml.heatTarget.vessel.gameObject;
                            if (heatTgtVessel) ml.targetVessel = heatTgtVessel.GetComponent<TargetInfo>();
                        }
                        break;
                    }
                case MissileBase.TargetingModes.Radar:
                    {
                        if (vesselRadarData && vesselRadarData.locked)//&& radar && radar.lockedTarget.exists)
                        {
                            if (targetVessel != null)
                            {
                                List<TargetSignatureData> possibleTargets = vesselRadarData.GetLockedTargets();
                                for (int i = 0; i < possibleTargets.Count; i++)
                                {
                                    if (possibleTargets[i].vessel == targetVessel)
                                    {
                                        ml.radarTarget = possibleTargets[i]; //send correct targetlock if firing multiple SARH missiles
                                    }
                                }
                            }
                            else ml.radarTarget = vesselRadarData.lockedTargetData.targetData;
                            ml.vrd = vesselRadarData;
                            vesselRadarData.LastMissile = ml;

                            var radarTgtvessel = vesselRadarData.lockedTargetData.targetData.vessel.gameObject;
                            if (radarTgtvessel) ml.targetVessel = radarTgtvessel.GetComponent<TargetInfo>();
                        }
                        else
                        {
                            dumbfire = true;
                            validTarget = true;
                        }
                        break;
                    }
                case MissileBase.TargetingModes.AntiRad:
                    {
                        if (antiRadTargetAcquired && antiRadiationTarget != Vector3.zero)
                        {
                            ml.TargetAcquired = true;
                            ml.targetGPSCoords = VectorUtils.WorldPositionToGeoCoords(antiRadiationTarget, vessel.mainBody);

                            if (AntiRadDistanceCheck()) validTarget = true;
                        }
                        break;
                    }
                case MissileBase.TargetingModes.None:
                    {
                        ml.TargetAcquired = true;
                        validTarget = true;
                        break;
                    }
                case MissileBase.TargetingModes.Inertial:
                    {
                        if (designatedINSCoords != Vector3d.zero) //coords from GRM under AI control
                        {
                            ml.targetGPSCoords = designatedINSCoords;
                            ml.TargetAcquired = true;
                            validTarget = true;
                        }
                        else
                        {
                            if (vesselRadarData)
                            {
                                if (vesselRadarData.locked) //grab target from pirmary lock
                                {
                                    targetVessel = vesselRadarData.lockedTargetData.targetData.vessel;
                                    validTarget = true;
                                    vesselRadarData.LastMissile = ml;
                                }
                                else if (_irstsEnabled) //or brightest ping on IRST
                                {
                                    targetVessel = vesselRadarData.activeIRTarget(null, this).vessel;
                                    validTarget = targetVessel;
                                }
                            }
                            if (validTarget)
                            {
                                Vector3 TargetLead = MissileGuidance.GetAirToAirFireSolution(ml, targetVessel.CoM, targetVessel.Velocity());
                                //designatedGPSInfo = new GPSTargetInfo(VectorUtils.WorldPositionToGeoCoords(TargetLead, targetVessel.mainBody), targetVessel.vesselName.Substring(0, Mathf.Min(12, targetVessel.vesselName.Length)));
                                designatedINSCoords = VectorUtils.WorldPositionToGeoCoords(TargetLead, targetVessel.mainBody);
                            }
                            else
                            {
                                if (ml.GetWeaponClass() == WeaponClasses.Bomb)
                                {
                                    dumbfire = true;
                                    validTarget = true;
                                    ml.TargetAcquired = false;
                                    break;
                                }
                                else
                                {
                                    //designatedGPSInfo = new GPSTargetInfo(VectorUtils.WorldPositionToGeoCoords(ml.MissileReferenceTransform.position + ml.MissileReferenceTransform.forward * 10000, vessel.mainBody), "null target");
                                    designatedINSCoords = VectorUtils.WorldPositionToGeoCoords(ml.MissileReferenceTransform.position + ml.MissileReferenceTransform.forward * 10000, vessel.mainBody);
                                }

                            }
                            ml.targetGPSCoords = designatedINSCoords;
                            ml.TargetINSCoords = VectorUtils.WorldPositionToGeoCoords(targetVessel.CoM, vessel.mainBody);
                            ml.TargetAcquired = true;
                        }
                        designatedINSCoords = Vector3d.zero;
                        break;
                    }
                default:
                    {
                        if (ml.GetWeaponClass() == WeaponClasses.Bomb)
                        {
                            validTarget = true;
                        }
                        break;
                    }
            }
            if (validTarget && targetVessel != null)
            {
                ml.targetVessel = targetVessel.gameObject ? targetVessel.gameObject.GetComponent<TargetInfo>() : null;
            }
            if (BDArmorySettings.DEBUG_MISSILES)
            {
                Debug.Log($"[BDArmory.MissileData]: Sending targetInfo to {(dumbfire ? "dumbfire " : "")}{Enum.GetName(typeof(MissileBase.TargetingModes), ml.TargetingMode)} Missile...");
                if (ml.targetVessel != null) Debug.Log($"[BDArmory.MissileData]: targetInfo sent for {ml.targetVessel.Vessel.GetName()}");
            }
            if (BDArmorySettings.DEBUG_MISSILES)
                Debug.Log($"[BDArmory.MissileData]: firing missile at {(targetVessel != null ? targetVessel.GetName() : "null target")}");
        }

        #endregion Targeting

        #region Guard

        public void ResetGuardInterval()
        {
            targetScanTimer = 0;
        }

        void GuardMode()
        {
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
                                weapon.Current.StandbyWeapon();
                                weapon.Current.aiControlled = true;
                            }
                            continue;
                        }
                        weapon.Current.EnableWeapon();
                        if (weapon.Current.dualModeAPS) weapon.Current.isAPS = false;
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
                        if (weapon.Current.isAPS) continue;
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
                    // StartCoroutine(LegacyCMRoutine()); // Deprecated
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
                            if (CurrentMissile != null) // Reloadable rails can give a null missile.
                            {
                                bool launchAuthorized = true;
                                bool pilotAuthorized = true;
                                //(!pilotAI || pilotAI.GetLaunchAuthorization(guardTarget, this));

                                if (selectedWeapon.GetWeaponClass() == WeaponClasses.Missile && vessel.Splashed && vessel.altitude < -10)
                                {
                                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileFire] missile below launch depth");
                                    launchAuthorized = false; //submarine below launch depth
                                }
                                if (selectedWeapon.GetWeaponClass() == WeaponClasses.SLW && !vessel.LandedOrSplashed && pilotAI && vessel.altitude > 50) launchAuthorized = false; //don't torpedo bomb from high up, the torp's won't survive water impact
                                //float targetAngle = Vector3.Angle(-transform.forward, guardTarget.transform.position - transform.position);
                                float targetAngle = Vector3.Angle(CurrentMissile.MissileReferenceTransform.forward, guardTarget.transform.position - transform.position);
                                float targetDistance = Vector3.Distance(currentTarget.position, transform.position);
                                if (!vessel.Splashed && !guardTarget.Splashed)
                                {
                                    if (RadarUtils.TerrainCheck(guardTarget.CoM, CurrentMissile.transform.position)) //vessel behind terrain. exception for ships where curvature of Kerbin comes into play
                                    {
                                        launchAuthorized = false;
                                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileFire] target behind terrain");
                                    }
                                }
                                MissileLaunchParams dlz = MissileLaunchParams.GetDynamicLaunchParams(CurrentMissile, guardTarget.Velocity(), guardTarget.CoM, -1, unguidedWeapon); // (CurrentMissile.TargetingMode == MissileBase.TargetingModes.Laser
                                                                                                                                                                                   // && BDATargetManager.ActiveLasers.Count <= 0 || CurrentMissile.TargetingMode == MissileBase.TargetingModes.Radar && !_radarsEnabled && !CurrentMissile.radarLOAL));
                                if (targetAngle > guardAngle / 2) //dont fire yet if target out of guard angle
                                {
                                    launchAuthorized = false;
                                }
                                else if (targetDistance >= dlz.maxLaunchRange || targetDistance <= dlz.minLaunchRange)  //fire the missile only if target is further than missiles min launch range
                                {
                                    launchAuthorized = false;
                                }
                                if (unguidedWeapon && targetDistance > CurrentMissile.GetEngagementRangeMax() / 10) launchAuthorized = false;
                                if (engagedTargets > multiMissileTgtNum) launchAuthorized = false; //already fired on max allowed targets
                                // Check that launch is possible before entering GuardMissileRoutine, or that missile is on a turret
                                MissileLauncher ml = CurrentMissile as MissileLauncher;
                                launchAuthorized = launchAuthorized && (GetLaunchAuthorization(guardTarget, this, CurrentMissile) || (ml is not null && (ml.missileTurret || (ml.multiLauncher && ml.multiLauncher.turret))));


                                if (BDArmorySettings.DEBUG_MISSILES)
                                    Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName}  launchAuth={launchAuthorized}, pilotAut={pilotAuthorized}, missilesAway/Max={firedMissiles}/{maxMissilesOnTarget}");

                                if (firedMissiles < maxMissilesOnTarget)
                                {
                                    if (CurrentMissile.TargetingMode == MissileBase.TargetingModes.Radar && (CurrentMissile.GetWeaponClass() == WeaponClasses.SLW ? _sonarsEnabled : _radarsEnabled) && !CurrentMissile.radarLOAL && MaxradarLocks < vesselRadarData.GetLockedTargets().Count)
                                    {
                                        launchAuthorized = false; //don't fire SARH if radar can't support the needed radar lock
                                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileFire]: radar lock number exceeded to launch!");
                                    }

                                    if (!guardFiringMissile && launchAuthorized)
                                    //&& (CurrentMissile.TargetingMode != MissileBase.TargetingModes.Radar || (vesselRadarData != null && (!vesselRadarData.locked || vesselRadarData.lockedTargetData.vessel == guardTarget)))) // Allow firing multiple missiles at the same target. FIXME This is a stop-gap until proper multi-locking support is available.
                                    {
                                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} firing {(unguidedWeapon ? "unguided" : "")} missile");
                                        StartCoroutine(GuardMissileRoutine(guardTarget, CurrentMissile));
                                    }
                                }
                                else if (BDArmorySettings.DEBUG_MISSILES)
                                {
                                    Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName}  waiting for missile to be ready...");
                                }

                                // if (!launchAuthorized || !pilotAuthorized || missilesAway >= maxMissilesOnTarget)
                                // {
                                //     targetScanTimer -= 0.5f * targetScanInterval;
                                // }
                            }
                        }
                        else if (selectedWeapon != null && selectedWeapon.GetWeaponClass() == WeaponClasses.Bomb)
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
            results = RadarUtils.GuardScanInDirection(this, transform, guardAngle, guardRange, rwr);
            incomingThreatVessel = null;
            if (results.foundMissile)
            {
                if (rwr && (rwr.omniDetection || results.foundRadarMissile)) //enable omniRWRs for all incoming threats. Moving this here as RWRs would be detecting missiles before they reached danger close
                {
                    if (!rwr.rwrEnabled) rwr.EnableRWR();
                    if (rwr.rwrEnabled && !rwr.displayRWR) rwr.displayRWR = true;
                }
            }
            if (results.foundMissile && (results.incomingMissiles[0].distance < guardRange || results.incomingMissiles[0].time < Mathf.Max(cmThreshold, evadeThreshold))) //RWR detects things beyond visual range, allow reaction to detected high-velocity missiles where waiting till visrange would leave very little time to react
            {
                if (BDArmorySettings.DEBUG_AI && (!missileIsIncoming || results.incomingMissiles[0].distance < 1000f))
                {
                    foreach (var incomingMissile in results.incomingMissiles)
                        Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} incoming missile ({incomingMissile.vessel.vesselName} of type {incomingMissile.guidanceType} from {(incomingMissile.weaponManager != null && incomingMissile.weaponManager.vessel != null ? incomingMissile.weaponManager.vessel.vesselName : "unknown")}) found at distance {incomingMissile.distance} m");
                }
                missileIsIncoming = true;
                incomingMissileLastDetected = Time.time;
                // Assign the closest missile as the main threat. FIXME In the future, we could do something more complex to handle all the incoming missiles.
                incomingMissileDistance = results.incomingMissiles[0].distance;
                incomingMissileTime = results.incomingMissiles[0].time;
                incomingThreatPosition = results.incomingMissiles[0].position;
                incomingThreatVessel = results.incomingMissiles[0].vessel;
                incomingMissileVessel = results.incomingMissiles[0].vessel;
                //radar missiles
                if (!results.foundTorpedo)
                {
                    //if (results.foundMissile) //make sure underAttack still gets called if incoming missile is GPS/INS
                    StartCoroutine(UnderAttackRoutine());
                    if (results.foundRadarMissile) //have this require an RWR?
                    {
                        //if (!rwr && incomingMissileDistance <= guardRange * 0.33f) //within ID range?
                        //StartCoroutine(UnderAttackRoutine());

                        FireChaff();
                        FireECM(10);
                    }
                    //laser missiles
                    if (results.foundAGM) //Assume Laser Warning Receiver regardless of omniDetection? Or move laser missiles to the passive missiles section?
                    {
                        //StartCoroutine(UnderAttackRoutine());

                        FireSmoke();
                        if (targetMissiles && guardTarget == null)
                        {
                            //targetScanTimer = Mathf.Min(targetScanInterval, Time.time - targetScanInterval + 0.5f);
                            targetScanTimer -= targetScanInterval / 2;
                        }
                    }
                    //passive missiles
                    if (results.foundHeatMissile || results.foundAntiRadiationMissile || results.foundGPSMissile)
                    {
                        if (rwr && rwr.omniDetection)
                        {
                            if (results.foundHeatMissile)
                            {
                                FireFlares();
                                FireOCM(true);
                            }
                            if (results.foundAntiRadiationMissile && DynamicRadarOverride)
                            {
                                using (List<ModuleRadar>.Enumerator rd = radars.GetEnumerator())
                                    while (rd.MoveNext())
                                    {
                                        if (rd.Current != null && rd.Current.DynamicRadar)
                                            rd.Current.DisableRadar();
                                    }
                                _radarsEnabled = false;
                                FireECM(0); //disable jammers
                            }

                            if (results.foundGPSMissile)
                                FireECM(cmThreshold);
                            //StartCoroutine(UnderAttackRoutine());
                        }
                        else //one passive missile is going to be indistinguishable from another, until it gets close enough to evaluate
                        {
                            if (vessel.LandedOrSplashed) //assume antirads against ground targets
                            {
                                if (radars.Count > 0 && DynamicRadarOverride)
                                {
                                    using (List<ModuleRadar>.Enumerator rd = radars.GetEnumerator())
                                        while (rd.MoveNext())
                                        {
                                            if (rd.Current != null && rd.Current.DynamicRadar)
                                                rd.Current.DisableRadar();
                                            _radarsEnabled = false;
                                        }
                                }

                                if (incomingMissileDistance <= guardRange * 0.33f) //within ID range?
                                {
                                    if (results.foundGPSMissile)
                                        FireECM(cmThreshold); //try to jam datalink to launcher/GPS
                                    if (results.foundHeatMissile) //AtG heater!? Flares!
                                    {
                                        FireFlares();
                                        FireOCM(true);
                                    }
                                }
                            }
                            else //likely a heatseeker, but could be an AA HARM...
                            {
                                if (incomingMissileDistance <= guardRange * 0.33f) //within ID range?
                                {
                                    if (results.foundHeatMissile)
                                    {
                                        FireFlares();
                                        FireOCM(true);
                                    }
                                    else if (results.foundGPSMissile)
                                    {
                                        FireECM(cmThreshold);
                                    }
                                    else //it's an Antirad!? Uh-oh, blip radar!
                                    {
                                        if (radars.Count > 0 && DynamicRadarOverride)
                                        {
                                            using (List<ModuleRadar>.Enumerator rd = radars.GetEnumerator())
                                                while (rd.MoveNext())
                                                {
                                                    if (rd.Current != null && rd.Current.DynamicRadar)
                                                        rd.Current.DisableRadar();
                                                }
                                            _radarsEnabled = false;
                                        }
                                        FireECM(0);//uh oh, blip ECM!
                                    }
                                }
                                else //assume heater
                                {
                                    FireFlares();
                                    FireOCM(true);
                                }
                            }
                            //StartCoroutine(UnderAttackRoutine());
                        }
                    }
                }
                else
                {
                    StartCoroutine(UnderAttackRoutine());
                    if (results.foundHeatMissile) //standin for passive acoustic homing. Will have to expand this if facing *actual* heat-seeking torpedoes
                    {
                        if ((rwr && rwr.omniDetection) || (incomingMissileDistance <= Mathf.Min(guardRange * 0.33f, 2500))) //within ID range?
                        {
                            if (radars.Count > 0)
                            {
                                using (List<ModuleRadar>.Enumerator rd = radars.GetEnumerator())
                                    while (rd.MoveNext())
                                    {
                                        if (rd.Current != null && rd.Current.sonarMode == ModuleRadar.SonarModes.Active) //kill active sonar
                                            rd.Current.DisableRadar();
                                    }
                                _sonarsEnabled = false;
                            }
                            FireECM(0); // kill active noisemakers
                            FireDecoys();
                        }
                    }
                    if (results.foundRadarMissile) //standin for active sonar
                    {
                        FireBubbles();
                        FireECM(10);
                    }
                    if (results.foundGPSMissile) //not really sure what you'd do vs a wireguided/INS torpedo - kill engines and active sonar + fire decoys to try and break detection by op4 passive sonar? fire bubblers to garble active sonar detection?
                    {                        
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
                incomingThreatDistanceSqr = (results.threatPosition - vessel.transform.position).sqrMagnitude;
                if ((pilotAI != null && incomingMissTime >= pilotAI.evasionTimeThreshold && incomingMissDistance < pilotAI.evasionThreshold) || AI != null && pilotAI == null) // If we haven't been under fire long enough, ignore gunfire
                {
                    FireOCM(false); //enable visual countermeasures if under fire
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
                                    Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} called for help from {nearbyFriendly.Vessel.vesselName} and took its target in return");
                                //basically, swap targets to cover each other
                            }
                            else
                            {
                                //otherwise, continue engaging the current target for now
                                nearbyFriendly.weaponManager.SetOverrideTarget(nearbyThreat);
                                if (BDArmorySettings.DEBUG_AI)
                                    Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} called for help from {nearbyFriendly.Vessel.vesselName}");
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
            int MissileTgtID = 0;
            List<TargetInfo> firedTargets = new List<TargetInfo>();
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
                            if (TurretID >= Mathf.Min((targetsAssigned.Count), multiTargetNum))
                            {
                                TurretID = 0; //if more turrets than targets, loop target list
                            }
                            if (targetsAssigned.Count > 0 && targetsAssigned[TurretID].Vessel != null)
                            {
                                if (((weapon.Current.engageAir && targetsAssigned[TurretID].isFlying) ||
                                    (weapon.Current.engageGround && targetsAssigned[TurretID].isLandedOrSurfaceSplashed) ||
                                    (weapon.Current.engageSLW && targetsAssigned[TurretID].isUnderwater)) //check engagement envelope
                                    && TargetInTurretRange(weapon.Current.turret, 7, targetsAssigned[TurretID].Vessel.CoM, weapon.Current))
                                {
                                    weapon.Current.visualTargetVessel = targetsAssigned[TurretID].Vessel; // if target within turret fire zone, assign
                                    firedTargets.Add(targetsAssigned[TurretID]);
                                }
                                else //else try remaining targets
                                {
                                    using (List<TargetInfo>.Enumerator item = targetsAssigned.GetEnumerator())
                                        while (item.MoveNext())
                                        {
                                            if (item.Current.Vessel == null) continue;
                                            if ((weapon.Current.engageAir && !item.Current.isFlying) ||
                                            (weapon.Current.engageGround && !item.Current.isLandedOrSurfaceSplashed) ||
                                            (weapon.Current.engageSLW && !item.Current.isUnderwater)) continue;
                                            if (TargetInTurretRange(weapon.Current.turret, 7, item.Current.Vessel.CoM, weapon.Current))
                                            {
                                                weapon.Current.visualTargetVessel = item.Current.Vessel;
                                                firedTargets.Add(item.Current);
                                                break;
                                            }
                                        }
                                }
                                TurretID++;
                            }
                            if (MissileTgtID >= Mathf.Min((missilesAssigned.Count), multiTargetNum))
                            {
                                MissileTgtID = 0; //if more turrets than targets, loop target list
                            }
                            if (missilesAssigned.Count > 0 && missilesAssigned[MissileTgtID].Vessel != null) //if missile, override non-missile target
                            {
                                if (weapon.Current.engageMissile)
                                {
                                    if (TargetInTurretRange(weapon.Current.turret, 7, missilesAssigned[MissileTgtID].Vessel.CoM, weapon.Current))
                                    {
                                        weapon.Current.visualTargetVessel = missilesAssigned[MissileTgtID].Vessel; // if target within turret fire zone, assign
                                        firedTargets.Add(missilesAssigned[MissileTgtID]);
                                    }
                                    else //assigned target outside turret arc, try the other targets on the list
                                    {
                                        using (List<TargetInfo>.Enumerator item = missilesAssigned.GetEnumerator())
                                            while (item.MoveNext())
                                            {
                                                if (item.Current.Vessel == null) continue;
                                                if (TargetInTurretRange(weapon.Current.turret, 7, item.Current.Vessel.CoM, weapon.Current))
                                                {
                                                    weapon.Current.visualTargetVessel = item.Current.Vessel;
                                                    firedTargets.Add(item.Current);
                                                    break;
                                                }
                                            }
                                    }
                                }
                                MissileTgtID++;
                            }
                        }
                        else
                        {
                            //weapon.Current.visualTargetVessel = guardTarget;
                            weapon.Current.visualTargetVessel = targetsAssigned.Count > 0 && targetsAssigned[0].Vessel != null ? targetsAssigned[0].Vessel : guardTarget; //make sure all guns targeting the same target, to ensure the leadOffest is the same, and that the Ai isn't trying to use the leadOffset from a turret
                            //Debug.Log("[BDArmory.MTD]: target from list was null, defaulting to " + guardTarget.name);
                        }
                    }
                    else
                    {
                        weapon.Current.visualTargetVessel = guardTarget;
                        //Debug.Log("[BDArmory.MTD]: non-turret, assigned " + guardTarget.name);
                    }
                    weapon.Current.targetCOM = targetCoM;
                    using (List<TargetInfo>.Enumerator Tgt = targetsAssigned.GetEnumerator())
                        while (Tgt.MoveNext())
                        {
                            if (!firedTargets.Contains(Tgt.Current))
                                Tgt.Current.Disengage(this);
                        }
                    using (List<TargetInfo>.Enumerator Tgt = missilesAssigned.GetEnumerator())
                        while (Tgt.MoveNext())
                        {
                            if (!firedTargets.Contains(Tgt.Current))
                                Tgt.Current.Disengage(this);
                        }
                    if (targetCoM)
                    {
                        weapon.Current.targetCockpits = false;
                        weapon.Current.targetEngines = false;
                        weapon.Current.targetWeapons = false;
                        weapon.Current.targetMass = false;
                        weapon.Current.targetRandom = false;
                    }
                    else
                    {
                        weapon.Current.targetCockpits = targetCommand;
                        weapon.Current.targetEngines = targetEngine;
                        weapon.Current.targetWeapons = targetWeapon;
                        weapon.Current.targetMass = targetMass;
                        weapon.Current.targetRandom = targetRandom;
                    }

                    weapon.Current.autoFireTimer = Time.time;
                    //weapon.Current.autoFireLength = 3 * targetScanInterval / 4;
                    weapon.Current.autoFireLength = (fireBurstLength < 0.01f) ? targetScanInterval / 2f : fireBurstLength;
                    weapon.Current.autofireShotCount = 0;
                }
        }
        int MissileID = 0;
        public void PointDefenseTurretFiring()
        {
            // Note: this runs in the Earlyish timing stage, before bullets have moved.
            int TurretID = 0;
            int ballisticTurretID = 0;
            int rocketTurretID = 0;
            PDMslTgts.Clear();
            PDBulletTgts.Clear();
            PDRktTgts.Clear();
            MslTurrets.Clear();
            missileTarget = null;
            int APScount = 0;
            int missileCount = 0;
            TargetInfo interceptiontarget = null;
            Vector3 closestTarget = Vector3.zero;
            Vector3 kbCorrection = BDKrakensbane.IsActive ? BDKrakensbane.FloatingOriginOffsetNonKrakensbane : Vector3.zero; // Correction for Krakensbane for bullets and rockets, which haven't been updated yet.
            using (var weapon = VesselModuleRegistry.GetModules<ModuleWeapon>(vessel).GetEnumerator())
                while (weapon.MoveNext())
                {
                    if (weapon.Current == null) continue;
                    if (weapon.Current.isAPS || weapon.Current.dualModeAPS)
                    {
                        APScount++;
                        if (weapon.Current.ammoCount <= 0 && !BDArmorySettings.INFINITE_AMMO) continue;
                        if (weapon.Current.eAPSType == ModuleWeapon.APSTypes.Missile || weapon.Current.eAPSType == ModuleWeapon.APSTypes.Omni)
                        {
                            interceptiontarget = BDATargetManager.GetClosestMissileThreat(this);
                            if (interceptiontarget != null) PDMslTgts.Add(interceptiontarget);

                            using (List<PooledRocket>.Enumerator target = BDATargetManager.FiredRockets.GetEnumerator())
                                while (target.MoveNext())
                                {
                                    if (target.Current == null) continue;
                                    if (target.Current.team == team) continue;
                                    if (PDRktTgts.Contains(target.Current)) continue;
                                    Vector3 targetPosition = target.Current.currentPosition - kbCorrection;
                                    float threatDirectionFactor = (transform.position - targetPosition).DotNormalized(target.Current.currentVelocity - vessel.Velocity());
                                    if (threatDirectionFactor < 0.95) continue; //if incoming round is heading this way 
                                    if (targetPosition.CloserToThan(weapon.Current.fireTransforms[0].position, weapon.Current.maxTargetingRange * 2))
                                    {
                                        if (RadarUtils.TerrainCheck(targetPosition, transform.position))
                                        {
                                            continue;
                                        }
                                        else
                                        {
                                            if (closestTarget == Vector3.zero || (targetPosition - weapon.Current.fireTransforms[0].position).sqrMagnitude < (closestTarget - weapon.Current.fireTransforms[0].position).sqrMagnitude)
                                            {

                                                closestTarget = targetPosition;
                                                PDRktTgts.Add(target.Current);
                                            }
                                        }
                                    }
                                }
                        }
                        if (weapon.Current.eAPSType == ModuleWeapon.APSTypes.Ballistic || weapon.Current.eAPSType == ModuleWeapon.APSTypes.Omni)
                        {
                            using (List<PooledBullet>.Enumerator target = BDATargetManager.FiredBullets.GetEnumerator())
                                while (target.MoveNext())
                                {
                                    if (target.Current == null) continue;
                                    if (target.Current.team == team) continue;
                                    if (PDBulletTgts.Contains(target.Current)) continue;
                                    Vector3 targetPosition = target.Current.currentPosition - kbCorrection;
                                    float threatDirectionFactor = (transform.position - targetPosition).DotNormalized(target.Current.currentVelocity - vessel.Velocity());
                                    if (threatDirectionFactor < 0.95) continue; //if incoming round is heading this way 
                                    if (targetPosition.CloserToThan(weapon.Current.fireTransforms[0].position, weapon.Current.maxTargetingRange * 2))
                                    {
                                        if (RadarUtils.TerrainCheck(targetPosition, transform.position))
                                        {
                                            continue;
                                        }
                                        else
                                        {
                                            if (closestTarget == Vector3.zero || (targetPosition - weapon.Current.fireTransforms[0].position).sqrMagnitude < (closestTarget - weapon.Current.fireTransforms[0].position).sqrMagnitude)
                                            {
                                                closestTarget = targetPosition;
                                                PDBulletTgts.Add(target.Current);
                                            }
                                        }
                                    }
                                }
                        }
                    }
                }
            //Debug.Log($"[BDArmory.MissileFire - {(this.vessel != null ? vessel.GetName() : "null")}] tgtcount: {PDBulletTgts.Count + PDRktTgts.Count + PDMslTgts.Count}, APS count: {APScount}");
            using (var missile = VesselModuleRegistry.GetModules<MissileBase>(vessel).GetEnumerator())
            {
                while (missile.MoveNext())
                {
                    if (missile.Current == null) continue;
                    if (!missile.Current.engageMissile) continue;
                    if (missile.Current.HasFired || missile.Current.launched) continue;
                    MissileLauncher ml = missile.Current as MissileLauncher;
                    if (ml && ml.multiLauncher && ml.multiLauncher.turret)
                    {
                        if (ml.multiLauncher.missileSpawner.ammoCount == 0 && !BDArmorySettings.INFINITE_ORDINANCE) continue;
                        if (!ml.multiLauncher.turret.turretEnabled)
                            ml.multiLauncher.turret.EnableTurret(missile.Current);
                        missileCount += Mathf.CeilToInt(ml.multiLauncher.missileSpawner.ammoCount / ml.multiLauncher.salvoSize);
                    }
                    else missileCount++;
                    interceptiontarget = BDATargetManager.GetClosestMissileThreat(this);
                    if (interceptiontarget != null && !PDMslTgts.Contains(interceptiontarget)) PDMslTgts.Add(interceptiontarget);
                }
            }

            if (APScount + missileCount <= 0)
            {
                PDScanTimer = -100;
                return;
            }

            if (APScount > 0)
                using (var weapon = VesselModuleRegistry.GetModules<ModuleWeapon>(vessel).GetEnumerator())
                    while (weapon.MoveNext())
                    {
                        if (weapon.Current == null) continue;
                        if (weapon.Current.isAPS || weapon.Current.dualModeAPS)
                        {
                            if (weapon.Current.eAPSType == ModuleWeapon.APSTypes.Ballistic || weapon.Current.eAPSType == ModuleWeapon.APSTypes.Omni)
                            {
                                if (PDBulletTgts.Count > 0)
                                {
                                    if (ballisticTurretID >= PDBulletTgts.Count)
                                    {
                                        if (weapon.Current.isReloading || weapon.Current.isOverheated || weapon.Current.baseDeviation > 0.05 && (weapon.Current.eWeaponType == ModuleWeapon.WeaponTypes.Ballistic || (weapon.Current.eWeaponType == ModuleWeapon.WeaponTypes.Laser && weapon.Current.pulseLaser)))
                                            //if more APS turrets than targets, and APS is a rotary weapon using volume of fire instead of precision, roll over target list to assign multiple turrets to the incoming shell
                                            ballisticTurretID = 0;
                                        //else assign one turret per target, and hold fire on the rest
                                    }
                                    if (ballisticTurretID < PDBulletTgts.Count)
                                    {
                                        if (PDBulletTgts[ballisticTurretID] != null && (PDBulletTgts[ballisticTurretID].currentPosition - kbCorrection).FurtherFromThan(weapon.Current.fireTransforms[0].position, weapon.Current.engageRangeMax * 2)) ballisticTurretID = 0; //reset cycle so out of range guns engage closer targets
                                        if (PDBulletTgts[ballisticTurretID] != null) //second check in case of turretID reset
                                        {
                                            if (TargetInTurretRange(weapon.Current.turret, 7, PDBulletTgts[ballisticTurretID].currentPosition - kbCorrection, weapon.Current))
                                            {
                                                weapon.Current.tgtShell = PDBulletTgts[ballisticTurretID]; // if target within turret fire zone, assign
                                            }
                                            else //else try remaining targets
                                            {
                                                using (List<PooledBullet>.Enumerator item = PDBulletTgts.GetEnumerator())
                                                    while (item.MoveNext())
                                                    {
                                                        if (item.Current == null) continue;
                                                        if (TargetInTurretRange(weapon.Current.turret, 7, item.Current.currentPosition - kbCorrection, weapon.Current))
                                                        {
                                                            weapon.Current.tgtShell = item.Current;
                                                            break;
                                                        }
                                                    }
                                            }
                                            ballisticTurretID++;
                                        }
                                    }
                                    else weapon.Current.tgtShell = null;
                                }
                                else weapon.Current.tgtShell = null;
                            }
                            if (weapon.Current.eAPSType == ModuleWeapon.APSTypes.Missile || weapon.Current.eAPSType == ModuleWeapon.APSTypes.Omni)
                            {
                                if (PDRktTgts.Count > 0)
                                {
                                    if (rocketTurretID >= PDRktTgts.Count)
                                    {
                                        if ((weapon.Current.isReloading || weapon.Current.isOverheated) || weapon.Current.baseDeviation > 0.05 && (weapon.Current.eWeaponType == ModuleWeapon.WeaponTypes.Ballistic || (weapon.Current.eWeaponType == ModuleWeapon.WeaponTypes.Laser && weapon.Current.pulseLaser)))
                                            rocketTurretID = 0;
                                    }
                                    if (rocketTurretID < PDRktTgts.Count)
                                    {
                                        if (PDRktTgts[rocketTurretID] != null && (PDRktTgts[rocketTurretID].currentPosition - kbCorrection).FurtherFromThan(weapon.Current.fireTransforms[0].position, weapon.Current.engageRangeMax * 2f)) rocketTurretID = 0; //reset cycle so out of range guns engage closer targets
                                        if (PDRktTgts[rocketTurretID] != null)
                                        {
                                            bool viableTarget = true;
                                            if (BDArmorySettings.BULLET_WATER_DRAG && weapon.Current.eWeaponType == ModuleWeapon.WeaponTypes.Ballistic && FlightGlobals.getAltitudeAtPos(PDRktTgts[rocketTurretID].currentPosition - kbCorrection) < 0) viableTarget = false;
                                            if (viableTarget && TargetInTurretRange(weapon.Current.turret, 7, PDRktTgts[rocketTurretID].currentPosition - kbCorrection, weapon.Current))
                                            {
                                                weapon.Current.tgtRocket = PDRktTgts[rocketTurretID]; // if target within turret fire zone, assign
                                                weapon.Current.tgtShell = null;
                                            }
                                            else //else try remaining targets
                                            {
                                                using (List<PooledRocket>.Enumerator item = PDRktTgts.GetEnumerator())
                                                    while (item.MoveNext())
                                                    {
                                                        if (item.Current == null) continue;
                                                        if (!viableTarget) continue;
                                                        if (TargetInTurretRange(weapon.Current.turret, 7, item.Current.currentPosition - kbCorrection, weapon.Current))
                                                        {
                                                            weapon.Current.tgtRocket = item.Current;
                                                            weapon.Current.tgtShell = null;
                                                            break;
                                                        }
                                                    }
                                            }
                                            rocketTurretID++;
                                        }
                                    }
                                }
                                else weapon.Current.tgtRocket = null;
                                if (TurretID >= PDMslTgts.Count) TurretID = 0;
                                if (PDMslTgts.Count > 0)
                                {
                                    if (PDMslTgts[TurretID].Vessel != null && PDMslTgts[TurretID].transform.position.FurtherFromThan(weapon.Current.fireTransforms[0].position, weapon.Current.engageRangeMax * 1.25f)) TurretID = 0; //reset cycle so out of range guns engage closer targets
                                    if (PDMslTgts[TurretID].Vessel != null)
                                    {
                                        bool viableTarget = true;
                                        if (BDArmorySettings.BULLET_WATER_DRAG && weapon.Current.eWeaponType == ModuleWeapon.WeaponTypes.Ballistic && PDMslTgts[TurretID].Vessel.Splashed) viableTarget = false;
                                        if (viableTarget && TargetInTurretRange(weapon.Current.turret, 7, PDMslTgts[TurretID].Vessel.CoM, weapon.Current))
                                        {
                                            weapon.Current.visualTargetPart = PDMslTgts[TurretID].Vessel.rootPart;  // if target within turret fire zone, assign
                                            weapon.Current.tgtShell = null;
                                            weapon.Current.tgtRocket = null;
                                        }
                                        else //else try remaining targets
                                        {
                                            using (List<TargetInfo>.Enumerator item = PDMslTgts.GetEnumerator())
                                                while (item.MoveNext())
                                                {
                                                    if (item.Current.Vessel == null) continue;
                                                    if (!viableTarget) continue;
                                                    if (TargetInTurretRange(weapon.Current.turret, 7, item.Current.Vessel.CoM, weapon.Current))
                                                    {
                                                        weapon.Current.visualTargetPart = item.Current.Vessel.rootPart;
                                                        weapon.Current.tgtShell = null;
                                                        weapon.Current.tgtRocket = null;
                                                        break;
                                                    }
                                                }
                                        }
                                        TurretID++;
                                    }
                                }
                                else
                                {
                                    if (guardTarget == null)
                                    {
                                        weapon.Current.visualTargetPart = null;
                                    }
                                    // weapon.Current.tgtShell = null; // FIXME These were wiping Omni type APS shell and rocket targets.
                                    // weapon.Current.tgtRocket = null;
                                }
                            }
                            if (BDArmorySettings.DEBUG_WEAPONS)
                                Debug.Log($"[BDArmory.MissileFire - {(this.vessel != null ? vessel.GetName() : "null")}]: {weapon.Current.shortName} assigned shell:{(weapon.Current.tgtShell != null ? "true" : "false")}; rocket: {(weapon.Current.tgtRocket != null ? "true" : "false")}; missile:{(weapon.Current.visualTargetPart != null ? weapon.Current.visualTargetPart.vessel.GetName() : "null")}");
                            weapon.Current.autoFireTimer = Time.time;
                            weapon.Current.autoFireLength = (fireBurstLength < 0.01f) ? targetScanInterval / 2f : fireBurstLength;
                            weapon.Current.autofireShotCount = 0;
                        }
                    }
            if (guardMode && missileCount > 0 && PDMslTgts.Count > 0 && !guardFiringMissile)
            {
                //Debug.Log($"[PD Missile Debug - {vessel.GetName()}] PDMslTgt size: {PDMslTgts.Count}; missile count: {missileCount}");
                using (List<IBDWeapon>.Enumerator weapon = weaponTypesMissile.GetEnumerator()) //have guardMode requirement?
                    while (weapon.MoveNext())
                    {
                        if (weapon.Current == null) continue;
                        MissileBase currMissile = weapon.Current as MissileBase;
                        MissileLauncher launcher = currMissile as MissileLauncher;
                        if (currMissile == null) continue;
                        if (!currMissile.engageMissile) continue;
                        if (currMissile.HasFired || currMissile.launched) continue;
                        if (MissileID >= PDMslTgts.Count) MissileID = 0;

                        float targetDist = Vector3.Distance(currMissile.MissileReferenceTransform.position, PDMslTgts[MissileID].Vessel.CoM);
                        if (PDMslTgts[MissileID].Vessel != null && targetDist > currMissile.engageRangeMax) MissileID = 0;
                        if (PDMslTgts[MissileID].Vessel != null)
                        {
                            if (targetDist < currMissile.engageRangeMin) continue;
                            bool viableTarget = true;
                            int interceptorsAway = 0;

                            if (currMissile.TargetingMode == MissileBase.TargetingModes.Radar && vesselRadarData != null && (!vesselRadarData.locked || vesselRadarData.lockedTargetData.vessel != PDMslTgts[MissileID].Vessel))
                            {
                                if (!vesselRadarData.locked)
                                {
                                    vesselRadarData.TryLockTarget(PDMslTgts[MissileID].Vessel);
                                }
                                else
                                {
                                    if (firedMissiles >= maxMissilesOnTarget && (multiMissileTgtNum > 1 && BDATargetManager.TargetList(Team).Count > 1))
                                    {
                                        if (!currMissile.radarLOAL) //switch active lock instead of clearing locks for SARH missiles
                                        {
                                            vesselRadarData.TryLockTarget(PDMslTgts[MissileID].Vessel);
                                        }
                                        else
                                        {
                                            List<TargetSignatureData> possibleTargets = vesselRadarData.GetLockedTargets();
                                            bool existingLock = false;
                                            for (int i = 0; i < possibleTargets.Count; i++)
                                            {
                                                if (possibleTargets[i].vessel == PDMslTgts[MissileID].Vessel)
                                                {
                                                    existingLock = true;
                                                    break;
                                                }
                                            }
                                            if (existingLock)
                                            {
                                                vesselRadarData.SwitchActiveLockedTarget(PDMslTgts[MissileID].Vessel);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        if (PreviousMissile != null && PreviousMissile.ActiveRadar && PreviousMissile.targetVessel != null && PreviousMissile.targetVessel.Vessel != null) //previous missile has gone active, don't need that lock anymore
                                        {
                                            vesselRadarData.UnlockSelectedTarget(PreviousMissile.targetVessel.Vessel);
                                        }
                                        vesselRadarData.TryLockTarget(PDMslTgts[MissileID].Vessel);
                                    }
                                }
                            }
                            if (currMissile.TargetingMode == MissileBase.TargetingModes.Heat)
                            {
                                SearchForHeatTarget(currMissile, PDMslTgts[MissileID]);
                            }
                            if (!CheckEngagementEnvelope(currMissile, targetDist, PDMslTgts[MissileID].Vessel)) continue;
                            bool torpedo = launcher && launcher.torpedo; //TODO - work out MMG torpedo support?
                            if (PDMslTgts[MissileID].Vessel.Splashed && !torpedo) viableTarget = false;
                            //need to see if missile is turreted (and is a unique turret we haven't seen yet); if so, check if target is within traverse, else see if target is within boresight
                            bool turreted = false;
                            if (currMissile.TargetingMode == MissileBase.TargetingModes.Radar && (torpedo ? _sonarsEnabled : _radarsEnabled) && !currMissile.radarLOAL && MaxradarLocks < vesselRadarData.GetLockedTargets().Count) continue; //don't have available radar lock, move to next missile                            
                            MissileTurret mT = null;
                            if (launcher && (launcher.missileTurret || launcher.multiLauncher && launcher.multiLauncher.turret))
                            {
                                mT = launcher.missileTurret ? launcher.missileTurret : launcher.multiLauncher.turret;
                                if (!MslTurrets.Contains(mT))
                                {
                                    turreted = true;
                                    mT.EnableTurret(currMissile);
                                    MslTurrets.Add(mT); //don't try to assign two different targets to a turret, so treat remaining missiles on the turret as boresight launch
                                    mT.slavedTargetPosition = PDMslTgts[MissileID].Vessel.CoM;
                                    mT.slaved = true;
                                    mT.SlavedAim();
                                }
                            }
                            if (missilesAway.ContainsKey(PDMslTgts[MissileID]))
                            {
                                missilesAway.TryGetValue(PDMslTgts[MissileID], out int missiles);
                                interceptorsAway = missiles;
                                //Debug.Log($"[PD Missile Debug - {vessel.GetName()}] Missiles aready fired against this target {PDMslTgts[MissileID].Vessel.GetName()}: {interceptorsAway}");
                            }
                            if (interceptorsAway < maxMissilesOnTarget)
                            {
                                //Debug.Log($"[PD Missile Debug - {vessel.GetName()}]viable: {viableTarget}; turreted: {turreted}; inRange: {(turreted ? TargetInTurretRange(mT.turret, mT.fireFOV, PDMslTgts[MissileID].Vessel.CoM) : GetLaunchAuthorization(PDMslTgts[MissileID].Vessel, this, currMissile))}");
                                if (viableTarget && turreted ? TargetInTurretRange(mT.turret, mT.fireFOV, PDMslTgts[MissileID].Vessel.CoM) : GetLaunchAuthorization(PDMslTgts[MissileID].Vessel, this, currMissile))
                                {
                                    missileTarget = PDMslTgts[MissileID].Vessel;
                                    //Debug.Log($"[BDArmory.MissileFire] firing interceptor missile at {PDMslTgts[MissileID].Vessel.name}");
                                    StartCoroutine(GuardMissileRoutine(PDMslTgts[MissileID].Vessel, currMissile));
                                    break;
                                }
                            }
                            else //else try remaining targets
                            {
                                using (List<TargetInfo>.Enumerator item = PDMslTgts.GetEnumerator())
                                    while (item.MoveNext())
                                    {
                                        if (item.Current.Vessel == null) continue;
                                        if (item.Current == PDMslTgts[MissileID]) continue;
                                        interceptorsAway = 0;
                                        if (missilesAway.ContainsKey(item.Current))
                                        {
                                            missilesAway.TryGetValue(item.Current, out int missiles);
                                            interceptorsAway = missiles;
                                            //Debug.Log($"[PD Missile Debug - {vessel.GetName()}] Missiles aready fired against this secondary target {item.Current.Vessel.GetName()}: {interceptorsAway}");
                                        }
                                        if (item.Current.Vessel.Splashed && !torpedo) viableTarget = false;
                                        if (interceptorsAway < maxMissilesOnTarget)
                                        {
                                            if (viableTarget && turreted ? TargetInTurretRange(mT.turret, mT.fireFOV, item.Current.Vessel.CoM) : GetLaunchAuthorization(item.Current.Vessel, this, currMissile))
                                            {
                                                missileTarget = item.Current.Vessel;
                                                //Debug.Log($"[PD Missile Debug - {vessel.GetName()}] triggering launch of interceptor against secondary target {missileTarget.GetName()}");
                                                StartCoroutine(GuardMissileRoutine(item.Current.Vessel, currMissile));
                                                break;
                                            }
                                        }
                                    }
                                if (missileTarget != null) break;
                            }
                            MissileID++;
                        }
                    }
            }
        }
        public void SetOverrideTarget(TargetInfo target)
        {
            overrideTarget = target;
            targetScanTimer = -100;
        }

        public void UpdateMaxGuardRange()
        {
            var rangeEditor = (UI_FloatSemiLogRange)Fields["guardRange"].uiControlEditor;
            rangeEditor.UpdateLimits(rangeEditor.minValue, BDArmorySettings.MAX_GUARD_VISUAL_RANGE);
        }

        /// <summary>
        /// Update the max gun range in-flight.
        /// </summary>
        public void UpdateMaxGunRange(Vessel v)
        {
            if (v != vessel || vessel == null || !vessel.loaded || !part.isActiveAndEnabled) return;
            VesselModuleRegistry.OnVesselModified(v);
            List<WeaponClasses> gunLikeClasses = [WeaponClasses.Gun, WeaponClasses.DefenseLaser, WeaponClasses.Rocket];
            maxGunRange = 10f;
            foreach (var weapon in VesselModuleRegistry.GetModules<ModuleWeapon>(vessel))
            {
                if (weapon == null) continue;
                if (gunLikeClasses.Contains(weapon.GetWeaponClass()))
                {
                    maxGunRange = Mathf.Max(maxGunRange, weapon.maxEffectiveDistance);
                }
            }
            if (BDArmorySetup.Instance.textNumFields != null && BDArmorySetup.Instance.textNumFields.ContainsKey("gunRange")) { BDArmorySetup.Instance.textNumFields["gunRange"].maxValue = maxGunRange; }
            var oldGunRange = gunRange;
            gunRange = Mathf.Min(gunRange, maxGunRange);
            if (BDArmorySettings.DEBUG_AI && gunRange != oldGunRange) Debug.Log($"[BDArmory.MissileFire]: Updating gun range of {v.vesselName} to {gunRange} of {maxGunRange} from {oldGunRange}");
        }

        /// <summary>
        /// Update the max gun range in the editor.
        /// </summary>
        public void UpdateMaxGunRange(Part eventPart)
        {
            if (EditorLogic.fetch.ship == null) return;
            List<WeaponClasses> gunLikeClasses = [WeaponClasses.Gun, WeaponClasses.DefenseLaser, WeaponClasses.Rocket];
            var rangeEditor = (UI_FloatPowerRange)Fields["gunRange"].uiControlEditor;
            maxGunRange = rangeEditor.minValue;
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
            if (gunRange == 0 || gunRange > rangeEditor.maxValue - 1) { gunRange = maxGunRange; }
            rangeEditor.UpdateLimits(rangeEditor.minValue, maxGunRange);
            var oldGunRange = gunRange;
            gunRange = Mathf.Min(gunRange, maxGunRange);
            if (BDArmorySettings.DEBUG_AI && gunRange != oldGunRange) Debug.Log($"[BDArmory.MissileFire]: Updating gun range of {EditorLogic.fetch.ship.shipName} to {gunRange} of {maxGunRange} from {oldGunRange}");
        }

        /// <summary>
        /// Update the max rangeSqr of engaging a visual target with guns.
        /// </summary>
        public void UpdateVisualGunRangeSqr(BaseField field, object obj)
        {
            maxVisualGunRangeSqr = Mathf.Min(gunRange * gunRange, guardRange * guardRange);
        }

        public float ThreatClosingTime(Vessel threat)
        {
            float closureTime = 3600f; // Default closure time of one hour
            if (threat) // If we weren't passed a null
            {
                closureTime = vessel.TimeToCPA(threat, closureTime);
            }
            return closureTime;
        }

        // moved from pilot AI, as it does not really do anything AI related?
        public bool GetLaunchAuthorization(Vessel targetV, MissileFire mf, MissileBase missile)
        {
            bool launchAuthorized = false;
            MissileLauncher mlauncher = missile as MissileLauncher;
            if (missile != null && targetV != null)
            {
                Vector3 target = targetV.transform.position;
                //if (!targetV.LandedOrSplashed) //no leading for moving surface targets? two use condtions come to mind - leading torps and unguided AtG missiles. Latter can use A2AFS, torps slightly complicated in that there's a ~2.5s drop time where theyr'e moving at parent speed
                if (targetV.speed > 1) //target is moving
                {
                    if (mlauncher && (mlauncher.missileTurret || (mlauncher.multiLauncher && mlauncher.multiLauncher.turret)))
                        target = MissileGuidance.GetAirToAirFireSolution(missile, targetV.CoM, targetV.Velocity());
                    else
                        target = MissileGuidance.GetAirToAirFireSolution(missile, targetV);
                }

                float boresightAngle = missile.maxOffBoresight * ((mf.vessel.LandedOrSplashed || targetV.LandedOrSplashed || missile.uncagedLock) ? 0.75f : 0.35f); // Allow launch at close to maxOffBoresight for ground targets or missiles with allAspect = true
                if (unguidedWeapon || missile.TargetingMode == MissileBase.TargetingModes.None) // Override boresightAngle based on blast radius for unguidedWeapons or weapons with no targeting mode
                {
                    if (mlauncher && (mlauncher.missileTurret || (mlauncher.multiLauncher && mlauncher.multiLauncher.turret)))
                        boresightAngle = 1f;
                    else
                        boresightAngle = Mathf.Max(Mathf.Rad2Deg * Mathf.Atan(missile.GetBlastRadius() / (target - missile.transform.position).magnitude) / 3, 1f); // 1deg - within 1/3 of blast radius
                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} boresight angle for unguided {missile.shortName} is {boresightAngle}.");
                }

                // Check that target is within maxOffBoresight now and in future time fTime if we are in atmosphere
                launchAuthorized = missile.maxOffBoresight >= 360 || Vector3.Angle(missile.GetForwardTransform(), target - missile.transform.position) < boresightAngle; // Launch is possible now
                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} final boresight check {(launchAuthorized ? "passed" : "failed")}.");
                if (launchAuthorized && !vessel.InVacuum())
                {
                    float fTime = Mathf.Min(missile.dropTime, 2f);
                    Vector3 futurePos = target + (targetV.Velocity() * fTime);
                    Vector3 myFuturePos = vessel.ReferenceTransform.position + (vessel.Velocity() * fTime);
                    launchAuthorized = launchAuthorized && ((!unguidedWeapon && missile.maxOffBoresight >= 360) || Vector3.Angle(missile.GetForwardTransform(), futurePos - myFuturePos) < boresightAngle); // Launch is likely also possible at fTime
                }
            }

            return launchAuthorized;
        }

        /// <summary>
        /// Check if missile is currently unable to be guided, does not apply for intentionally unguided missiles
        /// </summary>
        /// <returns>true if missile is unguided</returns>
        public bool UnguidedMissile(MissileBase ml, float distanceToTarget)
        {
            MissileLauncher mlauncher = ml as MissileLauncher;
            bool torpedo = mlauncher != null ? mlauncher.torpedo : false; //TODO - work out MMG torpedo support?

            bool unguidedWeapon =
                ml.TargetingMode switch
                {
                    MissileBase.TargetingModes.Laser => BDATargetManager.ActiveLasers.Count <= 0,
                    MissileBase.TargetingModes.Radar => !(torpedo ? _sonarsEnabled : _radarsEnabled) && (!ml.radarLOAL || (mlauncher != null && ml.radarTimeout < ((distanceToTarget - ml.activeRadarRange) / mlauncher.optimumAirspeed))),
                    MissileBase.TargetingModes.Inertial => !((torpedo ? _sonarsEnabled : _radarsEnabled) || _irstsEnabled),
                    MissileBase.TargetingModes.Gps => BDATargetManager.ActiveLasers.Count <= 0 && !_radarsEnabled,
                    _ => false
                }; //unify unguidedWeapon conditions

            return unguidedWeapon;
        }

        /// <summary>
        /// Clamps max missile engage range to max range of radar/laser/visual range
        /// </summary>
        /// <returns>max missile range clamped to targeting method range</returns>
        public float MaxMissileRange(MissileBase ml, bool unguidedGuidedMissile = false)
        {
            // Clamp missile range to targeting method range
            float maxEngageRange = ml.engageRangeMax;
            if (unguidedGuidedMissile)
                return 0.1f * maxEngageRange;

            switch (ml.TargetingMode)
            {
                case MissileBase.TargetingModes.Radar:
                    {
                        float radarRange = 0.1f * maxEngageRange;
                        VesselRadarData vrd = vessel.gameObject.GetComponent<VesselRadarData>();
                        if (vrd)
                            radarRange = Mathf.Max(vrd.MaxRadarRange(), ml.radarLOAL ? ml.activeRadarRange : 0f);
                        return Mathf.Min(maxEngageRange, radarRange);
                    }
                case MissileBase.TargetingModes.Heat:
                    {
                        float irstRange = 0.1f * maxEngageRange;
                        VesselRadarData vrd = vessel.gameObject.GetComponent<VesselRadarData>();
                        if (vrd)
                            irstRange = vrd.MaxIRSTRange();
                        irstRange = Mathf.Max(guardRange, irstRange);
                        return Mathf.Min(maxEngageRange, irstRange);
                    }
                case MissileBase.TargetingModes.Laser:
                    {
                        if (targetingPods.Count > 0)
                        {
                            float maxPodRange = 0.1f * maxEngageRange;
                            using (List<ModuleTargetingCamera>.Enumerator tgp = targetingPods.GetEnumerator())
                                while (tgp.MoveNext())
                                {
                                    if (tgp.Current == null) continue;
                                    maxPodRange = Mathf.Max(tgp.Current.maxRayDistance, maxPodRange);
                                }
                            return Mathf.Min(maxPodRange, maxEngageRange);
                        }
                        else
                            return 0.1f * maxEngageRange;
                    }
                default:
                    return Mathf.Min(maxEngageRange, guardRange);
            }
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
                    if (((AI != null && AI.pilotEnabled && AI.CanEngage()) || (TargetInTurretRange(weapon.Current.turret, gimbalTolerance, default, weapon.Current))) && weapon.Current.maxEffectiveDistance >= finalDistance)
                    {
                        if (weapon.Current.isOverheated)
                        {
                            if (BDArmorySettings.DEBUG_WEAPONS)
                            {
                                Debug.Log($"[BDArmory.MissileFire]: {selectedWeapon} is overheated!");
                            }
                            return -1;
                        }
                        if (weapon.Current.isReloading)
                        {
                            if (BDArmorySettings.DEBUG_WEAPONS)
                            {
                                Debug.Log($"[BDArmory.MissileFire]: {selectedWeapon} is reloading!");
                            }
                            return -1;
                        }
                        if (!weapon.Current.hasGunner)
                        {
                            if (BDArmorySettings.DEBUG_WEAPONS)
                            {
                                Debug.Log($"[BDArmory.MissileFire]: {selectedWeapon} has no gunner!");
                            }
                            return -1;
                        }
                        if (CheckAmmo(weapon.Current) || BDArmorySettings.INFINITE_AMMO)
                        {
                            if (BDArmorySettings.DEBUG_WEAPONS)
                            {
                                Debug.Log($"[BDArmory.MissileFire]: {selectedWeapon} is valid!");
                            }
                            return 1;
                        }
                        if (BDArmorySettings.DEBUG_WEAPONS)
                        {
                            Debug.Log($"[BDArmory.MissileFire]: {selectedWeapon} has no ammo.");
                        }
                        return -1;
                    }
                    if (BDArmorySettings.DEBUG_WEAPONS)
                    {
                        Debug.Log($"[BDArmory.MissileFire]: {selectedWeapon} cannot reach target ({distance} vs {weapon.Current.maxEffectiveDistance}, yawRange: {weapon.Current.yawRange}). Continuing.");
                    }
                    //else return 0;
                }
            return 2;
        }

        bool TargetInTurretRange(ModuleTurret turret, float tolerance, Vector3 gTarget = default(Vector3), ModuleWeapon weapon = null)
        {
            if (!turret)
            {
                return false;
            }

            if (gTarget == default && !guardTarget)
            {
                if (BDArmorySettings.DEBUG_WEAPONS)
                {
                    Debug.Log("[BDArmory.MissileFire]: Checking turret range but no guard target");
                }
                return false;
            }
            if (gTarget == default) gTarget = guardTarget.CoM;

            Transform turretTransform = turret.yawTransform.parent;
            Vector3 direction = gTarget - turretTransform.position;
            if (weapon != null && weapon.bulletDrop) // Account for bullet drop (rough approximation not accounting for target movement).
            {
                switch (weapon.GetWeaponClass())
                {
                    case WeaponClasses.Gun:
                        {
                            var effectiveBulletSpeed = (turret.part.rb.velocity + BDKrakensbane.FrameVelocityV3f + weapon.bulletVelocity * direction.normalized).magnitude;
                            var timeOfFlight = direction.magnitude / effectiveBulletSpeed;
                            direction -= 0.5f * FlightGlobals.getGeeForceAtPosition(vessel.transform.position) * timeOfFlight * timeOfFlight;
                            break;
                        }
                    case WeaponClasses.Rocket:
                        {
                            var effectiveRocketSpeed = (turret.part.rb.velocity + BDKrakensbane.FrameVelocityV3f + (weapon.thrust * weapon.thrustTime / weapon.rocketMass) * direction.normalized).magnitude;
                            var timeOfFlight = direction.magnitude / effectiveRocketSpeed;
                            direction -= 0.5f * FlightGlobals.getGeeForceAtPosition(vessel.transform.position) * timeOfFlight * timeOfFlight;
                            break;
                        }
                }
            }
            Vector3 directionYaw = direction.ProjectOnPlanePreNormalized(turretTransform.up);

            float angleYaw = Vector3.Angle(turretTransform.forward, directionYaw);
            float signedAnglePitch = 90 - Vector3.Angle(turretTransform.up, direction);
            bool withinPitchRange = (signedAnglePitch >= turret.minPitch - tolerance && signedAnglePitch <= turret.maxPitch + tolerance);

            if (angleYaw < (turret.yawRange / 2) + tolerance && withinPitchRange)
            {
                if (BDArmorySettings.DEBUG_WEAPONS)
                {
                    Debug.Log($"[BDArmory.MissileFire]: Checking turret range - target is INSIDE gimbal limits! signedAnglePitch: {signedAnglePitch}, minPitch: {turret.minPitch}, maxPitch: {turret.maxPitch}, tolerance: {tolerance}");
                }
                return true;
            }
            else
            {
                if (BDArmorySettings.DEBUG_WEAPONS)
                {
                    Debug.Log($"[BDArmory.MissileFire]: Checking turret range - target is OUTSIDE gimbal limits! signedAnglePitch: {signedAnglePitch}, minPitch: {turret.minPitch}, maxPitch: {turret.maxPitch}, angleYaw: {angleYaw}, tolerance: {tolerance}");
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
                        // using (IEnumerator<PartResource> resource = p.Current.Resources.GetEnumerator())
                        using (var resource = p.Current.Resources.dict.Values.GetEnumerator())
                            while (resource.MoveNext())
                            {
                                if (resource.Current == null) continue;
                                if (resource.Current.resourceName != ammoName) continue;
                                if (resource.Current.amount >= weapon.requestResourceAmount)
                                {
                                    return true;
                                }
                            }
                    }
                return false;
            }
        }

        public bool CheckAmmo(MissileBase weapon)
        {
            int ammoCount = weapon.missilecount;
            if (BDArmorySettings.INFINITE_ORDINANCE) //check for infinite ammo
            {
                return true;
            }
            else
            {
                if (ammoCount > 0) return true;
            }
            return false;
        }

        public bool outOfAmmo = false; // Indicator for being out of ammo.
        public bool hasWeapons = true; // Indicator for having weapons.
        public bool HasWeaponsAndAmmo()
        { // Check if the vessel has both weapons and ammo for them. Optionally, restrict checks to a subset of the weapon classes.
            if (!hasWeapons || (outOfAmmo && !BDArmorySettings.INFINITE_AMMO && !BDArmorySettings.INFINITE_ORDINANCE)) return false; // It's already been checked and found to be false, don't look again.
            bool hasWeaponsAndAmmo = false;
            hasWeapons = false;
            foreach (var weapon in VesselModuleRegistry.GetModules<IBDWeapon>(vessel))
            {
                if (weapon == null) continue; // First entry is the "no weapon" option.
                hasWeapons = true;
                if (weapon.GetWeaponClass() == WeaponClasses.Gun || weapon.GetWeaponClass() == WeaponClasses.Rocket || weapon.GetWeaponClass() == WeaponClasses.DefenseLaser)
                {
                    var gun = weapon.GetWeaponModule();
                    if (gun.isAPS && !gun.dualModeAPS) continue; //ignore non-dual purpose APS weapons, they can't attack
                    if (gun.ammoName == "ElectricCharge") { hasWeaponsAndAmmo = true; break; }
                    if (BDArmorySettings.INFINITE_AMMO || CheckAmmo((ModuleWeapon)weapon)) { hasWeaponsAndAmmo = true; break; } // If the gun has ammo or we're using infinite ammo, return true after cleaning up.
                }
                else if (weapon.GetWeaponClass() == WeaponClasses.Missile || weapon.GetWeaponClass() == WeaponClasses.Bomb || weapon.GetWeaponClass() == WeaponClasses.SLW)
                {
                    if (BDArmorySettings.INFINITE_ORDINANCE || CheckAmmo((MissileBase)weapon)) { hasWeaponsAndAmmo = true; break; } // If the gun has ammo or we're using infinite ammo, return true after cleaning up.
                }
                else { hasWeaponsAndAmmo = true; break; } // Other weapon types don't have ammo, or use electric charge, which could recharge.
            }
            outOfAmmo = !hasWeaponsAndAmmo; // Set outOfAmmo if we don't have any guns with compatible ammo.
            if (BDArmorySettings.DEBUG_WEAPONS && outOfAmmo) Debug.Log($"[BDArmory.MissileFire]: {vessel.vesselName} has run out of ammo!");
            return hasWeaponsAndAmmo;
        }

        public int CountWeapons()
        { // Count number of weapons with ammo
            int countWeaponsAndAmmo = 0;
            foreach (var weapon in VesselModuleRegistry.GetModules<IBDWeapon>(vessel))
            {
                if (weapon == null) continue; // First entry is the "no weapon" option.
                if (weapon.GetWeaponClass() == WeaponClasses.Gun || weapon.GetWeaponClass() == WeaponClasses.Rocket || weapon.GetWeaponClass() == WeaponClasses.DefenseLaser)
                {
                    var gun = weapon.GetWeaponModule();
                    if (gun.isAPS && !gun.dualModeAPS) continue; //ignore non-dual purpose APS weapons, they can't attack
                    if (gun.ammoName == "ElectricCharge") { countWeaponsAndAmmo++; continue; } // If it's a laser (counts as a gun) consider it as having ammo and count it, since electric charge can replenish.
                    if (BDArmorySettings.INFINITE_AMMO || CheckAmmo((ModuleWeapon)weapon)) { countWeaponsAndAmmo++; } // If the gun has ammo or we're using infinite ammo, count it.
                }
                else if (weapon.GetWeaponClass() == WeaponClasses.Missile || weapon.GetWeaponClass() == WeaponClasses.SLW || weapon.GetWeaponClass() == WeaponClasses.Bomb)
                {
                    if (BDArmorySettings.INFINITE_ORDINANCE || CheckAmmo((MissileBase)weapon)) { countWeaponsAndAmmo++; } // If the gun has ammo or we're using infinite ammo, count it.
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
                        if (weapon.Current.turret && guardMode)
                        {
                            weapon.Current.StandbyWeapon();
                        }
                        else
                        {
                            weapon.Current.DisableWeapon();
                        }
                    }
                    else if (weapon.Current.GetShortName() != selectedWeapon.GetShortName())
                    {
                        if (weapon.Current.turret != null && (weapon.Current.ammoCount > 0 || BDArmorySettings.INFINITE_AMMO)) // Put turrets in standby (tracking only) mode instead of disabling them if they have ammo.
                        {
                            weapon.Current.StandbyWeapon();
                        }
                        else
                        {
                            weapon.Current.DisableWeapon();
                        }
                    }
                    else
                    {
                        weapon.Current.EnableWeapon();
                        if (weapon.Current.dualModeAPS)
                        {
                            weapon.Current.isAPS = false;
                            if (!guardMode) weapon.Current.aiControlled = false;
                        }
                    }
                }
        }

        #endregion Turret

        #region Aimer

        float BombAimer()
        {
            var bomb = selectedWeapon; // Avoid repeated calls to selectedWeapon.get().
            if ( // General conditions for not bothering with the aimer.
                !BDArmorySettings.DRAW_AIMERS ||
                bomb == null ||
                bomb.GetWeaponClass() != WeaponClasses.Bomb ||
                !vessel.isActiveVessel ||
                MapView.MapIsEnabled
            )
            {
                showBombAimer = false;
                return 0f;
            }
            var bombPart = bomb.GetPart(); // We know the selected weapon is a bomb at this point.
            showBombAimer = bombPart != null && vessel.verticalSpeed < 50 && AltitudeTrigger(); // Situational conditions for showing the aimer.
            if (!showBombAimer) return 0f;
            MissileBase ml = bombPart.GetComponent<MissileBase>();

            float simDeltaTime = 5f * Time.fixedDeltaTime;
            float simTime = 0;
            Vector3 simVelocity = (bombPart.rb != null ? bombPart.rb : bombPart.parent.rb).velocity + BDKrakensbane.FrameVelocityV3f; // bombs on reloadable rails don't have a rigid body.
            Vector3 currPos = ml.MissileReferenceTransform.position + Time.fixedDeltaTime * simVelocity; // Start on the next frame.
            Vector3 prevPos = currPos;
            Vector3 closestPos = currPos;
            MissileLauncher launcher = ml as MissileLauncher;
            if (launcher != null)
            {
                if (launcher.multiLauncher && launcher.multiLauncher.salvoSize > 1)
                    currPos += ((launcher.multiLauncher.salvoSize / 2 * (60 / launcher.multiLauncher.rippleRPM)) + launcher.multiLauncher.deploySpeed) * vessel.Velocity(); //add an offset for bomblet dispensers, etc, to have them start deploying before target to carpet bomb
                simVelocity += launcher.decoupleSpeed * (launcher.decoupleForward ? launcher.MissileReferenceTransform.forward : -launcher.MissileReferenceTransform.up);
            }
            else
            {   //TODO: BDModularGuidance review this value
                simVelocity += 5 * -ml.MissileReferenceTransform.up;
            }

            bombAimerTrajectory.Clear();
            bombAimerPosition = Vector3.zero;

            // FIXME values for MMG missiles (launcher == null) need calculating.
            // FIXME mk82 bombs on reloadable rails behave like mk83 bombs.
            float ordinanceMass = launcher != null && launcher.multiLauncher ? launcher.multiLauncher.missileMass : ml.part.partInfo.partPrefab.mass;
            float ordinanceThrust = launcher != null ? launcher.cruiseThrust : 0;
            float ordinanceBoost = launcher != null ? launcher.thrust : 0;
            float thrustTime = launcher != null ? launcher.cruiseTime + launcher.boostTime : 0;
            Vector3 pointingDirection = ml.MissileReferenceTransform.forward;
            var upDirection = VectorUtils.GetUpDirection(currPos);
            float dragArea = launcher != null ? launcher.simpleDrag : 0;
            float liftArea;
            float liftForce = 0;
            float dragForce = 0;
            float AoA = 0;
            float atmDensity;
            float simSpeedSquared;
            var simStartTime = Time.realtimeSinceStartup;
            float CoDOffset = launcher != null ? Mathf.Abs(launcher.simpleCoD.z) : 0;
            float CoDOffsetSqrt = launcher != null ? BDAMath.Sqrt(CoDOffset) : 0;
            StringBuilder logstring = new();
            while (true) // Basic forward Euler, which should be good enough for this.
            {
                atmDensity = (float)FlightGlobals.getAtmDensity(FlightGlobals.getStaticPressure(currPos), FlightGlobals.getExternalTemperature(), FlightGlobals.currentMainBody);
                simSpeedSquared = simVelocity.sqrMagnitude;
                upDirection = VectorUtils.GetUpDirection(currPos);
                var simVelocityDir = simVelocity.normalized;
                var lastSimSpeed = simVelocity.magnitude;

                // Position update before the velocity update so that they're in sync.
                prevPos = currPos;
                currPos += simDeltaTime * simVelocity;
                bombAimerTrajectory.Add(currPos);

                if (Mathf.Floor(simVelocity.magnitude / 10f) != Mathf.Floor(lastSimSpeed / 10f)) logstring.Append($"; {simVelocity.magnitude}: {AoA}, {liftForce}, {dragForce}");

                Ray ray = new(prevPos, currPos - prevPos);
                if (Physics.Raycast(ray, out RaycastHit hitInfo, Vector3.Distance(prevPos, currPos), simTime < ml.dropTime ? (int)LayerMasks.Scenery : (int)(LayerMasks.Scenery | LayerMasks.Parts | LayerMasks.EVA))) // Only consider scenery during the drop time to avoid self hits.
                {
                    bombAimerPosition = hitInfo.point;
                    break;
                }
                else if (FlightGlobals.getAltitudeAtPos(currPos) < 0)
                {
                    bombAimerPosition = currPos - (FlightGlobals.getAltitudeAtPos(currPos) * upDirection);
                    break;
                }
                if (guardTarget)
                {
                    float targetDist = Vector3.Distance(currPos, guardTarget.CoM) - guardTarget.GetRadius();
                    if (targetDist < CurrentMissile.GetBlastRadius())
                    {
                        bombAimerPosition = currPos;
                        break;
                    }
                }

                if (FlightGlobals.RefFrameIsRotating)
                    simVelocity += simDeltaTime * (Vector3)FlightGlobals.getGeeForceAtPosition(currPos);

                if (launcher != null)
                {
                    dragArea = launcher.aero ? launcher.dragArea : (simTime > launcher.deployTime ? launcher.deployedDrag : launcher.simpleDrag) * (0.008f * ordinanceMass);
                    liftArea = launcher.liftArea;
                }
                else
                {
                    //TODO: BDModularGuidance drag and lift calculations
                    dragArea = ml.vessel.parts.Sum(p => p.dragScalar);
                    liftArea = 0.015f;
                }

                // AoA varies wildly for some bombs, e.g., JDAM (10—30°), B-83 (4—3.5°). The following is a rough approx from fitting data points from a JDAM and a B-83.
                AoA = liftArea > 0 && launcher != null && simTime > launcher.dropTime ?
                    Mathf.Min(launcher.maxAoA, (170f / CoDOffsetSqrt / (1 + simSpeedSquared / 1200f) + 2f / CoDOffset) * Mathf.Clamp01(simTime - launcher.dropTime)) :
                    0;
                pointingDirection = Vector3.RotateTowards(simVelocityDir, upDirection, Mathf.Deg2Rad * AoA, 0);

                if (launcher != null && launcher.aero)
                {
                    // Lift
                    liftForce = 0.5f * atmDensity * simSpeedSquared * liftArea * BDArmorySettings.GLOBAL_LIFT_MULTIPLIER * Mathf.Max(MissileGuidance.DefaultLiftCurve.Evaluate(AoA), 0);
                    simVelocity += simDeltaTime * liftForce / ordinanceMass * -simVelocity.ProjectOnPlanePreNormalized(pointingDirection).normalized;

                    // Drag
                    dragForce = 0.5f * atmDensity * simSpeedSquared * dragArea * BDArmorySettings.GLOBAL_DRAG_MULTIPLIER * Mathf.Max(MissileGuidance.DefaultDragCurve.Evaluate(AoA), 0f);
                    simVelocity += simDeltaTime * dragForce / ordinanceMass * -simVelocityDir;
                }
                else // Simple drag. This works fairly well for mk82 and mk82-brake bombs, but not JDAMs or B-83.
                {
                    dragForce = 0.5f * atmDensity * simSpeedSquared * dragArea;
                    simVelocity += simDeltaTime * dragForce / ordinanceMass * -simVelocityDir;
                }

                // Thrusting
                if (launcher != null && thrustTime > 0 && simTime <= thrustTime)
                {
                    if (simTime < launcher.boostTime)
                    {
                        simVelocity += ordinanceBoost / ordinanceMass * simDeltaTime * pointingDirection;
                    }
                    else
                    {
                        simVelocity += ordinanceThrust / ordinanceMass * simDeltaTime * pointingDirection;
                    }
                }

                simTime += simDeltaTime;

                if (Time.realtimeSinceStartup - simStartTime >= 0.1f)
                {
                    bombAimerPosition = currPos;
                    break;
                }
            }

            //debug lines
            if (BDArmorySettings.DEBUG_LINES && BDArmorySettings.DRAW_AIMERS && showBombAimer)
                BombAimerRender();
            return simTime;
        }

        void BombAimerRender()
        {
            if (bombAimerTrajectory.Count == 0) { lr.enabled = false; return; }
            lr = GetComponent<LineRenderer>();
            if (!lr) { lr = gameObject.AddComponent<LineRenderer>(); }
            lr.enabled = true;
            lr.startWidth = .1f;
            lr.endWidth = .1f;
            lr.positionCount = bombAimerTrajectory.Count;
            int i = 0;
            foreach (var point in bombAimerTrajectory)
                lr.SetPosition(i++, point);
        }

        // Check GPS target is within 20m for stationary targets, and a scaling distance based on target speed for targets moving faster than ~175 m/s
        bool GPSDistanceCheck()
        {
            if (!guardTarget) return false;
            return (guardTarget.CoM - VectorUtils.GetWorldSurfacePostion(designatedGPSCoords, vessel.mainBody)).sqrMagnitude < Mathf.Max(400, 0.013f * (float)guardTarget.srfSpeed * (float)guardTarget.srfSpeed);
        }

        // Check antiRad target is within 20m for stationary targets, and a scaling distance based on target speed for targets moving faster than ~175 m/s
        bool AntiRadDistanceCheck()
        {
            if (!guardTarget) return false;
            return (VectorUtils.WorldPositionToGeoCoords(antiRadiationTarget, vessel.mainBody) - VectorUtils.WorldPositionToGeoCoords(guardTarget.CoM, vessel.mainBody)).sqrMagnitude < Mathf.Max(400, 0.013f * (float)guardTarget.srfSpeed * (float)guardTarget.srfSpeed);
        }

        bool AltitudeTrigger()
        {
            const float maxAlt = 10000;
            double asl = vessel.mainBody.GetAltitude(vessel.CoM);
            double agl = asl - vessel.terrainAltitude;
            return agl < maxAlt || asl < maxAlt;
        }

        #endregion Aimer
    }
}