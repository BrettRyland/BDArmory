using System.Runtime.CompilerServices;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;
using KSP.Localization;
using KSP.UI.Screens;

using BDArmory.Armor;
using BDArmory.Bullets;
using BDArmory.Competition.RemoteOrchestration;
using BDArmory.Competition;
using BDArmory.Control;
using BDArmory.CounterMeasure;
using BDArmory.Evolution;
using BDArmory.Extensions;
using BDArmory.FX;
using BDArmory.GameModes;
using BDArmory.ModIntegration;
using BDArmory.Modules;
using BDArmory.Radar;
using BDArmory.Settings;
using BDArmory.Targeting;
using BDArmory.Utils;
using BDArmory.VesselSpawning;
using BDArmory.Weapons;

namespace BDArmory.UI
{
    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    public class BDArmorySetup : MonoBehaviour
    {
        public static bool SMART_GUARDS = true;
        public static bool showTargets = true;

        //=======Window position settings Git Issue #13
        [BDAWindowSettingsField] public static Rect WindowRectToolbar;
        [BDAWindowSettingsField] public static Rect WindowRectGps;
        [BDAWindowSettingsField] public static Rect WindowRectSettings;
        [BDAWindowSettingsField] public static Rect WindowRectRadar;
        [BDAWindowSettingsField] public static Rect WindowRectRwr;
        [BDAWindowSettingsField] public static Rect WindowRectVesselSwitcher;
        [BDAWindowSettingsField] public static Rect WindowRectWingCommander = new Rect(45, 75, 240, 800);
        [BDAWindowSettingsField] public static Rect WindowRectTargetingCam;

        [BDAWindowSettingsField] public static Rect WindowRectRemoteOrchestration;// = new Rect(45, 100, 200, 200);
        [BDAWindowSettingsField] public static Rect WindowRectEvolution;
        [BDAWindowSettingsField] public static Rect WindowRectVesselSpawner;
        [BDAWindowSettingsField] public static Rect WindowRectVesselMover;
        [BDAWindowSettingsField] public static Rect WindowRectAI;

        //reflection field lists
        static FieldInfo[] iFs;

        static FieldInfo[] inputFields
        {
            get
            {
                if (iFs == null)
                {
                    iFs = typeof(BDInputSettingsFields).GetFields();
                }
                return iFs;
            }
        }

        //dependency checks
        bool ModuleManagerLoaded = false;
        bool PhysicsRangeExtenderLoaded = false;
        PropertyInfo PREModEnabledField = null;

        //EVENTS
        public delegate void VolumeChange();

        public static event VolumeChange OnVolumeChange;

        public delegate void SavedSettings();

        public static event SavedSettings OnSavedSettings;

        public delegate void PeaceEnabled();

        public static event PeaceEnabled OnPeaceEnabled;

        //particle optimization
        public static int numberOfParticleEmitters = 0;
        public static BDArmorySetup Instance;
        public static bool GAME_UI_ENABLED = true;
        public static string Version { get; private set; } = "Unknown";

        //toolbar button
        public static bool toolbarButtonAdded = false;

        //settings gui
        public static bool windowSettingsEnabled;
        public string fireKeyGui;

        //editor alignment
        public static bool showWeaponAlignment;

        //check for Apple Silicon
        public static bool AppleSilicon = false;

        // Gui Skin
        public static GUISkin BDGuiSkin = HighLogic.Skin;
        public static GUIStyle ButtonStyle;
        public static GUIStyle SelectedButtonStyle;
        public static GUIStyle CloseButtonStyle;

        //toolbar gui
        public static bool hasAddedButton = false;
        public static bool windowBDAToolBarEnabled;
        float toolWindowWidth = 400;
        float toolWindowHeight = 100;
        float columnWidth = 400;
        bool showWeaponList;
        bool showGuardMenu;
        bool showModules;
        bool showPriorities;
        bool showTargetOptions;
        bool showEngageList;
        int numberOfModules;
        bool showWindowGPS;
        bool infoLinkEnabled;
        bool NumFieldsEnabled;
        int numberOfButtons = 6; // 6 without evolution, will adjust automatically.
        private Vector2 scrollInfoVector;
        public Dictionary<string, NumericInputField> textNumFields;

        //gps window
        public bool showingWindowGPS
        {
            get { return showWindowGPS; }
        }

        bool saveWindowPosition = false;
        float gpsEntryCount;
        float gpsEntryHeight = 24;
        float gpsBorder = 5;
        bool editingGPSName;
        int editingGPSNameIndex;
        bool hasEnteredGPSName;
        string newGPSName = String.Empty;

        public MissileFire ActiveWeaponManager;
        public bool missileWarning;
        public float missileWarningTime = 0;

        //load range stuff
        VesselRanges combatVesselRanges = new VesselRanges();
        float physRangeTimer;

        public static List<CMFlare> Flares = new List<CMFlare>();

        public List<string> mutators = new List<string>();
        bool[] mutators_selected;

        List<string> dependencyWarnings = new List<string>();
        double dependencyLastCheckTime = 0;

        //gui styles
        GUIStyle centerLabel;
        GUIStyle centerLabelRed;
        GUIStyle centerLabelOrange;
        GUIStyle centerLabelBlue;
        GUIStyle leftLabel;
        GUIStyle leftLabelBold;
        GUIStyle infoLinkStyle;
        GUIStyle leftLabelRed;
        GUIStyle rightLabelRed;
        GUIStyle leftLabelGray;
        GUIStyle rippleSliderStyle;
        GUIStyle rippleThumbStyle;
        GUIStyle kspTitleLabel;
        GUIStyle middleLeftLabel;
        GUIStyle middleLeftLabelOrange;
        GUIStyle targetModeStyle;
        GUIStyle targetModeStyleSelected;
        GUIStyle waterMarkStyle;
        GUIStyle redErrorStyle;
        GUIStyle redErrorShadowStyle;
        GUIStyle inputFieldStyle;
        bool stylesConfigured = false;

        public SortedList<string, BDTeam> Teams = new SortedList<string, BDTeam>
        {
            { "Neutral", new BDTeam("Neutral", neutral: true) }
        };

        static float _SystemMaxMemory = 0;
        public static float SystemMaxMemory
        {
            get
            {
                if (_SystemMaxMemory == 0)
                {
                    _SystemMaxMemory = SystemInfo.systemMemorySize / 1024; // System Memory in GB.
                    if (BDArmorySettings.QUIT_MEMORY_USAGE_THRESHOLD > _SystemMaxMemory + 1) BDArmorySettings.QUIT_MEMORY_USAGE_THRESHOLD = _SystemMaxMemory + 1;
                }
                return _SystemMaxMemory;
            }
        }
        string CheatCodeGUI = "";
        string HoSString = "";
        public string HoSTag = "";
        bool enteredHoS = false;

        //competition mode
        string compDistGui;
        string compIntraTeamSeparationBase;
        string compIntraTeamSeparationPerMember;

        #region Textures

        public static string textureDir = "BDArmory/Textures/";

        bool drawCursor;
        Texture2D cursorTexture = GameDatabase.Instance.GetTexture(textureDir + "aimer", false);

        private Texture2D dti;

        public Texture2D directionTriangleIcon
        {
            get { return dti ? dti : dti = GameDatabase.Instance.GetTexture(textureDir + "directionIcon", false); }
        }

        private Texture2D cgs;

        public Texture2D crossedGreenSquare
        {
            get { return cgs ? cgs : cgs = GameDatabase.Instance.GetTexture(textureDir + "crossedGreenSquare", false); }
        }

        private Texture2D dlgs;

        public Texture2D dottedLargeGreenCircle
        {
            get
            {
                return dlgs
                    ? dlgs
                    : dlgs = GameDatabase.Instance.GetTexture(textureDir + "dottedLargeGreenCircle", false);
            }
        }

        private Texture2D ogs;

        public Texture2D openGreenSquare
        {
            get { return ogs ? ogs : ogs = GameDatabase.Instance.GetTexture(textureDir + "openGreenSquare", false); }
        }

        private Texture2D gdott;

        public Texture2D greenDotTexture
        {
            get { return gdott ? gdott : gdott = GameDatabase.Instance.GetTexture(textureDir + "greenDot", false); }
        }

        private Texture2D rdott;

        public Texture2D redDotTexture
        {
            get { return rdott ? rdott : rdott = GameDatabase.Instance.GetTexture(textureDir + "redDot", false); }
        }

        private Texture2D rspike;

        public Texture2D irSpikeTexture
        {
            get { return rspike ? rspike : rspike = GameDatabase.Instance.GetTexture(textureDir + "IRspike", false); }
        }
        private Texture2D gdt;

        public Texture2D greenDiamondTexture
        {
            get { return gdt ? gdt : gdt = GameDatabase.Instance.GetTexture(textureDir + "greenDiamond", false); }
        }

        private Texture2D lgct;

        public Texture2D largeGreenCircleTexture
        {
            get { return lgct ? lgct : lgct = GameDatabase.Instance.GetTexture(textureDir + "greenCircle3", false); }
        }

        private Texture2D gct;

        public Texture2D greenCircleTexture
        {
            get { return gct ? gct : gct = GameDatabase.Instance.GetTexture(textureDir + "greenCircle2", false); }
        }

        private Texture2D gpct;

        public Texture2D greenPointCircleTexture
        {
            get
            {
                if (gpct == null)
                {
                    gpct = GameDatabase.Instance.GetTexture(textureDir + "greenPointCircle", false);
                }
                return gpct;
            }
        }

        private Texture2D gspct;

        public Texture2D greenSpikedPointCircleTexture
        {
            get
            {
                return gspct ? gspct : gspct = GameDatabase.Instance.GetTexture(textureDir + "greenSpikedCircle", false);
            }
        }

        private Texture2D wSqr;

        public Texture2D whiteSquareTexture
        {
            get { return wSqr ? wSqr : wSqr = GameDatabase.Instance.GetTexture(textureDir + "whiteSquare", false); }
        }

        private Texture2D oWSqr;

        public Texture2D openWhiteSquareTexture
        {
            get
            {
                return oWSqr ? oWSqr : oWSqr = GameDatabase.Instance.GetTexture(textureDir + "openWhiteSquare", false);
                ;
            }
        }

        private Texture2D tDir;

        public Texture2D targetDirectionTexture
        {
            get
            {
                return tDir
                    ? tDir
                    : tDir = GameDatabase.Instance.GetTexture(textureDir + "targetDirectionIndicator", false);
            }
        }

        private Texture2D hInd;

        public Texture2D horizonIndicatorTexture
        {
            get
            {
                return hInd ? hInd : hInd = GameDatabase.Instance.GetTexture(textureDir + "horizonIndicator", false);
            }
        }

        private Texture2D si;

        public Texture2D settingsIconTexture
        {
            get { return si ? si : si = GameDatabase.Instance.GetTexture(textureDir + "settingsIcon", false); }
        }


        private Texture2D FAimg;

        public Texture2D FiringAngleImage
        {
            get { return FAimg ? FAimg : FAimg = GameDatabase.Instance.GetTexture(textureDir + "FiringAnglePic", false); }
        }

        #endregion Textures

        public static bool GameIsPaused
        {
            get { return HighLogic.LoadedSceneIsFlight && (PauseMenu.isOpen || Time.timeScale == 0); }
        }

        void Awake()
        {
            if (Instance != null) Destroy(Instance);
            Instance = this;
            if (!(HighLogic.LoadedSceneIsFlight || HighLogic.LoadedSceneIsEditor))
            {
                windowSettingsEnabled = false; // Close the settings on other scenes (it's been saved when the other scene was destroyed).
            }

            // Create settings file if not present or migrate the old one to the PluginsData folder for compatibility with ModuleManager.
            var fileNode = ConfigNode.Load(BDArmorySettings.settingsConfigURL);
            if (fileNode == null)
            {
                fileNode = ConfigNode.Load(BDArmorySettings.oldSettingsConfigURL); // Try the old location.
                if (fileNode == null)
                {
                    fileNode = new ConfigNode();
                    fileNode.AddNode("BDASettings");
                }
                if (!Directory.GetParent(BDArmorySettings.settingsConfigURL).Exists)
                { Directory.GetParent(BDArmorySettings.settingsConfigURL).Create(); }
                var success = fileNode.Save(BDArmorySettings.settingsConfigURL);
                if (success && File.Exists(BDArmorySettings.oldSettingsConfigURL)) // Remove the old settings if it exists and the new settings were saved.
                { File.Delete(BDArmorySettings.oldSettingsConfigURL); }
            }

            // window position settings
            WindowRectToolbar = new Rect(Screen.width - toolWindowWidth - 40, 150, toolWindowWidth, toolWindowHeight);
            // Default, if not in file.
            WindowRectGps = new Rect(0, 0, WindowRectToolbar.width - 10, 0);
            SetupSettingsSize();
            BDAWindowSettingsField.Load();
            CheckIfWindowsSettingsAreWithinScreen();

            WindowRectGps.width = WindowRectToolbar.width - 10;

            // Get the BDA version. We can do this here since it's this assembly we're interested in, other assemblies have to wait until Start.
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().ToList())
                if (assembly.FullName.Split(new char[1] { ',' })[0] == "BDArmory")
                    Version = assembly.GetName().Version.ToString();

            // Load settings
            LoadConfig();

            // Check for Apple Processor
            AppleSilicon = CultureInfo.InvariantCulture.CompareInfo.IndexOf(SystemInfo.processorType, "Apple", CompareOptions.IgnoreCase) >= 0;

            // Ensure AutoSpawn folder exists.
            if (!Directory.Exists(Path.Combine(KSPUtil.ApplicationRootPath, "AutoSpawn")))
            { Directory.CreateDirectory(Path.Combine(KSPUtil.ApplicationRootPath, "AutoSpawn")); }
            // Ensure GameData/Custom/Flags folder exists.
            if (!Directory.Exists(Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Custom", "Flags")))
            { Directory.CreateDirectory(Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Custom", "Flags")); }
        }

        void Start()
        {
            //wmgr toolbar
            if (HighLogic.LoadedSceneIsFlight)
            {
                saveWindowPosition = true;     //otherwise later we should NOT save the current window positions!
                CheatOptions.InfinitePropellant = BDArmorySettings.INFINITE_FUEL;
                CheatOptions.InfiniteElectricity = BDArmorySettings.INFINITE_EC;
            }
            // // Create settings file if not present.
            // if (ConfigNode.Load(BDArmorySettings.settingsConfigURL) == null)
            // {
            //     var node = new ConfigNode();
            //     node.AddNode("BDASettings");
            //     node.Save(BDArmorySettings.settingsConfigURL);
            // }

            // // window position settings
            // WindowRectToolbar = new Rect(Screen.width - toolWindowWidth - 40, 150, toolWindowWidth, toolWindowHeight);
            // // Default, if not in file.
            // WindowRectGps = new Rect(0, 0, WindowRectToolbar.width - 10, 0);
            // SetupSettingsSize();
            // BDAWindowSettingsField.Load();
            // CheckIfWindowsSettingsAreWithinScreen();

            // WindowRectGps.width = WindowRectToolbar.width - 10;

            // //settings
            // LoadConfig();

            physRangeTimer = Time.time;
            GAME_UI_ENABLED = true;
            fireKeyGui = BDInputSettingsFields.WEAP_FIRE_KEY.inputString;

            //setup gui styles
            CloseButtonStyle = new GUIStyle(BDGuiSkin.button) { alignment = TextAnchor.MiddleCenter }; // Configure this one separately since it's static.
            CloseButtonStyle.hover.textColor = Color.red;

            ButtonStyle = new GUIStyle(BDArmorySetup.BDGuiSkin.button);
            SelectedButtonStyle = new GUIStyle(BDArmorySetup.BDGuiSkin.button);
            var tmp = SelectedButtonStyle.normal;
            SelectedButtonStyle.normal = SelectedButtonStyle.active;
            SelectedButtonStyle.active = tmp;
            SelectedButtonStyle.hover = SelectedButtonStyle.normal;
            //

            using (var a = AppDomain.CurrentDomain.GetAssemblies().ToList().GetEnumerator())
                while (a.MoveNext())
                {
                    string name = a.Current.FullName.Split(new char[1] { ',' })[0];
                    switch (name)
                    {
                        case "ModuleManager":
                            ModuleManagerLoaded = true;
                            break;

                        case "PhysicsRangeExtender":
                            foreach (var t in a.Current.GetTypes())
                            {
                                if (t != null && t.Name == "PreSettings")
                                {
                                    var PREInstance = FindObjectOfType(t);
                                    foreach (var propInfo in t.GetProperties(BindingFlags.Public | BindingFlags.Static))
                                        if (propInfo != null && propInfo.Name == "ModEnabled")
                                        {
                                            PREModEnabledField = propInfo;
                                            PhysicsRangeExtenderLoaded = true;
                                        }
                                }
                            }
                            break;
                    }
                }

            if (HighLogic.LoadedSceneIsFlight)
            {
                SaveVolumeSettings();

                GameEvents.onHideUI.Add(HideGameUI);
                GameEvents.onShowUI.Add(ShowGameUI);
                GameEvents.onVesselGoOffRails.Add(OnVesselGoOffRails);
                GameEvents.OnGameSettingsApplied.Add(SaveVolumeSettings);

                GameEvents.onVesselChange.Add(VesselChange);
            }

            BulletInfo.Load();
            RocketInfo.Load();
            ArmorInfo.Load();
            MutatorInfo.Load();
            HullInfo.Load();
            ProjectileUtils.SetUpPartsHashSets();
            ProjectileUtils.SetUpWeaponReporting();

            compDistGui = BDArmorySettings.COMPETITION_DISTANCE.ToString();
            compIntraTeamSeparationBase = BDArmorySettings.COMPETITION_INTRA_TEAM_SEPARATION_BASE.ToString();
            compIntraTeamSeparationPerMember = BDArmorySettings.COMPETITION_INTRA_TEAM_SEPARATION_PER_MEMBER.ToString();
            HoSTag = BDArmorySettings.HOS_BADGE;

            if (HighLogic.LoadedSceneIsFlight || HighLogic.LoadedSceneIsEditor)
            { StartCoroutine(ToolbarButtonRoutine()); }

            for (int i = 0; i < MutatorInfo.mutators.Count; i++)
            {
                mutators.Add(MutatorInfo.mutators[i].name);
            }
            mutators_selected = new bool[mutators.Count];
            for (int i = 0; i < mutators_selected.Length; ++i)
            {
                mutators_selected[i] = BDArmorySettings.MUTATOR_LIST.Contains(mutators[i]);
            }
        }

        void ConfigureStyles()
        {
            centerLabel = new GUIStyle();
            centerLabel.alignment = TextAnchor.UpperCenter;
            centerLabel.normal.textColor = Color.white;

            centerLabelRed = new GUIStyle();
            centerLabelRed.alignment = TextAnchor.UpperCenter;
            centerLabelRed.normal.textColor = Color.red;

            centerLabelOrange = new GUIStyle();
            centerLabelOrange.alignment = TextAnchor.UpperCenter;
            centerLabelOrange.normal.textColor = XKCDColors.BloodOrange;

            centerLabelBlue = new GUIStyle();
            centerLabelBlue.alignment = TextAnchor.UpperCenter;
            centerLabelBlue.normal.textColor = XKCDColors.AquaBlue;

            leftLabel = new GUIStyle();
            leftLabel.alignment = TextAnchor.UpperLeft;
            leftLabel.normal.textColor = Color.white;

            leftLabelBold = new GUIStyle();
            leftLabelBold.alignment = TextAnchor.UpperLeft;
            leftLabelBold.normal.textColor = Color.white;
            leftLabelBold.fontStyle = FontStyle.Bold;

            infoLinkStyle = new GUIStyle(BDArmorySetup.BDGuiSkin.label);
            infoLinkStyle.alignment = TextAnchor.UpperLeft;
            infoLinkStyle.normal.textColor = Color.white;

            middleLeftLabel = new GUIStyle(leftLabel);
            middleLeftLabel.alignment = TextAnchor.MiddleLeft;

            middleLeftLabelOrange = new GUIStyle(middleLeftLabel);
            middleLeftLabelOrange.normal.textColor = XKCDColors.BloodOrange;

            targetModeStyle = new GUIStyle();
            targetModeStyle.alignment = TextAnchor.MiddleRight;
            targetModeStyle.fontSize = 9;
            targetModeStyle.normal.textColor = Color.white;

            targetModeStyleSelected = new GUIStyle(targetModeStyle);
            targetModeStyleSelected.normal.textColor = XKCDColors.BloodOrange;

            waterMarkStyle = new GUIStyle(middleLeftLabel);
            waterMarkStyle.normal.textColor = XKCDColors.LightBlueGrey;

            leftLabelRed = new GUIStyle();
            leftLabelRed.alignment = TextAnchor.UpperLeft;
            leftLabelRed.normal.textColor = Color.red;

            rightLabelRed = new GUIStyle();
            rightLabelRed.alignment = TextAnchor.UpperRight;
            rightLabelRed.normal.textColor = Color.red;

            leftLabelGray = new GUIStyle();
            leftLabelGray.alignment = TextAnchor.UpperLeft;
            leftLabelGray.normal.textColor = Color.gray;

            rippleSliderStyle = new GUIStyle(BDGuiSkin.horizontalSlider);
            rippleThumbStyle = new GUIStyle(BDGuiSkin.horizontalSliderThumb);
            rippleSliderStyle.fixedHeight = rippleThumbStyle.fixedHeight = 0;

            kspTitleLabel = new GUIStyle();
            kspTitleLabel.normal.textColor = BDGuiSkin.window.normal.textColor;
            kspTitleLabel.font = BDGuiSkin.window.font;
            kspTitleLabel.fontSize = BDGuiSkin.window.fontSize;
            kspTitleLabel.fontStyle = BDGuiSkin.window.fontStyle;
            kspTitleLabel.alignment = TextAnchor.UpperCenter;

            redErrorStyle = new GUIStyle(BDGuiSkin.label);
            redErrorStyle.normal.textColor = Color.red;
            redErrorStyle.fontStyle = FontStyle.Bold;
            redErrorStyle.fontSize = 24;
            redErrorStyle.alignment = TextAnchor.UpperCenter;

            redErrorShadowStyle = new GUIStyle(redErrorStyle);
            redErrorShadowStyle.normal.textColor = new Color(0, 0, 0, 0.75f);

            inputFieldStyle = new GUIStyle(GUI.skin.textField);
            inputFieldStyle.alignment = TextAnchor.UpperRight;

            stylesConfigured = true;
        }

        /// <summary>
        /// Modify the background opacity of a window.
        /// 
        /// GUI.Window stores the color values it was called with, so call this with enable=true before GUI.Window to enable
        /// transparency for that window and again with enable=false afterwards to avoid affect later GUI.Window calls.
        ///
        /// Note: This can only lower the opacity of the window background, so windows with a background texture that
        /// already includes some transparency can only be made more transparent, not less.
        /// </summary>
        /// <param name="enable">Enable or reset the modified background opacity.</param>
        public static void SetGUIOpacity(bool enable = true)
        {
            if (!enable && BDArmorySettings.GUI_OPACITY == 1f) return; // Nothing to do.
            var guiColor = GUI.backgroundColor;
            if (guiColor.a != (enable ? BDArmorySettings.GUI_OPACITY : 1f))
            {
                guiColor.a = (enable ? BDArmorySettings.GUI_OPACITY : 1f);
                GUI.backgroundColor = guiColor;
            }
        }

        IEnumerator ToolbarButtonRoutine()
        {
            if (toolbarButtonAdded) yield break;
            yield return new WaitUntil(() => ApplicationLauncher.Ready);
            if (toolbarButtonAdded) yield break;
            Texture buttonTexture = GameDatabase.Instance.GetTexture(BDArmorySetup.textureDir + "icon", false);
            ApplicationLauncher.Instance.AddModApplication(
                ToggleToolbarButton,
                ToggleToolbarButton,
                () => { },
                () => { },
                () => { },
                () => { },
                ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.SPH | ApplicationLauncher.AppScenes.VAB,
                buttonTexture
            );
            toolbarButtonAdded = true;
        }
        /// <summary>
        /// Toggle the BDAToolbar or BDA settings window depending on the scene.
        /// </summary>
        void ToggleToolbarButton()
        {
            if (HighLogic.LoadedSceneIsFlight) { windowBDAToolBarEnabled = !windowBDAToolBarEnabled; }
            else { windowSettingsEnabled = !windowSettingsEnabled; }
        }

        private void CheckIfWindowsSettingsAreWithinScreen()
        {
            GUIUtils.RepositionWindow(ref WindowRectEvolution);
            GUIUtils.UseMouseEventInRect(WindowRectSettings);
            GUIUtils.RepositionWindow(ref WindowRectToolbar);
            GUIUtils.RepositionWindow(ref WindowRectSettings);
            GUIUtils.RepositionWindow(ref WindowRectRwr);
            GUIUtils.RepositionWindow(ref WindowRectVesselSwitcher);
            GUIUtils.RepositionWindow(ref WindowRectWingCommander);
            GUIUtils.RepositionWindow(ref WindowRectTargetingCam);
            GUIUtils.RepositionWindow(ref WindowRectAI);
        }

        void Update()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (missileWarning && Time.time - missileWarningTime > 1.5f)
                {
                    missileWarning = false;
                }

                if (BDInputUtils.GetKeyDown(BDInputSettingsFields.GUI_WM_TOGGLE))
                {
                    windowBDAToolBarEnabled = !windowBDAToolBarEnabled;
                }

                if (BDInputUtils.GetKeyDown(BDInputSettingsFields.TIME_SCALING))
                {
                    OtherUtils.SetTimeOverride(!BDArmorySettings.TIME_OVERRIDE);
                }
            }
            else if (HighLogic.LoadedSceneIsEditor)
            {
                if (Input.GetKeyDown(KeyCode.F2))
                {
                    showWeaponAlignment = !showWeaponAlignment;
                }
            }

            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
            {
                if (Input.GetKeyDown(KeyCode.B))
                {
                    ToggleWindowSettings();
                }
            }
        }

        void ToggleWindowSettings()
        {
            if (HighLogic.LoadedScene == GameScenes.LOADING || HighLogic.LoadedScene == GameScenes.LOADINGBUFFER)
            {
                return;
            }

            windowSettingsEnabled = !windowSettingsEnabled;
            if (windowSettingsEnabled)
            {
                // LoadConfig(); // Don't reload settings, since they're already loaded and mess with other settings windows.
            }
            else
            {
                SaveConfig();
            }
        }

        public void UpdateCursorState()
        {
            if (ActiveWeaponManager == null)
            {
                drawCursor = false;
                //Screen.showCursor = true;
                Cursor.visible = true;
                return;
            }

            if (!GAME_UI_ENABLED || CameraMouseLook.MouseLocked)
            {
                drawCursor = false;
                Cursor.visible = false;
                return;
            }

            if (HighLogic.LoadedSceneIsFlight)
            {
                drawCursor = false;
                if (!MapView.MapIsEnabled && !GUIUtils.CheckMouseIsOnGui() && !PauseMenu.isOpen)
                {
                    if (ActiveWeaponManager.selectedWeapon != null && ActiveWeaponManager.weaponIndex > 0 &&
                        !ActiveWeaponManager.guardMode)
                    {
                        if (ActiveWeaponManager.selectedWeapon.GetWeaponClass() == WeaponClasses.Gun ||
                            ActiveWeaponManager.selectedWeapon.GetWeaponClass() == WeaponClasses.Rocket ||
                            ActiveWeaponManager.selectedWeapon.GetWeaponClass() == WeaponClasses.DefenseLaser)
                        {
                            ModuleWeapon mw = ActiveWeaponManager.selectedWeapon.GetPart().FindModuleImplementing<ModuleWeapon>();
                            if (mw != null && mw.weaponState == ModuleWeapon.WeaponStates.Enabled && mw.maxPitch > 1 && !mw.slaved && !mw.GPSTarget && !mw.aiControlled)
                            {
                                //Screen.showCursor = false;
                                Cursor.visible = false;
                                drawCursor = true;
                                return;
                            }
                        }
                    }

                    if (MouseAimFlight.IsMouseAimActive)
                    {
                        Cursor.visible = false;
                        return;
                    }
                }
            }

            //Screen.showCursor = true;
            Cursor.visible = true;
        }

        void VesselChange(Vessel v)
        {
            if (v != null && v.isActiveVessel)
            {
                GetWeaponManager();
                Instance.UpdateCursorState();
            }
        }

        void GetWeaponManager()
        {
            ActiveWeaponManager = VesselModuleRegistry.GetMissileFire(FlightGlobals.ActiveVessel, true);
            if (ActiveWeaponManager != null)
            { ConfigTextFields(); }
        }
        public void ConfigTextFields()
        {
            textNumFields = new Dictionary<string, NumericInputField> {
                { "rippleRPM", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.rippleRPM, 0, 1600) },
                { "targetScanInterval", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.targetScanInterval, 0.5f, 60f) },
                { "fireBurstLength", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.fireBurstLength, 0, 10) },
                { "AutoFireCosAngleAdjustment", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.AutoFireCosAngleAdjustment, 0, 4) },
                { "guardAngle", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.guardAngle, 10, 360) },
                { "guardRange", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.guardRange, 100, BDArmorySettings.MAX_GUARD_VISUAL_RANGE) },
                { "gunRange", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.gunRange, 0, ActiveWeaponManager.maxGunRange) },
                { "multiTargetNum", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.multiTargetNum, 1, 10) },
                { "multiMissileTgtNum", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.multiMissileTgtNum, 1, 10) },
                { "maxMissilesOnTarget", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.maxMissilesOnTarget, 1, MissileFire.maxAllowableMissilesOnTarget) },

                { "targetBias", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.targetBias, -10, 10) },
                { "targetWeightRange", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.targetWeightRange, -10, 10) },
                { "targetWeightAirPreference", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.targetWeightAirPreference, -10, 10) },
                { "targetWeightATA", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.targetWeightATA, -10, 10) },
                { "targetWeightAoD", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.targetWeightAoD, -10, 10) },
                { "targetWeightAccel", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.targetWeightAccel,-10, 10) },
                { "targetWeightClosureTime", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.targetWeightClosureTime, -10, 10) },
                { "targetWeightWeaponNumber", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.targetWeightWeaponNumber, -10, 10) },
                { "targetWeightMass", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.targetWeightMass,-10, 10) },
                { "targetWeightDamage", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.targetWeightDamage,-10, 10) },
                { "targetWeightFriendliesEngaging", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.targetWeightFriendliesEngaging, -10, 10) },
                { "targetWeightThreat", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.targetWeightThreat, -10, 10) },
                { "targetWeightProtectTeammate", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.targetWeightProtectTeammate, -10, 10) },
                { "targetWeightProtectVIP", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.targetWeightProtectVIP, -10, 10) },
                { "targetWeightAttackVIP", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.targetWeightAttackVIP, -10, 10) },
            };
        }

        static bool firstLoad = true;
        public static void LoadConfig()
        {
            try
            {
                Debug.Log("[BDArmory.BDArmorySetup]=== Loading settings.cfg ===");

                if (firstLoad)
                {
                    BDAPersistentSettingsField.Upgrade();
                    firstLoad = false;
                }
                BDAPersistentSettingsField.Load();
                BDInputSettingsFields.LoadSettings();
                BDArmorySettings.ready = true;
            }
            catch (NullReferenceException e)
            {
                Debug.LogError("[BDArmory.BDArmorySetup]=== Failed to load settings config ===: " + e.Message + "\n" + e.StackTrace);
            }
        }

        public static void SaveConfig()
        {
            try
            {
                Debug.Log("[BDArmory.BDArmorySetup] == Saving settings.cfg ==	");

                BDAPersistentSettingsField.Save(BDArmorySettings.settingsConfigURL);

                BDInputSettingsFields.SaveSettings();

                if (OnSavedSettings != null)
                {
                    OnSavedSettings();
                }
            }
            catch (NullReferenceException e)
            {
                Debug.LogError("[BDArmory.BDArmorySetup]: === Failed to save settings.cfg ====: " + e.Message + "\n" + e.StackTrace);
            }
        }

        #region GUI

        void OnGUI()
        {
            if (!GAME_UI_ENABLED) return;
            if (!stylesConfigured) ConfigureStyles();
            if (windowSettingsEnabled)
            {
                WindowRectSettings = GUI.Window(129419, WindowRectSettings, WindowSettings, GUIContent.none);
            }

            if (drawCursor)
            {
                //mouse cursor
                int origDepth = GUI.depth;
                GUI.depth = -100;
                float cursorSize = 40;
                Vector3 cursorPos = Input.mousePosition;
                Rect cursorRect = new Rect(cursorPos.x - (cursorSize / 2), Screen.height - cursorPos.y - (cursorSize / 2), cursorSize, cursorSize);
                GUI.DrawTexture(cursorRect, cursorTexture);
                GUI.depth = origDepth;
            }

            if (!windowBDAToolBarEnabled || !HighLogic.LoadedSceneIsFlight) return;
            SetGUIOpacity();
            WindowRectToolbar = GUI.Window(321, WindowRectToolbar, WindowBDAToolbar, "", BDGuiSkin.window);//"BDA Weapon Manager"
            SetGUIOpacity(false);
            GUIUtils.UseMouseEventInRect(WindowRectToolbar);
            if (showWindowGPS && ActiveWeaponManager)
            {
                //gpsWindowRect = GUI.Window(424333, gpsWindowRect, GPSWindow, "", GUI.skin.box);
                GUIUtils.UseMouseEventInRect(WindowRectGps);
                using (var coord = BDATargetManager.GPSTargetList(ActiveWeaponManager.Team).GetEnumerator())
                    while (coord.MoveNext())
                    {
                        GUIUtils.DrawTextureOnWorldPos(coord.Current.worldPos, Instance.greenDotTexture, new Vector2(8, 8), 0);
                    }
            }

            if (Time.time - dependencyLastCheckTime > (dependencyWarnings.Count() == 0 ? 60 : 5)) // Only check once per minute if no issues are found, otherwise 5s.
            {
                CheckDependencies();
            }
            if (dependencyWarnings.Count() > 0)
            {
                GUI.Label(new Rect(Screen.width / 2 - 300 + 2, Screen.height / 6 + 2, 600, 100), string.Join("\n", dependencyWarnings), redErrorShadowStyle);
                GUI.Label(new Rect(Screen.width / 2 - 300, Screen.height / 6, 600, 100), string.Join("\n", dependencyWarnings), redErrorStyle);
            }
        }

        /// <summary>
        /// Check that the dependencies are satisfied.
        /// </summary>
        /// <returns>true if they are, false otherwise.</returns>
        public bool CheckDependencies()
        {
            dependencyLastCheckTime = Time.time;
            dependencyWarnings.Clear();
            if (!ModuleManagerLoaded) dependencyWarnings.Add("Module Manager dependency is missing!");
            if (!PhysicsRangeExtenderLoaded) dependencyWarnings.Add("Physics Range Extender dependency is missing!");
            else if ((
                    (BDACompetitionMode.Instance != null && (BDACompetitionMode.Instance.competitionIsActive || BDACompetitionMode.Instance.competitionStarting))
                    || VesselSpawnerStatus.vesselsSpawning
                )
                && !(bool)PREModEnabledField.GetValue(null)) dependencyWarnings.Add("Physics Range Extender is disabled!");
            if (dependencyWarnings.Count() > 0) dependencyWarnings.Add("BDArmory will not work properly.");
            return dependencyWarnings.Count() == 0;
        }

        public bool hasVesselSwitcher = false;
        public bool hasVesselSpawner = false;
        public bool hasVesselMover = false;
        public bool hasEvolution = false;
        public static bool showVesselSwitcherGUI = false;
        public static bool showVesselSpawnerGUI = false;
        public static bool showVesselMoverGUI = false;
        public bool showEvolutionGUI = false;

        float rippleHeight;
        float weaponsHeight;
        float priorityheight;
        float guardHeight;
        float TargetingHeight;
        float EngageHeight;
        float modulesHeight;
        float gpsHeight;
        bool toolMinimized = false;

        float leftIndent = 10;
        float guardLabelWidth = 90;
        float priorityLabelWidth = 120;
        float rightLabelWidth = 45;
        float contentTop = 10;
        float entryHeight = 20;
        float _buttonSize = 26;
        float _windowMargin = 4;

        Rect LabelRect(float line, float labelWidth) => new Rect(leftIndent + 3, line * entryHeight, labelWidth, entryHeight);
        Rect SliderRect(float line, float labelWidth) => new Rect(leftIndent + labelWidth + 16, (line + 0.2f) * entryHeight, columnWidth - 2 * leftIndent - labelWidth - rightLabelWidth - 28, entryHeight);
        Rect InputFieldRect(float line, float labelWidth) => new Rect(leftIndent + labelWidth + 16, line * entryHeight, columnWidth - 2 * leftIndent - labelWidth - 28, entryHeight);
        Rect RightLabelRect(float line) => new Rect(columnWidth - leftIndent - 3 - rightLabelWidth, line * entryHeight, rightLabelWidth, entryHeight);
        Rect ButtonRect(float line) => new Rect(leftIndent + 3, line * entryHeight, columnWidth - 2 * leftIndent - 16, entryHeight);

        void WindowBDAToolbar(int windowID)
        {
            float line = 0;
            float contentWidth = columnWidth - 2 * leftIndent;
            float windowColumns = 1;
            int buttonNumber = 0;

            GUI.DragWindow(new Rect(_windowMargin + _buttonSize, 0, columnWidth - 2 * _windowMargin - numberOfButtons * _buttonSize, _windowMargin + _buttonSize));

            line += 1.25f;
            line += 0.25f;

            //title
            GUI.Label(new Rect(_windowMargin + _buttonSize, _windowMargin, columnWidth - 2 * _windowMargin - numberOfButtons * _buttonSize, _windowMargin + _buttonSize), StringUtils.Localize("#LOC_BDArmory_WMWindow_title") + "          ", kspTitleLabel);

            // Version.
            GUI.Label(new Rect(columnWidth - _windowMargin - (numberOfButtons - 1) * _buttonSize - 100, 23, 57, 10), Version, waterMarkStyle);

            //SETTINGS BUTTON
            if (!BDKeyBinder.current &&
                GUI.Button(new Rect(columnWidth - _windowMargin - ++buttonNumber * _buttonSize, _windowMargin, _buttonSize, _buttonSize), settingsIconTexture, BDGuiSkin.button))
            {
                ToggleWindowSettings();
            }

            //vesselswitcher button
            if (hasVesselSwitcher)
            {
                GUIStyle vsStyle = showVesselSwitcherGUI ? BDGuiSkin.box : BDGuiSkin.button;
                if (GUI.Button(new Rect(columnWidth - _windowMargin - ++buttonNumber * _buttonSize, _windowMargin, _buttonSize, _buttonSize), "VS", vsStyle))
                {
                    LoadedVesselSwitcher.Instance.SetVisible(!showVesselSwitcherGUI);
                }
            }

            //VesselSpawner button
            if (hasVesselSpawner)
            {
                GUIStyle vsStyle = showVesselSpawnerGUI ? BDGuiSkin.box : BDGuiSkin.button;
                if (GUI.Button(new Rect(columnWidth - _windowMargin - ++buttonNumber * _buttonSize, _windowMargin, _buttonSize, _buttonSize), "Sp", vsStyle))
                {
                    VesselSpawnerWindow.Instance.SetVisible(!showVesselSpawnerGUI);
                    if (!showVesselSpawnerGUI)
                        SaveConfig();
                }
            }

            // VesselMover button
            if (hasVesselMover && GUI.Button(new Rect(columnWidth - _windowMargin - ++buttonNumber * _buttonSize, _windowMargin, _buttonSize, _buttonSize), "VM", showVesselMoverGUI ? BDGuiSkin.box : BDGuiSkin.button))
            {
                VesselMover.Instance.SetVisible(!showVesselMoverGUI);
            }

            // evolution button
            if (BDArmorySettings.EVOLUTION_ENABLED && hasEvolution)
            {
                var evolutionSkin = showEvolutionGUI ? BDGuiSkin.box : BDGuiSkin.button; ;
                if (GUI.Button(new Rect(columnWidth - _windowMargin - ++buttonNumber * _buttonSize, _windowMargin, _buttonSize, _buttonSize), "EV", evolutionSkin))
                {
                    EvolutionWindow.Instance.SetVisible(!showEvolutionGUI);
                }
            }

            //infolink
            GUIStyle iStyle = infoLinkEnabled ? BDGuiSkin.box : BDGuiSkin.button;
            if (GUI.Button(new Rect(columnWidth - _windowMargin - ++buttonNumber * _buttonSize, _windowMargin, _buttonSize, _buttonSize), "i", iStyle))
            {
                infoLinkEnabled = !infoLinkEnabled;
            }

            //numeric fields
            GUIStyle nStyle = NumFieldsEnabled ? BDGuiSkin.box : BDGuiSkin.button;
            if (GUI.Button(new Rect(columnWidth - _windowMargin - ++buttonNumber * _buttonSize, _windowMargin, _buttonSize, _buttonSize), "#", nStyle))
            {
                NumFieldsEnabled = !NumFieldsEnabled;
                if (!NumFieldsEnabled)
                {
                    // Try to parse all the fields immediately so that they're up to date.
                    foreach (var field in textNumFields.Keys)
                    { textNumFields[field].tryParseValueNow(); }
                    if (ActiveWeaponManager != null)
                    {
                        foreach (var field in textNumFields.Keys)
                        {
                            try
                            {
                                var fieldInfo = typeof(MissileFire).GetField(field);
                                if (fieldInfo != null)
                                { fieldInfo.SetValue(ActiveWeaponManager, Convert.ChangeType(textNumFields[field].currentValue, fieldInfo.FieldType)); }
                                else // Check if it's a property instead of a field.
                                {
                                    var propInfo = typeof(MissileFire).GetProperty(field);
                                    propInfo.SetValue(ActiveWeaponManager, Convert.ChangeType(textNumFields[field].currentValue, propInfo.PropertyType));
                                }
                            }
                            catch (Exception e) { Debug.LogError($"[BDArmory.BDArmorySetup]: Failed to set current value of {field}: " + e.Message); }
                        }
                    }
                    // Then make any special conversions here.
                }
                else // Set the input fields to their current values.
                {
                    // Make any special conversions first.
                    // Then set each of the field values to the current slider value.   
                    if (ActiveWeaponManager != null)
                    {
                        foreach (var field in textNumFields.Keys)
                        {
                            try
                            {
                                var fieldInfo = typeof(MissileFire).GetField(field);
                                if (fieldInfo != null)
                                { textNumFields[field].currentValue = Convert.ToDouble(fieldInfo.GetValue(ActiveWeaponManager)); }
                                else // Check if it's a property instead of a field.
                                {
                                    var propInfo = typeof(MissileFire).GetProperty(field);
                                    textNumFields[field].currentValue = Convert.ToDouble(propInfo.GetValue(ActiveWeaponManager));
                                }
                            }
                            catch (Exception e) { Debug.LogError($"[BDArmory.BDArmorySetup]: Failed to set current value of {field}: " + e.Message); }
                        }
                    }
                }
            }

            if (ActiveWeaponManager != null)
            {
                //MINIMIZE BUTTON
                toolMinimized = GUI.Toggle(new Rect(_windowMargin, _windowMargin, _buttonSize, _buttonSize), toolMinimized, "_", toolMinimized ? BDGuiSkin.box : BDGuiSkin.button);

                GUIStyle armedLabelStyle;
                Rect armedRect = new Rect(leftIndent, contentTop + (line * entryHeight), contentWidth / 2, entryHeight);
                if (ActiveWeaponManager.guardMode)
                {
                    if (GUI.Button(armedRect, "- " + StringUtils.Localize("#LOC_BDArmory_WMWindow_GuardModebtn") + " -", BDGuiSkin.box))//Guard Mode
                    {
                        showGuardMenu = true;
                    }
                }
                else
                {
                    string armedText = StringUtils.Localize("#LOC_BDArmory_WMWindow_ArmedText");//"Trigger is "
                    if (ActiveWeaponManager.isArmed)
                    {
                        armedText += StringUtils.Localize("#LOC_BDArmory_WMWindow_ArmedText_ARMED");//"ARMED."
                        armedLabelStyle = BDGuiSkin.box;
                    }
                    else
                    {
                        armedText += StringUtils.Localize("#LOC_BDArmory_WMWindow_ArmedText_DisArmed");//"disarmed."
                        armedLabelStyle = BDGuiSkin.button;
                    }
                    if (GUI.Button(armedRect, armedText, armedLabelStyle))
                    {
                        ActiveWeaponManager.ToggleArm();
                    }
                }

                GUIStyle teamButtonStyle = BDGuiSkin.box;
                string teamText = StringUtils.Localize("#LOC_BDArmory_WMWindow_TeamText") + $": {ActiveWeaponManager.Team.Name + (ActiveWeaponManager.Team.Neutral ? (ActiveWeaponManager.Team.Name != "Neutral" ? "(N)" : "") : "")}";//Team
                if (GUI.Button(new Rect(leftIndent + (contentWidth / 2), contentTop + (line * entryHeight), contentWidth / 2, entryHeight), teamText, teamButtonStyle))
                {
                    if (Event.current.button == 1)
                    {
                        BDTeamSelector.Instance.Open(ActiveWeaponManager, new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
                    }
                    else
                    {
                        ActiveWeaponManager.NextTeam();
                    }
                }
                line++;
                line += 0.25f;
                string weaponName = ActiveWeaponManager.selectedWeaponString;
                string selectionText = StringUtils.Localize("#LOC_BDArmory_WMWindow_selectionText", weaponName);//Weapon: <<1>>
                GUI.Label(new Rect(leftIndent, contentTop + (line * entryHeight), contentWidth, entryHeight * 1.25f), selectionText, BDGuiSkin.box);
                line += 1.25f;
                line += 0.1f;
                //if weapon can ripple, show option and slider.
                if (ActiveWeaponManager.hasLoadedRippleData && ActiveWeaponManager.canRipple)
                {
                    if (ActiveWeaponManager.selectedWeapon != null && ActiveWeaponManager.weaponIndex > 0 &&
                        (ActiveWeaponManager.selectedWeapon.GetWeaponClass() == WeaponClasses.Gun
                        || ActiveWeaponManager.selectedWeapon.GetWeaponClass() == WeaponClasses.Rocket
                        || ActiveWeaponManager.selectedWeapon.GetWeaponClass() == WeaponClasses.DefenseLaser)) //remove rocket ripple slider - moved to editor
                    {
                        string rippleText = ActiveWeaponManager.rippleFire
                            ? StringUtils.Localize("#LOC_BDArmory_WMWindow_rippleText1", ActiveWeaponManager.gunRippleRpm.ToString("0"))//"Barrage: " +  + " RPM"
                            : StringUtils.Localize("#LOC_BDArmory_WMWindow_rippleText2");//"Salvo"
                        GUIStyle rippleStyle = ActiveWeaponManager.rippleFire
                            ? BDGuiSkin.box
                            : BDGuiSkin.button;
                        if (
                            GUI.Button(
                                new Rect(leftIndent, contentTop + (line * entryHeight), contentWidth / 2, entryHeight * 1.25f),
                                rippleText, rippleStyle))
                        {
                            ActiveWeaponManager.ToggleRippleFire();
                        }
                        if (ActiveWeaponManager.rippleFire)
                        {
                            GUI.Label(new Rect(leftIndent + contentWidth / 2 + _windowMargin, contentTop + line * entryHeight, contentWidth / 4 - _windowMargin, entryHeight * 1.25f), $"{StringUtils.Localize("#LOC_BDArmory_WMWindow_barrageStagger")}: {(ActiveWeaponManager.barrageStagger > 0 ? ActiveWeaponManager.barrageStagger : 1):G1}");
                            ActiveWeaponManager.barrageStagger = BDAMath.RoundToUnit(GUI.HorizontalSlider(new Rect(leftIndent + 3 * contentWidth / 4, contentTop + (line + 0.25f) * entryHeight, contentWidth / 4, entryHeight), ActiveWeaponManager.barrageStagger, 0f, 0.1f), 0.01f);
                        }

                        rippleHeight = Mathf.Lerp(rippleHeight, 1.25f, 0.15f);
                    }
                    else
                    {
                        string rippleText = ActiveWeaponManager.rippleFire
                            ? StringUtils.Localize("#LOC_BDArmory_WMWindow_rippleText3", ActiveWeaponManager.rippleRPM.ToString("0"))//"Ripple: " +  + " RPM"
                            : StringUtils.Localize("#LOC_BDArmory_WMWindow_rippleText4");//"Ripple: OFF"
                        GUIStyle rippleStyle = ActiveWeaponManager.rippleFire
                            ? BDGuiSkin.box
                            : BDGuiSkin.button;
                        if (
                            GUI.Button(
                                new Rect(leftIndent, contentTop + (line * entryHeight), contentWidth / 2, entryHeight * 1.25f),
                                rippleText, rippleStyle))
                        {
                            ActiveWeaponManager.ToggleRippleFire();
                        }
                        if (ActiveWeaponManager.rippleFire)
                        {
                            if (!NumFieldsEnabled)
                            {
                                ActiveWeaponManager.rippleRPM = GUI.HorizontalSlider(new Rect(leftIndent + (contentWidth / 2) + 2, contentTop + (line * entryHeight) + 6.5f, (contentWidth / 2) - 2, 12),
                                    ActiveWeaponManager.rippleRPM, 100, 1600, rippleSliderStyle, rippleThumbStyle);
                            }
                            else
                            {
                                textNumFields["rippleRPM"].tryParseValue(GUI.TextField(new Rect(leftIndent + (contentWidth / 2) + 2, contentTop + (line * entryHeight) + 6.5f, (contentWidth / 2) - 2, entryHeight),
                                    textNumFields["rippleRPM"].possibleValue, 4, inputFieldStyle));
                                ActiveWeaponManager.rippleRPM = (float)textNumFields["rippleRPM"].currentValue;
                            }
                        }
                        rippleHeight = Mathf.Lerp(rippleHeight, 1.25f, 0.15f);
                    }
                }
                else
                {
                    rippleHeight = Mathf.Lerp(rippleHeight, 0, 0.15f);
                }
                line += rippleHeight;
                line += 0.1f;

                if (!toolMinimized)
                {
                    showWeaponList =
                        GUI.Toggle(new Rect(leftIndent, contentTop + (line * entryHeight), contentWidth / 4, entryHeight),
                            showWeaponList, StringUtils.Localize("#LOC_BDArmory_WMWindow_ListWeapons"), showWeaponList ? BDGuiSkin.box : BDGuiSkin.button);//"Weapons"
                    showGuardMenu =
                        GUI.Toggle(
                            new Rect(leftIndent + (contentWidth / 4), contentTop + (line * entryHeight), contentWidth / 4,
                                entryHeight), showGuardMenu, StringUtils.Localize("#LOC_BDArmory_WMWindow_GuardMenu"),//"Guard Menu"
                            showGuardMenu ? BDGuiSkin.box : BDGuiSkin.button);
                    showPriorities =
                        GUI.Toggle(new Rect(leftIndent + (2 * contentWidth / 4), contentTop + (line * entryHeight), contentWidth / 4,
                             entryHeight), showPriorities, StringUtils.Localize("#LOC_BDArmory_WMWindow_TargetPriority"),//"Tgt priority"
                            showPriorities ? BDGuiSkin.box : BDGuiSkin.button);
                    showModules =
                        GUI.Toggle(
                            new Rect(leftIndent + (3 * contentWidth / 4), contentTop + (line * entryHeight), contentWidth / 4,
                                entryHeight), showModules, StringUtils.Localize("#LOC_BDArmory_WMWindow_ModulesToggle"),//"Modules"
                            showModules ? BDGuiSkin.box : BDGuiSkin.button);
                    line++;
                }

                float weaponLines = 0;
                if (showWeaponList && !toolMinimized)
                {
                    line += 0.25f;
                    Rect weaponListGroupRect = new Rect(5, contentTop + (line * entryHeight), columnWidth - 10, weaponsHeight * entryHeight);
                    GUI.BeginGroup(weaponListGroupRect, GUIContent.none, BDGuiSkin.box); //darker box
                    weaponLines += 0.1f;

                    for (int i = 0; i < ActiveWeaponManager.weaponArray.Length; i++)
                    {
                        GUIStyle wpnListStyle;
                        GUIStyle tgtStyle;
                        if (i == ActiveWeaponManager.weaponIndex)
                        {
                            wpnListStyle = middleLeftLabelOrange;
                            tgtStyle = targetModeStyleSelected;
                        }
                        else
                        {
                            wpnListStyle = middleLeftLabel;
                            tgtStyle = targetModeStyle;
                        }
                        string label;
                        string subLabel;
                        if (ActiveWeaponManager.weaponArray[i] != null)
                        {
                            label = ActiveWeaponManager.weaponArray[i].GetShortName();
                            subLabel = ActiveWeaponManager.weaponArray[i].GetSubLabel();
                        }
                        else
                        {
                            label = StringUtils.Localize("#LOC_BDArmory_WMWindow_NoneWeapon");//"None"
                            subLabel = String.Empty;
                        }
                        Rect weaponButtonRect = new Rect(leftIndent, (weaponLines * entryHeight), weaponListGroupRect.width - (2 * leftIndent), entryHeight);

                        GUI.Label(weaponButtonRect, subLabel, tgtStyle);

                        if (GUI.Button(weaponButtonRect, label, wpnListStyle))
                        {
                            ActiveWeaponManager.CycleWeapon(i);
                        }

                        if (i < ActiveWeaponManager.weaponArray.Length - 1)
                        {
                            GUIUtils.DrawRectangle(
                                new Rect(weaponButtonRect.x, weaponButtonRect.y + weaponButtonRect.height,
                                    weaponButtonRect.width, 1), Color.white);
                        }
                        weaponLines++;
                    }

                    weaponLines += 0.1f;
                    GUI.EndGroup();
                }
                weaponsHeight = Mathf.Lerp(weaponsHeight, weaponLines, 0.15f);
                line += weaponsHeight;

                float guardLines = 0;
                if (showGuardMenu && !toolMinimized)
                {
                    line += 0.25f;
                    GUI.BeginGroup(new Rect(5, contentTop + line * entryHeight, columnWidth - 10, guardHeight * entryHeight), GUIContent.none, BDGuiSkin.box);
                    guardLines += 0.1f;

                    string guardButtonLabel = StringUtils.Localize("#LOC_BDArmory_WMWindow_NoneWeapon", (ActiveWeaponManager.guardMode ? StringUtils.Localize("#LOC_BDArmory_Generic_On") : StringUtils.Localize("#LOC_BDArmory_Generic_Off")));//"Guard Mode " + "ON""Off"
                    if (GUI.Button(ButtonRect(guardLines), guardButtonLabel, ActiveWeaponManager.guardMode ? BDGuiSkin.box : BDGuiSkin.button))
                    {
                        ActiveWeaponManager.ToggleGuardMode();
                    }
                    guardLines += 0.25f;

                    GUI.Label(LabelRect(++guardLines, guardLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_FiringInterval"), leftLabel);//"Firing Interval"                 
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.targetScanInterval = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(guardLines, guardLabelWidth), ActiveWeaponManager.targetScanInterval, 0.5f, 60f), 0.5f);
                        GUI.Label(RightLabelRect(guardLines), ActiveWeaponManager.targetScanInterval.ToString(), leftLabel);
                    }
                    else
                    {
                        textNumFields["targetScanInterval"].tryParseValue(GUI.TextField(InputFieldRect(guardLines, guardLabelWidth), textNumFields["targetScanInterval"].possibleValue, 4, inputFieldStyle));
                        ActiveWeaponManager.targetScanInterval = (float)textNumFields["targetScanInterval"].currentValue;
                    }

                    string burstLabel = StringUtils.Localize("#LOC_BDArmory_WMWindow_BurstLength");//"Burst Length"
                    GUI.Label(LabelRect(++guardLines, guardLabelWidth), burstLabel, leftLabel);
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.fireBurstLength = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(guardLines, guardLabelWidth), ActiveWeaponManager.fireBurstLength, 0, 10), 0.05f);
                        GUI.Label(RightLabelRect(guardLines), ActiveWeaponManager.fireBurstLength.ToString(), leftLabel);
                    }
                    else
                    {
                        textNumFields["fireBurstLength"].tryParseValue(GUI.TextField(InputFieldRect(guardLines, guardLabelWidth), textNumFields["fireBurstLength"].possibleValue, 4, inputFieldStyle));
                        ActiveWeaponManager.fireBurstLength = (float)textNumFields["fireBurstLength"].currentValue;
                    }

                    // extension for feature_engagementenvelope: set the firing accuracy tolarance
                    var oldAutoFireCosAngleAdjustment = ActiveWeaponManager.AutoFireCosAngleAdjustment;
                    string accuracyLabel = StringUtils.Localize("#LOC_BDArmory_WMWindow_FiringTolerance");//"Firing Angle"
                    GUI.Label(LabelRect(++guardLines, guardLabelWidth), accuracyLabel, leftLabel);
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.AutoFireCosAngleAdjustment = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(guardLines, guardLabelWidth), ActiveWeaponManager.AutoFireCosAngleAdjustment, 0, 4), 0.05f);
                        GUI.Label(RightLabelRect(guardLines), ActiveWeaponManager.AutoFireCosAngleAdjustment.ToString(), leftLabel);
                    }
                    else
                    {
                        textNumFields["AutoFireCosAngleAdjustment"].tryParseValue(GUI.TextField(InputFieldRect(guardLines, guardLabelWidth), textNumFields["AutoFireCosAngleAdjustment"].possibleValue, 4, inputFieldStyle));
                        ActiveWeaponManager.AutoFireCosAngleAdjustment = (float)textNumFields["AutoFireCosAngleAdjustment"].currentValue;
                    }
                    if (ActiveWeaponManager.AutoFireCosAngleAdjustment != oldAutoFireCosAngleAdjustment)
                        ActiveWeaponManager.OnAFCAAUpdated(null, null);

                    GUI.Label(LabelRect(++guardLines, guardLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_FieldofView"),//"Field of View"
                        leftLabel);
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.guardAngle = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(guardLines, guardLabelWidth), ActiveWeaponManager.guardAngle, 10, 360), 0.1f);
                        GUI.Label(RightLabelRect(guardLines), ActiveWeaponManager.guardAngle.ToString(), leftLabel);
                    }
                    else
                    {
                        textNumFields["guardAngle"].tryParseValue(GUI.TextField(InputFieldRect(guardLines, guardLabelWidth), textNumFields["guardAngle"].possibleValue, 4, inputFieldStyle));
                        ActiveWeaponManager.guardAngle = (float)textNumFields["guardAngle"].currentValue;
                    }

                    GUI.Label(LabelRect(++guardLines, guardLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_VisualRange"), leftLabel);//"Visual Range"
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.guardRange = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(guardLines, guardLabelWidth), ActiveWeaponManager.guardRange, 100, BDArmorySettings.MAX_GUARD_VISUAL_RANGE), 0.01f);
                        GUI.Label(RightLabelRect(guardLines), ActiveWeaponManager.guardRange.ToString(), leftLabel);
                    }
                    else
                    {
                        textNumFields["guardRange"].tryParseValue(GUI.TextField(InputFieldRect(guardLines, guardLabelWidth), textNumFields["guardRange"].possibleValue, 8, inputFieldStyle));
                        ActiveWeaponManager.guardRange = (float)textNumFields["guardRange"].currentValue;
                    }

                    GUI.Label(LabelRect(++guardLines, guardLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_GunsRange"), leftLabel);//"Guns Range"
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.gunRange = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(guardLines, guardLabelWidth), ActiveWeaponManager.gunRange, 0, ActiveWeaponManager.maxGunRange), 0.1f);
                        GUI.Label(RightLabelRect(guardLines), ActiveWeaponManager.gunRange.ToString(), leftLabel);
                    }
                    else
                    {
                        textNumFields["gunRange"].tryParseValue(GUI.TextField(InputFieldRect(guardLines, guardLabelWidth), textNumFields["gunRange"].possibleValue, 8, inputFieldStyle));
                        ActiveWeaponManager.gunRange = (float)textNumFields["gunRange"].currentValue;
                    }

                    GUI.Label(LabelRect(++guardLines, guardLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_MultiTargetNum"), leftLabel);//"Max Turret targets "
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.multiTargetNum = Mathf.Round(GUI.HorizontalSlider(SliderRect(guardLines, guardLabelWidth), ActiveWeaponManager.multiTargetNum, 1, 10));
                        GUI.Label(RightLabelRect(guardLines), ActiveWeaponManager.multiTargetNum.ToString(), leftLabel);
                    }
                    else
                    {
                        textNumFields["multiTargetNum"].tryParseValue(GUI.TextField(InputFieldRect(guardLines, guardLabelWidth), textNumFields["multiTargetNum"].possibleValue, 2, inputFieldStyle));
                        ActiveWeaponManager.multiTargetNum = (float)textNumFields["multiTargetNum"].currentValue;
                    }

                    GUI.Label(LabelRect(++guardLines, guardLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_MultiMissileNum"), leftLabel);//"Max Turret targets "
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.multiMissileTgtNum = Mathf.Round(GUI.HorizontalSlider(SliderRect(guardLines, guardLabelWidth), ActiveWeaponManager.multiMissileTgtNum, 1, 10));
                        GUI.Label(RightLabelRect(guardLines), ActiveWeaponManager.multiMissileTgtNum.ToString(), leftLabel);
                    }
                    else
                    {
                        textNumFields["multiMissileTgtNum"].tryParseValue(GUI.TextField(InputFieldRect(guardLines, guardLabelWidth), textNumFields["multiMissileTgtNum"].possibleValue, 2, inputFieldStyle));
                        ActiveWeaponManager.multiMissileTgtNum = (float)textNumFields["multiMissileTgtNum"].currentValue;
                    }

                    GUI.Label(LabelRect(++guardLines, guardLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_MissilesTgt"), leftLabel);//"Missiles/Tgt"
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.maxMissilesOnTarget = Mathf.Round(GUI.HorizontalSlider(SliderRect(guardLines, guardLabelWidth), ActiveWeaponManager.maxMissilesOnTarget, 1, MissileFire.maxAllowableMissilesOnTarget));
                        GUI.Label(RightLabelRect(guardLines), ActiveWeaponManager.maxMissilesOnTarget.ToString(), leftLabel);
                    }
                    else
                    {
                        textNumFields["maxMissilesOnTarget"].tryParseValue(GUI.TextField(InputFieldRect(guardLines, guardLabelWidth), textNumFields["maxMissilesOnTarget"].possibleValue, 2, inputFieldStyle));
                        ActiveWeaponManager.maxMissilesOnTarget = (float)textNumFields["maxMissilesOnTarget"].currentValue;
                    }

                    showTargetOptions = GUI.Toggle(ButtonRect(++guardLines), showTargetOptions, StringUtils.Localize("#LOC_BDArmory_Settings_Adv_Targeting"), showTargetOptions ? BDGuiSkin.box : BDGuiSkin.button);//"Advanced Targeting"
                    guardLines += 0.25f;

                    float TargetLines = 0;
                    if (showTargetOptions && showGuardMenu && !toolMinimized)
                    {
                        contentWidth = columnWidth - 30;
                        guardLines += 0.25f;
                        GUI.BeginGroup(new Rect(10, contentTop + (guardLines * entryHeight), contentWidth, (TargetingHeight + 0.25f) * entryHeight), GUIContent.none, BDGuiSkin.box);
                        TargetLines += 0.25f;
                        string CoMlabel = StringUtils.Localize("#LOC_BDArmory_TargetCOM", (ActiveWeaponManager.targetCoM ? StringUtils.Localize("#LOC_BDArmory_false") : StringUtils.Localize("#LOC_BDArmory_true")));//"Engage Air; True, False
                        if (GUI.Button(new Rect(leftIndent, TargetLines * entryHeight, contentWidth - 2 * leftIndent, entryHeight), CoMlabel, ActiveWeaponManager.targetCoM ? BDGuiSkin.box : BDGuiSkin.button))
                        {
                            ActiveWeaponManager.targetCoM = !ActiveWeaponManager.targetCoM;
                            ActiveWeaponManager.StartGuardTurretFiring(); //reset weapon targeting assignments
                            if (ActiveWeaponManager.targetCoM)
                            {
                                ActiveWeaponManager.targetCommand = false;
                                ActiveWeaponManager.targetEngine = false;
                                ActiveWeaponManager.targetWeapon = false;
                                ActiveWeaponManager.targetMass = false;
                                ActiveWeaponManager.targetRandom = false;
                            }
                            if (!ActiveWeaponManager.targetCoM && (!ActiveWeaponManager.targetWeapon && !ActiveWeaponManager.targetEngine && !ActiveWeaponManager.targetCommand && !ActiveWeaponManager.targetMass && !ActiveWeaponManager.targetRandom))
                            {
                                ActiveWeaponManager.targetRandom = true;
                            }
                        }
                        TargetLines += 1.1f;
                        string Commandlabel = StringUtils.Localize("#LOC_BDArmory_Command", (ActiveWeaponManager.targetCommand ? StringUtils.Localize("#LOC_BDArmory_false") : StringUtils.Localize("#LOC_BDArmory_true")));//"Engage Air; True, False
                        if (GUI.Button(new Rect(leftIndent, TargetLines * entryHeight, (contentWidth - 2 * leftIndent) / 2, entryHeight), Commandlabel, ActiveWeaponManager.targetCommand ? BDGuiSkin.box : BDGuiSkin.button))
                        {
                            ActiveWeaponManager.targetCommand = !ActiveWeaponManager.targetCommand;
                            ActiveWeaponManager.StartGuardTurretFiring();
                            if (ActiveWeaponManager.targetCommand)
                            {
                                ActiveWeaponManager.targetCoM = false;
                            }
                            if (!ActiveWeaponManager.targetCoM && (!ActiveWeaponManager.targetWeapon && !ActiveWeaponManager.targetEngine && !ActiveWeaponManager.targetCommand && !ActiveWeaponManager.targetMass && !ActiveWeaponManager.targetRandom))
                            {
                                ActiveWeaponManager.targetCoM = true;
                            }
                        }
                        string Engineslabel = StringUtils.Localize("#LOC_BDArmory_Engines", (ActiveWeaponManager.targetEngine ? StringUtils.Localize("#LOC_BDArmory_false") : StringUtils.Localize("#LOC_BDArmory_true")));//"Engage Missile; True, False
                        if (GUI.Button(new Rect(leftIndent + (contentWidth - 2 * leftIndent) / 2, TargetLines * entryHeight, (contentWidth - 2 * leftIndent) / 2, entryHeight), Engineslabel, ActiveWeaponManager.targetEngine ? BDGuiSkin.box : BDGuiSkin.button))
                        {
                            ActiveWeaponManager.targetEngine = !ActiveWeaponManager.targetEngine;
                            ActiveWeaponManager.StartGuardTurretFiring();
                            if (ActiveWeaponManager.targetEngine)
                            {
                                ActiveWeaponManager.targetCoM = false;
                            }
                            if (!ActiveWeaponManager.targetCoM && (!ActiveWeaponManager.targetWeapon && !ActiveWeaponManager.targetEngine && !ActiveWeaponManager.targetCommand && !ActiveWeaponManager.targetMass && !ActiveWeaponManager.targetRandom))
                            {
                                ActiveWeaponManager.targetCoM = true;
                            }
                        }
                        TargetLines += 1.1f;
                        string Weaponslabel = StringUtils.Localize("#LOC_BDArmory_Weapons", (ActiveWeaponManager.targetWeapon ? StringUtils.Localize("#LOC_BDArmory_false") : StringUtils.Localize("#LOC_BDArmory_true")));//"Engage Surface; True, False
                        if (GUI.Button(new Rect(leftIndent, TargetLines * entryHeight, (contentWidth - 2 * leftIndent) / 2, entryHeight), Weaponslabel, ActiveWeaponManager.targetWeapon ? BDGuiSkin.box : BDGuiSkin.button))
                        {
                            ActiveWeaponManager.targetWeapon = !ActiveWeaponManager.targetWeapon;
                            ActiveWeaponManager.StartGuardTurretFiring();
                            if (ActiveWeaponManager.targetWeapon)
                            {
                                ActiveWeaponManager.targetCoM = false;
                            }
                            if (!ActiveWeaponManager.targetCoM && (!ActiveWeaponManager.targetWeapon && !ActiveWeaponManager.targetEngine && !ActiveWeaponManager.targetCommand && !ActiveWeaponManager.targetMass && !ActiveWeaponManager.targetRandom))
                            {
                                ActiveWeaponManager.targetCoM = true;
                            }
                        }
                        string Masslabel = StringUtils.Localize("#LOC_BDArmory_Mass", (ActiveWeaponManager.targetMass ? StringUtils.Localize("#LOC_BDArmory_false") : StringUtils.Localize("#LOC_BDArmory_true")));//"Engage SLW; True, False
                        if (GUI.Button(new Rect(leftIndent + (contentWidth - 2 * leftIndent) / 2, TargetLines * entryHeight, (contentWidth - 2 * leftIndent) / 2, entryHeight), Masslabel, ActiveWeaponManager.targetMass ? BDGuiSkin.box : BDGuiSkin.button))
                        {
                            ActiveWeaponManager.targetMass = !ActiveWeaponManager.targetMass;
                            ActiveWeaponManager.StartGuardTurretFiring();
                            if (ActiveWeaponManager.targetMass)
                            {
                                ActiveWeaponManager.targetCoM = false;
                            }
                            if (!ActiveWeaponManager.targetCoM && (!ActiveWeaponManager.targetWeapon && !ActiveWeaponManager.targetEngine && !ActiveWeaponManager.targetCommand && !ActiveWeaponManager.targetMass && !ActiveWeaponManager.targetRandom))
                            {
                                ActiveWeaponManager.targetCoM = true;
                            }
                        }
                        TargetLines += 1.1f;
                        string Randomlabel = StringUtils.Localize("#LOC_BDArmory_Random", (ActiveWeaponManager.targetRandom ? StringUtils.Localize("#LOC_BDArmory_false") : StringUtils.Localize("#LOC_BDArmory_true")));//"Engage Surface; True, False
                        if (GUI.Button(new Rect(leftIndent, TargetLines * entryHeight, (contentWidth - 2 * leftIndent) / 2, entryHeight), Randomlabel, ActiveWeaponManager.targetRandom ? BDGuiSkin.box : BDGuiSkin.button))
                        {
                            ActiveWeaponManager.targetRandom = !ActiveWeaponManager.targetRandom;
                            ActiveWeaponManager.StartGuardTurretFiring();
                            if (ActiveWeaponManager.targetRandom)
                            {
                                ActiveWeaponManager.targetCoM = false;
                            }
                            if (!ActiveWeaponManager.targetCoM && (!ActiveWeaponManager.targetWeapon && !ActiveWeaponManager.targetEngine && !ActiveWeaponManager.targetCommand && !ActiveWeaponManager.targetMass && !ActiveWeaponManager.targetRandom))
                            {
                                ActiveWeaponManager.targetCoM = true;
                            }
                        }
                        TargetLines += 1.1f;
                        ActiveWeaponManager.targetingString = (ActiveWeaponManager.targetCoM ? StringUtils.Localize("#LOC_BDArmory_TargetCOM") + "; " : "")
                            + (ActiveWeaponManager.targetMass ? StringUtils.Localize("#LOC_BDArmory_Mass") + "; " : "")
                            + (ActiveWeaponManager.targetCommand ? StringUtils.Localize("#LOC_BDArmory_Command") + "; " : "")
                            + (ActiveWeaponManager.targetEngine ? StringUtils.Localize("#LOC_BDArmory_Engines") + "; " : "")
                            + (ActiveWeaponManager.targetWeapon ? StringUtils.Localize("#LOC_BDArmory_Weapons") + "; " : "")
                            + (ActiveWeaponManager.targetWeapon ? StringUtils.Localize("#LOC_BDArmory_Random") + "; " : "");
                        GUI.EndGroup();
                    }
                    TargetingHeight = Mathf.Lerp(TargetingHeight, TargetLines, 0.15f);
                    guardLines += TargetingHeight;

                    showEngageList = GUI.Toggle(ButtonRect(++guardLines), showEngageList, showEngageList ? StringUtils.Localize("#LOC_BDArmory_DisableEngageOptions") : StringUtils.Localize("#LOC_BDArmory_EnableEngageOptions"), showEngageList ? BDGuiSkin.box : BDGuiSkin.button);//"Enable/Disable Engagement options"
                    guardLines += 0.25f;

                    float EngageLines = 0;
                    if (showEngageList && showGuardMenu && !toolMinimized)
                    {
                        contentWidth = columnWidth - 30;
                        guardLines += 0.25f;
                        GUI.BeginGroup(new Rect(10, contentTop + guardLines * entryHeight, contentWidth, (EngageHeight + 0.25f) * entryHeight), GUIContent.none, BDGuiSkin.box);
                        EngageLines += 0.25f;

                        string Airlabel = StringUtils.Localize("#LOC_BDArmory_EngageAir", (ActiveWeaponManager.engageAir ? StringUtils.Localize("#LOC_BDArmory_false") : StringUtils.Localize("#LOC_BDArmory_true")));//"Engage Air; True, False
                        if (GUI.Button(new Rect(leftIndent, EngageLines * entryHeight, (contentWidth - 2 * leftIndent) / 2, entryHeight), Airlabel, ActiveWeaponManager.engageAir ? BDGuiSkin.box : BDGuiSkin.button))
                        {
                            ActiveWeaponManager.ToggleEngageAir();
                        }
                        string Missilelabel = StringUtils.Localize("#LOC_BDArmory_EngageMissile", (ActiveWeaponManager.engageMissile ? StringUtils.Localize("#LOC_BDArmory_false") : StringUtils.Localize("#LOC_BDArmory_true")));//"Engage Missile; True, False
                        if (GUI.Button(new Rect(leftIndent + (contentWidth - 2 * leftIndent) / 2, EngageLines * entryHeight, (contentWidth - 2 * leftIndent) / 2, entryHeight), Missilelabel, ActiveWeaponManager.engageMissile ? BDGuiSkin.box : BDGuiSkin.button))
                        {
                            ActiveWeaponManager.ToggleEngageMissile();
                        }
                        EngageLines += 1.1f;
                        string Srflabel = StringUtils.Localize("#LOC_BDArmory_EngageSurface", (ActiveWeaponManager.engageSrf ? StringUtils.Localize("#LOC_BDArmory_false") : StringUtils.Localize("#LOC_BDArmory_true")));//"Engage Surface; True, False
                        if (GUI.Button(new Rect(leftIndent, EngageLines * entryHeight, (contentWidth - 2 * leftIndent) / 2, entryHeight), Srflabel, ActiveWeaponManager.engageSrf ? BDGuiSkin.box : BDGuiSkin.button))
                        {
                            ActiveWeaponManager.ToggleEngageSrf();
                        }

                        string SLWlabel = StringUtils.Localize("#LOC_BDArmory_EngageSLW", (ActiveWeaponManager.engageSLW ? StringUtils.Localize("#LOC_BDArmory_false") : StringUtils.Localize("#LOC_BDArmory_true")));//"Engage SLW; True, False
                        if (GUI.Button(new Rect(leftIndent + (contentWidth - 2 * leftIndent) / 2, EngageLines * entryHeight, (contentWidth - 2 * leftIndent) / 2, entryHeight), SLWlabel, ActiveWeaponManager.engageSLW ? BDGuiSkin.box : BDGuiSkin.button))
                        {
                            ActiveWeaponManager.ToggleEngageSLW();
                        }
                        EngageLines += 1.1f;
                        GUI.EndGroup();
                    }
                    EngageHeight = Mathf.Lerp(EngageHeight, EngageLines, 0.15f);
                    guardLines += EngageHeight;
                    GUI.EndGroup();
                    ++guardLines;
                }
                guardHeight = Mathf.Lerp(guardHeight, guardLines, 0.15f);
                line += guardHeight;

                float priorityLines = 0;
                if (showPriorities && !toolMinimized)
                {
                    line += 0.25f;
                    GUI.BeginGroup(new Rect(5, contentTop + line * entryHeight, columnWidth - 10, priorityheight * entryHeight), GUIContent.none, BDGuiSkin.box);
                    priorityLines += 0.1f;

                    GUI.Label(LabelRect(++priorityLines, priorityLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_targetBias"), leftLabel);//"current target bias"                 
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.targetBias = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(priorityLines, priorityLabelWidth), ActiveWeaponManager.targetBias, -10, 10), 0.1f);
                        GUI.Label(RightLabelRect(priorityLines), ActiveWeaponManager.targetBias.ToString(), leftLabel);
                    }
                    else
                    {
                        textNumFields["targetBias"].tryParseValue(GUI.TextField(InputFieldRect(priorityLines, priorityLabelWidth), textNumFields["targetBias"].possibleValue, 4, inputFieldStyle));
                        ActiveWeaponManager.targetBias = (float)textNumFields["targetBias"].currentValue;
                    }

                    GUI.Label(LabelRect(++priorityLines, priorityLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_targetProximity"), leftLabel); //target proximity"
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.targetWeightRange = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(priorityLines, priorityLabelWidth), ActiveWeaponManager.targetWeightRange, -10, 10), 0.1f);
                        GUI.Label(RightLabelRect(priorityLines), ActiveWeaponManager.targetWeightRange.ToString(), leftLabel);
                    }
                    else
                    {
                        textNumFields["targetWeightRange"].tryParseValue(GUI.TextField(InputFieldRect(priorityLines, priorityLabelWidth), textNumFields["targetWeightRange"].possibleValue, 4, inputFieldStyle));
                        ActiveWeaponManager.targetWeightRange = (float)textNumFields["targetWeightRange"].currentValue;
                    }

                    GUI.Label(LabelRect(++priorityLines, priorityLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_targetPreference"), leftLabel); //target Air preference"
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.targetWeightAirPreference = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(priorityLines, priorityLabelWidth), ActiveWeaponManager.targetWeightAirPreference, -10, 10), 0.1f);
                        GUI.Label(RightLabelRect(priorityLines), ActiveWeaponManager.targetWeightAirPreference.ToString(), leftLabel);
                    }
                    else
                    {
                        textNumFields["targetWeightAirPreference"].tryParseValue(GUI.TextField(InputFieldRect(priorityLines, priorityLabelWidth), textNumFields["targetWeightAirPreference"].possibleValue, 4, inputFieldStyle));
                        ActiveWeaponManager.targetWeightAirPreference = (float)textNumFields["targetWeightAirPreference"].currentValue;
                    }

                    GUI.Label(LabelRect(++priorityLines, priorityLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_targetAngletoTarget"), leftLabel); //target angle"
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.targetWeightATA = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(priorityLines, priorityLabelWidth), ActiveWeaponManager.targetWeightATA, -10, 10), 0.1f);
                        GUI.Label(RightLabelRect(priorityLines), ActiveWeaponManager.targetWeightATA.ToString(), leftLabel);
                    }
                    else
                    {
                        textNumFields["targetWeightATA"].tryParseValue(GUI.TextField(InputFieldRect(priorityLines, priorityLabelWidth), textNumFields["targetWeightATA"].possibleValue, 4, inputFieldStyle));
                        ActiveWeaponManager.targetWeightATA = (float)textNumFields["targetWeightATA"].currentValue;
                    }

                    GUI.Label(LabelRect(++priorityLines, priorityLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_targetAngleDist"), leftLabel); //Angle over Distance"
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.targetWeightAoD = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(priorityLines, priorityLabelWidth), ActiveWeaponManager.targetWeightAoD, -10, 10), 0.1f);
                        GUI.Label(RightLabelRect(priorityLines), ActiveWeaponManager.targetWeightAoD.ToString(), leftLabel);
                    }
                    else
                    {
                        textNumFields["targetWeightAoD"].tryParseValue(GUI.TextField(InputFieldRect(priorityLines, priorityLabelWidth), textNumFields["targetWeightAoD"].possibleValue, 4, inputFieldStyle));
                        ActiveWeaponManager.targetWeightAoD = (float)textNumFields["targetWeightAoD"].currentValue;
                    }

                    GUI.Label(LabelRect(++priorityLines, priorityLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_targetAccel"), leftLabel); //target accel"
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.targetWeightAccel = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(priorityLines, priorityLabelWidth), ActiveWeaponManager.targetWeightAccel, -10, 10), 0.1f);
                        GUI.Label(RightLabelRect(priorityLines), ActiveWeaponManager.targetWeightAccel.ToString(), leftLabel);
                    }
                    else
                    {
                        textNumFields["targetWeightAccel"].tryParseValue(GUI.TextField(InputFieldRect(priorityLines, priorityLabelWidth), textNumFields["targetWeightAccel"].possibleValue, 4, inputFieldStyle));
                        ActiveWeaponManager.targetWeightAccel = (float)textNumFields["targetWeightAccel"].currentValue;
                    }

                    GUI.Label(LabelRect(++priorityLines, priorityLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_targetClosingTime"), leftLabel); //target closing time"
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.targetWeightClosureTime = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(priorityLines, priorityLabelWidth), ActiveWeaponManager.targetWeightClosureTime, -10, 10), 0.1f);
                        GUI.Label(RightLabelRect(priorityLines), ActiveWeaponManager.targetWeightClosureTime.ToString(), leftLabel);
                    }
                    else
                    {
                        textNumFields["targetWeightClosureTime"].tryParseValue(GUI.TextField(InputFieldRect(priorityLines, priorityLabelWidth), textNumFields["targetWeightClosureTime"].possibleValue, 4, inputFieldStyle));
                        ActiveWeaponManager.targetWeightClosureTime = (float)textNumFields["targetWeightClosureTime"].currentValue;
                    }

                    GUI.Label(LabelRect(++priorityLines, priorityLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_targetgunNumber"), leftLabel); //target weapon num."
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.targetWeightWeaponNumber = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(priorityLines, priorityLabelWidth), ActiveWeaponManager.targetWeightWeaponNumber, -10, 10), 0.1f);
                        GUI.Label(RightLabelRect(priorityLines), ActiveWeaponManager.targetWeightWeaponNumber.ToString(), leftLabel);
                    }
                    else
                    {
                        textNumFields["targetWeightWeaponNumber"].tryParseValue(GUI.TextField(InputFieldRect(priorityLines, priorityLabelWidth), textNumFields["targetWeightWeaponNumber"].possibleValue, 4, inputFieldStyle));
                        ActiveWeaponManager.targetWeightWeaponNumber = (float)textNumFields["targetWeightWeaponNumber"].currentValue;
                    }

                    GUI.Label(LabelRect(++priorityLines, priorityLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_targetMass"), leftLabel); //target mass"
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.targetWeightMass = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(priorityLines, priorityLabelWidth), ActiveWeaponManager.targetWeightMass, -10, 10), 0.1f);
                        GUI.Label(RightLabelRect(priorityLines), ActiveWeaponManager.targetWeightMass.ToString(), leftLabel);
                    }
                    else
                    {
                        textNumFields["targetWeightMass"].tryParseValue(GUI.TextField(InputFieldRect(priorityLines, priorityLabelWidth), textNumFields["targetWeightMass"].possibleValue, 4, inputFieldStyle));
                        ActiveWeaponManager.targetWeightMass = (float)textNumFields["targetWeightMass"].currentValue;
                    }

                    GUI.Label(LabelRect(++priorityLines, priorityLabelWidth), StringUtils.Localize("#LOC_BDArmory_TargetPriority_TargetDmg"), leftLabel); //target Damage"
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.targetWeightDamage = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(priorityLines, priorityLabelWidth), ActiveWeaponManager.targetWeightDamage, -10, 10), 0.1f);
                        GUI.Label(RightLabelRect(priorityLines), ActiveWeaponManager.targetWeightDamage.ToString(), leftLabel);
                    }
                    else
                    {
                        textNumFields["targetWeightDamage"].tryParseValue(GUI.TextField(InputFieldRect(priorityLines, priorityLabelWidth), textNumFields["targetWeightDamage"].possibleValue, 4, inputFieldStyle));
                        ActiveWeaponManager.targetWeightDamage = (float)textNumFields["targetWeightDamage"].currentValue;
                    }

                    GUI.Label(LabelRect(++priorityLines, priorityLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_targetAllies"), leftLabel); //target mass"
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.targetWeightFriendliesEngaging = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(priorityLines, priorityLabelWidth), ActiveWeaponManager.targetWeightFriendliesEngaging, -10, 10), 0.1f);
                        GUI.Label(RightLabelRect(priorityLines), ActiveWeaponManager.targetWeightFriendliesEngaging.ToString(), leftLabel);
                    }
                    else
                    {
                        textNumFields["targetWeightFriendliesEngaging"].tryParseValue(GUI.TextField(InputFieldRect(priorityLines, priorityLabelWidth), textNumFields["targetWeightFriendliesEngaging"].possibleValue, 4, inputFieldStyle));
                        ActiveWeaponManager.targetWeightFriendliesEngaging = (float)textNumFields["targetWeightFriendliesEngaging"].currentValue;
                    }

                    GUI.Label(LabelRect(++priorityLines, priorityLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_targetThreat"), leftLabel); //target proximity"
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.targetWeightThreat = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(priorityLines, priorityLabelWidth), ActiveWeaponManager.targetWeightThreat, -10, 10), 0.1f);
                        GUI.Label(RightLabelRect(priorityLines), ActiveWeaponManager.targetWeightThreat.ToString(), leftLabel);
                    }
                    else
                    {
                        textNumFields["targetWeightThreat"].tryParseValue(GUI.TextField(InputFieldRect(priorityLines, priorityLabelWidth), textNumFields["targetWeightThreat"].possibleValue, 4, inputFieldStyle));
                        ActiveWeaponManager.targetWeightThreat = (float)textNumFields["targetWeightThreat"].currentValue;
                    }

                    GUI.Label(LabelRect(++priorityLines, priorityLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_defendTeammate"), leftLabel); //defend teammate"
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.targetWeightProtectTeammate = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(priorityLines, priorityLabelWidth), ActiveWeaponManager.targetWeightProtectTeammate, -10, 10), 0.1f);
                        GUI.Label(RightLabelRect(priorityLines), ActiveWeaponManager.targetWeightProtectTeammate.ToString(), leftLabel);
                    }
                    else
                    {
                        textNumFields["targetWeightProtectTeammate"].tryParseValue(GUI.TextField(InputFieldRect(priorityLines, priorityLabelWidth), textNumFields["targetWeightProtectTeammate"].possibleValue, 4, inputFieldStyle));
                        ActiveWeaponManager.targetWeightProtectTeammate = (float)textNumFields["targetWeightProtectTeammate"].currentValue;
                    }

                    GUI.Label(LabelRect(++priorityLines, priorityLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_defendVIP"), leftLabel); //target proximity"
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.targetWeightProtectVIP = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(priorityLines, priorityLabelWidth), ActiveWeaponManager.targetWeightProtectVIP, -10, 10), 0.1f);
                        GUI.Label(RightLabelRect(priorityLines), ActiveWeaponManager.targetWeightProtectVIP.ToString(), leftLabel);
                    }
                    else
                    {
                        textNumFields["targetWeightProtectVIP"].tryParseValue(GUI.TextField(InputFieldRect(priorityLines, priorityLabelWidth), textNumFields["targetWeightProtectVIP"].possibleValue, 4, inputFieldStyle));
                        ActiveWeaponManager.targetWeightProtectVIP = (float)textNumFields["targetWeightProtectVIP"].currentValue;
                    }

                    GUI.Label(LabelRect(++priorityLines, priorityLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_targetVIP"), leftLabel); //target proximity"
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.targetWeightAttackVIP = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(priorityLines, priorityLabelWidth), ActiveWeaponManager.targetWeightAttackVIP, -10, 10), 0.1f);
                        GUI.Label(RightLabelRect(priorityLines), ActiveWeaponManager.targetWeightAttackVIP.ToString(), leftLabel);
                    }
                    else
                    {
                        textNumFields["targetWeightAttackVIP"].tryParseValue(GUI.TextField(InputFieldRect(priorityLines, priorityLabelWidth), textNumFields["targetWeightAttackVIP"].possibleValue, 4, inputFieldStyle));
                        ActiveWeaponManager.targetWeightAttackVIP = (float)textNumFields["targetWeightAttackVIP"].currentValue;
                    }

                    priorityLines += 1.1f;
                    GUI.EndGroup();
                }
                priorityheight = Mathf.Lerp(priorityheight, priorityLines, 0.15f);
                line += priorityheight;

                float moduleLines = 0;
                if (showModules && !toolMinimized)
                {
                    line += 0.25f;
                    GUI.BeginGroup(
                        new Rect(5, contentTop + (line * entryHeight), columnWidth - 10, numberOfModules * entryHeight),
                        GUIContent.none, BDGuiSkin.box);
                    moduleLines += 0.1f;

                    numberOfModules = 0;
                    //RWR
                    if (ActiveWeaponManager.rwr)
                    {
                        numberOfModules++;
                        bool isEnabled = ActiveWeaponManager.rwr.displayRWR;
                        string label = StringUtils.Localize("#LOC_BDArmory_WMWindow_RadarWarning");//"Radar Warning Receiver"
                        Rect rwrRect = new Rect(leftIndent, +(moduleLines * entryHeight), columnWidth - 2 * leftIndent, entryHeight);
                        if (GUI.Button(rwrRect, label, isEnabled ? centerLabelOrange : centerLabel))
                        {
                            if (isEnabled)
                            {
                                //ActiveWeaponManager.rwr.DisableRWR();
                                ActiveWeaponManager.rwr.displayRWR = false;
                            }
                            else
                            {
                                //ActiveWeaponManager.rwr.EnableRWR();
                                ActiveWeaponManager.rwr.displayRWR = true;
                            }
                        }
                        moduleLines++;
                    }

                    //TGP
                    using (List<ModuleTargetingCamera>.Enumerator mtc = ActiveWeaponManager.targetingPods.GetEnumerator())
                        while (mtc.MoveNext())
                        {
                            if (mtc.Current == null) continue;
                            numberOfModules++;
                            bool isEnabled = (mtc.Current.cameraEnabled);
                            bool isActive = (mtc.Current == ModuleTargetingCamera.activeCam);
                            GUIStyle moduleStyle = isEnabled ? centerLabelOrange : centerLabel; // = mtc
                            string label = mtc.Current.part.partInfo.title;
                            if (isActive)
                            {
                                moduleStyle = centerLabelRed;
                                label = $"[{label}]";
                            }
                            if (GUI.Button(new Rect(leftIndent, +(moduleLines * entryHeight), columnWidth - 2 * leftIndent, entryHeight),
                                label, moduleStyle))
                            {
                                if (isActive)
                                {
                                    mtc.Current.ToggleCamera();
                                }
                                else
                                {
                                    mtc.Current.EnableCamera();
                                }
                            }
                            moduleLines++;
                        }

                    //RADAR
                    using (List<ModuleRadar>.Enumerator mr = ActiveWeaponManager.radars.GetEnumerator())
                        while (mr.MoveNext())
                        {
                            if (mr.Current == null) continue;
                            numberOfModules++;
                            GUIStyle moduleStyle = mr.Current.radarEnabled ? centerLabelBlue : centerLabel;
                            string label = mr.Current.radarName;
                            if (GUI.Button(new Rect(leftIndent, +(moduleLines * entryHeight), columnWidth - 2 * leftIndent, entryHeight),
                                label, moduleStyle))
                            {
                                mr.Current.Toggle();
                            }
                            moduleLines++;
                        }
                    using (List<ModuleIRST>.Enumerator mr = ActiveWeaponManager.irsts.GetEnumerator())
                        while (mr.MoveNext())
                        {
                            if (mr.Current == null) continue;
                            numberOfModules++;
                            GUIStyle moduleStyle = mr.Current.irstEnabled ? centerLabelBlue : centerLabel;
                            string label = mr.Current.IRSTName;
                            if (GUI.Button(new Rect(leftIndent, +(moduleLines * entryHeight), columnWidth - 2 * leftIndent, entryHeight),
                                label, moduleStyle))
                            {
                                mr.Current.Toggle();
                            }
                            moduleLines++;
                        }
                    //JAMMERS
                    using (List<ModuleECMJammer>.Enumerator jammer = ActiveWeaponManager.jammers.GetEnumerator())
                        while (jammer.MoveNext())
                        {
                            if (jammer.Current == null) continue;
                            if (jammer.Current.alwaysOn) continue;

                            numberOfModules++;
                            GUIStyle moduleStyle = jammer.Current.jammerEnabled ? centerLabelBlue : centerLabel;
                            string label = jammer.Current.part.partInfo.title;
                            if (GUI.Button(new Rect(leftIndent, +(moduleLines * entryHeight), columnWidth - 2 * leftIndent, entryHeight),
                                label, moduleStyle))
                            {
                                jammer.Current.Toggle();
                            }
                            moduleLines++;
                        }
                    //CLOAKS
                    using (List<ModuleCloakingDevice>.Enumerator cloak = ActiveWeaponManager.cloaks.GetEnumerator())
                        while (cloak.MoveNext())
                        {
                            if (cloak.Current == null) continue;
                            if (cloak.Current.alwaysOn) continue;

                            numberOfModules++;
                            GUIStyle moduleStyle = cloak.Current.cloakEnabled ? centerLabelBlue : centerLabel;
                            string label = cloak.Current.part.partInfo.title;
                            if (GUI.Button(new Rect(leftIndent, +(moduleLines * entryHeight), columnWidth - 2 * leftIndent, entryHeight),
                                label, moduleStyle))
                            {
                                cloak.Current.Toggle();
                            }
                            moduleLines++;
                        }

                    //Other modules
                    using (var module = ActiveWeaponManager.wmModules.GetEnumerator())
                        while (module.MoveNext())
                        {
                            if (module.Current == null) continue;

                            numberOfModules++;
                            GUIStyle moduleStyle = module.Current.Enabled ? centerLabelBlue : centerLabel;
                            string label = module.Current.Name;
                            if (GUI.Button(new Rect(leftIndent, +(moduleLines * entryHeight), columnWidth - 2 * leftIndent, entryHeight),
                                label, moduleStyle))
                            {
                                module.Current.Toggle();
                            }
                            moduleLines++;
                        }

                    //GPS coordinator
                    GUIStyle gpsModuleStyle = showWindowGPS ? centerLabelBlue : centerLabel;
                    numberOfModules++;
                    if (GUI.Button(new Rect(leftIndent, +(moduleLines * entryHeight), columnWidth - 2 * leftIndent, entryHeight),
                        StringUtils.Localize("#LOC_BDArmory_WMWindow_GPSCoordinator"), gpsModuleStyle))//"GPS Coordinator"
                    {
                        showWindowGPS = !showWindowGPS;
                    }
                    moduleLines++;

                    //wingCommander
                    if (ActiveWeaponManager.wingCommander)
                    {
                        GUIStyle wingComStyle = ActiveWeaponManager.wingCommander.showGUI
                            ? centerLabelBlue
                            : centerLabel;
                        numberOfModules++;
                        if (GUI.Button(new Rect(leftIndent, +(moduleLines * entryHeight), columnWidth - 2 * leftIndent, entryHeight),
                            StringUtils.Localize("#LOC_BDArmory_WMWindow_WingCommand"), wingComStyle))//"Wing Command"
                        {
                            ActiveWeaponManager.wingCommander.ToggleGUI();
                        }
                        moduleLines++;
                    }

                    moduleLines += 0.1f;
                    GUI.EndGroup();
                }
                modulesHeight = Mathf.Lerp(modulesHeight, moduleLines, 0.15f);
                line += modulesHeight;

                float gpsLines = 0;
                if (showWindowGPS && !toolMinimized)
                {
                    line += 0.25f;
                    GUI.BeginGroup(new Rect(5, contentTop + (line * entryHeight), columnWidth, WindowRectGps.height));
                    WindowGPS();
                    GUI.EndGroup();
                    gpsLines = WindowRectGps.height / entryHeight;
                }
                gpsHeight = Mathf.Lerp(gpsHeight, gpsLines, 0.15f);
                line += gpsHeight;

                if (infoLinkEnabled && !toolMinimized)
                {
                    windowColumns = 2;

                    GUI.Label(new Rect(leftIndent + columnWidth, contentTop, columnWidth - (leftIndent), entryHeight), StringUtils.Localize("#LOC_BDArmory_AIWindow_infoLink"), kspTitleLabel);//"infolink"
                    GUILayout.BeginArea(new Rect(leftIndent + columnWidth, contentTop + (entryHeight * 1.5f), columnWidth - (leftIndent), toolWindowHeight - (entryHeight * 1.5f) - (2 * contentTop)));
                    using (var scrollViewScope = new GUILayout.ScrollViewScope(scrollInfoVector, GUILayout.Width(columnWidth - (leftIndent)), GUILayout.Height(toolWindowHeight - (entryHeight * 1.5f) - (2 * contentTop))))
                    {
                        scrollInfoVector = scrollViewScope.scrollPosition;
                        if (showWeaponList)
                        {
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_WMWindow_ListWeapons"), leftLabelBold, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Weapons
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_WMWindow_Weapons_Desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //weapons desc
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_WMWindow_Ripple_Salvo_Desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //ripple/salvo desc
                        }
                        if (showGuardMenu)
                        {
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_WMWindow_GuardMenu"), leftLabelBold, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Guard Mode
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_WMWindow_GuardTab_Desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Guard desc
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_WMWindow_FiringInterval_Desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //firing inverval desc
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_WMWindow_BurstLength_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //burst length desc
                            GUILayout.Label(FiringAngleImage);
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_WMWindow_FiringTolerance_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //firing angle desc
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_WMWindow_FieldofView_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //FoV desc
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_WMWindow_VisualRange_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //guard range desc
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_WMWindow_GunsRange_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //weapon range desc
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_WMWindow_MultiTargetNum_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //multiturrets desc
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_WMWindow_MultiMissileTgtNum_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //multiturrets desc
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_WMWindow_MissilesTgt_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //multimissiles desc
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_WMWindow_TargetType_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //subsection targeting desc
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_WMWindow_EngageType_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //engagement toggles desc
                        }
                        if (showPriorities)
                        {
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_WMWindow_Prioritues_Desc"), leftLabelBold, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Tgt Priorities
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_WMWindow_targetBias_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Tgt Bias
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_WMWindow_targetPreference_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Tgt engagement Pref
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_WMWindow_targetProximity_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Tgt dist
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_WMWindow_targetAngletoTarget_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Tgt angle
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_WMWindow_targetAngleDist_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Tgt angle/dist
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_WMWindow_targetAccel_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Tgt accel
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_WMWindow_targetClosingTime_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Tgt closing time
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_WMWindow_targetgunNumber_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Tgt weapons num
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_WMWindow_targetMass_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Tgt mass
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_WMWindow_targetDmg_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Tgt Damage
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_WMWindow_targetAllies_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Tgt allies attacking
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_WMWindow_targetThreat_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Tgt threat
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_WMWindow_targetVIP_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Tgt VIP
                        }

                    }
                    GUILayout.EndArea();
                }
            }
            else
            {
                GUI.Label(new Rect(leftIndent, contentTop + (line * entryHeight), columnWidth - 2 * leftIndent, entryHeight),
                   StringUtils.Localize("#LOC_BDArmory_WMWindow_NoWeaponManager"), BDGuiSkin.box);// "No Weapon Manager found."
                line++;
            }
#if DEBUG
            if (GUI.Button(new Rect(leftIndent, contentTop + (line++ * entryHeight), contentWidth, entryHeight * 1.25f), "Double click to QUIT", Time.realtimeSinceStartup - quitTimer > 1 ? BDGuiSkin.button : BDGuiSkin.box)) // Big QUIT button for debug mode. Double click within 1s to quit.
            {
                if (Time.realtimeSinceStartup - quitTimer < 1)
                {
                    SaveConfig();
                    TournamentAutoResume.AutoQuit(0);
                }
                quitTimer = Time.realtimeSinceStartup;
            }
#endif
            toolWindowWidth = Mathf.Lerp(toolWindowWidth, columnWidth * windowColumns, 0.15f);
            toolWindowHeight = Mathf.Lerp(toolWindowHeight, contentTop + (line * entryHeight) + 5, 1);
            var previousWindowHeight = WindowRectToolbar.height;
            WindowRectToolbar.height = toolWindowHeight;
            WindowRectToolbar.width = toolWindowWidth;
            numberOfButtons = buttonNumber + 1;
            if (BDArmorySettings.STRICT_WINDOW_BOUNDARIES && toolWindowHeight < previousWindowHeight && Mathf.Round(WindowRectToolbar.y + previousWindowHeight) == Screen.height) // Window shrunk while being at edge of screen.
                WindowRectToolbar.y = Screen.height - WindowRectToolbar.height;
            GUIUtils.RepositionWindow(ref WindowRectToolbar);
        }
#if DEBUG
        float quitTimer = 0;
#endif

        bool validGPSName = true;

        //GPS window
        public void WindowGPS()
        {
            GUI.Box(WindowRectGps, GUIContent.none, BDGuiSkin.box);
            gpsEntryCount = 0;
            Rect listRect = new Rect(gpsBorder, gpsBorder, WindowRectGps.width - (2 * gpsBorder),
                WindowRectGps.height - (2 * gpsBorder));
            GUI.BeginGroup(listRect);
            string targetLabel = $"{StringUtils.Localize("#LOC_BDArmory_WMWindow_GPSTarget")}: {ActiveWeaponManager.designatedGPSInfo.name}";//GPS Target
            GUI.Label(new Rect(0, 0, listRect.width, gpsEntryHeight), targetLabel, kspTitleLabel);

            // Expand/Collapse Target Toggle button
            if (GUI.Button(new Rect(listRect.width - gpsEntryHeight, 0, gpsEntryHeight, gpsEntryHeight), showTargets ? "-" : "+", BDGuiSkin.button))
                showTargets = !showTargets;

            gpsEntryCount += 0.85f;
            if (ActiveWeaponManager.designatedGPSCoords != Vector3d.zero)
            {
                GUI.Label(new Rect(0, gpsEntryCount * gpsEntryHeight, listRect.width - gpsEntryHeight, gpsEntryHeight),
                    BodyUtils.FormattedGeoPos(ActiveWeaponManager.designatedGPSCoords, true), BDGuiSkin.box);
                if (
                    GUI.Button(
                        new Rect(listRect.width - gpsEntryHeight, gpsEntryCount * gpsEntryHeight, gpsEntryHeight,
                            gpsEntryHeight), "X", BDGuiSkin.button))
                {
                    ActiveWeaponManager.designatedGPSInfo = new GPSTargetInfo();
                }
            }
            else
            {
                GUI.Label(new Rect(0, gpsEntryCount * gpsEntryHeight, listRect.width - gpsEntryHeight, gpsEntryHeight),
                    StringUtils.Localize("#LOC_BDArmory_WMWindow_NoTarget"), BDGuiSkin.box);//"No Target"
            }

            gpsEntryCount += 1.35f;
            int indexToRemove = -1;
            int index = 0;
            BDTeam myTeam = ActiveWeaponManager.Team;
            if (showTargets)
            {
                using (var coordinate = BDATargetManager.GPSTargetList(myTeam).GetEnumerator())
                    while (coordinate.MoveNext())
                    {
                        Color origWColor = GUI.color;
                        if (coordinate.Current.EqualsTarget(ActiveWeaponManager.designatedGPSInfo))
                        {
                            GUI.color = XKCDColors.LightOrange;
                        }

                        string label = BodyUtils.FormattedGeoPosShort(coordinate.Current.gpsCoordinates, false);
                        float nameWidth = 100;
                        if (editingGPSName && index == editingGPSNameIndex)
                        {
                            if (validGPSName && Event.current.type == EventType.KeyDown &&
                                Event.current.keyCode == KeyCode.Return)
                            {
                                editingGPSName = false;
                                hasEnteredGPSName = true;
                            }
                            else
                            {
                                Color origColor = GUI.color;
                                if (newGPSName.Contains(";") || newGPSName.Contains(":") || newGPSName.Contains(","))
                                {
                                    validGPSName = false;
                                    GUI.color = Color.red;
                                }
                                else
                                {
                                    validGPSName = true;
                                }

                                newGPSName = GUI.TextField(
                                  new Rect(0, gpsEntryCount * gpsEntryHeight, nameWidth, gpsEntryHeight), newGPSName, 12, inputFieldStyle);
                                GUI.color = origColor;
                            }
                        }
                        else
                        {
                            if (GUI.Button(new Rect(0, gpsEntryCount * gpsEntryHeight, nameWidth, gpsEntryHeight),
                              coordinate.Current.name,
                              BDGuiSkin.button))
                            {
                                editingGPSName = true;
                                editingGPSNameIndex = index;
                                newGPSName = coordinate.Current.name;
                            }
                        }

                        if (
                          GUI.Button(
                            new Rect(nameWidth, gpsEntryCount * gpsEntryHeight, listRect.width - gpsEntryHeight - nameWidth,
                              gpsEntryHeight), label, BDGuiSkin.button))
                        {
                            ActiveWeaponManager.designatedGPSInfo = coordinate.Current;
                            ActiveWeaponManager.designatedGPSCoordsIndex = index;
                            editingGPSName = false;
                        }

                        if (
                          GUI.Button(
                            new Rect(listRect.width - gpsEntryHeight, gpsEntryCount * gpsEntryHeight, gpsEntryHeight,
                              gpsEntryHeight), "X", BDGuiSkin.button))
                        {
                            indexToRemove = index;
                        }

                        gpsEntryCount++;
                        index++;
                        GUI.color = origWColor;
                    }
            }

            if (hasEnteredGPSName && editingGPSNameIndex < BDATargetManager.GPSTargetList(myTeam).Count)
            {
                hasEnteredGPSName = false;
                GPSTargetInfo old = BDATargetManager.GPSTargetList(myTeam)[editingGPSNameIndex];
                if (ActiveWeaponManager.designatedGPSInfo.EqualsTarget(old))
                {
                    ActiveWeaponManager.designatedGPSInfo.name = newGPSName;
                }
                BDATargetManager.GPSTargetList(myTeam)[editingGPSNameIndex] =
                    new GPSTargetInfo(BDATargetManager.GPSTargetList(myTeam)[editingGPSNameIndex].gpsCoordinates,
                        newGPSName);
                editingGPSNameIndex = 0;
                BDATargetManager.Instance.SaveGPSTargets();
            }

            GUI.EndGroup();

            if (indexToRemove >= 0)
            {
                BDATargetManager.GPSTargetList(myTeam).RemoveAt(indexToRemove);
                BDATargetManager.Instance.SaveGPSTargets();
            }

            WindowRectGps.height = (2 * gpsBorder) + (gpsEntryCount * gpsEntryHeight);
        }

        Rect SLineRect(float line, float indentLevel = 0, bool symmetric = false)
        {
            return new Rect(settingsMargin + indentLevel * settingsMargin, line * settingsLineHeight, settingsWidth - 2 * settingsMargin - (symmetric ? 2 : 1) * indentLevel * settingsMargin, settingsLineHeight);
        }

        Rect SLeftRect(float line, float indentLevel = 0, bool symmetric = false)
        {
            return new Rect(settingsMargin + indentLevel * settingsMargin, line * settingsLineHeight, settingsWidth / 2 - settingsMargin - settingsMargin / 4 - (symmetric ? 2 : 1) * indentLevel * settingsMargin, settingsLineHeight);
        }

        Rect SRightRect(float line, float indentLevel = 0, bool symmetric = false)
        {
            return new Rect(settingsWidth / 2 + settingsMargin / 4 + indentLevel * settingsMargin, line * settingsLineHeight, settingsWidth / 2 - settingsMargin - settingsMargin / 4 - (symmetric ? 2 : 1) * indentLevel * settingsMargin, settingsLineHeight);
        }

        Rect SLeftSliderRect(float line, float indentLevel = 0)
        {
            return new Rect(settingsMargin + indentLevel * settingsMargin, (line + 0.1f) * settingsLineHeight, settingsWidth / 2 + settingsMargin / 2 - indentLevel * settingsMargin, settingsLineHeight); // Sliders are slightly out of alignment vertically.
        }

        Rect SRightSliderRect(float line)
        {
            return new Rect(settingsMargin + settingsWidth / 2 + settingsMargin / 2, (line + 0.2f) * settingsLineHeight, settingsWidth / 2 - 7 / 2 * settingsMargin, settingsLineHeight); // Sliders are slightly out of alignment vertically.
        }

        Rect SLeftButtonRect(float line)
        {
            return new Rect(settingsMargin, line * settingsLineHeight, (settingsWidth - 2 * settingsMargin) / 2 - settingsMargin / 4, settingsLineHeight);
        }

        Rect SRightButtonRect(float line)
        {
            return new Rect(settingsWidth / 2 + settingsMargin / 4, line * settingsLineHeight, (settingsWidth - 2 * settingsMargin) / 2 - settingsMargin / 4, settingsLineHeight);
        }

        Rect SLineThirdRect(float line, int pos, int span = 1)
        {
            return new Rect(settingsMargin + pos * (settingsWidth - 2f * settingsMargin) / 3f, line * settingsLineHeight, span * (settingsWidth - 2f * settingsMargin) / 3f, settingsLineHeight);
        }

        Rect SQuarterRect(float line, int pos, int span = 1)
        {
            return new Rect(settingsMargin + (pos % 4) * (settingsWidth - 2f * settingsMargin) / 4f, (line + (int)(pos / 4)) * settingsLineHeight, span * (settingsWidth - 2f * settingsMargin) / 4f, settingsLineHeight);
        }

        Rect SEighthRect(float line, int pos)
        {
            return new Rect(settingsMargin + (pos % 8) * (settingsWidth - 2f * settingsMargin) / 8f, (line + (int)(pos / 8)) * settingsLineHeight, (settingsWidth - 2.5f * settingsMargin) / 8f, settingsLineHeight);
        }

        List<Rect> SRight2Rects(float line)
        {
            var rectGap = settingsMargin / 2;
            var rectWidth = ((settingsWidth - 2 * settingsMargin) / 2 - 2 * rectGap) / 2;
            var rects = new List<Rect>();
            rects.Add(new Rect(settingsWidth / 2 + rectGap / 2, line * settingsLineHeight, rectWidth, settingsLineHeight));
            rects.Add(new Rect(settingsWidth / 2 + rectWidth + rectGap * 3 / 2, line * settingsLineHeight, rectWidth, settingsLineHeight));
            return rects;
        }

        List<Rect> SRight3Rects(float line)
        {
            var rectGap = settingsMargin / 3;
            var rectWidth = ((settingsWidth - 2 * settingsMargin) / 2 - 3 * rectGap) / 3;
            var rects = new List<Rect>();
            rects.Add(new Rect(settingsWidth / 2 + rectGap / 2, line * settingsLineHeight, rectWidth, settingsLineHeight));
            rects.Add(new Rect(settingsWidth / 2 + rectWidth + rectGap * 3 / 2, line * settingsLineHeight, rectWidth, settingsLineHeight));
            rects.Add(new Rect(settingsWidth / 2 + 2 * rectWidth + rectGap * 5 / 2, line * settingsLineHeight, rectWidth, settingsLineHeight));
            return rects;
        }

        float settingsWidth;
        float settingsHeight;
        float settingsLeft;
        float settingsTop;
        float settingsLineHeight;
        float settingsMargin;

        private Vector2 scrollViewVector;
        private bool selectMutators = false;
        public List<string> selectedMutators;
        float mutatorHeight = 25;
        bool editKeys;
#if DEBUG
        // int debug_numRaycasts = 4;
#endif

        void SetupSettingsSize()
        {
            settingsWidth = 420;
            settingsHeight = 480;
            settingsLeft = Screen.width / 2 - settingsWidth / 2;
            settingsTop = 100;
            settingsLineHeight = 22;
            settingsMargin = 12;
            WindowRectSettings = new Rect(settingsLeft, settingsTop, settingsWidth, settingsHeight);
        }

        void WindowSettings(int windowID)
        {
            float line = 0.25f; // Top internal margin.
            GUI.Box(new Rect(0, 0, settingsWidth, settingsHeight), StringUtils.Localize("#LOC_BDArmory_Settings_Title"));//"BDArmory Settings"
            if (GUI.Button(new Rect(settingsWidth - 18, 2, 16, 16), "X"))
            {
                windowSettingsEnabled = false;
            }
            GUI.DragWindow(new Rect(0, 0, settingsWidth, 25));
            if (editKeys)
            {
                InputSettings();
                return;
            }

            GameSettings.ADVANCED_TWEAKABLES = GUI.Toggle(GameSettings.ADVANCED_TWEAKABLES ? SLeftRect(++line) : SLineRect(++line), GameSettings.ADVANCED_TWEAKABLES, StringUtils.Localize("#autoLOC_900906") + (GameSettings.ADVANCED_TWEAKABLES ? "" : " <— Access many more AI tuning options")); // Advanced tweakables
            BDArmorySettings.ADVANCED_USER_SETTINGS = GUI.Toggle(GameSettings.ADVANCED_TWEAKABLES ? SRightRect(line) : SLineRect(++line), BDArmorySettings.ADVANCED_USER_SETTINGS, StringUtils.Localize("#LOC_BDArmory_Settings_AdvancedUserSettings"));// Advanced User Settings

            if (GUI.Button(SLineRect(++line), $"{(BDArmorySettings.GRAPHICS_UI_SETTINGS_TOGGLE ? StringUtils.Localize("#LOC_BDArmory_Generic_Hide") : StringUtils.Localize("#LOC_BDArmory_Generic_Show"))} {StringUtils.Localize("#LOC_BDArmory_Settings_GraphicsSettingsToggle")}"))//Show/hide Graphics/UI settings.
            {
                BDArmorySettings.GRAPHICS_UI_SETTINGS_TOGGLE = !BDArmorySettings.GRAPHICS_UI_SETTINGS_TOGGLE;
            }
            if (BDArmorySettings.GRAPHICS_UI_SETTINGS_TOGGLE)
            {
                line += 0.2f;
                BDArmorySettings.DRAW_AIMERS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.DRAW_AIMERS, StringUtils.Localize("#LOC_BDArmory_Settings_DrawAimers"));//"Draw Aimers"

                if (!BDArmorySettings.ADVANCED_USER_SETTINGS)
                {
                    BDArmorySettings.BULLET_HITS = GUI.Toggle(SRightRect(line), BDArmorySettings.BULLET_HITS, StringUtils.Localize("#LOC_BDArmory_Settings_BulletFX"));//"Bullet Hits"
                    BDArmorySettings.BULLET_DECALS = BDArmorySettings.BULLET_HITS;
                    BDArmorySettings.EJECT_SHELLS = BDArmorySettings.BULLET_HITS;
                    BDArmorySettings.SHELL_COLLISIONS = BDArmorySettings.BULLET_HITS;
                }
                else
                {
                    BDArmorySettings.BULLET_HITS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.BULLET_HITS, StringUtils.Localize("#LOC_BDArmory_Settings_BulletHits"));//"Bullet Hits"
                    if (BDArmorySettings.BULLET_HITS)
                    {
                        BDArmorySettings.BULLET_DECALS = GUI.Toggle(SLeftRect(++line, 1), BDArmorySettings.BULLET_DECALS, StringUtils.Localize("#LOC_BDArmory_Settings_BulletHoleDecals"));//"Bullet Hole Decals"
                        if (BDArmorySettings.BULLET_HITS)
                        {
                            GUI.Label(SLeftSliderRect(++line, 1), $"{StringUtils.Localize("#LOC_BDArmory_Settings_MaxBulletHoles")}:  ({BDArmorySettings.MAX_NUM_BULLET_DECALS})", leftLabel); // Max Bullet Holes
                            if (BDArmorySettings.MAX_NUM_BULLET_DECALS != (BDArmorySettings.MAX_NUM_BULLET_DECALS = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.MAX_NUM_BULLET_DECALS, 1f, 999f))))
                                BulletHitFX.AdjustDecalPoolSizes(BDArmorySettings.MAX_NUM_BULLET_DECALS);
                        }
                    }
                    BDArmorySettings.EJECT_SHELLS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.EJECT_SHELLS, StringUtils.Localize("#LOC_BDArmory_Settings_EjectShells"));//"Eject Shells"
                    if (BDArmorySettings.EJECT_SHELLS)
                    {
                        BDArmorySettings.SHELL_COLLISIONS = GUI.Toggle(SLeftRect(++line, 1), BDArmorySettings.SHELL_COLLISIONS, StringUtils.Localize("#LOC_BDArmory_Settings_ShellCollisions"));//"Shell Collisions"}
                    }
                }

                BDArmorySettings.SHOW_AMMO_GAUGES = GUI.Toggle(SLeftRect(++line), BDArmorySettings.SHOW_AMMO_GAUGES, StringUtils.Localize("#LOC_BDArmory_Settings_AmmoGauges"));//"Ammo Gauges"
                //BDArmorySettings.PERSISTENT_FX = GUI.Toggle(SRightRect(line), BDArmorySettings.PERSISTENT_FX, StringUtils.Localize("#LOC_BDArmory_Settings_PersistentFX"));//"Persistent FX"
                BDArmorySettings.GAPLESS_PARTICLE_EMITTERS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.GAPLESS_PARTICLE_EMITTERS, StringUtils.Localize("#LOC_BDArmory_Settings_GaplessParticleEmitters"));//"Gapless Particle Emitters"
                if (BDArmorySettings.FLARE_SMOKE != (BDArmorySettings.FLARE_SMOKE = GUI.Toggle(SRightRect(line), BDArmorySettings.FLARE_SMOKE, StringUtils.Localize("#LOC_BDArmory_Settings_FlareSmoke"))))//"Flare Smoke"
                {
                    foreach (var flareObj in CMDropper.flarePool.pool)
                        if (flareObj.activeInHierarchy)
                        {
                            var flare = flareObj.GetComponent<CMFlare>();
                            if (flare == null) continue;
                            flare.EnableEmitters();
                        }
                }
                BDArmorySettings.STRICT_WINDOW_BOUNDARIES = GUI.Toggle(SLeftRect(++line), BDArmorySettings.STRICT_WINDOW_BOUNDARIES, StringUtils.Localize("#LOC_BDArmory_Settings_StrictWindowBoundaries"));//"Strict Window Boundaries"
                if (BDArmorySettings.AI_TOOLBAR_BUTTON != (BDArmorySettings.AI_TOOLBAR_BUTTON = GUI.Toggle(SRightRect(line), BDArmorySettings.AI_TOOLBAR_BUTTON, StringUtils.Localize("#LOC_BDArmory_Settings_AIToolbarButton")))) // AI Toobar Button
                {
                    if (BDArmorySettings.AI_TOOLBAR_BUTTON)
                    { BDArmoryAIGUI.Instance.AddToolbarButton(); }
                    else
                    { BDArmoryAIGUI.Instance.RemoveToolbarButton(); }
                }
                if (BDArmorySettings.SCROLL_ZOOM_PREVENTION != (BDArmorySettings.SCROLL_ZOOM_PREVENTION = GUI.Toggle(SLeftRect(++line), BDArmorySettings.SCROLL_ZOOM_PREVENTION, StringUtils.Localize("#LOC_BDArmory_Settings_ScrollZoomPrevention"))))
                { GUIUtils.EndDisableScrollZoom(); }
                if (BDArmorySettings.VM_TOOLBAR_BUTTON != (BDArmorySettings.VM_TOOLBAR_BUTTON = GUI.Toggle(SRightRect(line), BDArmorySettings.VM_TOOLBAR_BUTTON, StringUtils.Localize("#LOC_BDArmory_Settings_VMToolbarButton")))) // VM Toobar Button
                {
                    if (BDArmorySettings.VM_TOOLBAR_BUTTON)
                    { VesselMover.Instance.AddToolbarButton(); }
                    else
                    { VesselMover.Instance.RemoveToolbarButton(); }
                }
                BDArmorySettings.DISPLAY_COMPETITION_STATUS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.DISPLAY_COMPETITION_STATUS, StringUtils.Localize("#LOC_BDArmory_Settings_DisplayCompetitionStatus"));
                BDArmorySettings.AUTO_DISABLE_UI = GUI.Toggle(SRightRect(line), BDArmorySettings.AUTO_DISABLE_UI, StringUtils.Localize("#LOC_BDArmory_Settings_AutoDisableUI")); // Auto-disable UI
                if (BDArmorySettings.DISPLAY_COMPETITION_STATUS)
                {
                    BDArmorySettings.DISPLAY_COMPETITION_STATUS_WITH_HIDDEN_UI = GUI.Toggle(SLeftRect(++line, 1), BDArmorySettings.DISPLAY_COMPETITION_STATUS_WITH_HIDDEN_UI, StringUtils.Localize("#LOC_BDArmory_Settings_DisplayCompetitionStatusHiddenUI"));
                }
                BDArmorySettings.CAMERA_SWITCH_INCLUDE_MISSILES = GUI.Toggle(SLeftRect(++line), BDArmorySettings.CAMERA_SWITCH_INCLUDE_MISSILES, StringUtils.Localize("#LOC_BDArmory_Settings_CameraSwitchIncludeMissiles"));
                if (HighLogic.LoadedSceneIsEditor && BDArmorySettings.ADVANCED_USER_SETTINGS)
                {
                    if (BDArmorySettings.SHOW_CATEGORIES != (BDArmorySettings.SHOW_CATEGORIES = GUI.Toggle(SLeftRect(++line), BDArmorySettings.SHOW_CATEGORIES, StringUtils.Localize("#LOC_BDArmory_Settings_ShowEditorSubcategories"))))//"Show Editor Subcategories"
                    {
                        KSP.UI.Screens.PartCategorizer.Instance.editorPartList.Refresh();
                    }
                    if (BDArmorySettings.AUTOCATEGORIZE_PARTS != (BDArmorySettings.AUTOCATEGORIZE_PARTS = GUI.Toggle(SRightRect(line), BDArmorySettings.AUTOCATEGORIZE_PARTS, StringUtils.Localize("#LOC_BDArmory_Settings_AutocategorizeParts"))))//"Autocategorize Parts"
                    {
                        KSP.UI.Screens.PartCategorizer.Instance.editorPartList.Refresh();
                    }
                }

                if (BDArmorySettings.ADVANCED_USER_SETTINGS)
                {
                    { // GUI background opacity
                        GUI.Label(SLeftSliderRect(++line), StringUtils.Localize("#LOC_BDArmory_Settings_GUIBackgroundOpacity") + $" ({BDArmorySettings.GUI_OPACITY.ToString("F2")})", leftLabel);
                        BDArmorySettings.GUI_OPACITY = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.GUI_OPACITY, 0f, 1f), 0.05f);
                    }

                    if (GUI.Button(SLineRect(++line, 1, true), (BDArmorySettings.DEBUG_SETTINGS_TOGGLE ? "Disable " : "Enable ") + StringUtils.Localize("#LOC_BDArmory_Settings_DebugSettingsToggle")))//Enable/Disable Debugging.
                    {
                        BDArmorySettings.DEBUG_SETTINGS_TOGGLE = !BDArmorySettings.DEBUG_SETTINGS_TOGGLE;
                        if (!BDArmorySettings.DEBUG_SETTINGS_TOGGLE) // Disable all debugging when closing the debugging section.
                        {
                            BDArmorySettings.DEBUG_AI = false;
                            BDArmorySettings.DEBUG_ARMOR = false;
                            BDArmorySettings.DEBUG_COMPETITION = false;
                            BDArmorySettings.DEBUG_DAMAGE = false;
                            BDArmorySettings.DEBUG_LINES = false;
                            BDArmorySettings.DEBUG_MISSILES = false;
                            BDArmorySettings.DEBUG_OTHER = false;
                            BDArmorySettings.DEBUG_RADAR = false;
                            BDArmorySettings.DEBUG_SPAWNING = false;
                            BDArmorySettings.DEBUG_TELEMETRY = false;
                            BDArmorySettings.DEBUG_WEAPONS = false;
                        }
                    }
                    if (BDArmorySettings.DEBUG_SETTINGS_TOGGLE)
                    {
                        BDArmorySettings.DEBUG_TELEMETRY = GUI.Toggle(SQuarterRect(++line, 0, 2), BDArmorySettings.DEBUG_TELEMETRY, StringUtils.Localize("#LOC_BDArmory_Settings_DebugTelemetry"));//"On-Screen Telemetry"
                        BDArmorySettings.DEBUG_LINES = GUI.Toggle(SQuarterRect(line, 2), BDArmorySettings.DEBUG_LINES, StringUtils.Localize("#LOC_BDArmory_Settings_DebugLines"));//"Debug Lines"
                        BDArmorySettings.DEBUG_WEAPONS = GUI.Toggle(SQuarterRect(++line, 0), BDArmorySettings.DEBUG_WEAPONS, StringUtils.Localize("#LOC_BDArmory_Settings_DebugWeapons"));//"Debug Weapons"
                        BDArmorySettings.DEBUG_MISSILES = GUI.Toggle(SQuarterRect(line, 1), BDArmorySettings.DEBUG_MISSILES, StringUtils.Localize("#LOC_BDArmory_Settings_DebugMissiles"));//"Debug Missiles"
                        BDArmorySettings.DEBUG_ARMOR = GUI.Toggle(SQuarterRect(line, 2), BDArmorySettings.DEBUG_ARMOR, StringUtils.Localize("#LOC_BDArmory_Settings_DebugArmor"));//"Debug Armor"
                        BDArmorySettings.DEBUG_DAMAGE = GUI.Toggle(SQuarterRect(line, 3), BDArmorySettings.DEBUG_DAMAGE, StringUtils.Localize("#LOC_BDArmory_Settings_DebugDamage"));//"Debug Damage"
                        BDArmorySettings.DEBUG_AI = GUI.Toggle(SQuarterRect(++line, 0), BDArmorySettings.DEBUG_AI, StringUtils.Localize("#LOC_BDArmory_Settings_DebugAI"));//"Debug AI"
                        BDArmorySettings.DEBUG_COMPETITION = GUI.Toggle(SQuarterRect(line, 1), BDArmorySettings.DEBUG_COMPETITION, StringUtils.Localize("#LOC_BDArmory_Settings_DebugCompetition"));//"Debug Competition"
                        BDArmorySettings.DEBUG_RADAR = GUI.Toggle(SQuarterRect(line, 2), BDArmorySettings.DEBUG_RADAR, StringUtils.Localize("#LOC_BDArmory_Settings_DebugRadar"));//"Debug Detectors"
                        BDArmorySettings.DEBUG_SPAWNING = GUI.Toggle(SQuarterRect(line, 3), BDArmorySettings.DEBUG_SPAWNING, StringUtils.Localize("#LOC_BDArmory_Settings_DebugSpawning"));//"Debug Spawning"
                        BDArmorySettings.DEBUG_OTHER = GUI.Toggle(SQuarterRect(++line, 0), BDArmorySettings.DEBUG_OTHER, StringUtils.Localize("#LOC_BDArmory_Settings_DebugOther"));//"Debug Other"

                        if (BDArmorySettings.DEBUG_OTHER && GUI.Button(SLineRect(++line), StringUtils.Localize("#LOC_BDArmory_Settings_ResetScrollZoom"))) GUIUtils.ResetScrollRate(); // Reset scroll-zoom.
                        if (BDArmorySettings.DEBUG_AI && GUI.Button(SLineRect(++line), "Debug Extending")) // Debug why a vessel is stuck in extending.
                        {
                            var AI = VesselModuleRegistry.GetBDModulePilotAI(FlightGlobals.ActiveVessel);
                            if (AI is not null) AI.DebugExtending();
                        }
                        if (BDArmorySettings.DEBUG_OTHER && HighLogic.LoadedSceneIsEditor && GUI.Button(SLineRect(++line), "Dump parts"))
                        {
                            BDAEditorTools.dumpParts();
                        }
                    }
#if DEBUG  // Only visible when compiled in Debug configuration.
                    if (BDArmorySettings.DEBUG_SETTINGS_TOGGLE)
                    {
                        // GUI.Label(SLeftSliderRect(++line), $"Outer loops N ({PROF_N}):");
                        // if (PROF_N_pow != (PROF_N_pow = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), PROF_N_pow, 0, 8))))
                        // {
                        //     PROF_N = Mathf.RoundToInt(Mathf.Pow(10, PROF_N_pow));
                        // }
                        // GUI.Label(SLeftSliderRect(++line), $"Inner loops n ({PROF_n}):");
                        // if (PROF_n_pow != (PROF_n_pow = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), PROF_n_pow, 0, 6))))
                        // {
                        //     PROF_n = Mathf.RoundToInt(Mathf.Pow(10, PROF_n_pow));
                        // }

                        // if (GUI.Button(SLineRect(++line), "Test ProjectOnPlane and PredictPosition"))
                        //     TestProjectOnPlaneAndPredictPosition();
                        // if (GUI.Button(SLineRect(++line), "Say hello KAL"))
                        // {
                        //     foreach (var kal in FlightGlobals.ActiveVessel.FindPartModulesImplementing<Expansions.Serenity.ModuleRoboticController>())
                        //     {
                        //         if (kal == null) continue;
                        //         Debug.Log($"DEBUG KAL {kal.displayName} found on part {kal.part} ({kal.part.persistentId}), enabled: {kal.controllerEnabled}");
                        //         Utils.ConfigNodeUtils.PrintConfigNode(kal.snapshot.moduleValues);
                        //     }
                        //     foreach (var part in FlightGlobals.ActiveVessel.Parts)
                        //     {
                        //         if (part == null) continue;
                        //         Debug.Log($"DEBUG  Part {part.name} ({part.persistentId})");
                        //         foreach (var module in part.Modules)
                        //         {
                        //             if (module.PersistentId != 0) Debug.Log($"DEBUG     Module {module.name} ({module.PersistentId}, {module.moduleName})");
                        //             if (module.moduleName == "ModuleRoboticController") foreach (var axis in ((Expansions.Serenity.ModuleRoboticController)module).ControlledAxes) Debug.Log($"DEBUG      KAL controls part {axis.PartPersistentId}, module {axis.Module.moduleName} ({axis.Module.PersistentId}), axisField {axis.AxisField.guiName} ({axis.AxisField.name})");
                        //         }
                        //         var kal = part.FindModuleImplementing<Expansions.Serenity.ModuleRoboticController>();
                        //         if (kal != null)
                        //         {
                        //             Debug.Log($"DEBUG KAL found on {part.name} ({part.persistentId}))");
                        //             foreach (var axis in kal.ControlledAxes) Debug.Log($"DEBUG      KAL controls part {axis.PartPersistentId}, module {axis.Module.moduleName} ({axis.Module.PersistentId})");
                        //         }
                        //     }
                        // }
                        // GUI.Label(SLeftSliderRect(++line), $"#raycasts {debug_numRaycasts}");
                        // debug_numRaycasts = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), debug_numRaycasts, 1, 20));
                        // if (GUI.Button(SLineRect(++line), "Test RaycastCommand")) // The break-even appears to be around 8 raycasts.
                        // {
                        //     int N = 100000;
                        //     Unity.Collections.NativeArray<RaycastCommand> proximityRaycastCommands = new Unity.Collections.NativeArray<RaycastCommand>(debug_numRaycasts, Unity.Collections.Allocator.TempJob);
                        //     Unity.Collections.NativeArray<RaycastHit> proximityRaycastHits = new Unity.Collections.NativeArray<RaycastHit>(debug_numRaycasts, Unity.Collections.Allocator.TempJob); // Note: RaycastCommands only return the first hit until Unity 2022.2.
                        //     var vesselPosition = FlightGlobals.ActiveVessel.transform.position;
                        //     var vesselSrfVelDir = FlightGlobals.ActiveVessel.srf_vel_direction;
                        //     var relativeVelocityRightDirection = Vector3.Cross((vesselPosition - FlightGlobals.currentMainBody.transform.position).normalized, vesselSrfVelDir).normalized;
                        //     var relativeVelocityDownDirection = Vector3.Cross(relativeVelocityRightDirection, vesselSrfVelDir).normalized;
                        //     var terrainAlertDetectionRadius = 3f * FlightGlobals.ActiveVessel.GetRadius();
                        //     var watch = new System.Diagnostics.Stopwatch();
                        //     float µsResolution = 1e6f / System.Diagnostics.Stopwatch.Frequency;
                        //     watch.Start();
                        //     for (int i = 0; i < N; ++i)
                        //     {
                        //         for (int j = 0; j < debug_numRaycasts; ++j)
                        //             proximityRaycastCommands[j] = new RaycastCommand(vesselPosition, vesselSrfVelDir - relativeVelocityDownDirection, terrainAlertDetectionRadius, (int)LayerMasks.Scenery);
                        //         var job = RaycastCommand.ScheduleBatch(proximityRaycastCommands, proximityRaycastHits, 1, default(Unity.Jobs.JobHandle));
                        //         job.Complete(); // Wait for the job to complete.
                        //     }
                        //     watch.Stop();
                        //     Debug.Log($"Batch RaycastCommand[{debug_numRaycasts}] took {watch.ElapsedTicks * µsResolution / N:G3}µs");
                        //     RaycastHit rayHit;
                        //     watch.Reset(); watch.Start();
                        //     for (int i = 0; i < N; ++i)
                        //         for (int j = 0; j < debug_numRaycasts; ++j)
                        //             Physics.Raycast(new Ray(vesselPosition, (vesselSrfVelDir + relativeVelocityDownDirection).normalized), out rayHit, terrainAlertDetectionRadius, (int)LayerMasks.Scenery);
                        //     watch.Stop();
                        //     Debug.Log($"{debug_numRaycasts} Raycasts took {watch.ElapsedTicks * µsResolution / N:G3}µs");
                        //     proximityRaycastCommands.Dispose();
                        //     proximityRaycastHits.Dispose();
                        // }
                        // if (GUI.Button(SLineRect(++line), "Dump staging")) { var vessel = FlightGlobals.ActiveVessel; if (vessel != null) Debug.Log($"DEBUG {vessel.vesselName} is at stage {vessel.currentStage}, part stages: {string.Join("; ", vessel.parts.Select(p => $"{p}: index: {p.inStageIndex}, offset: {p.stageOffset}, orig: {p.originalStage}, child: {p.childStageOffset}, inv: {p.inverseStage}, default inv: {p.defaultInverseStage}, inv carryover: {p.inverseStageCarryover}, manual: {p.manualStageOffset}, after: {p.stageAfter}, before: {p.stageBefore}"))}"); }
                        // if (GUI.Button(SLineRect(++line), "Vessel Mass"))
                        // {
                        //     BDACompetitionMode.Instance.competitionStatus.Add($"{FlightGlobals.ActiveVessel.vesselName} has mass {FlightGlobals.ActiveVessel.GetTotalMass()}t");
                        // }
                        // if (GUI.Button(SLineRect(++line), "Test Collider.ClosestPoint[OnBounds]"))
                        // {
                        //     var watch = new System.Diagnostics.Stopwatch();
                        //     float µsResolution = 1e6f / System.Diagnostics.Stopwatch.Frequency;
                        //     int N = 1 << 16;
                        //     Ray ray = FlightCamera.fetch.mainCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                        //     int layerMask = (int)(LayerMasks.Parts | LayerMasks.EVA | LayerMasks.Wheels | LayerMasks.Scenery);
                        //     float dist = 10000;
                        //     RaycastHit hit;
                        //     Vector3 closestPoint = default;
                        //     watch.Start();
                        //     if (Physics.Raycast(ray, out hit, dist, layerMask))
                        //     {
                        //         watch.Stop();
                        //         var raycastTicks = watch.ElapsedTicks;
                        //         string raycastString = $"Raycast took {raycastTicks * µsResolution:G3}µs";
                        //         Part partHit = hit.collider.GetComponentInParent<Part>();
                        //         MeshCollider mcol = null;
                        //         bool isMeshCollider = false;
                        //         bool isNonConvexMeshCollider = false;
                        //         watch.Reset(); watch.Start();
                        //         for (int i = 0; i < N; ++i)
                        //         {
                        //             mcol = hit.collider as MeshCollider;
                        //             isMeshCollider = mcol != null;
                        //             if (isMeshCollider && !mcol.convex)  // non-convex mesh colliders are expensive to use ClosestPoint on.
                        //             {
                        //                 isNonConvexMeshCollider = true;
                        //                 closestPoint = hit.collider.ClosestPointOnBounds(ray.origin);
                        //             }
                        //             else
                        //                 closestPoint = hit.collider.ClosestPoint(ray.origin);
                        //         }
                        //         watch.Stop();
                        //         Debug.Log($"DEBUG {raycastString}, {(isNonConvexMeshCollider ? "ClosestPointOnBounds" : "ClosestPoint")} ({closestPoint}) on{(isMeshCollider ? $" {(isNonConvexMeshCollider ? "non-" : "")}convex mesh" : "")} collider {hit.collider} from camera ({ray.origin}) took {watch.ElapsedTicks * µsResolution / N:G3}µs{(partHit != null ? $", offset from part ({partHit.name}): {closestPoint - partHit.transform.position}" : "")}, offset from hit: {hit.point - closestPoint}");
                        //     }
                        // }
                        // if (GUI.Button(SLineRect(++line), "Test 2x Raycast vs RaycastNonAlloc"))
                        // {
                        //     var watch = new System.Diagnostics.Stopwatch();
                        //     float µsResolution = 1e6f / System.Diagnostics.Stopwatch.Frequency;
                        //     int N = 1 << 20;
                        //     Ray ray = FlightCamera.fetch.mainCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                        //     RaycastHit hit;
                        //     RaycastHit[] hits = new RaycastHit[100];
                        //     int layerMask = (int)(LayerMasks.Parts | LayerMasks.EVA | LayerMasks.Wheels | LayerMasks.Scenery);
                        //     float dist = 10000;
                        //     bool didHit = false;
                        //     watch.Start();
                        //     for (int i = 0; i < N; ++i)
                        //     {
                        //         didHit = Physics.Raycast(ray, out hit, dist, layerMask);
                        //         didHit = Physics.Raycast(ray, out hit, dist, layerMask);
                        //     }
                        //     watch.Stop();
                        //     Debug.Log($"DEBUG Raycast 2x (hit? {didHit}) took {watch.ElapsedTicks * µsResolution / N:G3}µs");
                        //     int hitCount = 0;
                        //     watch.Reset(); watch.Start();
                        //     for (int i = 0; i < N; ++i)
                        //         hitCount = Physics.RaycastNonAlloc(ray, hits, dist, layerMask);
                        //     watch.Stop();
                        //     Debug.Log($"DEBUG RaycastNonAlloc ({hitCount} hits) took {watch.ElapsedTicks * µsResolution / N:G3}µs");
                        // }
                        // if (GUI.Button(SLineRect(++line), "Test GetFrameVelocityV3f"))
                        // {
                        //     var watch = new System.Diagnostics.Stopwatch();
                        //     float resolution = 1e9f / System.Diagnostics.Stopwatch.Frequency;
                        //     int N = 1000;
                        //     Vector3 frameVelocity;
                        //     watch.Start();
                        //     for (int i = 0; i < N; ++i)
                        //         frameVelocity = Krakensbane.GetFrameVelocityV3f();
                        //     watch.Stop();
                        //     Debug.Log($"DEBUG Getting KbVF took {watch.ElapsedTicks * resolution / N:G3}ns");
                        //     watch.Reset(); watch.Start();
                        //     for (int i = 0; i < N; ++i)
                        //         frameVelocity = BDKrakensbane.FrameVelocityV3f;
                        //     watch.Stop();
                        //     Debug.Log($"DEBUG Using BDKrakensbane took {watch.ElapsedTicks * resolution / N:G3}ns");
                        //     Vector3d FOOffset;
                        //     watch.Reset(); watch.Start();
                        //     for (int i = 0; i < N; ++i)
                        //         FOOffset = FloatingOrigin.Offset;
                        //     watch.Stop();
                        //     Debug.Log($"DEBUG Getting FO.Offset took {watch.ElapsedTicks * resolution / N:G3}ns");
                        //     watch.Reset(); watch.Start();
                        //     for (int i = 0; i < N; ++i)
                        //         FOOffset = BDKrakensbane.FloatingOriginOffset;
                        //     watch.Stop();
                        //     Debug.Log($"DEBUG Using BDKrakensbane took {watch.ElapsedTicks * resolution / N:G3}ns");
                        //     Vector3d FOOffsetNKb;
                        //     watch.Reset(); watch.Start();
                        //     for (int i = 0; i < N; ++i)
                        //         FOOffsetNKb = FloatingOrigin.OffsetNonKrakensbane;
                        //     watch.Stop();
                        //     Debug.Log($"DEBUG Getting FO.OffsetNonKrakensbane took {watch.ElapsedTicks * resolution / N:G3}ns");
                        //     watch.Reset(); watch.Start();
                        //     for (int i = 0; i < N; ++i)
                        //         FOOffsetNKb = BDKrakensbane.FloatingOriginOffsetNonKrakensbane;
                        //     watch.Stop();
                        //     Debug.Log($"DEBUG Using BDKrakensbane took {watch.ElapsedTicks * resolution / N:G3}ns");
                        //     bool KBIsActive;
                        //     watch.Reset(); watch.Start();
                        //     for (int i = 0; i < N; ++i)
                        //         KBIsActive = !BDKrakensbane.FloatingOriginOffset.IsZero() || !Krakensbane.GetFrameVelocity().IsZero();
                        //     watch.Stop();
                        //     Debug.Log($"DEBUG Getting KB is active took {watch.ElapsedTicks * resolution / N:G3}ns");
                        //     watch.Reset(); watch.Start();
                        //     for (int i = 0; i < N; ++i)
                        //         KBIsActive = BDKrakensbane.IsActive;
                        //     watch.Stop();
                        //     Debug.Log($"DEBUG Using BDKrakensbane took {watch.ElapsedTicks * resolution / N:G3}ns");
                        // }
                        // if (GUI.Button(SLineRect(++line), "Test GetAudioClip"))
                        // {
                        //     StartCoroutine(TestGetAudioClip());
                        // }
                        // if (GUI.Button(SLineRect(++line), "Test vesselName vs GetName()"))
                        // {
                        //     StartCoroutine(TestVesselName());
                        // }
                        // if (GUI.Button(SLineRect(++line), "Test RaycastHit merge and sort"))
                        // {
                        //     StartCoroutine(TestRaycastHitMergeAndSort());
                        // }
                        // if (GUI.Button(SLineRect(++line), "Test Localizer.Format vs StringUtils.Localize"))
                        // {
                        //     StartCoroutine(TestLocalization());
                        // }
                        // if (GUI.Button(SLineRect(++line), "Test yield wait lengths")) // Test yield wait lengths
                        // {
                        //     StartCoroutine(TestYieldWaitLengths());
                        // }
                        // if (BDACompetitionMode.Instance != null)
                        // {
                        //     if (GUI.Button(SLineRect(++line), "Run DEBUG checks"))// Run DEBUG checks
                        //     {
                        //         switch (Event.current.button)
                        //         {
                        //             case 1: // right click
                        //                 StartCoroutine(BDACompetitionMode.Instance.CheckGCPerformance());
                        //                 break;
                        //             default:
                        //                 BDACompetitionMode.Instance.CleanUpKSPsDeadReferences();
                        //                 BDACompetitionMode.Instance.RunDebugChecks();
                        //                 break;
                        //         }
                        //     }
                        //     if (GUI.Button(SLineRect(++line), "Test Vessel Module Registry"))
                        //     {
                        //         StartCoroutine(VesselModuleRegistry.Instance.PerformanceTest());
                        //     }
                        // }
                        // if (GUI.Button(SLineRect(++line), "timing test")) // Timing tests.
                        // {
                        //     var test = FlightGlobals.ActiveVessel.transform.position;
                        //     float FiringTolerance = 1f;
                        //     float targetRadius = 20f;
                        //     Vector3 finalAimTarget = new Vector3(10f, 20f, 30f);
                        //     Vector3 pos = new Vector3(2f, 3f, 4f);
                        //     float theta_const = Mathf.Deg2Rad * 1f;
                        //     float test_out = 0f;
                        //     int iters = 10000000;
                        //     var now = Time.realtimeSinceStartup;
                        //     for (int i = 0; i < iters; ++i)
                        //     {
                        //         test_out = i > iters ? 1f : 1f - 0.5f * FiringTolerance * FiringTolerance * targetRadius * targetRadius / (finalAimTarget - pos).sqrMagnitude;
                        //     }
                        //     Debug.Log("DEBUG sqrMagnitude " + (Time.realtimeSinceStartup - now) / iters + "s/iter, out: " + test_out);
                        //     now = Time.realtimeSinceStartup;
                        //     for (int i = 0; i < iters; ++i)
                        //     {
                        //         var theta = FiringTolerance * targetRadius / (finalAimTarget - pos).magnitude + theta_const;
                        //         test_out = i > iters ? 1f : 1f - 0.5f * (theta * theta);
                        //     }
                        //     Debug.Log("DEBUG magnitude " + (Time.realtimeSinceStartup - now) / iters + "s/iter, out: " + test_out);
                        // }
                        // if (GUI.Button(SLineRect(++line), "Hash vs SubStr test"))
                        // {
                        //     var armourParts = PartLoader.LoadedPartsList.Select(p => p.partPrefab.partInfo.name).Where(name => name.ToLower().Contains("armor")).ToHashSet();
                        //     Debug.Log($"DEBUG Armour parts in game: " + string.Join(", ", armourParts));
                        //     int N = 1 << 24;
                        //     var tic = Time.realtimeSinceStartup;
                        //     for (int i = 0; i < N; ++i)
                        //         armourParts.Contains("BD.PanelArmor");
                        //     var dt = Time.realtimeSinceStartup - tic;
                        //     Debug.Log($"DEBUG HashSet lookup took {dt / N:G3}s");
                        //     var armourPart = "BD.PanelArmor";
                        //     tic = Time.realtimeSinceStartup;
                        //     for (int i = 0; i < N; ++i)
                        //         armourPart.ToLower().Contains("armor");
                        //     dt = Time.realtimeSinceStartup - tic;
                        //     Debug.Log($"DEBUG SubStr lookup took {dt / N:G3}s");

                        //     // Using an actual part to include the part name access.
                        //     var testPart = PartLoader.LoadedPartsList.Select(p => p.partPrefab).First();
                        //     ProjectileUtils.IsArmorPart(testPart); // Bootstrap the HashSet
                        //     tic = Time.realtimeSinceStartup;
                        //     for (int i = 0; i < N; ++i)
                        //         ProjectileUtils.IsArmorPart(testPart);
                        //     dt = Time.realtimeSinceStartup - tic;
                        //     Debug.Log($"DEBUG Real part HashSet lookup first part took {dt / N:G3}s");
                        //     testPart = PartLoader.LoadedPartsList.Select(p => p.partPrefab).Last();
                        //     tic = Time.realtimeSinceStartup;
                        //     for (int i = 0; i < N; ++i)
                        //         ProjectileUtils.IsArmorPart(testPart);
                        //     dt = Time.realtimeSinceStartup - tic;
                        //     Debug.Log($"DEBUG Real part HashSet lookup last part took {dt / N:G3}s");
                        //     tic = Time.realtimeSinceStartup;
                        //     for (int i = 0; i < N; ++i)
                        //         testPart.partInfo.name.ToLower().Contains("armor");
                        //     dt = Time.realtimeSinceStartup - tic;
                        //     Debug.Log($"DEBUG Real part SubStr lookup took {dt / N:G3}s");

                        // }
                        // if (GUI.Button(SLineRect(++line), "Layer test"))
                        // {
                        //     for (int i = 0; i < 32; ++i)
                        //     {
                        //         // Vector3 mouseAim = new Vector3(Input.mousePosition.x / Screen.width, Input.mousePosition.y / Screen.height, 0);
                        //         Ray ray = FlightCamera.fetch.mainCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                        //         RaycastHit hit;

                        //         if (Physics.Raycast(ray, out hit, 1000f, (1 << i)))
                        //         {
                        //             var hitPart = hit.collider.gameObject.GetComponentInParent<Part>();
                        //             var hitEVA = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                        //             var hitBuilding = hit.collider.gameObject.GetComponentUpwards<DestructibleBuilding>();
                        //             if (hitEVA != null) hitPart = hitEVA.part;
                        //             if (hitPart != null) Debug.Log($"DEBUG Bitmask at {i} hit {hitPart.name}.");
                        //             else if (hitBuilding != null) Debug.Log($"DEBUG Bitmask at {i} hit {hitBuilding.name}");
                        //             else Debug.Log($"DEBUG Bitmask at {i} hit {hit.collider.gameObject.name}");
                        //         }
                        //     }
                        // }
                        // if (GUI.Button(SLineRect(++line), "Test vessel position timing."))
                        // { StartCoroutine(TestVesselPositionTiming()); }
                        // if (GUI.Button(SLineRect(++line), "FS engine status"))
                        // {
                        //     foreach (var vessel in FlightGlobals.VesselsLoaded)
                        //         FireSpitter.CheckStatus(vessel);
                        // }
                        // if (GUI.Button(SLineRect(++line), "Quit KSP."))
                        // {
                        //     TournamentAutoResume.AutoQuit(0);
                        // }
                    }
#endif
                }

                line += 0.5f;
            }

            if (GUI.Button(SLineRect(++line), $"{(BDArmorySettings.GAMEPLAY_SETTINGS_TOGGLE ? StringUtils.Localize("#LOC_BDArmory_Generic_Hide") : StringUtils.Localize("#LOC_BDArmory_Generic_Show"))} {StringUtils.Localize("#LOC_BDArmory_Settings_GeneralSettingsToggle")}"))//Show/hide Gameplay settings.
            {
                BDArmorySettings.GAMEPLAY_SETTINGS_TOGGLE = !BDArmorySettings.GAMEPLAY_SETTINGS_TOGGLE;
            }
            if (BDArmorySettings.GAMEPLAY_SETTINGS_TOGGLE)
            {
                line += 0.2f;

                BDArmorySettings.AUTO_ENABLE_VESSEL_SWITCHING = GUI.Toggle(SLeftRect(++line), BDArmorySettings.AUTO_ENABLE_VESSEL_SWITCHING, StringUtils.Localize("#LOC_BDArmory_Settings_AutoEnableVesselSwitching"));
                { // Kerbal Safety
                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_KerbalSafety")}:  ({(KerbalSafetyLevel)BDArmorySettings.KERBAL_SAFETY})", leftLabel); // Kerbal Safety
                    if (BDArmorySettings.KERBAL_SAFETY != (BDArmorySettings.KERBAL_SAFETY = BDArmorySettings.KERBAL_SAFETY = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.KERBAL_SAFETY, (float)KerbalSafetyLevel.Off, (float)KerbalSafetyLevel.Full))))
                    {
                        if (BDArmorySettings.KERBAL_SAFETY != (int)KerbalSafetyLevel.Off)
                            KerbalSafetyManager.Instance.EnableKerbalSafety();
                        else
                            KerbalSafetyManager.Instance.DisableKerbalSafety();
                    }
                    if (BDArmorySettings.KERBAL_SAFETY != (int)KerbalSafetyLevel.Off)
                    {
                        string inventory;
                        switch (BDArmorySettings.KERBAL_SAFETY_INVENTORY)
                        {
                            case 1:
                                inventory = StringUtils.Localize("#LOC_BDArmory_Settings_KerbalSafetyInventory_ResetDefault");
                                break;
                            case 2:
                                inventory = StringUtils.Localize("#LOC_BDArmory_Settings_KerbalSafetyInventory_ChuteOnly");
                                break;
                            default:
                                inventory = StringUtils.Localize("#LOC_BDArmory_Settings_KerbalSafetyInventory_NoChange");
                                break;
                        }
                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_KerbalSafetyInventory")}:  ({inventory})", leftLabel); // Kerbal Safety inventory
                        if (BDArmorySettings.KERBAL_SAFETY_INVENTORY != (BDArmorySettings.KERBAL_SAFETY_INVENTORY = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.KERBAL_SAFETY_INVENTORY, 0f, 2f))))
                        { if (KerbalSafetyManager.Instance is not null) KerbalSafetyManager.Instance.ReconfigureInventories(); }
                    }
                }
                if (BDArmorySettings.HACK_INTAKES != (BDArmorySettings.HACK_INTAKES = GUI.Toggle(SLeftRect(++line), BDArmorySettings.HACK_INTAKES, StringUtils.Localize("#LOC_BDArmory_Settings_IntakeHack"))))// Hack Intakes
                {
                    if (HighLogic.LoadedSceneIsFlight)
                    {
                        SpawnUtils.HackIntakesOnNewVessels(BDArmorySettings.HACK_INTAKES);
                        if (BDArmorySettings.HACK_INTAKES) // Add the hack to all in-game intakes.
                        {
                            foreach (var vessel in FlightGlobals.Vessels)
                            {
                                if (vessel == null || !vessel.loaded) continue;
                                SpawnUtils.HackIntakes(vessel, true);
                            }
                        }
                        else // Reset all the in-game intakes back to their part-defined settings.
                        {
                            foreach (var vessel in FlightGlobals.Vessels)
                            {
                                if (vessel == null || !vessel.loaded) continue;
                                SpawnUtils.HackIntakes(vessel, false);
                            }
                        }
                    }
                }

                if (BDArmorySettings.ADVANCED_USER_SETTINGS)
                {
                    if (BDArmorySettings.PWING_EDGE_LIFT != (BDArmorySettings.PWING_EDGE_LIFT = GUI.Toggle(SRightRect(line), BDArmorySettings.PWING_EDGE_LIFT, StringUtils.Localize("#LOC_BDArmory_Settings_PWingsHack")))) //Toggle Pwing Edge Lift
                    {
                        if (HighLogic.LoadedSceneIsEditor && EditorLogic.fetch.ship is not null) GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
                    }
                    BDArmorySettings.DEFAULT_FFA_TARGETING = GUI.Toggle(SLeftRect(++line), BDArmorySettings.DEFAULT_FFA_TARGETING, StringUtils.Localize("#LOC_BDArmory_Settings_DefaultFFATargeting"));// Free-for-all combat style
                    if (BDArmorySettings.PWING_THICKNESS_AFFECT_MASS_HP != (BDArmorySettings.PWING_THICKNESS_AFFECT_MASS_HP = GUI.Toggle(SRightRect(line), BDArmorySettings.PWING_THICKNESS_AFFECT_MASS_HP, StringUtils.Localize("#LOC_BDArmory_Settings_PWingsThickHP")))) //Toggle Pwing Thickness based Mass/HP
                    {
                        if (HighLogic.LoadedSceneIsEditor && EditorLogic.fetch.ship is not null) GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
                    }
                    BDArmorySettings.AUTONOMOUS_COMBAT_SEATS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.AUTONOMOUS_COMBAT_SEATS, StringUtils.Localize("#LOC_BDArmory_Settings_AutonomousCombatSeats"));
                    BDArmorySettings.DESTROY_UNCONTROLLED_WMS = GUI.Toggle(SRightRect(line), BDArmorySettings.DESTROY_UNCONTROLLED_WMS, StringUtils.Localize("#LOC_BDArmory_Settings_DestroyWMWhenNotControlled"));
                    BDArmorySettings.AIM_ASSIST = GUI.Toggle(SLeftRect(++line), BDArmorySettings.AIM_ASSIST, StringUtils.Localize("#LOC_BDArmory_Settings_AimAssist"));//"Aim Assist"
                    BDArmorySettings.AIM_ASSIST_MODE = GUI.Toggle(SRightRect(line), BDArmorySettings.AIM_ASSIST_MODE, BDArmorySettings.AIM_ASSIST_MODE ? StringUtils.Localize("#LOC_BDArmory_Settings_AimAssistMode_Target") : StringUtils.Localize("#LOC_BDArmory_Settings_AimAssistMode_Aimer"));//"Aim Assist Mode (Target/Aimer)"
                    BDArmorySettings.REMOTE_SHOOTING = GUI.Toggle(SLeftRect(++line), BDArmorySettings.REMOTE_SHOOTING, StringUtils.Localize("#LOC_BDArmory_Settings_RemoteFiring"));//"Remote Firing"
                    BDArmorySettings.BOMB_CLEARANCE_CHECK = GUI.Toggle(SRightRect(line), BDArmorySettings.BOMB_CLEARANCE_CHECK, StringUtils.Localize("#LOC_BDArmory_Settings_ClearanceCheck"));//"Clearance Check"
                    BDArmorySettings.DISABLE_RAMMING = GUI.Toggle(SLeftRect(++line), BDArmorySettings.DISABLE_RAMMING, StringUtils.Localize("#LOC_BDArmory_Settings_DisableRamming"));// Disable Ramming
                    BDArmorySettings.RESET_HP = GUI.Toggle(SRightRect(line), BDArmorySettings.RESET_HP, StringUtils.Localize("#LOC_BDArmory_Settings_ResetHP"));
                    BDArmorySettings.BULLET_WATER_DRAG = GUI.Toggle(SLeftRect(++line), BDArmorySettings.BULLET_WATER_DRAG, StringUtils.Localize("#LOC_BDArmory_Settings_waterDrag"));// Underwater bullet drag
                    BDArmorySettings.RESET_ARMOUR = GUI.Toggle(SRightRect(line), BDArmorySettings.RESET_ARMOUR, StringUtils.Localize("#LOC_BDArmory_Settings_ResetArmor"));
                    BDArmorySettings.VESSEL_RELATIVE_BULLET_CHECKS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.VESSEL_RELATIVE_BULLET_CHECKS, StringUtils.Localize("#LOC_BDArmory_Settings_VesselRelativeBulletChecks"));//"Vessel-Relative Bullet Checks"
                    BDArmorySettings.RESET_HULL = GUI.Toggle(SRightRect(line), BDArmorySettings.RESET_HULL, StringUtils.Localize("#LOC_BDArmory_Settings_ResetHull")); //Reset Hull
                    if (BDArmorySettings.RESTORE_KAL != (BDArmorySettings.RESTORE_KAL = GUI.Toggle(SLeftRect(++line), BDArmorySettings.RESTORE_KAL, StringUtils.Localize("#LOC_BDArmory_Settings_RestoreKAL")))) //Restore KAL
                    { SpawnUtils.RestoreKALGlobally(BDArmorySettings.RESTORE_KAL); }
                    BDArmorySettings.AUTO_LOAD_TO_KSC = GUI.Toggle(SLeftRect(++line), BDArmorySettings.AUTO_LOAD_TO_KSC, StringUtils.Localize("#LOC_BDArmory_Settings_AutoLoadToKSC")); // Auto-Load To KSC
                    BDArmorySettings.GENERATE_CLEAN_SAVE = GUI.Toggle(SRightRect(line), BDArmorySettings.GENERATE_CLEAN_SAVE, StringUtils.Localize("#LOC_BDArmory_Settings_GenerateCleanSave")); // Generate Clean Save
                    BDArmorySettings.AUTO_RESUME_TOURNAMENT = GUI.Toggle(SLeftRect(++line), BDArmorySettings.AUTO_RESUME_TOURNAMENT, StringUtils.Localize("#LOC_BDArmory_Settings_AutoResumeTournaments")); // Auto-Resume Tournaments
                    if (BDArmorySettings.AUTO_RESUME_TOURNAMENT)
                    {
                        BDArmorySettings.AUTO_QUIT_AT_END_OF_TOURNAMENT = GUI.Toggle(SRightRect(line), BDArmorySettings.AUTO_QUIT_AT_END_OF_TOURNAMENT, StringUtils.Localize("#LOC_BDArmory_Settings_AutoQuitAtEndOfTournament")); // Auto Quit At End Of Tournament
                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_AutoQuitMemoryUsage")}:  ({(BDArmorySettings.QUIT_MEMORY_USAGE_THRESHOLD > SystemMaxMemory ? "Off" : $"{BDArmorySettings.QUIT_MEMORY_USAGE_THRESHOLD}GB")})", leftLabel); // Auto-Quit Memory Threshold
                        BDArmorySettings.QUIT_MEMORY_USAGE_THRESHOLD = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.QUIT_MEMORY_USAGE_THRESHOLD, 1f, SystemMaxMemory + 1));
                        if (BDArmorySettings.QUIT_MEMORY_USAGE_THRESHOLD <= SystemMaxMemory)
                        {
                            GUI.Label(SLineRect(++line, 1), $"{StringUtils.Localize("#LOC_BDArmory_Settings_CurrentMemoryUsageEstimate")}: {TournamentAutoResume.memoryUsage:F1}GB / {SystemMaxMemory}GB", leftLabel);
                        }
                    }
                    if (BDArmorySettings.TIME_OVERRIDE != (BDArmorySettings.TIME_OVERRIDE = GUI.Toggle(SLeftRect(++line), BDArmorySettings.TIME_OVERRIDE, StringUtils.Localize("#LOC_BDArmory_Settings_TimeOverride")))) // Time override.
                    {
                        OtherUtils.SetTimeOverride(BDArmorySettings.TIME_OVERRIDE);
                    }
                    if (BDArmorySettings.TIME_OVERRIDE)
                    {
                        GUI.Label(SLeftSliderRect(++line, 1), $"{StringUtils.Localize("#LOC_BDArmory_Settings_TimeScale")}; ({BDArmorySettings.TIME_SCALE:G2}x)", leftLabel);
                        if (BDArmorySettings.TIME_SCALE != (BDArmorySettings.TIME_SCALE = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.TIME_SCALE, 0f, BDArmorySettings.TIME_SCALE_MAX), BDArmorySettings.TIME_SCALE > 5f ? 1f : 0.1f)))
                        {
                            Time.timeScale = BDArmorySettings.TIME_SCALE;
                        }
                    }
                    BDArmorySettings.MISSILE_CM_SETTING_TOGGLE = GUI.Toggle(SLineRect(++line), BDArmorySettings.MISSILE_CM_SETTING_TOGGLE, StringUtils.Localize("#LOC_BDArmory_Settings_MissileCMToggle"));
                    if (BDArmorySettings.MISSILE_CM_SETTING_TOGGLE)
                    {
                        BDArmorySettings.ASPECTED_IR_SEEKERS = GUI.Toggle(SLineRect(++line, 1), BDArmorySettings.ASPECTED_IR_SEEKERS, StringUtils.Localize("#LOC_BDArmory_Settings_AspectedIRSeekers"));
                        GUI.Label(SLeftSliderRect(++line, 1), $"{StringUtils.Localize("#LOC_BDArmory_Settings_FlareFactor")}:  ({BDArmorySettings.FLARE_FACTOR})", leftLabel);
                        BDArmorySettings.FLARE_FACTOR = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.FLARE_FACTOR, 0f, 3f), 0.05f);

                        GUI.Label(SLeftSliderRect(++line, 1), $"{StringUtils.Localize("#LOC_BDArmory_Settings_ChaffFactor")}:  ({BDArmorySettings.CHAFF_FACTOR})", leftLabel);
                        BDArmorySettings.CHAFF_FACTOR = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.CHAFF_FACTOR, 0f, 3f), 0.05f);

                        GUI.Label(SLeftSliderRect(++line, 1), $"{StringUtils.Localize("#LOC_BDArmory_Settings_SmokeDeflectionFactor")}:  ({BDArmorySettings.SMOKE_DEFLECTION_FACTOR})", leftLabel);
                        BDArmorySettings.SMOKE_DEFLECTION_FACTOR = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.SMOKE_DEFLECTION_FACTOR, 0f, 40f), 0.5f);

                        GUI.Label(SLeftSliderRect(++line, 1), $"{StringUtils.Localize("#LOC_BDArmory_Settings_APSThreshold")}:  ({BDArmorySettings.APS_THRESHOLD})", leftLabel);
                        BDArmorySettings.APS_THRESHOLD = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.APS_THRESHOLD, 1f, 356f));
                    }
                }

                line += 0.5f;
            }

            if (GUI.Button(SLineRect(++line), $"{(BDArmorySettings.SLIDER_SETTINGS_TOGGLE ? StringUtils.Localize("#LOC_BDArmory_Generic_Hide") : StringUtils.Localize("#LOC_BDArmory_Generic_Show"))} {StringUtils.Localize("#LOC_BDArmory_Settings_SliderSettingsToggle")}"))//Show/hide General Slider settings.
            {
                BDArmorySettings.SLIDER_SETTINGS_TOGGLE = !BDArmorySettings.SLIDER_SETTINGS_TOGGLE;
            }
            if (BDArmorySettings.SLIDER_SETTINGS_TOGGLE)
            {
                line += 0.2f;

                float dmgMultiplier = BDArmorySettings.DMG_MULTIPLIER <= 100f ? BDArmorySettings.DMG_MULTIPLIER / 10f : BDArmorySettings.DMG_MULTIPLIER / 50f + 8f;
                GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_DamageMultiplier")}:  ({BDArmorySettings.DMG_MULTIPLIER})", leftLabel); // Damage Multiplier
                dmgMultiplier = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), dmgMultiplier, 1f, 28f));
                BDArmorySettings.DMG_MULTIPLIER = dmgMultiplier < 11 ? (int)(dmgMultiplier * 10f) : (int)(50f * (dmgMultiplier - 8f));
                if (BDArmorySettings.ADVANCED_USER_SETTINGS)
                {
                    BDArmorySettings.EXTRA_DAMAGE_SLIDERS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.EXTRA_DAMAGE_SLIDERS, StringUtils.Localize("#LOC_BDArmory_Settings_ExtraDamageSliders"));

                    if (BDArmorySettings.EXTRA_DAMAGE_SLIDERS)
                    {
                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_BallisticDamageMultiplier")}:  ({BDArmorySettings.BALLISTIC_DMG_FACTOR})", leftLabel);
                        BDArmorySettings.BALLISTIC_DMG_FACTOR = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.BALLISTIC_DMG_FACTOR, 0f, 3f), 0.05f);

                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_ExplosiveDamageMultiplier")}:  ({BDArmorySettings.EXP_DMG_MOD_BALLISTIC_NEW})", leftLabel);
                        BDArmorySettings.EXP_DMG_MOD_BALLISTIC_NEW = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.EXP_DMG_MOD_BALLISTIC_NEW, 0f, 1.5f), 0.05f);

                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_RocketExplosiveDamageMultiplier")}:  ({BDArmorySettings.EXP_DMG_MOD_ROCKET})", leftLabel);
                        BDArmorySettings.EXP_DMG_MOD_ROCKET = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.EXP_DMG_MOD_ROCKET, 0f, 2f), 0.05f);

                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_MissileExplosiveDamageMultiplier")}:  ({BDArmorySettings.EXP_DMG_MOD_MISSILE})", leftLabel);
                        BDArmorySettings.EXP_DMG_MOD_MISSILE = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.EXP_DMG_MOD_MISSILE, 0f, 10f), 0.25f);

                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_ImplosiveDamageMultiplier")}:  ({BDArmorySettings.EXP_IMP_MOD})", leftLabel);
                        BDArmorySettings.EXP_IMP_MOD = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.EXP_IMP_MOD, 0f, 1f), 0.05f);


                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_ArmorExplosivePenetrationResistanceMultiplier")}:  ({BDArmorySettings.EXP_PEN_RESIST_MULT})", leftLabel);
                        BDArmorySettings.EXP_PEN_RESIST_MULT = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.EXP_PEN_RESIST_MULT, 0f, 10f), 0.25f);

                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_ExplosiveBattleDamageMultiplier")}:  ({BDArmorySettings.EXP_DMG_MOD_BATTLE_DAMAGE})", leftLabel);
                        BDArmorySettings.EXP_DMG_MOD_BATTLE_DAMAGE = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.EXP_DMG_MOD_BATTLE_DAMAGE, 0f, 2f), 0.1f);

                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_BuildingDamageMultiplier")}:  ({BDArmorySettings.BUILDING_DMG_MULTIPLIER})", leftLabel);
                        BDArmorySettings.BUILDING_DMG_MULTIPLIER = BDAMath.RoundToUnit((GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.BUILDING_DMG_MULTIPLIER, 0f, 10f)), 0.1f);

                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_SecondaryEffectDuration")}:  ({BDArmorySettings.WEAPON_FX_DURATION})", leftLabel);
                        BDArmorySettings.WEAPON_FX_DURATION = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.WEAPON_FX_DURATION, 5f, 20f));

                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_BallisticTrajectorSimulationMultiplier")}:  ({BDArmorySettings.BALLISTIC_TRAJECTORY_SIMULATION_MULTIPLIER})", leftLabel);
                        BDArmorySettings.BALLISTIC_TRAJECTORY_SIMULATION_MULTIPLIER = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.BALLISTIC_TRAJECTORY_SIMULATION_MULTIPLIER, 1f, 256f));

                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_ArmorMassMultiplier")}:  ({BDArmorySettings.ARMOR_MASS_MOD})", leftLabel);
                        BDArmorySettings.ARMOR_MASS_MOD = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.ARMOR_MASS_MOD, 0.05f, 2f), 0.05f); //armor mult shouldn't be zero, else armor will never take damage, might also break some other things
                    }
                }

                // Kill categories
                GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_Scoring_HeadShot")}:  ({BDArmorySettings.SCORING_HEADSHOT}s)", leftLabel); // Scoring head-shot time limit
                BDArmorySettings.SCORING_HEADSHOT = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.SCORING_HEADSHOT, 1f, 10f));
                BDArmorySettings.SCORING_KILLSTEAL = Mathf.Max(BDArmorySettings.SCORING_HEADSHOT, BDArmorySettings.SCORING_KILLSTEAL);
                GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_Scoring_KillSteal")}:  ({BDArmorySettings.SCORING_KILLSTEAL}s)", leftLabel); // Scoring kill-steal time limit
                BDArmorySettings.SCORING_KILLSTEAL = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.SCORING_KILLSTEAL, BDArmorySettings.SCORING_HEADSHOT, 30f));

                if (BDArmorySettings.ADVANCED_USER_SETTINGS)
                {
                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_TerrainAlertFrequency")}:  ({BDArmorySettings.TERRAIN_ALERT_FREQUENCY})", leftLabel); // Terrain alert frequency. Note: this is scaled by (int)(1+(radarAlt/500)^2) to avoid wasting too many cycles.
                    BDArmorySettings.TERRAIN_ALERT_FREQUENCY = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.TERRAIN_ALERT_FREQUENCY, 1f, 5f));
                }
                GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_CameraSwitchFrequency")}:  ({BDArmorySettings.CAMERA_SWITCH_FREQUENCY}s)", leftLabel); // Minimum camera switching frequency
                BDArmorySettings.CAMERA_SWITCH_FREQUENCY = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.CAMERA_SWITCH_FREQUENCY, 1f, 10f));

                if (BDArmorySettings.ADVANCED_USER_SETTINGS)
                {
                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_DeathCameraInhibitPeriod")}:  ({(BDArmorySettings.DEATH_CAMERA_SWITCH_INHIBIT_PERIOD == 0 ? BDArmorySettings.CAMERA_SWITCH_FREQUENCY / 2f : BDArmorySettings.DEATH_CAMERA_SWITCH_INHIBIT_PERIOD)}s)", leftLabel); // Camera switch inhibit period after the active vessel dies.
                    BDArmorySettings.DEATH_CAMERA_SWITCH_INHIBIT_PERIOD = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.DEATH_CAMERA_SWITCH_INHIBIT_PERIOD, 0f, 10f));
                }
                GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_Max_PWing_HP")}:  {(BDArmorySettings.HP_THRESHOLD >= 100 ? (BDArmorySettings.HP_THRESHOLD.ToString()) : "Unclamped")}", leftLabel); // HP Scaling Threshold
                if (BDArmorySettings.HP_THRESHOLD != (BDArmorySettings.HP_THRESHOLD = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.HP_THRESHOLD, 0, 10000), 100)))
                {
                    if (HighLogic.LoadedSceneIsEditor && EditorLogic.fetch.ship is not null) GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
                }
                GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_HP_Clamp")}:  {(BDArmorySettings.HP_CLAMP >= 100 ? (BDArmorySettings.HP_CLAMP.ToString()) : "Unclamped")}", leftLabel); // HP Scaling Threshold
                if (BDArmorySettings.HP_CLAMP != (BDArmorySettings.HP_CLAMP = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.HP_CLAMP, 0, 25000), 250)))
                {
                    if (HighLogic.LoadedSceneIsEditor && EditorLogic.fetch.ship is not null) GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
                }

                line += 0.5f;
            }

            if (GUI.Button(SLineRect(++line), $"{(BDArmorySettings.GAME_MODES_SETTINGS_TOGGLE ? StringUtils.Localize("#LOC_BDArmory_Generic_Hide") : StringUtils.Localize("#LOC_BDArmory_Generic_Show"))} {StringUtils.Localize("#LOC_BDArmory_Settings_GameModesSettingsToggle")}"))//Show/hide Game Modes settings.
            {
                BDArmorySettings.GAME_MODES_SETTINGS_TOGGLE = !BDArmorySettings.GAME_MODES_SETTINGS_TOGGLE;
            }
            if (BDArmorySettings.GAME_MODES_SETTINGS_TOGGLE)
            {
                line += 0.2f;

                BDArmorySettings.BATTLEDAMAGE = GUI.Toggle(SLeftRect(++line), BDArmorySettings.BATTLEDAMAGE, StringUtils.Localize("#LOC_BDArmory_Settings_BattleDamage"));
                BDArmorySettings.INFINITE_AMMO = GUI.Toggle(SRightRect(line), BDArmorySettings.INFINITE_AMMO, StringUtils.Localize("#LOC_BDArmory_Settings_InfiniteAmmo"));//"Infinite Ammo"
                BDArmorySettings.TAG_MODE = GUI.Toggle(SLeftRect(++line), BDArmorySettings.TAG_MODE, StringUtils.Localize("#LOC_BDArmory_Settings_TagMode"));//"Tag Mode"
                BDArmorySettings.INFINITE_ORDINANCE = GUI.Toggle(SRightRect(line), BDArmorySettings.INFINITE_ORDINANCE, StringUtils.Localize("#LOC_BDArmory_Settings_InfiniteMissiles"));//"Infinite Ammo"
                if (BDArmorySettings.GRAVITY_HACKS != (BDArmorySettings.GRAVITY_HACKS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.GRAVITY_HACKS, StringUtils.Localize("#LOC_BDArmory_Settings_GravityHacks"))))//"Gravity hacks"
                {
                    if (BDArmorySettings.GRAVITY_HACKS)
                    {
                        BDArmorySettings.COMPETITION_INITIAL_GRACE_PERIOD = 10; // For gravity hacks, we need a shorter grace period.
                        BDArmorySettings.COMPETITION_KILL_TIMER = 1; // and a shorter kill timer.
                    }
                    else
                    {
                        BDArmorySettings.COMPETITION_INITIAL_GRACE_PERIOD = 60; // Reset grace period back to default of 60s.
                        BDArmorySettings.COMPETITION_KILL_TIMER = 15; // Reset kill timer period back to default of 15s.
                        PhysicsGlobals.GraviticForceMultiplier = 1;
                        VehiclePhysics.Gravity.Refresh();
                    }
                }
                if (BDArmorySettings.PAINTBALL_MODE != (BDArmorySettings.PAINTBALL_MODE = GUI.Toggle(SRightRect(line), BDArmorySettings.PAINTBALL_MODE, StringUtils.Localize("#LOC_BDArmory_Settings_PaintballMode"))))//"Paintball Mode"
                {
                    BulletHitFX.SetupShellPool();
                    BDArmorySettings.BATTLEDAMAGE = false;
                }
                if (BDArmorySettings.PEACE_MODE != (BDArmorySettings.PEACE_MODE = GUI.Toggle(SRightRect(++line), BDArmorySettings.PEACE_MODE, StringUtils.Localize("#LOC_BDArmory_Settings_PeaceMode"))))//"Peace Mode"
                {
                    BDATargetManager.ClearDatabase();
                    if (OnPeaceEnabled != null)
                    {
                        OnPeaceEnabled();
                    }
                    CheatOptions.InfinitePropellant = BDArmorySettings.PEACE_MODE || BDArmorySettings.INFINITE_FUEL;
                }
                //Mutators
                var oldMutators = BDArmorySettings.MUTATOR_MODE;
                BDArmorySettings.MUTATOR_MODE = GUI.Toggle(SLeftRect(line), BDArmorySettings.MUTATOR_MODE, StringUtils.Localize("#LOC_BDArmory_Settings_Mutators"));
                {
                    if (BDArmorySettings.MUTATOR_MODE)
                    {
                        if (!oldMutators)  // Add missing modules when Space Hacks is toggled.
                        {
                            foreach (var vessel in FlightGlobals.Vessels)
                            {
                                if (VesselModuleRegistry.GetMissileFire(vessel, true) != null && vessel.rootPart.FindModuleImplementing<BDAMutator>() == null)
                                {
                                    vessel.rootPart.AddModule("BDAMutator");
                                }
                            }
                        }
                        selectMutators = GUI.Toggle(SLeftRect(++line, 1f), selectMutators, StringUtils.Localize("#LOC_BDArmory_MutatorSelect"));
                        if (selectMutators)
                        {
                            ++line;
                            scrollViewVector = GUI.BeginScrollView(new Rect(settingsMargin + 1 * settingsMargin, line * settingsLineHeight, settingsWidth - 2 * settingsMargin - 1 * settingsMargin, settingsLineHeight * 6f), scrollViewVector,
                                               new Rect(0, 0, settingsWidth - 2 * settingsMargin - 2 * settingsMargin, mutatorHeight));
                            GUI.BeginGroup(new Rect(0, 0, settingsWidth - 2 * settingsMargin - 2 * settingsMargin, mutatorHeight), GUIContent.none);
                            int mutatorLine = 0;
                            for (int i = 0; i < mutators.Count; i++)
                            {
                                Rect buttonRect = new Rect(0, (i * 25), (settingsWidth - 4 * settingsMargin) / 2, 20);
                                if (mutators_selected[i] != (mutators_selected[i] = GUI.Toggle(buttonRect, mutators_selected[i], mutators[i])))
                                {
                                    if (mutators_selected[i])
                                    {
                                        BDArmorySettings.MUTATOR_LIST.Add(mutators[i]);
                                    }
                                    else
                                    {
                                        BDArmorySettings.MUTATOR_LIST.Remove(mutators[i]);
                                    }
                                }
                                mutatorLine++;
                            }

                            mutatorHeight = Mathf.Lerp(mutatorHeight, (mutatorLine * 25), 1);
                            GUI.EndGroup();
                            GUI.EndScrollView();
                            line += 6.5f;

                            if (GUI.Button(SRightRect(line), StringUtils.Localize("#LOC_BDArmory_reset")))
                            {
                                switch (Event.current.button)
                                {
                                    case 1: // right click
                                        Debug.Log($"[BDArmory.BDArmorySetup]: MutatorList: {string.Join("; ", BDArmorySettings.MUTATOR_LIST)}");
                                        break;
                                    default:
                                        BDArmorySettings.MUTATOR_LIST.Clear();
                                        for (int i = 0; i < mutators_selected.Length; ++i) mutators_selected[i] = false;
                                        Debug.Log("[BDArmory.BDArmorySetup]: Resetting Mutator list");
                                        break;
                                }
                            }
                            line += .2f;
                        }
                        BDArmorySettings.MUTATOR_APPLY_GLOBAL = GUI.Toggle(SLeftRect(++line, 1f), BDArmorySettings.MUTATOR_APPLY_GLOBAL, StringUtils.Localize("#LOC_BDArmory_Settings_MutatorGlobal"));
                        if (BDArmorySettings.MUTATOR_APPLY_GLOBAL) //if more than 1 mutator selected, will shuffle each round
                        {
                            BDArmorySettings.MUTATOR_APPLY_KILL = false;
                        }
                        BDArmorySettings.MUTATOR_APPLY_KILL = GUI.Toggle(SRightRect(line, 1f), BDArmorySettings.MUTATOR_APPLY_KILL, StringUtils.Localize("#LOC_BDArmory_Settings_MutatorKill"));
                        if (BDArmorySettings.MUTATOR_APPLY_KILL) // if more than 1 mutator selected, will randomly assign mutator on kill
                        {
                            BDArmorySettings.MUTATOR_APPLY_GLOBAL = false;
                            BDArmorySettings.MUTATOR_APPLY_TIMER = false;
                        }

                        if (BDArmorySettings.MUTATOR_LIST.Count > 1)

                        {
                            BDArmorySettings.MUTATOR_APPLY_TIMER = GUI.Toggle(SLeftRect(++line, 1f), BDArmorySettings.MUTATOR_APPLY_TIMER, StringUtils.Localize("#LOC_BDArmory_Settings_MutatorTimed"));
                            if (BDArmorySettings.MUTATOR_APPLY_TIMER) //only an option if more than one mutator selected
                            {
                                BDArmorySettings.MUTATOR_APPLY_KILL = false;
                                //BDArmorySettings.MUTATOR_APPLY_GLOBAL = false; //global + timer causes a single globally appled mutator that shuffles, instead of chaos mode
                            }
                        }
                        else
                        {
                            BDArmorySettings.MUTATOR_APPLY_TIMER = false;
                        }
                        if (!BDArmorySettings.MUTATOR_APPLY_TIMER && !BDArmorySettings.MUTATOR_APPLY_KILL)
                        {
                            BDArmorySettings.MUTATOR_APPLY_GLOBAL = true;
                        }

                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_MutatorDuration")}: ({(BDArmorySettings.MUTATOR_DURATION > 0 ? BDArmorySettings.MUTATOR_DURATION + (BDArmorySettings.MUTATOR_DURATION > 1 ? " mins" : " min") : "Unlimited")})", leftLabel);
                        BDArmorySettings.MUTATOR_DURATION = (float)Math.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.MUTATOR_DURATION, 0f, BDArmorySettings.COMPETITION_DURATION), 1);

                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_MutatorNum")}:  ({BDArmorySettings.MUTATOR_APPLY_NUM})", leftLabel);//Number of active mutators
                        BDArmorySettings.MUTATOR_APPLY_NUM = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.MUTATOR_APPLY_NUM, 1f, BDArmorySettings.MUTATOR_LIST.Count));
                        if (BDArmorySettings.MUTATOR_LIST.Count < BDArmorySettings.MUTATOR_APPLY_NUM)
                        {
                            BDArmorySettings.MUTATOR_APPLY_NUM = BDArmorySettings.MUTATOR_LIST.Count;
                        }
                        if (BDArmorySettings.MUTATOR_LIST.Count > 0 && BDArmorySettings.MUTATOR_APPLY_NUM < 1)
                        {
                            BDArmorySettings.MUTATOR_APPLY_NUM = 1;
                        }
                        BDArmorySettings.MUTATOR_ICONS = GUI.Toggle(SLeftRect(++line, 1f), BDArmorySettings.MUTATOR_ICONS, StringUtils.Localize("#LOC_BDArmory_Settings_MutatorIcons"));
                    }
                }
                BDArmorySettings.INFINITE_FUEL = CheatOptions.InfinitePropellant; // Sync with the Alt-F12 window if the checkbox was toggled there.
                if (BDArmorySettings.INFINITE_FUEL != (BDArmorySettings.INFINITE_FUEL = GUI.Toggle(SRightRect(++line), BDArmorySettings.INFINITE_FUEL, StringUtils.Localize("#autoLOC_900349"))))//"Infinite Propellant"
                {
                    CheatOptions.InfinitePropellant = BDArmorySettings.INFINITE_FUEL;
                }
                // Heartbleed
                BDArmorySettings.HEART_BLEED_ENABLED = GUI.Toggle(SLeftRect(line), BDArmorySettings.HEART_BLEED_ENABLED, StringUtils.Localize("#LOC_BDArmory_Settings_HeartBleed"));//"Heart Bleed"
                if (BDArmorySettings.HEART_BLEED_ENABLED)
                {
                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_HeartBleedRate")}:  ({BDArmorySettings.HEART_BLEED_RATE})", leftLabel);//Heart Bleed Rate
                    BDArmorySettings.HEART_BLEED_RATE = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.HEART_BLEED_RATE, 0f, 0.1f) * 1000f) / 1000f;
                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_HeartBleedInterval")}:  ({BDArmorySettings.HEART_BLEED_INTERVAL})", leftLabel);//Heart Bleed Interval
                    BDArmorySettings.HEART_BLEED_INTERVAL = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.HEART_BLEED_INTERVAL, 1f, 60f));
                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_HeartBleedThreshold")}:  ({BDArmorySettings.HEART_BLEED_THRESHOLD})", leftLabel);//Heart Bleed Threshold
                    BDArmorySettings.HEART_BLEED_THRESHOLD = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.HEART_BLEED_THRESHOLD, 1f, 100f));
                }
                BDArmorySettings.INFINITE_EC = CheatOptions.InfiniteElectricity; // Sync with the Alt-F12 window if the checkbox was toggled there.
                if (BDArmorySettings.INFINITE_EC != (BDArmorySettings.INFINITE_EC = GUI.Toggle(SRightRect(++line), BDArmorySettings.INFINITE_EC, StringUtils.Localize("#autoLOC_900361"))))//"Infinite Electricity"
                {
                    CheatOptions.InfiniteElectricity = BDArmorySettings.INFINITE_EC;
                }
                // Resource steal
                BDArmorySettings.RESOURCE_STEAL_ENABLED = GUI.Toggle(SLeftRect(line), BDArmorySettings.RESOURCE_STEAL_ENABLED, StringUtils.Localize("#LOC_BDArmory_Settings_ResourceSteal"));//"Resource Steal"
                if (BDArmorySettings.RESOURCE_STEAL_ENABLED)
                {
                    BDArmorySettings.RESOURCE_STEAL_RESPECT_FLOWSTATE_IN = GUI.Toggle(SLeftRect(++line, 1), BDArmorySettings.RESOURCE_STEAL_RESPECT_FLOWSTATE_IN, StringUtils.Localize("#LOC_BDArmory_Settings_ResourceSteal_RespectFlowStateIn"));//Respect Flow State In
                    BDArmorySettings.RESOURCE_STEAL_RESPECT_FLOWSTATE_OUT = GUI.Toggle(SRightRect(line, 1), BDArmorySettings.RESOURCE_STEAL_RESPECT_FLOWSTATE_OUT, StringUtils.Localize("#LOC_BDArmory_Settings_ResourceSteal_RespectFlowStateOut"));//Respect Flow State Out
                    GUI.Label(SLeftSliderRect(++line, 1), $"{StringUtils.Localize("#LOC_BDArmory_Settings_FuelStealRation")}:  ({BDArmorySettings.RESOURCE_STEAL_FUEL_RATION})", leftLabel);//Fuel Steal Ration
                    BDArmorySettings.RESOURCE_STEAL_FUEL_RATION = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.RESOURCE_STEAL_FUEL_RATION, 0f, 1f) * 100f) / 100f;
                    GUI.Label(SLeftSliderRect(++line, 1), $"{StringUtils.Localize("#LOC_BDArmory_Settings_AmmoStealRation")}:  ({BDArmorySettings.RESOURCE_STEAL_AMMO_RATION})", leftLabel);//Ammo Steal Ration
                    BDArmorySettings.RESOURCE_STEAL_AMMO_RATION = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.RESOURCE_STEAL_AMMO_RATION, 0f, 1f) * 100f) / 100f;
                    GUI.Label(SLeftSliderRect(++line, 1), $"{StringUtils.Localize("#LOC_BDArmory_Settings_CMStealRation")}:  ({BDArmorySettings.RESOURCE_STEAL_CM_RATION})", leftLabel);//CM Steal Ration
                    BDArmorySettings.RESOURCE_STEAL_CM_RATION = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.RESOURCE_STEAL_CM_RATION, 0f, 1f) * 100f) / 100f;
                }
                bool oldSpaceHacks = BDArmorySettings.SPACE_HACKS;
                BDArmorySettings.SPACE_HACKS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.SPACE_HACKS, StringUtils.Localize("#LOC_BDArmory_Settings_SpaceHacks"));//Space Tools
                if (BDArmorySettings.SPACE_HACKS)
                {
                    if (HighLogic.LoadedSceneIsFlight)
                    {
                        if (oldSpaceHacks != BDArmorySettings.SPACE_HACKS)
                        {
                            SpawnUtils.SpaceFrictionOnNewVessels(BDArmorySettings.SPACE_HACKS);
                            if (BDArmorySettings.SPACE_HACKS) // Add the hack to all in-game intakes.
                            {
                                foreach (var vessel in FlightGlobals.Vessels)
                                {
                                    if (vessel == null || !vessel.loaded) continue;
                                    SpawnUtils.SpaceHacks(vessel);
                                }
                            }
                        }
                    }
                    //ModuleSpaceFriction.AddSpaceFrictionToAllValidVessels(); // Add missing modules when Space Hacks is toggled.

                    BDArmorySettings.SF_FRICTION = GUI.Toggle(SLeftRect(++line, 1f), BDArmorySettings.SF_FRICTION, StringUtils.Localize("#LOC_BDArmory_Settings_SpaceFriction"));
                    BDArmorySettings.SF_GRAVITY = GUI.Toggle(SLeftRect(++line, 1f), BDArmorySettings.SF_GRAVITY, StringUtils.Localize("#LOC_BDArmory_Settings_IgnoreGravity"));
                    GUI.Label(SLeftSliderRect(++line, 1f), $"{StringUtils.Localize("#LOC_BDArmory_Settings_SpaceFrictionMult")}:  ({BDArmorySettings.SF_DRAGMULT})", leftLabel);//Space Friction Mult
                    BDArmorySettings.SF_DRAGMULT = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.SF_DRAGMULT, 0f, 10));
                    BDArmorySettings.SF_REPULSOR = GUI.Toggle(SLeftRect(++line, 1f), BDArmorySettings.SF_REPULSOR, $"{StringUtils.Localize("#LOC_BDArmory_Settings_Repulsor")} ({BDArmorySettings.SF_REPULSOR_STRENGTH:0.0})");
                    BDArmorySettings.SF_REPULSOR_STRENGTH = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.SF_REPULSOR_STRENGTH, 1f, 10f), 0.1f);
                }
                else
                {
                    BDArmorySettings.SF_FRICTION = false;
                    BDArmorySettings.SF_GRAVITY = false;
                    BDArmorySettings.SF_REPULSOR = false;
                }

                // Asteroids
                if (BDArmorySettings.ASTEROID_FIELD != (BDArmorySettings.ASTEROID_FIELD = GUI.Toggle(SLeftRect(++line), BDArmorySettings.ASTEROID_FIELD, StringUtils.Localize("#LOC_BDArmory_Settings_AsteroidField")))) // Asteroid Field
                {
                    if (!BDArmorySettings.ASTEROID_FIELD) AsteroidField.Instance.Reset(true);
                }
                if (BDArmorySettings.ASTEROID_FIELD)
                {
                    if (GUI.Button(SRightButtonRect(line), "Spawn Field Now"))//"Spawn Field Now"))
                    {
                        if (Event.current.button == 1)
                            AsteroidField.Instance.Reset();
                        else if (Event.current.button == 2) // Middle click
                                                            // AsteroidUtils.CheckOrbit();
                            AsteroidField.Instance.CheckPooledAsteroids();
                        else
                            AsteroidField.Instance.SpawnField(BDArmorySettings.ASTEROID_FIELD_NUMBER, BDArmorySettings.ASTEROID_FIELD_ALTITUDE, BDArmorySettings.ASTEROID_FIELD_RADIUS, BDArmorySettings.VESSEL_SPAWN_GEOCOORDS);
                    }
                    line += 0.25f;
                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_AsteroidFieldNumber")}:  ({BDArmorySettings.ASTEROID_FIELD_NUMBER})", leftLabel);
                    BDArmorySettings.ASTEROID_FIELD_NUMBER = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), Mathf.Round(BDArmorySettings.ASTEROID_FIELD_NUMBER / 10f), 1f, 200f) * 10f); // Asteroid Field Number
                    var altitudeString = BDArmorySettings.ASTEROID_FIELD_ALTITUDE < 10f ? $"{BDArmorySettings.ASTEROID_FIELD_ALTITUDE * 100f:F0}m" : $"{BDArmorySettings.ASTEROID_FIELD_ALTITUDE / 10f:F1}km";
                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_AsteroidFieldAltitude")}:  ({altitudeString})", leftLabel);
                    BDArmorySettings.ASTEROID_FIELD_ALTITUDE = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.ASTEROID_FIELD_ALTITUDE, 1f, 200f)); // Asteroid Field Altitude
                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_AsteroidFieldRadius")}:  ({BDArmorySettings.ASTEROID_FIELD_RADIUS}km)", leftLabel);
                    BDArmorySettings.ASTEROID_FIELD_RADIUS = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.ASTEROID_FIELD_RADIUS, 1f, 10f)); // Asteroid Field Radius
                    line -= 0.25f;
                    if (BDArmorySettings.ASTEROID_FIELD_ANOMALOUS_ATTRACTION != (BDArmorySettings.ASTEROID_FIELD_ANOMALOUS_ATTRACTION = GUI.Toggle(SLeftRect(++line), BDArmorySettings.ASTEROID_FIELD_ANOMALOUS_ATTRACTION, BDArmorySettings.ASTEROID_FIELD_ANOMALOUS_ATTRACTION ? $"{StringUtils.Localize("#LOC_BDArmory_Settings_AsteroidFieldAnomalousAttraction")}:  ({BDArmorySettings.ASTEROID_FIELD_ANOMALOUS_ATTRACTION_STRENGTH:G2})" : StringUtils.Localize("#LOC_BDArmory_Settings_AsteroidFieldAnomalousAttraction")))) // Anomalous Attraction
                    {
                        if (!BDArmorySettings.ASTEROID_FIELD_ANOMALOUS_ATTRACTION && AsteroidField.Instance != null)
                        { AsteroidField.Instance.anomalousAttraction = Vector3d.zero; }
                    }
                    if (BDArmorySettings.ASTEROID_FIELD_ANOMALOUS_ATTRACTION)
                    {
                        BDArmorySettings.ASTEROID_FIELD_ANOMALOUS_ATTRACTION_STRENGTH = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.ASTEROID_FIELD_ANOMALOUS_ATTRACTION_STRENGTH * 20f, 1f, 20f)) / 20f; // Asteroid Field Anomalous Attraction Strength
                    }
                }
                if (BDArmorySettings.ASTEROID_RAIN != (BDArmorySettings.ASTEROID_RAIN = GUI.Toggle(SLeftRect(++line), BDArmorySettings.ASTEROID_RAIN, StringUtils.Localize("#LOC_BDArmory_Settings_AsteroidRain")))) // Asteroid Rain
                {
                    if (!BDArmorySettings.ASTEROID_RAIN) AsteroidRain.Instance.Reset();
                }
                if (BDArmorySettings.ASTEROID_RAIN)
                {
                    if (GUI.Button(SRightButtonRect(line), "Spawn Rain Now"))
                    {
                        if (Event.current.button == 1)
                            AsteroidRain.Instance.Reset();
                        else if (Event.current.button == 2)
                            AsteroidRain.Instance.CheckPooledAsteroids();
                        else
                            AsteroidRain.Instance.SpawnRain(BDArmorySettings.VESSEL_SPAWN_GEOCOORDS);
                    }
                    BDArmorySettings.ASTEROID_RAIN_FOLLOWS_CENTROID = GUI.Toggle(SLeftRect(++line), BDArmorySettings.ASTEROID_RAIN_FOLLOWS_CENTROID, StringUtils.Localize("#LOC_BDArmory_Settings_AsteroidRainFollowsCentroid")); // Follows Vessels' Location.
                    if (BDArmorySettings.ASTEROID_RAIN_FOLLOWS_CENTROID)
                    {
                        BDArmorySettings.ASTEROID_RAIN_FOLLOWS_SPREAD = GUI.Toggle(SRightRect(line), BDArmorySettings.ASTEROID_RAIN_FOLLOWS_SPREAD, StringUtils.Localize("#LOC_BDArmory_Settings_AsteroidRainFollowsSpread")); // Follows Vessels' Spread.
                    }
                    line += 0.25f;
                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_AsteroidRainNumber")}:  ({BDArmorySettings.ASTEROID_RAIN_NUMBER})", leftLabel);
                    if (BDArmorySettings.ASTEROID_RAIN_NUMBER != (BDArmorySettings.ASTEROID_RAIN_NUMBER = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), Mathf.Round(BDArmorySettings.ASTEROID_RAIN_NUMBER / 10f), 1f, 200f) * 10f))) // Asteroid Rain Number
                    { if (HighLogic.LoadedSceneIsFlight) AsteroidRain.Instance.UpdateSettings(); }
                    var altitudeString = BDArmorySettings.ASTEROID_RAIN_ALTITUDE < 10f ? $"{BDArmorySettings.ASTEROID_RAIN_ALTITUDE * 100f:F0}m" : $"{BDArmorySettings.ASTEROID_RAIN_ALTITUDE / 10f:F1}km";
                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_AsteroidRainAltitude")}:  ({altitudeString})", leftLabel);
                    if (BDArmorySettings.ASTEROID_RAIN_ALTITUDE != (BDArmorySettings.ASTEROID_RAIN_ALTITUDE = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.ASTEROID_RAIN_ALTITUDE, 1f, 100f)))) // Asteroid Rain Altitude
                    { if (HighLogic.LoadedSceneIsFlight) AsteroidRain.Instance.UpdateSettings(); }
                    if (!BDArmorySettings.ASTEROID_RAIN_FOLLOWS_SPREAD)
                    {
                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_AsteroidRainRadius")}:  ({BDArmorySettings.ASTEROID_RAIN_RADIUS}km)", leftLabel);
                        if (BDArmorySettings.ASTEROID_RAIN_RADIUS != (BDArmorySettings.ASTEROID_RAIN_RADIUS = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.ASTEROID_RAIN_RADIUS, 1f, 10f)))) // Asteroid Rain Radius
                        { if (HighLogic.LoadedSceneIsFlight) AsteroidRain.Instance.UpdateSettings(); }
                    }
                    line -= 0.25f;
                }
                BDArmorySettings.WAYPOINTS_MODE = GUI.Toggle(SLeftRect(++line), BDArmorySettings.WAYPOINTS_MODE, StringUtils.Localize("#LOC_BDArmory_Settings_WaypointsMode"));
                if (BDArmorySettings.ADVANCED_USER_SETTINGS)
                {
                    if (BDArmorySettings.RUNWAY_PROJECT != (BDArmorySettings.RUNWAY_PROJECT = GUI.Toggle(SLeftRect(++line), BDArmorySettings.RUNWAY_PROJECT, StringUtils.Localize("#LOC_BDArmory_Settings_RunwayProject"))))//Runway Project
                    {
                        if (HighLogic.LoadedSceneIsFlight)
                        {
                            SpawnUtils.HackActuatorsOnNewVessels(BDArmorySettings.RUNWAY_PROJECT);

                            foreach (var vessel in FlightGlobals.Vessels)
                            {
                                if (vessel == null || !vessel.loaded) continue;
                                SpawnUtils.HackActuators(vessel, BDArmorySettings.RUNWAY_PROJECT);
                            }
                        }
                        if (HighLogic.LoadedSceneIsEditor && EditorLogic.fetch.ship is not null) GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
                    }
                    if (BDArmorySettings.RUNWAY_PROJECT)
                    {
                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_RunwayProjectRound")}: ({(BDArmorySettings.RUNWAY_PROJECT_ROUND > 10 ? $"S{(BDArmorySettings.RUNWAY_PROJECT_ROUND - 1) / 10}R{(BDArmorySettings.RUNWAY_PROJECT_ROUND - 1) % 10 + 1}" : "—")})", leftLabel); // RWP round
                        BDArmorySettings.RUNWAY_PROJECT_ROUND = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.RUNWAY_PROJECT_ROUND, 10f, 60f));

                        if (BDArmorySettings.RUNWAY_PROJECT_ROUND == 41)
                        {
                            GUI.Label(SLeftSliderRect(++line, 1f), $"{StringUtils.Localize("#LOC_BDArmory_settings_FireRateCenter")}:  ({BDArmorySettings.FIRE_RATE_OVERRIDE_CENTER})", leftLabel);//Fire Rate Override Center
                            BDArmorySettings.FIRE_RATE_OVERRIDE_CENTER = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.FIRE_RATE_OVERRIDE_CENTER, 10f, 300f) / 5f) * 5f;
                            GUI.Label(SLeftSliderRect(++line, 1f), $"{StringUtils.Localize("#LOC_BDArmory_settings_FireRateSpread")}:  ({BDArmorySettings.FIRE_RATE_OVERRIDE_SPREAD})", leftLabel);//Fire Rate Override Spread
                            BDArmorySettings.FIRE_RATE_OVERRIDE_SPREAD = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.FIRE_RATE_OVERRIDE_SPREAD, 0f, 50f));
                            GUI.Label(SLeftSliderRect(++line, 1f), $"{StringUtils.Localize("#LOC_BDArmory_settings_FireRateBias")}:  ({BDArmorySettings.FIRE_RATE_OVERRIDE_BIAS * BDArmorySettings.FIRE_RATE_OVERRIDE_BIAS:G2})", leftLabel);//Fire Rate Override Bias
                            BDArmorySettings.FIRE_RATE_OVERRIDE_BIAS = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.FIRE_RATE_OVERRIDE_BIAS, 0f, 1f) * 50f) / 50f;
                            GUI.Label(SLeftSliderRect(++line, 1f), $"{StringUtils.Localize("#LOC_BDArmory_settings_FireRateHitMultiplier")}:  ({BDArmorySettings.FIRE_RATE_OVERRIDE_HIT_MULTIPLIER})", leftLabel);//Fire Rate Hit Multiplier
                            BDArmorySettings.FIRE_RATE_OVERRIDE_HIT_MULTIPLIER = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.FIRE_RATE_OVERRIDE_HIT_MULTIPLIER, 1f, 4f) * 10f) / 10f;
                        }
                        if (BDArmorySettings.RUNWAY_PROJECT_ROUND == 55 && !BDArmorySettings.WAYPOINTS_MODE)
                        {
                            BDArmorySettings.WAYPOINTS_MODE = true;
                        }
                        // if (BDArmorySettings.RUNWAY_PROJECT_ROUND == 46) BDArmorySettings.NO_ENGINES = true;
                        if (CheatCodeGUI != (CheatCodeGUI = GUI.TextField(SLeftRect(++line, 1, true), CheatCodeGUI, inputFieldStyle))) //if we need super-secret stuff
                        {
                            if (CheatCodeGUI == "ZombieMode")
                            {
                                BDArmorySettings.ZOMBIE_MODE = !BDArmorySettings.ZOMBIE_MODE; //sticking this here until we figure out a better home for it
                                CheatCodeGUI = "";
                            }
                            else if (CheatCodeGUI == "DiscoInferno")
                            {
                                BDArmorySettings.DISCO_MODE = !BDArmorySettings.DISCO_MODE;
                                CheatCodeGUI = "";
                            }
                            else if (CheatCodeGUI == "NoEngines")
                            {
                                BDArmorySettings.NO_ENGINES = !BDArmorySettings.NO_ENGINES;
                                CheatCodeGUI = "";
                            }
                            else if (CheatCodeGUI == "HallOfShame")
                            {
                                BDArmorySettings.ENABLE_HOS = !BDArmorySettings.ENABLE_HOS;
                                CheatCodeGUI = "";
                            }
                            else if (CheatCodeGUI.ToLower() == "altitudehack") //until we figure out where to put this
                            {
                                BDArmorySettings.ALTITUDE_HACKS = !BDArmorySettings.ALTITUDE_HACKS;
                                CheatCodeGUI = "";
                            }
                        }
                        //BDArmorySettings.ZOMBIE_MODE = GUI.Toggle(SLeftRect(++line), BDArmorySettings.ZOMBIE_MODE, StringUtils.Localize("#LOC_BDArmory_settings_ZombieMode"));
                        if (BDArmorySettings.ZOMBIE_MODE)
                        {
                            GUI.Label(SLeftSliderRect(++line, 1f), $"{StringUtils.Localize("#LOC_BDArmory_settings_zombieDmgMod")}:  ({BDArmorySettings.ZOMBIE_DMG_MULT})", leftLabel);//"S4R2 Non-headshot Dmg Mult"

                            //if (BDArmorySettings.RUNWAY_PROJECT_ROUND == -1) // FIXME Set when the round is actually run! Also check for other "RUNWAY_PROJECT_ROUND == -1" checks.
                            //{
                            //    GUI.Label(SLeftSliderRect(++line, 1f), $"{StringUtils.Localize("#LOC_BDArmory_settings_zombieDmgMod")}:  ({BDArmorySettings.ZOMBIE_DMG_MULT})", leftLabel);//"Zombie Non-headshot Dmg Mult"

                            BDArmorySettings.ZOMBIE_DMG_MULT = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.ZOMBIE_DMG_MULT, 0.05f, 0.95f) * 100f) / 100f;
                            if (BDArmorySettings.BATTLEDAMAGE)
                            {
                                BDArmorySettings.ALLOW_ZOMBIE_BD = GUI.Toggle(SLeftRect(++line, 1), BDArmorySettings.ALLOW_ZOMBIE_BD, StringUtils.Localize("#LOC_BDArmory_Settings_BD_ZombieMode"));//"Allow battle Damage"
                            }
                        }
                        if (BDArmorySettings.ENABLE_HOS)
                        {
                            GUI.Label(SLeftRect(++line), StringUtils.Localize("--Hall Of Shame Enabled--"));//"Competition Distance"
                            HoSString = GUI.TextField(SLeftRect(++line, 1, true), HoSString, inputFieldStyle);
                            if (!string.IsNullOrEmpty(HoSString))
                            {
                                enteredHoS = GUI.Toggle(SRightRect(line), enteredHoS, StringUtils.Localize("Enter to Hall of Shame"));
                                {
                                    if (enteredHoS)
                                    {
                                        if (HoSString == "Clear()")
                                        {
                                            BDArmorySettings.HALL_OF_SHAME_LIST.Clear();
                                        }
                                        else
                                        {
                                            if (!BDArmorySettings.HALL_OF_SHAME_LIST.Contains(HoSString))
                                            {
                                                BDArmorySettings.HALL_OF_SHAME_LIST.Add(HoSString);
                                            }
                                            else
                                            {
                                                BDArmorySettings.HALL_OF_SHAME_LIST.Remove(HoSString);
                                            }
                                        }
                                        HoSString = "";
                                        enteredHoS = false;
                                    }
                                }
                            }
                            GUI.Label(SLeftRect(++line), StringUtils.Localize("--Select Punishment--"));
                            GUI.Label(SLeftSliderRect(++line, 2f), $"{StringUtils.Localize("Fire")}:  ({(float)Math.Round(BDArmorySettings.HOS_FIRE, 1)} Burn Rate)", leftLabel);
                            BDArmorySettings.HOS_FIRE = (GUI.HorizontalSlider(SRightSliderRect(line), (float)Math.Round(BDArmorySettings.HOS_FIRE, 1), 0, 10));
                            GUI.Label(SLeftSliderRect(++line, 2f), $"{StringUtils.Localize("Mass")}:  ({(float)Math.Round(BDArmorySettings.HOS_MASS, 1)} ton deadweight)", leftLabel);
                            BDArmorySettings.HOS_MASS = (GUI.HorizontalSlider(SRightSliderRect(line), (float)Math.Round(BDArmorySettings.HOS_MASS, 1), -10, 10));
                            GUI.Label(SLeftSliderRect(++line, 2f), $"{StringUtils.Localize("Frailty")}:  ({(float)Math.Round(BDArmorySettings.HOS_DMG, 2) * 100}%) Dmg taken", leftLabel);
                            BDArmorySettings.HOS_DMG = (GUI.HorizontalSlider(SRightSliderRect(line), (float)Math.Round(BDArmorySettings.HOS_DMG, 2), 0.1f, 10));
                            GUI.Label(SLeftSliderRect(++line, 2f), $"{StringUtils.Localize("Thrust")}:  ({(float)Math.Round(BDArmorySettings.HOS_THRUST, 1)}%) Engine Thrust", leftLabel);
                            BDArmorySettings.HOS_THRUST = (GUI.HorizontalSlider(SRightSliderRect(line), (float)Math.Round(BDArmorySettings.HOS_THRUST, 1), 0, 200));
                            GUI.Label(SLeftRect(++line), StringUtils.Localize("--Shame badge--"));
                            HoSTag = GUI.TextField(SLeftRect(++line, 1, true), HoSTag, inputFieldStyle);
                            BDArmorySettings.HOS_BADGE = HoSTag;
                        }
                        else
                        {
                            BDArmorySettings.HOS_FIRE = 0;
                            BDArmorySettings.HOS_MASS = 0;
                            BDArmorySettings.HOS_DMG = 100;
                            BDArmorySettings.HOS_THRUST = 100;
                            //partloss = false; //- would need special module, but could also be a mutator mode
                            //timebomb = false //same
                            //might be more elegant to simply have this use Mutator framework and load the HoS craft with a select mutator(s) instead... Something to look into later, maybe, but ideally this shouldn't need to be used in the first place.
                        }
                    }
                }

                line += 0.5f;
            }

            if (BDArmorySettings.BATTLEDAMAGE)
            {
                if (GUI.Button(SLineRect(++line), $"{(BDArmorySettings.BATTLEDAMAGE_TOGGLE ? StringUtils.Localize("#LOC_BDArmory_Generic_Hide") : StringUtils.Localize("#LOC_BDArmory_Generic_Show"))} {StringUtils.Localize("#LOC_BDArmory_Settings_BDSettingsToggle")}"))//Show/hide Battle Damage settings.
                {
                    BDArmorySettings.BATTLEDAMAGE_TOGGLE = !BDArmorySettings.BATTLEDAMAGE_TOGGLE;
                }
                if (BDArmorySettings.BATTLEDAMAGE_TOGGLE)
                {
                    line += 0.2f;

                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_BD_Proc")}: ({BDArmorySettings.BD_DAMAGE_CHANCE}%)", leftLabel); //Proc Chance Frequency
                    BDArmorySettings.BD_DAMAGE_CHANCE = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.BD_DAMAGE_CHANCE, 0f, 100));

                    BDArmorySettings.BD_PROPULSION = GUI.Toggle(SLeftRect(++line), BDArmorySettings.BD_PROPULSION, StringUtils.Localize("#LOC_BDArmory_Settings_BD_Engines"));//"Propulsion Systems Damage"
                    if (BDArmorySettings.BD_PROPULSION && BDArmorySettings.ADVANCED_USER_SETTINGS)
                    {
                        GUI.Label(SLeftSliderRect(++line, 1f), $"{StringUtils.Localize("#LOC_BDArmory_Settings_BD_Prop_Dmg_Mult")}:  ({BDArmorySettings.BD_PROP_DAM_RATE}x)", leftLabel); //Propulsion Damage Multiplier
                        BDArmorySettings.BD_PROP_DAM_RATE = (GUI.HorizontalSlider(SRightSliderRect(line), (float)Math.Round(BDArmorySettings.BD_PROP_DAM_RATE, 1), 0, 2));
                        GUI.Label(SLeftSliderRect(++line, 1f), $"{StringUtils.Localize("#LOC_BDArmory_Settings_BD_Prop_floor")}:  ({BDArmorySettings.BD_PROP_FLOOR}%)", leftLabel); //Min Engine Thrust
                        BDArmorySettings.BD_PROP_FLOOR = (GUI.HorizontalSlider(SRightSliderRect(line), (float)Math.Round(BDArmorySettings.BD_PROP_FLOOR, 1), 0, 100));

                        GUI.Label(SLeftSliderRect(++line, 1f), $"{StringUtils.Localize("#LOC_BDArmory_Settings_BD_Prop_flameout")}:  ({BDArmorySettings.BD_PROP_FLAMEOUT}% HP)", leftLabel); //Engine Flameout
                        BDArmorySettings.BD_PROP_FLAMEOUT = (GUI.HorizontalSlider(SRightSliderRect(line), (float)Math.Round(BDArmorySettings.BD_PROP_FLAMEOUT, 0), 0, 95));
                        BDArmorySettings.BD_INTAKES = GUI.Toggle(SLeftRect(++line, 1f), BDArmorySettings.BD_INTAKES, StringUtils.Localize("#LOC_BDArmory_Settings_BD_Intakes"));//"Intake Damage"
                        BDArmorySettings.BD_GIMBALS = GUI.Toggle(SRightRect(line, 1f), BDArmorySettings.BD_GIMBALS, StringUtils.Localize("#LOC_BDArmory_Settings_BD_Gimbals"));//"Gimbal Damage"
                    }

                    BDArmorySettings.BD_AEROPARTS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.BD_AEROPARTS, StringUtils.Localize("#LOC_BDArmory_Settings_BD_Aero"));//"Flight Systems Damage"
                    if (BDArmorySettings.BD_AEROPARTS && BDArmorySettings.ADVANCED_USER_SETTINGS)
                    {
                        GUI.Label(SLeftSliderRect(++line, 1f), $"{StringUtils.Localize("#LOC_BDArmory_Settings_BD_Aero_Dmg_Mult")}:  ({BDArmorySettings.BD_LIFT_LOSS_RATE}x)", leftLabel); //Wing Damage Magnitude
                        BDArmorySettings.BD_LIFT_LOSS_RATE = (GUI.HorizontalSlider(SRightSliderRect(line), (float)Math.Round(BDArmorySettings.BD_LIFT_LOSS_RATE, 1), 0, 5));
                        BDArmorySettings.BD_CTRL_SRF = GUI.Toggle(SLeftRect(++line, 1f), BDArmorySettings.BD_CTRL_SRF, StringUtils.Localize("#LOC_BDArmory_Settings_BD_CtrlSrf"));//"Ctrl Surface Damage"
                    }

                    BDArmorySettings.BD_COCKPITS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.BD_COCKPITS, StringUtils.Localize("#LOC_BDArmory_Settings_BD_Command"));//"Command & Control Damage"
                    if (BDArmorySettings.BD_COCKPITS && BDArmorySettings.ADVANCED_USER_SETTINGS)
                    {
                        BDArmorySettings.BD_PILOT_KILLS = GUI.Toggle(SLeftRect(++line, 1f), BDArmorySettings.BD_PILOT_KILLS, StringUtils.Localize("#LOC_BDArmory_Settings_BD_PilotKill"));//"Crew Fatalities"
                    }

                    BDArmorySettings.BD_TANKS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.BD_TANKS, StringUtils.Localize("#LOC_BDArmory_Settings_BD_Tanks"));//"FuelTank Damage"
                    if (BDArmorySettings.BD_TANKS && BDArmorySettings.ADVANCED_USER_SETTINGS)
                    {
                        GUI.Label(SLeftSliderRect(++line, 1f), $"{StringUtils.Localize("#LOC_BDArmory_Settings_BD_Leak_Time")}:  ({BDArmorySettings.BD_TANK_LEAK_TIME}s)", leftLabel); // Leak Duration
                        BDArmorySettings.BD_TANK_LEAK_TIME = Mathf.Round((GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.BD_TANK_LEAK_TIME, 0, 100)));
                        GUI.Label(SLeftSliderRect(++line, 1f), $"{StringUtils.Localize("#LOC_BDArmory_Settings_BD_Leak_Rate")}:  ({BDArmorySettings.BD_TANK_LEAK_RATE}x)", leftLabel); //Leak magnitude
                        BDArmorySettings.BD_TANK_LEAK_RATE = (GUI.HorizontalSlider(SRightSliderRect(line), (float)Math.Round(BDArmorySettings.BD_TANK_LEAK_RATE, 1), 0, 5));
                    }
                    BDArmorySettings.BD_SUBSYSTEMS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.BD_SUBSYSTEMS, StringUtils.Localize("#LOC_BDArmory_Settings_BD_SubSystems"));//"Subsystem Damage"
                    BDArmorySettings.BD_PART_STRENGTH = GUI.Toggle(SLeftRect(++line), BDArmorySettings.BD_PART_STRENGTH, StringUtils.Localize("#LOC_BDArmory_Settings_BD_JointStrength"));//"Structural Damage"

                    BDArmorySettings.BD_AMMOBINS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.BD_AMMOBINS, StringUtils.Localize("#LOC_BDArmory_Settings_BD_Ammo"));//"Ammo Explosions"
                    if (BDArmorySettings.BD_AMMOBINS && BDArmorySettings.ADVANCED_USER_SETTINGS)
                    {
                        BDArmorySettings.BD_VOLATILE_AMMO = GUI.Toggle(SLineRect(++line, 1f), BDArmorySettings.BD_VOLATILE_AMMO, StringUtils.Localize("#LOC_BDArmory_Settings_BD_Volatile_Ammo"));//"Ammo Bins Explode When Destroyed"
                    }

                    BDArmorySettings.BD_FIRES_ENABLED = GUI.Toggle(SLeftRect(++line), BDArmorySettings.BD_FIRES_ENABLED, StringUtils.Localize("#LOC_BDArmory_Settings_BD_Fires"));//"Fires"
                    if (BDArmorySettings.BD_FIRES_ENABLED && BDArmorySettings.ADVANCED_USER_SETTINGS)
                    {
                        BDArmorySettings.BD_FIRE_DOT = GUI.Toggle(SLeftRect(++line, 1f), BDArmorySettings.BD_FIRE_DOT, StringUtils.Localize("#LOC_BDArmory_Settings_BD_DoT"));//"Fire Damage"
                        GUI.Label(SLeftSliderRect(++line, 1f), $"{StringUtils.Localize("#LOC_BDArmory_Settings_BD_Fire_Dmg")}:  ({BDArmorySettings.BD_FIRE_DAMAGE}/s)", leftLabel); // "Fire Damage magnitude"
                        BDArmorySettings.BD_FIRE_DAMAGE = Mathf.Round((GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.BD_FIRE_DAMAGE, 0f, 20)));
                        BDArmorySettings.BD_FIRE_FUELEX = GUI.Toggle(SLeftRect(++line, 1f), BDArmorySettings.BD_FIRE_FUELEX, StringUtils.Localize("#LOC_BDArmory_Settings_BD_FuelFireEX"));//"Fueltank Explosions
                        BDArmorySettings.BD_FIRE_HEATDMG = GUI.Toggle(SLeftRect(++line, 1f), BDArmorySettings.BD_FIRE_HEATDMG, StringUtils.Localize("#LOC_BDArmory_Settings_BD_FireHeat"));//"Fires add Heat
                    }

                    line += 0.5f;
                }
            }

            if (GUI.Button(SLineRect(++line), (BDArmorySettings.RADAR_SETTINGS_TOGGLE ? StringUtils.Localize("#LOC_BDArmory_Generic_Hide") : StringUtils.Localize("#LOC_BDArmory_Generic_Show")) + " " + StringUtils.Localize("#LOC_BDArmory_Settings_RadarSettingsToggle"))) // Show/hide Radar settings.
            {
                BDArmorySettings.RADAR_SETTINGS_TOGGLE = !BDArmorySettings.RADAR_SETTINGS_TOGGLE;
            }
            if (BDArmorySettings.RADAR_SETTINGS_TOGGLE)
            {
                line += 0.2f;

                GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_RWRWindowScale")}: {(BDArmorySettings.RWR_WINDOW_SCALE * 100):0} %", leftLabel); // RWR Window Scale
                float rwrScale = BDArmorySettings.RWR_WINDOW_SCALE;
                rwrScale = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), rwrScale, BDArmorySettings.RWR_WINDOW_SCALE_MIN, BDArmorySettings.RWR_WINDOW_SCALE_MAX) * 100.0f) * 0.01f;
                if (rwrScale.ToString(CultureInfo.InvariantCulture) != BDArmorySettings.RWR_WINDOW_SCALE.ToString(CultureInfo.InvariantCulture))
                {
                    ResizeRwrWindow(rwrScale);
                }

                GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_RadarWindowScale")}: {(BDArmorySettings.RADAR_WINDOW_SCALE * 100):0} %", leftLabel); // Radar Window Scale
                float radarScale = BDArmorySettings.RADAR_WINDOW_SCALE;
                radarScale = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), radarScale, BDArmorySettings.RADAR_WINDOW_SCALE_MIN, BDArmorySettings.RADAR_WINDOW_SCALE_MAX) * 100.0f) * 0.01f;
                if (radarScale.ToString(CultureInfo.InvariantCulture) != BDArmorySettings.RADAR_WINDOW_SCALE.ToString(CultureInfo.InvariantCulture))
                {
                    ResizeRadarWindow(radarScale);
                }

                GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_TargetWindowScale")}: {(BDArmorySettings.TARGET_WINDOW_SCALE * 100):0} %", leftLabel); // Target Window Scale
                float targetScale = BDArmorySettings.TARGET_WINDOW_SCALE;
                targetScale = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), targetScale, BDArmorySettings.TARGET_WINDOW_SCALE_MIN, BDArmorySettings.TARGET_WINDOW_SCALE_MAX) * 100.0f) * 0.01f;
                if (targetScale.ToString(CultureInfo.InvariantCulture) != BDArmorySettings.TARGET_WINDOW_SCALE.ToString(CultureInfo.InvariantCulture))
                {
                    ResizeTargetWindow(targetScale);
                }

                GUI.Label(SLeftRect(++line), StringUtils.Localize("#LOC_BDArmory_Settings_TargetWindowInvertMouse"), leftLabel);
                BDArmorySettings.TARGET_WINDOW_INVERT_MOUSE_X = GUI.Toggle(SEighthRect(line, 5), BDArmorySettings.TARGET_WINDOW_INVERT_MOUSE_X, "X");
                BDArmorySettings.TARGET_WINDOW_INVERT_MOUSE_Y = GUI.Toggle(SEighthRect(line, 6), BDArmorySettings.TARGET_WINDOW_INVERT_MOUSE_Y, "Y");
                BDArmorySettings.LOGARITHMIC_RADAR_DISPLAY = GUI.Toggle(SLeftRect(++line), BDArmorySettings.LOGARITHMIC_RADAR_DISPLAY, StringUtils.Localize("#LOC_BDArmory_Settings_LogarithmicRWRDisplay")); //"Logarithmic RWR Display"

                line += 0.5f;
            }

            if (GUI.Button(SLineRect(++line), (BDArmorySettings.OTHER_SETTINGS_TOGGLE ? StringUtils.Localize("#LOC_BDArmory_Generic_Hide") : StringUtils.Localize("#LOC_BDArmory_Generic_Show")) + " " + StringUtils.Localize("#LOC_BDArmory_Settings_OtherSettingsToggle"))) // Show/hide Other settings.
            {
                BDArmorySettings.OTHER_SETTINGS_TOGGLE = !BDArmorySettings.OTHER_SETTINGS_TOGGLE;
            }
            if (BDArmorySettings.OTHER_SETTINGS_TOGGLE)
            {
                line += 0.2f;

                GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_TriggerHold")}: {BDArmorySettings.TRIGGER_HOLD_TIME: 0} s", leftLabel);//Trigger Hold
                BDArmorySettings.TRIGGER_HOLD_TIME = GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.TRIGGER_HOLD_TIME, 0.02f, 1f);

                GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_UIVolume")}: {(BDArmorySettings.BDARMORY_UI_VOLUME * 100):0}", leftLabel);//UI Volume
                float uiVol = BDArmorySettings.BDARMORY_UI_VOLUME;
                uiVol = GUI.HorizontalSlider(SRightSliderRect(line), uiVol, 0f, 1f);
                if (uiVol != BDArmorySettings.BDARMORY_UI_VOLUME && OnVolumeChange != null)
                {
                    OnVolumeChange();
                }
                BDArmorySettings.BDARMORY_UI_VOLUME = uiVol;

                GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_WeaponVolume")}: {(BDArmorySettings.BDARMORY_WEAPONS_VOLUME * 100):0}", leftLabel);//Weapon Volume
                float weaponVol = BDArmorySettings.BDARMORY_WEAPONS_VOLUME;
                weaponVol = GUI.HorizontalSlider(SRightSliderRect(line), weaponVol, 0f, 1f);
                if (uiVol != BDArmorySettings.BDARMORY_WEAPONS_VOLUME && OnVolumeChange != null)
                {
                    OnVolumeChange();
                }
                BDArmorySettings.BDARMORY_WEAPONS_VOLUME = weaponVol;

                if (BDArmorySettings.ADVANCED_USER_SETTINGS)
                {
                    BDArmorySettings.TRACE_VESSELS_DURING_COMPETITIONS = GUI.Toggle(new Rect(settingsMargin, ++line * settingsLineHeight, 2f * (settingsWidth - 2f * settingsMargin) / 3f, settingsLineHeight), BDArmorySettings.TRACE_VESSELS_DURING_COMPETITIONS, StringUtils.Localize("#LOC_BDArmory_Settings_TraceVessels"));// Trace Vessels (custom 2/3 width)
                    if (LoadedVesselSwitcher.Instance != null)
                    {
                        if (GUI.Button(SLineThirdRect(line, 2), LoadedVesselSwitcher.Instance.vesselTraceEnabled ? StringUtils.Localize("#LOC_BDArmory_Settings_TraceVesselsManualStop") : StringUtils.Localize("#LOC_BDArmory_Settings_TraceVesselsManualStart")))
                        {
                            if (LoadedVesselSwitcher.Instance.vesselTraceEnabled)
                            { LoadedVesselSwitcher.Instance.StopVesselTracing(); }
                            else
                            { LoadedVesselSwitcher.Instance.StartVesselTracing(); }
                        }
                    }
                }

                line += 0.5f;
            }

            if (GUI.Button(SLineRect(++line), $"{(BDArmorySettings.COMPETITION_SETTINGS_TOGGLE ? StringUtils.Localize("#LOC_BDArmory_Generic_Hide") : StringUtils.Localize("#LOC_BDArmory_Generic_Show"))} {StringUtils.Localize("#LOC_BDArmory_Settings_CompSettingsToggle")}"))//Show/hide Competition settings.
            {
                BDArmorySettings.COMPETITION_SETTINGS_TOGGLE = !BDArmorySettings.COMPETITION_SETTINGS_TOGGLE;
            }
            if (BDArmorySettings.COMPETITION_SETTINGS_TOGGLE)
            {
                line += 0.2f;

                BDArmorySettings.COMPETITION_CLOSE_SETTINGS_ON_COMPETITION_START = GUI.Toggle(SLineRect(++line), BDArmorySettings.COMPETITION_CLOSE_SETTINGS_ON_COMPETITION_START, StringUtils.Localize("#LOC_BDArmory_Settings_CompetitionCloseSettingsOnCompetitionStart"));

                BDArmorySettings.COMPETITION_START_DESPITE_FAILURES = GUI.Toggle(SLineRect(++line), BDArmorySettings.COMPETITION_START_DESPITE_FAILURES, StringUtils.Localize("#LOC_BDArmory_Settings_CompetitionStartDespiteFailures"));

                if (BDArmorySettings.ADVANCED_USER_SETTINGS)
                {
                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_DebrisCleanUpDelay")}:  ({BDArmorySettings.DEBRIS_CLEANUP_DELAY}s)", leftLabel); // Debris Clean-up delay
                    BDArmorySettings.DEBRIS_CLEANUP_DELAY = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.DEBRIS_CLEANUP_DELAY, 1f, 60f));

                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_CompetitionNonCompetitorRemovalDelay")}:  ({(BDArmorySettings.COMPETITION_NONCOMPETITOR_REMOVAL_DELAY > 60 ? "Off" : BDArmorySettings.COMPETITION_NONCOMPETITOR_REMOVAL_DELAY + "s")})", leftLabel); // Non-competitor removal frequency
                    BDArmorySettings.COMPETITION_NONCOMPETITOR_REMOVAL_DELAY = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.COMPETITION_NONCOMPETITOR_REMOVAL_DELAY, 1f, 61f));
                }
                GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_CompetitionDuration")}: ({(BDArmorySettings.COMPETITION_DURATION > 0 ? BDArmorySettings.COMPETITION_DURATION + (BDArmorySettings.COMPETITION_DURATION > 1 ? " mins" : " min") : "Unlimited")})", leftLabel);
                BDArmorySettings.COMPETITION_DURATION = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.COMPETITION_DURATION, 0f, 15f));
                if (BDArmorySettings.ADVANCED_USER_SETTINGS)
                {
                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_CompetitionInitialGracePeriod")}: ({BDArmorySettings.COMPETITION_INITIAL_GRACE_PERIOD}s)", leftLabel);
                    BDArmorySettings.COMPETITION_INITIAL_GRACE_PERIOD = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.COMPETITION_INITIAL_GRACE_PERIOD, 0f, 60f));
                }
                GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_CompetitionFinalGracePeriod")}: ({(BDArmorySettings.COMPETITION_FINAL_GRACE_PERIOD > 60 ? "Inf" : BDArmorySettings.COMPETITION_FINAL_GRACE_PERIOD + "s")})", leftLabel);
                BDArmorySettings.COMPETITION_FINAL_GRACE_PERIOD = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.COMPETITION_FINAL_GRACE_PERIOD, 0f, 61f));

                { // Auto Start Competition NOW Delay
                    string startNowAfter;
                    if (BDArmorySettings.COMPETITION_START_NOW_AFTER > 10)
                    {
                        startNowAfter = "Off";
                    }
                    else if (BDArmorySettings.COMPETITION_START_NOW_AFTER > 5)
                    {
                        startNowAfter = $"{BDArmorySettings.COMPETITION_START_NOW_AFTER - 5}mins";
                    }
                    else
                    {
                        startNowAfter = $"{BDArmorySettings.COMPETITION_START_NOW_AFTER * 10}s";
                    }
                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_CompetitionStartNowAfter")}: ({startNowAfter})", leftLabel);
                    BDArmorySettings.COMPETITION_START_NOW_AFTER = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.COMPETITION_START_NOW_AFTER, 0f, 11f));
                }

                GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_CompetitionKillTimer")}: (" + (BDArmorySettings.COMPETITION_KILL_TIMER > 0 ? (BDArmorySettings.COMPETITION_KILL_TIMER + "s") : "Off") + ")", leftLabel); // FIXME the toggle and this slider could be merged
                BDArmorySettings.COMPETITION_KILL_TIMER = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.COMPETITION_KILL_TIMER, 0, 60f));

                GUI.Label(SLeftRect(++line), StringUtils.Localize("#LOC_BDArmory_Settings_CompetitionIntraTeamSeparation")); // Intra-team separation.
                var intraTeamSepRect = SRightRect(line, 1, true);
                compIntraTeamSeparationBase = GUI.TextField(new Rect(intraTeamSepRect.x, intraTeamSepRect.y, intraTeamSepRect.width / 4 + 2, intraTeamSepRect.height), compIntraTeamSeparationBase, 6, inputFieldStyle);
                GUI.Label(new Rect(intraTeamSepRect.x + intraTeamSepRect.width / 4 + 2, intraTeamSepRect.y, 15, intraTeamSepRect.height), " + ");
                compIntraTeamSeparationPerMember = GUI.TextField(new Rect(intraTeamSepRect.x + intraTeamSepRect.width / 4 + 17, intraTeamSepRect.y, intraTeamSepRect.width / 4 + 2, intraTeamSepRect.height), compIntraTeamSeparationPerMember, 6, inputFieldStyle);
                GUI.Label(new Rect(intraTeamSepRect.x + intraTeamSepRect.width / 2 + 25, intraTeamSepRect.y, intraTeamSepRect.width / 2 - 25, intraTeamSepRect.height), StringUtils.Localize("#LOC_BDArmory_Settings_CompetitionIntraTeamSeparationPerMember"));
                if (Single.TryParse(compIntraTeamSeparationBase, out float cIntraBase) && BDArmorySettings.COMPETITION_INTRA_TEAM_SEPARATION_BASE != cIntraBase)
                {
                    cIntraBase = Mathf.Round(cIntraBase); BDArmorySettings.COMPETITION_INTRA_TEAM_SEPARATION_BASE = cIntraBase;
                    if (cIntraBase != 0) compIntraTeamSeparationBase = cIntraBase.ToString();
                }
                if (Single.TryParse(compIntraTeamSeparationPerMember, out float cIntraPerMember) && BDArmorySettings.COMPETITION_INTRA_TEAM_SEPARATION_PER_MEMBER != cIntraPerMember)
                {
                    cIntraPerMember = Mathf.Round(cIntraPerMember); BDArmorySettings.COMPETITION_INTRA_TEAM_SEPARATION_PER_MEMBER = cIntraPerMember;
                    if (cIntraPerMember != 0) compIntraTeamSeparationPerMember = cIntraPerMember.ToString();
                }

                GUI.Label(SLeftRect(++line), StringUtils.Localize("#LOC_BDArmory_Settings_CompetitionDistance"));//"Competition Distance"
                compDistGui = GUI.TextField(SRightRect(line, 1, true), compDistGui, inputFieldStyle);
                if (Single.TryParse(compDistGui, out float cDist) && BDArmorySettings.COMPETITION_DISTANCE != cDist)
                {
                    cDist = Mathf.Round(cDist); BDArmorySettings.COMPETITION_DISTANCE = cDist;
                    if (cDist != 0) compDistGui = cDist.ToString();
                }

                line += 0.2f;
                if (GUI.Button(SLineRect(++line, 1, true), (BDArmorySettings.GM_SETTINGS_TOGGLE ? StringUtils.Localize("#LOC_BDArmory_Generic_Hide") : StringUtils.Localize("#LOC_BDArmory_Generic_Show")) + " " + StringUtils.Localize("#LOC_BDArmory_Settings_GMSettingsToggle")))//Show/hide slider settings.
                {
                    BDArmorySettings.GM_SETTINGS_TOGGLE = !BDArmorySettings.GM_SETTINGS_TOGGLE;
                }
                if (BDArmorySettings.GM_SETTINGS_TOGGLE)
                {
                    line += 0.2f;

                    { // Killer GM Max Altitude
                        string killerGMMaxAltitudeText;
                        if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH > 54f) killerGMMaxAltitudeText = "Never";
                        else if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH < 20f) killerGMMaxAltitudeText = Mathf.RoundToInt(BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH * 100f) + "m";
                        else if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH < 39f) killerGMMaxAltitudeText = Mathf.RoundToInt(BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH - 18f) + "km";
                        else killerGMMaxAltitudeText = Mathf.RoundToInt((BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH - 38f) * 5f + 20f) + "km";
                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_CompetitionAltitudeLimitHigh")}: ({killerGMMaxAltitudeText})", leftLabel);
                        BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH, 1f, 55f));
                    }
                    { // Killer GM Min Altitude
                        string killerGMMinAltitudeText;
                        if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW < -38f) killerGMMinAltitudeText = "Never"; // Never
                        else if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW < -28f) killerGMMinAltitudeText = Mathf.RoundToInt(BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW + 28f) + "km"; // -10km — -1km @ 1km
                        else if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW < -19f) killerGMMinAltitudeText = Mathf.RoundToInt((BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW + 19f) * 100f) + "m"; // -900m — -100m @ 100m
                        else if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW < 0f) killerGMMinAltitudeText = Mathf.RoundToInt(BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW * 5f) + "m"; // -95m — -5m  @ 5m
                        else if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW < 20f) killerGMMinAltitudeText = Mathf.RoundToInt(BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW * 100f) + "m"; // 0m — 1900m @ 100m
                        else if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW < 39f) killerGMMinAltitudeText = Mathf.RoundToInt(BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW - 18f) + "km"; // 2km — 20km @ 1km
                        else killerGMMinAltitudeText = Mathf.RoundToInt((BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW - 38f) * 5f + 20f) + "km"; // 25km — 50km @ 5km
                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_CompetitionAltitudeLimitLow")}: ({killerGMMinAltitudeText})", leftLabel);
                        BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW, -39f, 44f));
                    }
                    BDArmorySettings.COMPETITION_ALTITUDE__LIMIT_ASL = GUI.Toggle(SLineRect(++line), BDArmorySettings.COMPETITION_ALTITUDE__LIMIT_ASL, "Use Absolute Altitude?"); // StringUtils.Localize("#LLOC_BDArmory_Settings_CompetitionAltitudeLimitASL"));                    
                    if (BDArmorySettings.RUNWAY_PROJECT)
                    {
                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_CompetitionKillerGMGracePeriod")}: ({BDArmorySettings.COMPETITION_KILLER_GM_GRACE_PERIOD}s)", leftLabel);
                        BDArmorySettings.COMPETITION_KILLER_GM_GRACE_PERIOD = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.COMPETITION_KILLER_GM_GRACE_PERIOD / 10f, 0f, 18f)) * 10f;

                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_CompetitionKillerGMFrequency")}: ({(BDArmorySettings.COMPETITION_KILLER_GM_FREQUENCY > 60 ? "Off" : BDArmorySettings.COMPETITION_KILLER_GM_FREQUENCY + "s")}, {(BDACompetitionMode.Instance != null && BDACompetitionMode.Instance.killerGMenabled ? "on" : "off")})", leftLabel);
                        BDArmorySettings.COMPETITION_KILLER_GM_FREQUENCY = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.COMPETITION_KILLER_GM_FREQUENCY / 10f, 1, 6)) * 10f; // For now, don't control the killerGMEnabled flag (it's controlled by right clicking M).
                    }
                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_CompetitionWaypointTimeThreshold")}: ({(BDArmorySettings.COMPETITION_WAYPOINTS_GM_KILL_PERIOD > 0 ? $"{BDArmorySettings.COMPETITION_WAYPOINTS_GM_KILL_PERIOD:0}s" : "Off")})", leftLabel); // Waypoint threshold
                    BDArmorySettings.COMPETITION_WAYPOINTS_GM_KILL_PERIOD = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.COMPETITION_WAYPOINTS_GM_KILL_PERIOD, 0, 120), 5);

                    line += 0.2f;
                }

                if (BDArmorySettings.REMOTE_LOGGING_VISIBLE)
                {
                    if (GUI.Button(SLineRect(++line, 1, true), StringUtils.Localize(BDArmorySettings.REMOTE_LOGGING_ENABLED ? "#LOC_BDArmory_Disable" : "#LOC_BDArmory_Enable") + " " + StringUtils.Localize("#LOC_BDArmory_Settings_RemoteLogging")))
                    {
                        BDArmorySettings.REMOTE_LOGGING_ENABLED = !BDArmorySettings.REMOTE_LOGGING_ENABLED;
                    }
                    if (BDArmorySettings.REMOTE_LOGGING_ENABLED)
                    {
                        GUI.Label(SLeftRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_CompetitionID")}: ", leftLabel); // Competition hash.
                        BDArmorySettings.COMPETITION_HASH = GUI.TextField(SRightRect(line, 1, true), BDArmorySettings.COMPETITION_HASH, inputFieldStyle);
                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_RemoteInterheatDelay")}: ({BDArmorySettings.REMOTE_INTERHEAT_DELAY}s)", leftLabel); // Inter-heat delay
                        BDArmorySettings.REMOTE_INTERHEAT_DELAY = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.REMOTE_INTERHEAT_DELAY, 1f, 30f));
                    }
                }
                else
                {
                    BDArmorySettings.REMOTE_LOGGING_ENABLED = false;
                }

                line += 0.5f;
            }

            if (HighLogic.LoadedSceneIsFlight && BDACompetitionMode.Instance != null)
            {
                line += 0.5f;

                GUI.Label(SLineRect(++line), $"=== {StringUtils.Localize("#LOC_BDArmory_Settings_DogfightCompetition")} ===", centerLabel);//Dogfight Competition
                if (BDACompetitionMode.Instance.competitionIsActive)
                {
                    if (GUI.Button(SLineRect(++line), StringUtils.Localize("#LOC_BDArmory_Settings_StopCompetition"))) // Stop competition.
                    {
                        BDACompetitionMode.Instance.StopCompetition();
                    }
                }
                else if (BDACompetitionMode.Instance.competitionStarting)
                {
                    GUI.Label(SLineRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_CompetitionStarting")} ({compDistGui})");//Starting Competition...
                    if (GUI.Button(SLeftButtonRect(++line), StringUtils.Localize("#LOC_BDArmory_Generic_Cancel")))//"Cancel"
                    {
                        BDACompetitionMode.Instance.StopCompetition();
                    }
                    if (GUI.Button(SRightButtonRect(line), StringUtils.Localize("#LOC_BDArmory_Settings_StartCompetitionNow"))) // Start competition NOW button.
                    {
                        BDACompetitionMode.Instance.StartCompetitionNow();
                        if (BDArmorySettings.COMPETITION_CLOSE_SETTINGS_ON_COMPETITION_START) CloseSettingsWindow();
                    }
                }
                else
                {
                    if (BDArmorySettings.REMOTE_LOGGING_ENABLED)
                    {
                        if (GUI.Button(SLineRect(++line), StringUtils.Localize("#LOC_BDArmory_Settings_RemoteSync"))) // Run Via Remote Orchestration
                        {
                            string vesselPath = Path.Combine(KSPUtil.ApplicationRootPath, "AutoSpawn");
                            if (!System.IO.Directory.Exists(vesselPath))
                            {
                                System.IO.Directory.CreateDirectory(vesselPath);
                            }
                            BDAScoreService.Instance.Configure(vesselPath, BDArmorySettings.COMPETITION_HASH);
                            if (BDArmorySettings.COMPETITION_CLOSE_SETTINGS_ON_COMPETITION_START) CloseSettingsWindow();
                        }
                    }
                    else
                    {
                        string startCompetitionText = StringUtils.Localize("#LOC_BDArmory_Settings_StartCompetition");
                        if (BDArmorySettings.RUNWAY_PROJECT)
                        {
                            switch (BDArmorySettings.RUNWAY_PROJECT_ROUND)
                            {
                                case 33:
                                    startCompetitionText = StringUtils.Localize("#LOC_BDArmory_Settings_StartRapidDeployment");
                                    break;
                                case 44:
                                    startCompetitionText = StringUtils.Localize("#LOC_BDArmory_Settings_LowGravDeployment");
                                    break;
                                case 53: // FIXME temporary index, to be assigned later
                                    startCompetitionText = StringUtils.Localize("#LOC_BDArmory_Settings_StartOrbitalDeployment");
                                    break;
                            }
                        }
                        if (GUI.Button(SLineRect(++line), startCompetitionText))//"Start Competition"
                        {

                            BDArmorySettings.COMPETITION_DISTANCE = Mathf.Max(BDArmorySettings.COMPETITION_DISTANCE, 0);
                            compDistGui = BDArmorySettings.COMPETITION_DISTANCE.ToString();
                            if (BDArmorySettings.RUNWAY_PROJECT)
                            {
                                switch (BDArmorySettings.RUNWAY_PROJECT_ROUND)
                                {
                                    case 33:
                                        BDACompetitionMode.Instance.StartRapidDeployment(0);
                                        break;
                                    case 44:
                                        BDACompetitionMode.Instance.StartRapidDeployment(0);
                                        break;
                                    case 53:
                                        BDACompetitionMode.Instance.StartRapidDeployment(0);
                                        break;
                                    case 60: // FIXME temporary index, to be assigned later
                                        BDACompetitionMode.Instance.StartRapidDeployment(0);
                                        break;
                                    default:
                                        BDACompetitionMode.Instance.StartCompetitionMode(BDArmorySettings.COMPETITION_DISTANCE, BDArmorySettings.COMPETITION_START_DESPITE_FAILURES);
                                        break;
                                }
                            }
                            else
                                BDACompetitionMode.Instance.StartCompetitionMode(BDArmorySettings.COMPETITION_DISTANCE, BDArmorySettings.COMPETITION_START_DESPITE_FAILURES);
                            if (BDArmorySettings.COMPETITION_CLOSE_SETTINGS_ON_COMPETITION_START) CloseSettingsWindow();
                        }
                    }
                }
            }

            ++line;
            if (GUI.Button(SLineRect(++line), StringUtils.Localize("#LOC_BDArmory_Settings_EditInputs")))//"Edit Inputs"
            {
                editKeys = true;
            }
            line += 0.5f;
            if (!BDKeyBinder.current && GUI.Button(SLineRect(++line), StringUtils.Localize("#LOC_BDArmory_Generic_SaveandClose")))//"Save and Close"
            {
                SaveConfig();
                windowSettingsEnabled = false;
            }

            line += 1.5f; // Bottom internal margin
            settingsHeight = (line * settingsLineHeight);
            WindowRectSettings.height = settingsHeight;
            GUIUtils.RepositionWindow(ref WindowRectSettings);
            GUIUtils.UseMouseEventInRect(WindowRectSettings);
        }

        void CloseSettingsWindow()
        {
            SaveConfig();
            windowSettingsEnabled = false;
        }

        internal static void ResizeRwrWindow(float rwrScale)
        {
            BDArmorySettings.RWR_WINDOW_SCALE = rwrScale;
            RadarWarningReceiver.RwrDisplayRect = new Rect(0, 0, RadarWarningReceiver.RwrSize * rwrScale,
              RadarWarningReceiver.RwrSize * rwrScale);
            BDArmorySetup.WindowRectRwr =
              new Rect(BDArmorySetup.WindowRectRwr.x, BDArmorySetup.WindowRectRwr.y,
                RadarWarningReceiver.RwrDisplayRect.height + RadarWarningReceiver.BorderSize,
                RadarWarningReceiver.RwrDisplayRect.height + RadarWarningReceiver.BorderSize + RadarWarningReceiver.HeaderSize);
        }

        internal static void ResizeRadarWindow(float radarScale)
        {
            BDArmorySettings.RADAR_WINDOW_SCALE = radarScale;
            VesselRadarData.RadarDisplayRect =
              new Rect(VesselRadarData.BorderSize / 2, VesselRadarData.BorderSize / 2 + VesselRadarData.HeaderSize,
                VesselRadarData.RadarScreenSize * radarScale,
                VesselRadarData.RadarScreenSize * radarScale);
            WindowRectRadar =
              new Rect(WindowRectRadar.x, WindowRectRadar.y,
                VesselRadarData.RadarDisplayRect.height + VesselRadarData.BorderSize + VesselRadarData.ControlsWidth + VesselRadarData.Gap * 3,
                VesselRadarData.RadarDisplayRect.height + VesselRadarData.BorderSize + VesselRadarData.HeaderSize);
        }

        internal static void ResizeTargetWindow(float targetScale)
        {
            BDArmorySettings.TARGET_WINDOW_SCALE = targetScale;
            ModuleTargetingCamera.ResizeTargetWindow();
        }

        private static Vector2 _displayViewerPosition = Vector2.zero;

        void InputSettings()
        {
            float line = 0f;
            int inputID = 0;
            float origSettingsWidth = settingsWidth;
            float origSettingsHeight = settingsHeight;
            float origSettingsMargin = settingsMargin;

            settingsMargin = 10;
            settingsWidth = origSettingsWidth - 2 * settingsMargin;
            settingsHeight = origSettingsHeight - 100;
            Rect viewRect = new Rect(2, 20, settingsWidth + GUI.skin.verticalScrollbar.fixedWidth, settingsHeight);
            Rect scrollerRect = new Rect(0, 0, settingsWidth - GUI.skin.verticalScrollbar.fixedWidth - 1, inputFields != null ? (inputFields.Length + 13) * settingsLineHeight : settingsHeight);

            _displayViewerPosition = GUI.BeginScrollView(viewRect, _displayViewerPosition, scrollerRect, false, true);

            GUI.Label(SLineRect(line++), $"- {StringUtils.Localize("#LOC_BDArmory_InputSettings_GUI")} -", centerLabel); //GUI
            InputSettingsList("GUI_", ref inputID, ref line);
            ++line;

            GUI.Label(SLineRect(line++), $"- {StringUtils.Localize("#LOC_BDArmory_InputSettings_Weapons")} -", centerLabel);//Weapons
            InputSettingsList("WEAP_", ref inputID, ref line);
            ++line;

            GUI.Label(SLineRect(line++), $"- {StringUtils.Localize("#LOC_BDArmory_InputSettings_TargetingPod")} -", centerLabel);//Targeting Pod
            InputSettingsList("TGP_", ref inputID, ref line);
            ++line;

            GUI.Label(SLineRect(line++), $"- {StringUtils.Localize("#LOC_BDArmory_InputSettings_Radar")} -", centerLabel);//Radar
            InputSettingsList("RADAR_", ref inputID, ref line);
            ++line;

            GUI.Label(SLineRect(line++), $"- {StringUtils.Localize("#LOC_BDArmory_InputSettings_VesselSwitcher")} -", centerLabel);//Vessel Switcher
            InputSettingsList("VS_", ref inputID, ref line);
            ++line;

            GUI.Label(SLineRect(line++), $"- {StringUtils.Localize("#LOC_BDArmory_InputSettings_Tournament")} -", centerLabel);//Tournament
            InputSettingsList("TOURNAMENT_", ref inputID, ref line);
            ++line;

            GUI.Label(SLineRect(line++), $"- {StringUtils.Localize("#LOC_BDArmory_InputSettings_TimeScaling")} -", centerLabel);//Time Scaling
            InputSettingsList("TIME_", ref inputID, ref line);
            GUI.EndScrollView();

            line = settingsHeight / settingsLineHeight;
            line += 2;
            settingsWidth = origSettingsWidth;
            settingsMargin = origSettingsMargin;
            if (!BDKeyBinder.current && GUI.Button(SLineRect(line), StringUtils.Localize("#LOC_BDArmory_InputSettings_BackBtn")))//"Back"
            {
                editKeys = false;
            }

            settingsHeight = origSettingsHeight;
            WindowRectSettings.height = origSettingsHeight;
            GUIUtils.UseMouseEventInRect(WindowRectSettings);
        }

        void InputSettingsList(string prefix, ref int id, ref float line)
        {
            if (inputFields != null)
            {
                for (int i = 0; i < inputFields.Length; i++)
                {
                    string fieldName = inputFields[i].Name;
                    if (fieldName.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        InputSettingsLine(fieldName, id++, ref line);
                    }
                }
            }
        }

        void InputSettingsLine(string fieldName, int id, ref float line)
        {
            GUI.Box(SLineRect(line), GUIContent.none);
            string label = String.Empty;
            if (BDKeyBinder.IsRecordingID(id))
            {
                string recordedInput;
                if (BDKeyBinder.current.AcquireInputString(out recordedInput))
                {
                    BDInputInfo orig = (BDInputInfo)typeof(BDInputSettingsFields).GetField(fieldName).GetValue(null);
                    BDInputInfo recorded = new BDInputInfo(recordedInput, orig.description);
                    typeof(BDInputSettingsFields).GetField(fieldName).SetValue(null, recorded);
                }

                label = $"      {StringUtils.Localize("#LOC_BDArmory_InputSettings_recordedInput")}";//Press a key or button.
            }
            else
            {
                BDInputInfo inputInfo = new BDInputInfo();
                try
                {
                    inputInfo = (BDInputInfo)typeof(BDInputSettingsFields).GetField(fieldName).GetValue(null);
                }
                catch (NullReferenceException e)
                {
                    Debug.LogWarning("[BDArmory.BDArmorySetup]: Reflection failed to find input info of field: " + fieldName + ": " + e.Message);
                    editKeys = false;
                    return;
                }
                label = " " + inputInfo.description + " : " + inputInfo.inputString;

                if (GUI.Button(SSetKeyRect(line), StringUtils.Localize("#LOC_BDArmory_InputSettings_SetKey")))//"Set Key"
                {
                    BDKeyBinder.BindKey(id);
                }
                if (GUI.Button(SClearKeyRect(line), StringUtils.Localize("#LOC_BDArmory_InputSettings_Clear")))//"Clear"
                {
                    typeof(BDInputSettingsFields).GetField(fieldName)
                        .SetValue(null, new BDInputInfo(inputInfo.description));
                }
            }
            GUI.Label(SLineThirdRect(line, 0, 2), label);
            line++;
        }

        Rect SSetKeyRect(float line)
        {
            return new Rect(settingsMargin + (2 * (settingsWidth - 2 * settingsMargin) / 3), line * settingsLineHeight, (settingsWidth - (2 * settingsMargin)) / 6, settingsLineHeight);
        }

        Rect SClearKeyRect(float line)
        {
            return
                new Rect(
                    settingsMargin + (2 * (settingsWidth - 2 * settingsMargin) / 3) + (settingsWidth - 2 * settingsMargin) / 6,
                    line * settingsLineHeight, (settingsWidth - (2 * settingsMargin)) / 6, settingsLineHeight);
        }

        #endregion GUI

        void HideGameUI()
        {
            GAME_UI_ENABLED = false;
            BDACompetitionMode.Instance.UpdateGUIElements();
            UpdateCursorState();
        }

        void ShowGameUI()
        {
            GAME_UI_ENABLED = true;
            BDACompetitionMode.Instance.UpdateGUIElements();
            UpdateCursorState();
        }

        internal void OnDestroy()
        {
            if (saveWindowPosition)
            {
                BDAWindowSettingsField.Save();
            }
            if (windowSettingsEnabled || showVesselSpawnerGUI)
                SaveConfig();

            GameEvents.onHideUI.Remove(HideGameUI);
            GameEvents.onShowUI.Remove(ShowGameUI);
            GameEvents.onVesselGoOffRails.Remove(OnVesselGoOffRails);
            GameEvents.OnGameSettingsApplied.Remove(SaveVolumeSettings);
            GameEvents.onVesselChange.Remove(VesselChange);
        }

        void OnVesselGoOffRails(Vessel v)
        {
            if (BDArmorySettings.DEBUG_OTHER)
            {
                Debug.Log("[BDArmory.BDArmorySetup]: Loaded vessel: " + v.vesselName + ", Velocity: " + v.Velocity() + ", packed: " + v.packed);
                //v.SetWorldVelocity(Vector3d.zero);
            }
        }

        public void SaveVolumeSettings()
        {
            SeismicChargeFX.originalShipVolume = GameSettings.SHIP_VOLUME;
            SeismicChargeFX.originalMusicVolume = GameSettings.MUSIC_VOLUME;
            SeismicChargeFX.originalAmbienceVolume = GameSettings.AMBIENCE_VOLUME;
        }

#if DEBUG
        // static int PROF_N_pow = 5, PROF_n_pow = 2;
        static int PROF_N = 100000, PROF_n = 100;
        IEnumerator TestVesselPositionTiming()
        {
            var wait = new WaitForFixedUpdate();
            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.ObscenelyEarly, ObscenelyEarly);
            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.Early, Early);
            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.Precalc, Precalc);
            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.Earlyish, Earlyish);
            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.Normal, Normal);
            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.FashionablyLate, FashionablyLate);
            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.FlightIntegrator, FlightIntegrator);
            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.Late, Late);
            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.BetterLateThanNever, BetterLateThanNever);
            yield return wait;
            yield return wait;
            yield return wait;
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.ObscenelyEarly, ObscenelyEarly);
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.Early, Early);
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.Precalc, Precalc);
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.Earlyish, Earlyish);
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.Normal, Normal);
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.FashionablyLate, FashionablyLate);
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.FlightIntegrator, FlightIntegrator);
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.Late, Late);
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.BetterLateThanNever, BetterLateThanNever);
        }
        void ObscenelyEarly() { Debug.Log($"DEBUG {Time.time} ObscenelyEarly, active vessel position: {FlightGlobals.ActiveVessel.transform.position.ToString("G6")}, KbFV: {Krakensbane.GetFrameVelocityV3f()}"); }
        void Early() { Debug.Log($"DEBUG {Time.time} Early, active vessel position: {FlightGlobals.ActiveVessel.transform.position.ToString("G6")}, KbFV: {Krakensbane.GetFrameVelocityV3f()}"); }
        void Precalc() { Debug.Log($"DEBUG {Time.time} Precalc, active vessel position: {FlightGlobals.ActiveVessel.transform.position.ToString("G6")}, KbFV: {Krakensbane.GetFrameVelocityV3f()}"); }
        void Earlyish() { Debug.Log($"DEBUG {Time.time} Earlyish, active vessel position: {FlightGlobals.ActiveVessel.transform.position.ToString("G6")}, KbFV: {Krakensbane.GetFrameVelocityV3f()}"); }
        void Normal() { Debug.Log($"DEBUG {Time.time} Normal, active vessel position: {FlightGlobals.ActiveVessel.transform.position.ToString("G6")}, KbFV: {Krakensbane.GetFrameVelocityV3f()}"); }
        void FashionablyLate() { Debug.Log($"DEBUG {Time.time} FashionablyLate, active vessel position: {FlightGlobals.ActiveVessel.transform.position.ToString("G6")}, KbFV: {Krakensbane.GetFrameVelocityV3f()}"); }
        void FlightIntegrator() { Debug.Log($"DEBUG {Time.time} FlightIntegrator, active vessel position: {FlightGlobals.ActiveVessel.transform.position.ToString("G6")}, KbFV: {Krakensbane.GetFrameVelocityV3f()}"); }
        void Late() { Debug.Log($"DEBUG {Time.time} Late, active vessel position: {FlightGlobals.ActiveVessel.transform.position.ToString("G6")}, KbFV: {Krakensbane.GetFrameVelocityV3f()}"); }
        void BetterLateThanNever() { Debug.Log($"DEBUG {Time.time} BetterLateThanNever, active vessel position: {FlightGlobals.ActiveVessel.transform.position.ToString("G6")}, KbFV: {Krakensbane.GetFrameVelocityV3f()}"); }

        IEnumerator TestYieldWaitLengths()
        {
            Debug.Log($"DEBUG Starting yield wait tests at {Time.time} with timeScale {Time.timeScale}");
            var tic = Time.time;
            for (int i = 0; i < 3; ++i)
            {
                yield return new WaitForFixedUpdate();
                Debug.Log($"DEBUG WaitForFixedUpdate took {Time.time - tic}s at {Time.time}");
                tic = Time.time;
            }
            for (int i = 0; i < 3; ++i)
            {
                yield return null;
                Debug.Log($"DEBUG yield null took {Time.time - tic}s at {Time.time}");
                tic = Time.time;
            }
            yield return new WaitForSeconds(1);
            Debug.Log($"DEBUG WaitForSeconds(1) took {Time.time - tic}s at {Time.time}");
            tic = Time.time;
            yield return new WaitForSecondsFixed(1);
            Debug.Log($"DEBUG WaitForSecondsFixed(1) took {Time.time - tic}s at {Time.time}");
            tic = Time.time;
            yield return new WaitUntil(() => Time.time - tic > 1);
            Debug.Log($"DEBUG WaitUntil took {Time.time - tic}s at {Time.time}");
            tic = Time.time;
            yield return new WaitUntilFixed(() => Time.time - tic > 1);
            Debug.Log($"DEBUG WaitUntilFixed took {Time.time - tic}s at {Time.time}");
        }

        IEnumerator TestLocalization()
        {
            int N = 1 << 18; // With stack traces enabled, this takes around 30s and gives ~8MB GC alloc for the Localizer.Format and 0 for StringUtils.Localize.
            var tic = Time.realtimeSinceStartup;
            string result = "";
            for (int i = 0; i < N; ++i)
                result = Localizer.Format("#LOC_BDArmory_Settings_GUIBackgroundOpacity");
            var dt = Time.realtimeSinceStartup - tic;
            Debug.Log($"DEBUG Result {result} with Localizer.Format took {dt / N:G3}s");
            yield return null;
            yield return null;
            tic = Time.realtimeSinceStartup;
            result = "";
            for (int i = 0; i < N; ++i)
                result = StringUtils.Localize("#LOC_BDArmory_Settings_GUIBackgroundOpacity");
            dt = Time.realtimeSinceStartup - tic;
            Debug.Log($"DEBUG Result {result} with StringUtils.Localize took {dt / N:G3}s");
        }

        IEnumerator TestRaycastHitMergeAndSort()
        {
            RaycastHit[] forwardHits = new RaycastHit[100];
            RaycastHit[] reverseHits = new RaycastHit[100];
            RaycastHit[] sortedHits2 = new RaycastHit[200];
            int forwardHitCount = 97, reverseHitCount = 13;
            for (int i = 0; i < forwardHitCount; ++i) forwardHits[i].distance = UnityEngine.Random.Range(0f, 1f);
            for (int i = 0; i < reverseHitCount; ++i) reverseHits[i].distance = UnityEngine.Random.Range(0f, 1f);
            List<RaycastHit> sortedHits = forwardHits.Take(forwardHitCount).Concat(reverseHits.Take(reverseHitCount)).ToList();
            yield return null;
            yield return null;
            int N = 10000;
            var tic = Time.realtimeSinceStartup;
            for (int i = 0; i < N; ++i)
            {
                sortedHits.Clear();
                sortedHits.AddRange(forwardHits.Take(forwardHitCount).Concat(reverseHits.Take(reverseHitCount)));
                sortedHits.Sort((x1, x2) => x1.distance.CompareTo(x2.distance));
            }
            var dt = Time.realtimeSinceStartup - tic;
            Debug.Log($"DEBUG Clear->AddRange->Sort took {dt / N:G3}s");
            yield return null;
            yield return null;
            tic = Time.realtimeSinceStartup;
            for (int i = 0; i < N; ++i)
            {
                Array.Copy(forwardHits, sortedHits2, forwardHitCount);
                Array.Copy(reverseHits, 0, sortedHits2, forwardHitCount, reverseHitCount);
                Array.Sort<RaycastHit>(sortedHits2, 0, forwardHitCount + reverseHitCount, RaycastHitComparer.raycastHitComparer);
            }
            dt = Time.realtimeSinceStartup - tic;
            Debug.Log($"DEBUG Array.Copy -> Sort took {dt / N:G3}s"); // This seems to be the fastest and causes the least amount of GC.
            yield return null;
            yield return null;
            tic = Time.realtimeSinceStartup;
            for (int i = 0; i < N; ++i)
            {
                sortedHits = forwardHits.Take(forwardHitCount).Concat(reverseHits.Take(reverseHitCount)).ToList();
                sortedHits.Sort((x1, x2) => x1.distance.CompareTo(x2.distance));
            }
            dt = Time.realtimeSinceStartup - tic;
            Debug.Log($"DEBUG ToList->Sort took {dt / N:G3}s");
            yield return null;
            yield return null;
            tic = Time.realtimeSinceStartup;
            for (int i = 0; i < N; ++i)
            {
                sortedHits = forwardHits.Take(forwardHitCount).Concat(reverseHits.Take(reverseHitCount)).OrderBy(x => x.distance).ToList();
            }
            dt = Time.realtimeSinceStartup - tic;
            Debug.Log($"DEBUG OrderBy->ToList took {dt / N:G3}s");
            yield return null;
            yield return null;
            tic = Time.realtimeSinceStartup;
            for (int i = 0; i < N; ++i)
            {
                sortedHits.Clear();
                sortedHits.AddRange(forwardHits.Take(forwardHitCount).Concat(reverseHits.Take(reverseHitCount)).OrderBy(x => x.distance));
            }
            dt = Time.realtimeSinceStartup - tic;
            Debug.Log($"DEBUG OrderBy->AddRange took {dt / N:G3}s");
        }

        IEnumerator TestVesselName()
        {
            int N = 1 << 24;
            var tic = Time.realtimeSinceStartup;
            string result = "";
            var vessel = FlightGlobals.ActiveVessel;
            if (vessel is null) yield break;
            yield return null;
            yield return null;
            for (int i = 0; i < N; ++i)
                result = vessel.vesselName;
            var dt = Time.realtimeSinceStartup - tic;
            Debug.Log($"DEBUG Name: {result} with vessel.vesselName took {dt / N:G3}s");
            yield return null;
            yield return null;
            tic = Time.realtimeSinceStartup;
            result = "";
            for (int i = 0; i < N; ++i)
                result = vessel.GetName();
            dt = Time.realtimeSinceStartup - tic;
            Debug.Log($"DEBUG Name {result} with vessel.GetName() took {dt / N:G3}s");
            yield return null;
            yield return null;
            tic = Time.realtimeSinceStartup;
            result = "";
            for (int i = 0; i < N; ++i)
                result = vessel.GetDisplayName();
            dt = Time.realtimeSinceStartup - tic;
            Debug.Log($"DEBUG Name {result} with vessel.GetDisplayName() took {dt / N:G3}s");
        }

        IEnumerator TestGetAudioClip()
        {
            int N = 1 << 16;
            var tic = Time.realtimeSinceStartup;
            AudioClip clip;
            var vessel = FlightGlobals.ActiveVessel;
            if (vessel is null) yield break;
            yield return null;
            yield return null;
            for (int i = 0; i < N; ++i)
                clip = GameDatabase.Instance.GetAudioClip("BDArmory/Sounds/deployClick");
            var dt = Time.realtimeSinceStartup - tic;
            Debug.Log($"DEBUG GetAudioClip took {dt / N:G3}s");
            yield return null;
            yield return null;
            tic = Time.realtimeSinceStartup;
            clip = null;
            for (int i = 0; i < N; ++i)
                clip = SoundUtils.GetAudioClip("BDArmory/Sounds/deployClick");
            dt = Time.realtimeSinceStartup - tic;
            Debug.Log($"DEBUG GetAudioClip took {dt / N:G3}s");
        }

        public static void TestProjectOnPlaneAndPredictPosition()
        {
            var watch = new System.Diagnostics.Stopwatch();
            float µsResolution = 1e6f / System.Diagnostics.Stopwatch.Frequency;
            Debug.Log($"DEBUG Clock resolution: {µsResolution}µs, {PROF_N} outer loops, {PROF_n} inner loops");
            Vessel vessel = FlightGlobals.ActiveVessel;
            Vector3 p = vessel.CoM, v = vessel.srf_velocity, a = vessel.acceleration;
            Vector3 result = default;
            float time = 5f;
            var upNormal = VectorUtils.GetUpDirection(p);

            var func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = AIUtils.PredictPosition(p, v, a, time); } };
            Debug.Log($"DEBUG AIUtils.PredictPosition(p, v, a, time) took {ProfileFunc(func, PROF_N) / PROF_n:G3}µs to give {(Vector3d)result}");

            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = PredictPositionNoInline(p, v, a, time); } };
            Debug.Log($"DEBUG PredictPositionNoInline(p, v, a, time) took {ProfileFunc(func, PROF_N) / PROF_n:G3}µs to give {(Vector3d)result}");

            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = p + time * v + 0.5f * time * time * a; } };
            Debug.Log($"DEBUG p + time * v + 0.5f * time * time * a took {ProfileFunc(func, PROF_N) / PROF_n:G3}µs to give {(Vector3d)result}");

            func = [MethodImpl(MethodImplOptions.NoInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = p + time * v + 0.5f * time * time * a; } };
            Debug.Log($"DEBUG p + time * v + 0.5f * time * time * a no-inlining took {ProfileFunc(func, PROF_N) / PROF_n:G3}µs to give {(Vector3d)result}");

            watch.Reset(); watch.Start();
            for (int i = 0; i < PROF_N * PROF_n; ++i) { result = p + time * v + 0.5f * time * time * a; }
            watch.Stop();
            Debug.Log($"DEBUG fully inlined took {watch.ElapsedTicks * µsResolution / PROF_N / PROF_n:G3}µs to give {(Vector3d)result}");

            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = Vector3.ProjectOnPlane(v, upNormal); } };
            Debug.Log($"DEBUG Vector3.ProjectOnPlane(v, upNormal) took {ProfileFunc(func, PROF_N) / PROF_n:G3}µs to give {(Vector3d)result}");

            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = v.ProjectOnPlane(upNormal); } };
            Debug.Log($"DEBUG v.ProjectOnPlane(upNormal) took {ProfileFunc(func, PROF_N) / PROF_n:G3}µs to give {(Vector3d)result}");

            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = ProjectOnPlaneOpt(v, upNormal); } };
            Debug.Log($"DEBUG ProjectOnPlaneOpt(v, upNormal) took {ProfileFunc(func, PROF_N) / PROF_n:G3}µs to give {(Vector3d)result}");

            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = v.ProjectOnPlanePreNormalized(upNormal); } };
            Debug.Log($"DEBUG v.ProjectOnPlanePreNormalized(upNormal) took {ProfileFunc(func, PROF_N) / PROF_n:G3}µs to give {(Vector3d)result}");

            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = ProjectOnPlanePreNorm(v, upNormal); } };
            Debug.Log($"DEBUG ProjectOnPlanePreNorm(v, upNormal) took {ProfileFunc(func, PROF_N) / PROF_n:G3}µs to give {(Vector3d)result}");

            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = ProjectOnPlanePreNormNoInline(v, upNormal); } };
            Debug.Log($"DEBUG ProjectOnPlanePreNormNoInline(v, upNormal) took {ProfileFunc(func, PROF_N) / PROF_n:G3}µs to give {(Vector3d)result}");

            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = v - upNormal * Vector3.Dot(v, upNormal); } };
            Debug.Log($"DEBUG v - upNormal * Vector3.Dot(v, upNormal) took {ProfileFunc(func, PROF_N) / PROF_n:G3}µs to give {(Vector3d)result}");

            watch.Reset(); watch.Start();
            for (int i = 0; i < PROF_N * PROF_n; ++i) { result = v - upNormal * Vector3.Dot(v, upNormal); }
            watch.Stop();
            Debug.Log($"DEBUG fully inlined took {watch.ElapsedTicks * µsResolution / PROF_N / PROF_n:G3}µs to give {(Vector3d)result}");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static float ProfileFunc(Action func, int N)
        {
            var watch = new System.Diagnostics.Stopwatch();
            float µsResolution = 1e6f / System.Diagnostics.Stopwatch.Frequency;
            func(); // Warm-up
            watch.Start();
            for (int i = 0; i < N; ++i) func();
            watch.Stop();
            return watch.ElapsedTicks * µsResolution / N;
        }

        public static Vector3 PredictPositionNoInline(Vector3 position, Vector3 velocity, Vector3 acceleration, float time)
        {
            return position + time * velocity + 0.5f * time * time * acceleration;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ProjectOnPlaneOpt(Vector3 vector, Vector3 planeNormal)
        {
            float sqrMag = Vector3.Dot(planeNormal, planeNormal);
            if (sqrMag < Mathf.Epsilon)
                return vector;
            else
            {
                var dotNorm = Vector3.Dot(vector, planeNormal) / sqrMag;
                return new Vector3(vector.x - planeNormal.x * dotNorm,
                    vector.y - planeNormal.y * dotNorm,
                    vector.z - planeNormal.z * dotNorm);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ProjectOnPlanePreNorm(Vector3 vector, Vector3 planeNormal)
        {
            var dot = Vector3.Dot(vector, planeNormal);
            return new Vector3(vector.x - planeNormal.x * dot,
                vector.y - planeNormal.y * dot,
                vector.z - planeNormal.z * dot);
        }
        public static Vector3 ProjectOnPlanePreNormNoInline(Vector3 vector, Vector3 planeNormal)
        {
            var dot = Vector3.Dot(vector, planeNormal);
            return new Vector3(vector.x - planeNormal.x * dot,
                vector.y - planeNormal.y * dot,
                vector.z - planeNormal.z * dot);
        }
#endif
    }
}
