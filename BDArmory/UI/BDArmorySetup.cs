using System.Collections.Generic;
using System.Collections;
using System.Globalization;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System;
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

        //=======Window position settings
        [BDAWindowSettingsField] public static Rect WindowRectToolbar;
        [BDAWindowSettingsField] public static Rect WindowRectGps;
        [BDAWindowSettingsField] public static Rect WindowRectSettings;
        [BDAWindowSettingsField] public static Rect WindowRectRadar;
        [BDAWindowSettingsField] public static Rect WindowRectRwr;
        [BDAWindowSettingsField] public static Rect WindowRectVesselSwitcher;
        [BDAWindowSettingsField] static Rect _WindowRectVesselSwitcherUIHidden;
        [BDAWindowSettingsField] static Rect _WindowRectVesselSwitcherUIVisible;
        [BDAWindowSettingsField] public static Rect WindowRectWingCommander = new Rect(45, 75, 240, 800);
        [BDAWindowSettingsField] public static Rect WindowRectTargetingCam;

        [BDAWindowSettingsField] public static Rect WindowRectRemoteOrchestration;// = new Rect(45, 100, 200, 200);
        [BDAWindowSettingsField] public static Rect WindowRectEvolution;
        [BDAWindowSettingsField] public static Rect WindowRectVesselSpawner;

        [BDAWindowSettingsField] public static Rect WindowRectWayPointSpawner;
        [BDAWindowSettingsField] public static Rect WindowRectVesselMover;
        [BDAWindowSettingsField] public static Rect WindowRectVesselMoverVesselSelection = new Rect(Screen.width / 2 - 300, Screen.height / 2 - 400, 660, 800);

        [BDAWindowSettingsField] public static Rect WindowRectAI;
        [BDAWindowSettingsField] public static Rect WindowRectScores = new Rect(0, 0, 500, 50);
        [BDAWindowSettingsField] static Rect _WindowRectScoresUIHidden;
        [BDAWindowSettingsField] static Rect _WindowRectScoresUIVisible;

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
        static GameScenes InScene = GameScenes.CREDITS; // The scene the instance was instantiated in (for duplicate detection).
        public static bool GAME_UI_ENABLED = true;
        public static string Version { get; private set; } = "Unknown";

        //toolbar button
        public static bool toolbarButtonAdded = false;

        //settings gui
        public static bool windowSettingsEnabled;
        public string fireKeyGui;

        //editor alignment
        public static bool showWeaponAlignment;
        public static bool showCASESimulation;

        //check for Apple Silicon
        public static bool AppleSilicon = false;

        // Gui Skin
        public static GUISkin BDGuiSkin = HighLogic.Skin;
        public static GUIStyle ButtonStyle;
        public static GUIStyle SelectedButtonStyle; // Uses many of the same settings as the ButtonStyle, but with the box colour scheme.
        public static GUIStyle CloseButtonStyle; // Use this for " X" buttons on windows using BDGuiSkin. Note: the extra space helps with alignment.
        public static GUIStyle CloseButtonStyleDark; // Use this for " X" buttons on windows using GUI.skin

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
        string newGPSName = string.Empty;

        // Note: Use OnGUIWM instead for stuff in OnGUI.
        public MissileFire ActiveWeaponManager
        {
            get
            {
                var activeVessel = HighLogic.LoadedSceneIsFlight ? FlightGlobals.ActiveVessel : null;
                if (activeVessel == null || !activeVessel.loaded || activeVessel.packed) _activeWeaponManager = null;
                else if (_activeWeaponManager == null || !_activeWeaponManager.IsPrimaryWM || _activeWeaponManager.vessel != activeVessel)
                {
                    _activeWeaponManager = (activeVessel != null && activeVessel.loaded) ? activeVessel.ActiveController().WM : null;
                    if (_activeWeaponManager != null && _activeWeaponManager.vessel != activeVessel) _activeWeaponManager = null;
                    if (_activeWeaponManager != null) ConfigTextFields(_activeWeaponManager);
                }
                return _activeWeaponManager;
            }
        }
        MissileFire _activeWeaponManager;
        public MissileFire OnGUIWM; // Separate instance to make sure we only update the active WM once for OnGUI rendering. This is updated in LateUpdate.
        public bool missileWarning;
        public float missileWarningTime = 0;

        public static List<CMFlare> Flares = new List<CMFlare>();
        public static List<CMDecoy> Decoys = new List<CMDecoy>();

        public List<string> mutators = new List<string>();
        bool[] mutators_selected;

        List<string> dependencyWarnings = new List<string>();
        double dependencyLastCheckTime = 0;

        //gui styles
        GUIStyle settingsTitleStyle;
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
        GUIStyle textFieldStyle;
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
        (float, float)[] hosDmgCache;
        static GameParameters.AdvancedParams advancedParams;

        //competition mode
        public string compDistGui;
        string compIntraTeamSeparationBase;
        string compIntraTeamSeparationPerMember;

        #region Textures

        public static string textureDir = "BDArmory/Textures/";

        bool cursorOnGUI = false;
        bool drawCursor;
        Texture2D cursorTexture = GameDatabase.Instance.GetTexture(textureDir + "aimer", false);
        bool temporarilyShowMouse = false;

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

        public const float largeGreenCircleScale = 256f / 230f; // Circle is 230 pixels in diameter vs. 256 texture size

        private Texture2D gct;

        public Texture2D greenCircleTexture
        {
            get { return gct ? gct : gct = GameDatabase.Instance.GetTexture(textureDir + "greenCircle2", false); }
        }

        public const float greenCircleScale = 128f / 64f; // Circle is 64 pixels in diameter vs. 128 texture size

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

        private Texture2D gC;

        public Texture2D greenCross
        {
            get
            {
                return gC ? gC : gC = GameDatabase.Instance.GetTexture(textureDir + "greenCross", false);
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
            if (Instance != null)
            {
                if (InScene == HighLogic.LoadedScene)
                {
                    // In some scenes (such as the editors) addons get instantiated multiple times if using KSPAddon.Startup.EveryScene.
                    // This avoids the duplicate instance from messing with our syncing to KSP's settings.
                    Destroy(this);
                    return;
                }
                Destroy(Instance);
            }
            Instance = this;
            InScene = HighLogic.LoadedScene;
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
            WindowRectToolbar = new Rect(Screen.width - toolWindowWidth - 40, 150, toolWindowWidth, toolWindowHeight); // Default, if not in file.
            WindowRectGps = new Rect(0, 0, WindowRectToolbar.width - 10, 0);
            SetupSettingsSize();
            BDAWindowSettingsField.Load();
            CheckIfWindowsSettingsAreWithinScreen();
            toolWindowHeight = WindowRectToolbar.height;

            // Configure UI visibility and window rects
            GAME_UI_ENABLED = true;
            if (_WindowRectScoresUIVisible != default) WindowRectScores = _WindowRectScoresUIVisible;
            if (_WindowRectVesselSwitcherUIVisible != default) WindowRectVesselSwitcher = _WindowRectVesselSwitcherUIVisible;

            WindowRectGps.width = WindowRectToolbar.width - 10;

            // Get the BDA version. We can do this here since it's this assembly we're interested in, other assemblies have to wait until Start.
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().ToList())
                if (assembly.FullName.Split([','])[0] == "BDArmory")
                    Version = assembly.GetName().Version.ToString();

            // Load settings
            LoadConfig();

            // Check for Apple Processor
            AppleSilicon = CultureInfo.InvariantCulture.CompareInfo.IndexOf(SystemInfo.processorType, "Apple", CompareOptions.IgnoreCase) >= 0;

            // Ensure AutoSpawn folder exists.
            var autoSpawnFolder = Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath, "AutoSpawn"));
            if (!Directory.Exists(autoSpawnFolder))
            { Directory.CreateDirectory(autoSpawnFolder); }
            // Ensure GameData/Custom/Flags folder exists.
            var customFlagsFolder = Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Custom", "Flags"));
            if (!Directory.Exists(customFlagsFolder))
            { Directory.CreateDirectory(customFlagsFolder); }
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

            fireKeyGui = BDInputSettingsFields.WEAP_FIRE_KEY.inputString;

            ModuleManagerLoaded = ModuleManager.CheckForModuleManager();
            PhysicsRangeExtenderLoaded = PhysicsRangeExtender.CheckForPhysicsRangeExtender();

            if (HighLogic.LoadedSceneIsFlight)
            {
                SaveVolumeSettings();

                GameEvents.onHideUI.Add(HideGameUI);
                GameEvents.onShowUI.Add(ShowGameUI);
                GameEvents.onVesselGoOffRails.Add(OnVesselGoOffRails);
                GameEvents.OnGameSettingsApplied.Add(SaveVolumeSettings);
                GameEvents.onVesselChange.Add(VesselChange);
            }
            GameEvents.onGameSceneSwitchRequested.Add(OnGameSceneSwitchRequested);
            GameEvents.onGameStateSave.Add(OnGameStateSave);
            GameEvents.onGameStateSaved.Add(OnGameStateSaved);

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
            if (HighLogic.LoadedSceneIsFlight || HighLogic.LoadedSceneIsEditor) RWPSettings.SyncWithGameSettings(); // Re-sync BDA settings to game settings in Start since they get overwritten during scene initialisation.
            if (!HighLogic.LoadedSceneIsFlight) OtherUtils.SetTimeOverride(false); // Make sure time override is disabled when switching to any scene other than flight.

            if (HighLogic.LoadedSceneIsFlight || HighLogic.LoadedSceneIsEditor)
            { StartCoroutine(ToolbarButtonRoutine()); }

            for (int i = 0; i < MutatorInfo.mutators.Count; i++)
            {
                mutators.Add(MutatorInfo.mutators[i].name);
            }
            UpdateSelectedMutators();
        }

        void ConfigureStyles()
        {
            centerLabel = new GUIStyle
            {
                alignment = TextAnchor.UpperCenter,
                normal = new GUIStyleState { textColor = Color.white }
            };

            settingsTitleStyle = new GUIStyle(centerLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16,
                fontStyle = FontStyle.Bold
            };

            centerLabelRed = new GUIStyle
            {
                alignment = TextAnchor.UpperCenter,
                normal = new GUIStyleState { textColor = Color.red }
            };

            centerLabelOrange = new GUIStyle
            {
                alignment = TextAnchor.UpperCenter,
                normal = new GUIStyleState { textColor = XKCDColors.BloodOrange }
            };

            centerLabelBlue = new GUIStyle
            {
                alignment = TextAnchor.UpperCenter,
                normal = new GUIStyleState { textColor = XKCDColors.AquaBlue }
            };

            leftLabel = new GUIStyle
            {
                alignment = TextAnchor.UpperLeft,
                normal = new GUIStyleState { textColor = Color.white }
            };

            leftLabelBold = new GUIStyle
            {
                alignment = TextAnchor.UpperLeft,
                fontStyle = FontStyle.Bold,
                normal = new GUIStyleState { textColor = Color.white }
            };

            infoLinkStyle = new GUIStyle(BDGuiSkin.label)
            {
                alignment = TextAnchor.UpperLeft
            };
            infoLinkStyle.normal.textColor = Color.white;

            middleLeftLabel = new GUIStyle(leftLabel)
            {
                alignment = TextAnchor.MiddleLeft
            };

            middleLeftLabelOrange = new GUIStyle(middleLeftLabel);
            middleLeftLabelOrange.normal.textColor = XKCDColors.BloodOrange;

            targetModeStyle = new GUIStyle
            {
                alignment = TextAnchor.MiddleRight,
                fontSize = 9,
                normal = new GUIStyleState { textColor = Color.white }
            };

            targetModeStyleSelected = new GUIStyle(targetModeStyle);
            targetModeStyleSelected.normal.textColor = XKCDColors.BloodOrange;

            waterMarkStyle = new GUIStyle(middleLeftLabel);
            waterMarkStyle.normal.textColor = XKCDColors.LightBlueGrey;

            leftLabelRed = new GUIStyle
            {
                alignment = TextAnchor.UpperLeft,
                normal = new GUIStyleState { textColor = Color.red }
            };

            rightLabelRed = new GUIStyle
            {
                alignment = TextAnchor.UpperRight,
                normal = new GUIStyleState { textColor = Color.red }
            };

            leftLabelGray = new GUIStyle
            {
                alignment = TextAnchor.UpperLeft,
                normal = new GUIStyleState { textColor = Color.gray }
            };

            rippleSliderStyle = new GUIStyle(BDGuiSkin.horizontalSlider);
            rippleThumbStyle = new GUIStyle(BDGuiSkin.horizontalSliderThumb);
            rippleSliderStyle.fixedHeight = rippleThumbStyle.fixedHeight = 0;

            kspTitleLabel = new GUIStyle
            {
                font = BDGuiSkin.window.font,
                fontSize = BDGuiSkin.window.fontSize,
                fontStyle = BDGuiSkin.window.fontStyle,
                alignment = TextAnchor.UpperCenter,
                normal = new GUIStyleState { textColor = BDGuiSkin.window.normal.textColor }
            };

            redErrorStyle = new GUIStyle(BDGuiSkin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 24,
                alignment = TextAnchor.UpperCenter
            };
            redErrorStyle.normal.textColor = Color.red;

            redErrorShadowStyle = new GUIStyle(redErrorStyle);
            redErrorShadowStyle.normal.textColor = new Color(0, 0, 0, 0.75f);

            textFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                alignment = TextAnchor.MiddleRight
            };

            ButtonStyle = new GUIStyle(BDGuiSkin.button);
            SelectedButtonStyle = new GUIStyle(BDGuiSkin.box) // Copy most of the ButtonStyle, but keep the box style backgrounds and font styles.
            {
                alignment = ButtonStyle.alignment,
                // border = ButtonStyle.border,
                clipping = ButtonStyle.clipping,
                contentOffset = ButtonStyle.contentOffset,
                fixedHeight = ButtonStyle.fixedHeight,
                fixedWidth = ButtonStyle.fixedWidth,
                fontSize = ButtonStyle.fontSize,
                imagePosition = ButtonStyle.imagePosition,
                margin = ButtonStyle.margin,
                overflow = ButtonStyle.overflow,
                padding = ButtonStyle.padding,
                richText = ButtonStyle.richText,
                stretchHeight = ButtonStyle.stretchHeight,
                stretchWidth = ButtonStyle.stretchWidth,
                wordWrap = ButtonStyle.wordWrap
            };
            CloseButtonStyle = new GUIStyle(ButtonStyle) { alignment = TextAnchor.MiddleCenter };
            CloseButtonStyle.hover.textColor = Color.red;
            CloseButtonStyleDark = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleCenter, padding = new(0, 0, 0, 0) };
            CloseButtonStyleDark.hover.textColor = Color.red;

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
            else { ToggleWindowSettings(); }
        }

        private void CheckIfWindowsSettingsAreWithinScreen()
        {
            GUIUtils.UseMouseEventInRect(WindowRectSettings);
            GUIUtils.RepositionWindow(ref WindowRectToolbar);
            GUIUtils.RepositionWindow(ref WindowRectSettings);
            GUIUtils.RepositionWindow(ref WindowRectRwr);
            GUIUtils.RepositionWindow(ref WindowRectVesselSwitcher);
            GUIUtils.RepositionWindow(ref _WindowRectVesselSwitcherUIHidden);
            GUIUtils.RepositionWindow(ref _WindowRectVesselSwitcherUIVisible);
            GUIUtils.RepositionWindow(ref WindowRectWingCommander);
            GUIUtils.RepositionWindow(ref WindowRectTargetingCam);
            GUIUtils.RepositionWindow(ref WindowRectAI);
            GUIUtils.RepositionWindow(ref WindowRectScores);
            GUIUtils.RepositionWindow(ref _WindowRectScoresUIHidden);
            GUIUtils.RepositionWindow(ref _WindowRectScoresUIVisible);
            GUIUtils.RepositionWindow(ref WindowRectEvolution);
        }

        void Update()
        {
            if (!scalingUI) BDArmorySettings.PREVIOUS_UI_SCALE = BDArmorySettings.UI_SCALE_ACTUAL;

            if (HighLogic.LoadedSceneIsFlight)
            {
                if (missileWarning && Time.time - missileWarningTime > 1.5f) missileWarning = false;
                if (BDInputUtils.GetKeyDown(BDInputSettingsFields.GUI_WM_TOGGLE)) windowBDAToolBarEnabled = !windowBDAToolBarEnabled;
                if (BDInputUtils.GetKeyDown(BDInputSettingsFields.TIME_SCALING)) OtherUtils.SetTimeOverride(!BDArmorySettings.TIME_OVERRIDE);
#if DEBUG
                if (BDInputUtils.GetKeyDown(BDInputSettingsFields.DEBUG_CLEAR_DEV_CONSOLE)) Debug.ClearDeveloperConsole();
#endif
                if (temporarilyShowMouse != (temporarilyShowMouse = BDInputUtils.GetKey(BDInputSettingsFields.TEMPORARILY_SHOW_MOUSE))) UpdateCursorState();
                if (cursorOnGUI && !GUIUtils.CheckMouseIsOnGui()) UpdateCursorState();
            }
            else if (HighLogic.LoadedSceneIsEditor)
            {
                if (Input.GetKeyDown(KeyCode.F2))
                {
                    showWeaponAlignment = !showWeaponAlignment;
                }
                if (Input.GetKeyDown(KeyCode.F3))
                {
                    showCASESimulation = !showCASESimulation;
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
            if (temporarilyShowMouse)
            {
                drawCursor = false;
                Cursor.visible = true;
                return;
            }
            var weaponManager = ActiveWeaponManager;
            if (weaponManager == null)
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
                cursorOnGUI = GUIUtils.CheckMouseIsOnGui();
                if (!MapView.MapIsEnabled && !cursorOnGUI && !PauseMenu.isOpen)
                {
                    if (weaponManager.selectedWeapon != null && weaponManager.weaponIndex > 0 &&
                        !weaponManager.guardMode)
                    {
                        if (weaponManager.selectedWeapon.GetWeaponClass() == WeaponClasses.Gun ||
                            weaponManager.selectedWeapon.GetWeaponClass() == WeaponClasses.Rocket ||
                            weaponManager.selectedWeapon.GetWeaponClass() == WeaponClasses.DefenseLaser)
                        {
                            ModuleWeapon mw = weaponManager.selectedWeapon.GetWeaponModule();
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
                        drawCursor = false;
                        return;
                    }

                    if (!weaponManager.guardMode && weaponManager.isHMDEnabled)
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
                Instance.UpdateCursorState();
            }
        }

        public void ConfigTextFields(MissileFire weaponManager)
        {
            textNumFields = new Dictionary<string, NumericInputField> {
                { "rippleRPM", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.rippleRPM, 0, 1600) },
                { "targetScanInterval", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.targetScanInterval, 0.5f, 60f) },
                { "fireBurstLength", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.fireBurstLength, 0, 10) },
                { "AutoFireCosAngleAdjustment", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.AutoFireCosAngleAdjustment, 0, 4) },
                { "guardAngle", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.guardAngle, 10, 360) },
                { "guardRange", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.guardRange, 100, BDArmorySettings.MAX_GUARD_VISUAL_RANGE) },
                { "gunRange", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.gunRange, 0, weaponManager.maxGunRange) },
                { "multiTargetNum", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.multiTargetNum, 1, 10) },
                { "multiMissileTgtNum", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.multiMissileTgtNum, 1, 10) },
                { "maxMissilesOnTarget", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.maxMissilesOnTarget, 1, MissileFire.maxAllowableMissilesOnTarget) },
                { "maxMissilesOnTargetAir", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.maxMissilesOnTargetAir, 1, MissileFire.maxAllowableMissilesOnTarget) },
                { "maxMissilesOnTargetSrf", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.maxMissilesOnTargetSrf, 1, MissileFire.maxAllowableMissilesOnTarget) },
                { "maxMissilesOnTargetSea", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.maxMissilesOnTargetSea, 1, MissileFire.maxAllowableMissilesOnTarget) },
                { "maxMissilesOnTargetMsl", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.maxMissilesOnTargetMsl, 1, MissileFire.maxAllowableMissilesOnTarget) },
                { "maxTNTOnTargetAir", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.maxTNTOnTargetAir, 1, 10000) },
                { "maxTNTOnTargetSrf", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.maxTNTOnTargetSrf, 1, 10000) },
                { "maxTNTOnTargetSea", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.maxTNTOnTargetSea, 1, 10000) },
                { "maxTNTOnTargetMsl", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.maxTNTOnTargetMsl, 1, 1000) },
                { "targetBias", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.targetBias, 0, 10) },
                { "targetWeightRange", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.targetWeightRange, -10, 10) },
                { "targetWeightAirPreference", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.targetWeightAirPreference, -10, 10) },
                { "targetWeightATA", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.targetWeightATA, -10, 10) },
                { "targetWeightAoD", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.targetWeightAoD, -10, 10) },
                { "targetWeightAccel", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.targetWeightAccel,-10, 10) },
                { "targetWeightClosureTime", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.targetWeightClosureTime, -10, 10) },
                { "targetWeightWeaponNumber", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.targetWeightWeaponNumber, -10, 10) },
                { "targetWeightMass", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.targetWeightMass,-10, 10) },
                { "targetWeightDamage", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.targetWeightDamage,-10, 10) },
                { "targetWeightFriendliesEngaging", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.targetWeightFriendliesEngaging, -10, 10) },
                { "targetWeightThreat", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.targetWeightThreat, -10, 10) },
                { "targetWeightProtectTeammate", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.targetWeightProtectTeammate, -10, 10) },
                { "targetWeightProtectVIP", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.targetWeightProtectVIP, -10, 10) },
                { "targetWeightAttackVIP", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.targetWeightAttackVIP, -10, 10) },
                { "targetWeightUncontrolled", gameObject.AddComponent<NumericInputField>().Initialise(0, weaponManager.targetWeightUncontrolled, -10, 10) },
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
                TournamentScores.LoadWeights();
                ContinuousSpawning.LoadWeights();
                SanitiseSettings();
                RWPSettings.SyncWithGameSettings(toKSP: false); // Sync KSP's advanced settings to BDA's backing values before we load any overrides.
                RWPSettings.Load();
                CompSettings.Load();
                VesselSpawnerField.Load();
                BDArmorySettings.ready = true;
                if (BDAEditorArmorWindow.Instance) BDAEditorArmorWindow.Instance.SetupLegalityValues();
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

                if (BDArmorySettings.RUNWAY_PROJECT)
                {
                    RWPSettings.StoreSettings(true); // Temporarily store the current RWP settings.
                    RWPSettings.RestoreSettings(); // Revert RWP specific settings so we save the underlying ones.
                }
                RWPSettings.Save(); // Save the RWP settings to file.
                BDAPersistentSettingsField.Save(BDArmorySettings.settingsConfigURL);
                if (BDArmorySettings.RUNWAY_PROJECT)
                {
                    RWPSettings.RestoreSettings(true); // Restore the current RWP settings.
                }

                BDInputSettingsFields.SaveSettings();
                TournamentScores.SaveWeights();
                ContinuousSpawning.SaveWeights();

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

        static void SanitiseSettings()
        {
            BDArmorySettings.PROC_ARMOR_ALT_LIMITS.y = Mathf.Min(BDArmorySettings.PROC_ARMOR_ALT_LIMITS.y, 1e5f); // Anything over this pretty much breaks KSP.
            BDArmorySettings.PROC_ARMOR_ALT_LIMITS.x = Mathf.Clamp(BDArmorySettings.PROC_ARMOR_ALT_LIMITS.x, BDArmorySettings.PROC_ARMOR_ALT_LIMITS.y * 1e-8f, BDArmorySettings.PROC_ARMOR_ALT_LIMITS.y); // More than 8 orders of magnitude breaks the mesh collider engine.
            BDArmorySettings.PREVIOUS_UI_SCALE = BDArmorySettings.UI_SCALE;
        }

        /// <summary>
        /// Update which mutators are selected in the UI.
        /// Call this if the mutators are modified somewhere other than by toggling them in the UI.
        /// </summary>
        public void UpdateSelectedMutators()
        {
            mutators_selected = new bool[mutators.Count];
            for (int i = 0; i < mutators_selected.Length; ++i)
            {
                mutators_selected[i] = BDArmorySettings.MUTATOR_LIST.Contains(mutators[i]);
            }
        }
        #region GUI

        void LateUpdate()
        {
            OnGUIWM = ActiveWeaponManager;
        }
        void OnGUI()
        {
            if (!GAME_UI_ENABLED) return;
            if (!stylesConfigured) ConfigureStyles();
            if (windowSettingsEnabled)
            {
                var guiMatrix = GUI.matrix; // Store and restore the GUI.matrix so we can apply a different scaling for the WM window.
                if (scalingUI && Mouse.Left.GetButtonUp()) scalingUI = false; // Don't rescale the settings window until the mouse is released otherwise it messes with the slider.
                if (!scalingUI) oldUIScale = BDArmorySettings.UI_SCALE_ACTUAL;
                if (oldUIScale != 1) GUIUtility.ScaleAroundPivot(oldUIScale * Vector2.one, WindowRectSettings.position);
                WindowRectSettings = GUI.Window(129419, WindowRectSettings, WindowSettings, GUIContent.none, settingsTitleStyle);
                GUI.matrix = guiMatrix;
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
            if (BDArmorySettings.UI_SCALE_ACTUAL != 1) GUIUtility.ScaleAroundPivot(BDArmorySettings.UI_SCALE_ACTUAL * Vector2.one, WindowRectToolbar.position);
            WindowRectToolbar = GUI.Window(321, WindowRectToolbar, WindowBDAToolbar, "", BDGuiSkin.window);//"BDA Weapon Manager"
            SetGUIOpacity(false);
            GUIUtils.UseMouseEventInRect(WindowRectToolbar);
            if (showWindowGPS && OnGUIWM)
            {
                //gpsWindowRect = GUI.Window(424333, gpsWindowRect, GPSWindow, "", GUI.skin.box);
                GUIUtils.UseMouseEventInRect(WindowRectGps);
                using (var coord = BDATargetManager.GPSTargetList(OnGUIWM.Team).GetEnumerator())
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
                && !PhysicsRangeExtender.IsPREEnabled) dependencyWarnings.Add("Physics Range Extender is disabled!");
            if (dependencyWarnings.Count() > 0) dependencyWarnings.Add("BDArmory will not work properly.");
            return dependencyWarnings.Count() == 0;
        }

        public bool hasVesselSwitcher = false;
        public bool hasVesselSpawner = false;
        public bool hasWPCourseSpawner = false;
        public bool hasVesselMover = false;
        public bool hasEvolution = false;
        public static bool showVesselSwitcherGUI = false;
        public static bool showVesselSpawnerGUI = false;
        public static bool showWPBuilderGUI = false;
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

        (float, float)[] cacheGuardRange, cacheGunRange;
        void WindowBDAToolbar(int windowID)
        {
            float line = 0;
            float contentWidth = columnWidth - 2 * leftIndent;
            float windowColumns = 1;
            int buttonNumber = 0;

            line += 1.25f;
            line += 0.25f;

            //title
            GUI.Label(new Rect(_windowMargin + _buttonSize, _windowMargin, columnWidth - 2 * _windowMargin - numberOfButtons * _buttonSize, _windowMargin + _buttonSize), StringUtils.Localize("#LOC_BDArmory_WMWindow_title"), kspTitleLabel);

            // Version.
            GUI.Label(new Rect(columnWidth - _windowMargin - (numberOfButtons - 1) * _buttonSize - 100, 23, 57, 10), Version, waterMarkStyle);

            //SETTINGS BUTTON
            if (!BDKeyBinder.current &&
                GUI.Button(new Rect(columnWidth - _windowMargin - ++buttonNumber * _buttonSize, _windowMargin, _buttonSize, _buttonSize), settingsIconTexture, ButtonStyle))
            {
                ToggleWindowSettings();
            }

            //vesselswitcher button
            if (hasVesselSwitcher)
            {
                GUIStyle vsStyle = showVesselSwitcherGUI ? SelectedButtonStyle : ButtonStyle;
                if (GUI.Button(new Rect(columnWidth - _windowMargin - ++buttonNumber * _buttonSize, _windowMargin, _buttonSize, _buttonSize), "VS", vsStyle))
                {
                    LoadedVesselSwitcher.Instance.SetVisible(!showVesselSwitcherGUI);
                }
            }

            //VesselSpawner button
            if (hasVesselSpawner)
            {
                GUIStyle vsStyle = showVesselSpawnerGUI ? SelectedButtonStyle : ButtonStyle;
                if (GUI.Button(new Rect(columnWidth - _windowMargin - ++buttonNumber * _buttonSize, _windowMargin, _buttonSize, _buttonSize), "Sp", vsStyle))
                {
                    VesselSpawnerWindow.Instance.SetVisible(!showVesselSpawnerGUI);
                    if (!showVesselSpawnerGUI)
                        SaveConfig();
                }
            }

            // VesselMover button
            if (hasVesselMover && GUI.Button(new Rect(columnWidth - _windowMargin - ++buttonNumber * _buttonSize, _windowMargin, _buttonSize, _buttonSize), "VM", showVesselMoverGUI ? SelectedButtonStyle : ButtonStyle))
            {
                VesselMover.Instance.SetVisible(!showVesselMoverGUI);
            }

            // evolution button
            if (BDArmorySettings.EVOLUTION_ENABLED && hasEvolution)
            {
                var evolutionSkin = showEvolutionGUI ? SelectedButtonStyle : ButtonStyle; ;
                if (GUI.Button(new Rect(columnWidth - _windowMargin - ++buttonNumber * _buttonSize, _windowMargin, _buttonSize, _buttonSize), "EV", evolutionSkin))
                {
                    EvolutionWindow.Instance.SetVisible(!showEvolutionGUI);
                }
            }

            //infolink
            GUIStyle iStyle = infoLinkEnabled ? SelectedButtonStyle : ButtonStyle;
            if (GUI.Button(new Rect(columnWidth - _windowMargin - (buttonNumber += 2) * _buttonSize, _windowMargin, 2 * _buttonSize, _buttonSize), "Info", iStyle))
            {
                infoLinkEnabled = !infoLinkEnabled;
            }

            //numeric fields
            GUIStyle nStyle = NumFieldsEnabled ? SelectedButtonStyle : ButtonStyle;
            if (GUI.Button(new Rect(columnWidth - _windowMargin - ++buttonNumber * _buttonSize, _windowMargin, _buttonSize, _buttonSize), "#", nStyle))
            {
                NumFieldsEnabled = !NumFieldsEnabled;
                if (!NumFieldsEnabled)
                {
                    // Try to parse all the fields immediately so that they're up to date.
                    foreach (var field in textNumFields.Keys)
                    { textNumFields[field].tryParseValueNow(); }
                    if (OnGUIWM != null)
                    {
                        foreach (var field in textNumFields.Keys)
                        {
                            try
                            {
                                var fieldInfo = typeof(MissileFire).GetField(field);
                                if (fieldInfo != null)
                                { fieldInfo.SetValue(OnGUIWM, Convert.ChangeType(textNumFields[field].CurrentValue, fieldInfo.FieldType)); }
                                else // Check if it's a property instead of a field.
                                {
                                    var propInfo = typeof(MissileFire).GetProperty(field);
                                    propInfo.SetValue(OnGUIWM, Convert.ChangeType(textNumFields[field].CurrentValue, propInfo.PropertyType));
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
                    if (OnGUIWM != null)
                    {
                        foreach (var field in textNumFields.Keys)
                        {
                            try
                            {
                                var fieldInfo = typeof(MissileFire).GetField(field);
                                if (fieldInfo != null)
                                { textNumFields[field].SetCurrentValue(Convert.ToDouble(fieldInfo.GetValue(OnGUIWM))); }
                                else // Check if it's a property instead of a field.
                                {
                                    var propInfo = typeof(MissileFire).GetProperty(field);
                                    textNumFields[field].SetCurrentValue(Convert.ToDouble(propInfo.GetValue(OnGUIWM)));
                                }
                            }
                            catch (Exception e) { Debug.LogError($"[BDArmory.BDArmorySetup]: Failed to set current value of {field}: " + e.Message); }
                        }
                    }
                }
            }

            if (OnGUIWM != null)
            {
                //MINIMIZE BUTTON
                toolMinimized = GUI.Toggle(new Rect(_windowMargin, _windowMargin, _buttonSize, _buttonSize), toolMinimized, "_", toolMinimized ? SelectedButtonStyle : ButtonStyle);

                GUIStyle armedLabelStyle;
                Rect armedRect = new Rect(leftIndent, contentTop + (line * entryHeight), contentWidth / 2, entryHeight);
                if (OnGUIWM.guardMode)
                {
                    if (GUI.Button(armedRect, "- " + StringUtils.Localize("#LOC_BDArmory_WMWindow_GuardModebtn") + " -", SelectedButtonStyle))//Guard Mode
                    {
                        showGuardMenu = true;
                    }
                }
                else
                {
                    string armedText = StringUtils.Localize("#LOC_BDArmory_WMWindow_ArmedText");//"Trigger is "
                    if (OnGUIWM.isArmed)
                    {
                        armedText += StringUtils.Localize("#LOC_BDArmory_WMWindow_ArmedText_ARMED");//"ARMED."
                        armedLabelStyle = SelectedButtonStyle;
                    }
                    else
                    {
                        armedText += StringUtils.Localize("#LOC_BDArmory_WMWindow_ArmedText_DisArmed");//"disarmed."
                        armedLabelStyle = ButtonStyle;
                    }
                    if (GUI.Button(armedRect, armedText, armedLabelStyle))
                    {
                        OnGUIWM.ToggleArm();
                    }
                }

                GUIStyle teamButtonStyle = SelectedButtonStyle;
                string teamText = StringUtils.Localize("#LOC_BDArmory_WMWindow_TeamText") + $": {OnGUIWM.Team.Name + (OnGUIWM.Team.Neutral ? (OnGUIWM.Team.Name != "Neutral" ? "(N)" : "") : "")}";//Team
                if (GUI.Button(new Rect(leftIndent + (contentWidth / 2), contentTop + (line * entryHeight), contentWidth / 2, entryHeight), teamText, teamButtonStyle))
                {
                    if (Event.current.button == 1)
                    {
                        BDTeamSelector.Instance.Open(OnGUIWM, new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
                    }
                    else
                    {
                        OnGUIWM.NextTeam();
                    }
                }
                line++;
                line += 0.25f;
                string weaponName = OnGUIWM.selectedWeaponString;
                string selectionText = StringUtils.Localize("#LOC_BDArmory_WMWindow_selectionText", weaponName);//Weapon: <<1>>
                GUI.Label(new Rect(leftIndent, contentTop + (line * entryHeight), contentWidth, entryHeight * 1.25f), selectionText, SelectedButtonStyle);
                line += 1.25f;
                line += 0.1f;
                //if weapon can ripple, show option and slider.
                if (OnGUIWM.hasLoadedRippleData && OnGUIWM.canRipple)
                {
                    if (OnGUIWM.selectedWeapon != null && OnGUIWM.weaponIndex > 0 &&
                        (OnGUIWM.selectedWeapon.GetWeaponClass() == WeaponClasses.Gun
                        || OnGUIWM.selectedWeapon.GetWeaponClass() == WeaponClasses.Rocket
                        || OnGUIWM.selectedWeapon.GetWeaponClass() == WeaponClasses.DefenseLaser)) //remove rocket ripple slider - moved to editor
                    {
                        string rippleText = OnGUIWM.rippleFire
                            ? StringUtils.Localize("#LOC_BDArmory_WMWindow_rippleText1", OnGUIWM.gunRippleRpm.ToString("0"))//"Barrage: " +  + " RPM"
                            : StringUtils.Localize("#LOC_BDArmory_WMWindow_rippleText2");//"Salvo"
                        GUIStyle rippleStyle = OnGUIWM.rippleFire
                            ? SelectedButtonStyle
                            : ButtonStyle;
                        if (
                            GUI.Button(
                                new Rect(leftIndent, contentTop + (line * entryHeight), contentWidth / 2, entryHeight * 1.25f),
                                rippleText, rippleStyle))
                        {
                            OnGUIWM.ToggleRippleFire();
                        }
                        if (OnGUIWM.rippleFire)
                        {
                            GUI.Label(new Rect(leftIndent + contentWidth / 2 + _windowMargin, contentTop + line * entryHeight, contentWidth / 4 - _windowMargin, entryHeight * 1.25f), $"{StringUtils.Localize("#LOC_BDArmory_WMWindow_barrageStagger")}: {(OnGUIWM.barrageStagger > 0 ? OnGUIWM.barrageStagger : 1):G1}");
                            OnGUIWM.barrageStagger = BDAMath.RoundToUnit(GUI.HorizontalSlider(new Rect(leftIndent + 3 * contentWidth / 4, contentTop + (line + 0.25f) * entryHeight, contentWidth / 4, entryHeight), OnGUIWM.barrageStagger, 0f, 0.1f), 0.01f);
                        }

                        rippleHeight = Mathf.Lerp(rippleHeight, 1.25f, 0.15f);
                    }
                    else
                    {
                        string rippleText = OnGUIWM.rippleFire
                            ? StringUtils.Localize("#LOC_BDArmory_WMWindow_rippleText3", OnGUIWM.rippleRPM.ToString("0"))//"Ripple: " +  + " RPM"
                            : StringUtils.Localize("#LOC_BDArmory_WMWindow_rippleText4");//"Ripple: OFF"
                        GUIStyle rippleStyle = OnGUIWM.rippleFire
                            ? SelectedButtonStyle
                            : ButtonStyle;
                        if (
                            GUI.Button(
                                new Rect(leftIndent, contentTop + (line * entryHeight), contentWidth / 2, entryHeight * 1.25f),
                                rippleText, rippleStyle))
                        {
                            OnGUIWM.ToggleRippleFire();
                        }
                        if (OnGUIWM.rippleFire)
                        {
                            if (!NumFieldsEnabled)
                            {
                                OnGUIWM.rippleRPM = GUI.HorizontalSlider(new Rect(leftIndent + (contentWidth / 2) + 2, contentTop + (line * entryHeight) + 6.5f, (contentWidth / 2) - 2, 12),
                                    OnGUIWM.rippleRPM, 100, 1600, rippleSliderStyle, rippleThumbStyle);
                            }
                            else
                            {
                                var field = textNumFields["rippleRPM"];
                                field.TryParseValue(GUI.TextField(new Rect(leftIndent + (contentWidth / 2) + 2, contentTop + (line * entryHeight) + 6.5f, (contentWidth / 2) - 2, entryHeight),
                                    field.possibleValue, 4, field.style));
                                OnGUIWM.rippleRPM = (float)field.CurrentValue;
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
                            showWeaponList, StringUtils.Localize("#LOC_BDArmory_WMWindow_ListWeapons"), showWeaponList ? SelectedButtonStyle : ButtonStyle);//"Weapons"
                    showGuardMenu =
                        GUI.Toggle(
                            new Rect(leftIndent + (contentWidth / 4), contentTop + (line * entryHeight), contentWidth / 4,
                                entryHeight), showGuardMenu, StringUtils.Localize("#LOC_BDArmory_WMWindow_GuardMenu"),//"Guard Menu"
                            showGuardMenu ? SelectedButtonStyle : ButtonStyle);
                    showPriorities =
                        GUI.Toggle(new Rect(leftIndent + (2 * contentWidth / 4), contentTop + (line * entryHeight), contentWidth / 4,
                             entryHeight), showPriorities, StringUtils.Localize("#LOC_BDArmory_WMWindow_TargetPriority"),//"Tgt priority"
                            showPriorities ? SelectedButtonStyle : ButtonStyle);
                    showModules =
                        GUI.Toggle(
                            new Rect(leftIndent + (3 * contentWidth / 4), contentTop + (line * entryHeight), contentWidth / 4,
                                entryHeight), showModules, StringUtils.Localize("#LOC_BDArmory_WMWindow_ModulesToggle"),//"Modules"
                            showModules ? SelectedButtonStyle : ButtonStyle);
                    line++;
                }

                float weaponLines = 0;
                if (showWeaponList && !toolMinimized)
                {
                    line += 0.25f;
                    Rect weaponListGroupRect = new Rect(5, contentTop + (line * entryHeight), columnWidth - 10, weaponsHeight * entryHeight);
                    GUI.BeginGroup(weaponListGroupRect, GUIContent.none, SelectedButtonStyle); //darker box
                    weaponLines += 0.1f;

                    for (int i = 0; i < OnGUIWM.weaponArray.Length; i++)
                    {
                        GUIStyle wpnListStyle;
                        GUIStyle tgtStyle;
                        if (i == OnGUIWM.weaponIndex)
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
                        if (OnGUIWM.weaponArray[i] != null)
                        {
                            label = OnGUIWM.weaponArray[i].GetShortName();
                            subLabel = OnGUIWM.weaponArray[i].GetSubLabel();
                        }
                        else
                        {
                            label = StringUtils.Localize("#LOC_BDArmory_WMWindow_NoneWeapon");//"None"
                            subLabel = string.Empty;
                        }
                        Rect weaponButtonRect = new Rect(leftIndent, (weaponLines * entryHeight), weaponListGroupRect.width - (2 * leftIndent), entryHeight);

                        GUI.Label(weaponButtonRect, subLabel, tgtStyle);

                        if (GUI.Button(weaponButtonRect, label, wpnListStyle))
                        {
                            OnGUIWM.CycleWeapon(i);
                        }

                        if (i < OnGUIWM.weaponArray.Length - 1)
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
                    GUI.BeginGroup(new Rect(5, contentTop + line * entryHeight, columnWidth - 10, guardHeight * entryHeight), GUIContent.none, SelectedButtonStyle);
                    guardLines += 0.1f;

                    string guardButtonLabel = StringUtils.Localize("#LOC_BDArmory_WMWindow_GuardMode", OnGUIWM.guardMode ? StringUtils.Localize("#LOC_BDArmory_Generic_On") : StringUtils.Localize("#LOC_BDArmory_Generic_Off"));//"Guard Mode " + "ON""Off"
                    if (GUI.Button(ButtonRect(guardLines), guardButtonLabel, OnGUIWM.guardMode ? SelectedButtonStyle : ButtonStyle))
                    {
                        OnGUIWM.ToggleGuardMode();
                    }
                    guardLines += 0.25f;

                    GUI.Label(LabelRect(++guardLines, guardLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_FiringInterval"), leftLabel);//"Firing Interval"                 
                    if (!NumFieldsEnabled)
                    {
                        OnGUIWM.targetScanInterval = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(guardLines, guardLabelWidth), OnGUIWM.targetScanInterval, 0.5f, 60f), 0.5f);
                        GUI.Label(RightLabelRect(guardLines), OnGUIWM.targetScanInterval.ToString(), leftLabel);
                    }
                    else
                    {
                        var field = textNumFields["targetScanInterval"];
                        field.TryParseValue(GUI.TextField(InputFieldRect(guardLines, guardLabelWidth), field.possibleValue, 4, field.style));
                        OnGUIWM.targetScanInterval = (float)field.CurrentValue;
                    }

                    string burstLabel = StringUtils.Localize("#LOC_BDArmory_WMWindow_BurstLength");//"Burst Length"
                    GUI.Label(LabelRect(++guardLines, guardLabelWidth), burstLabel, leftLabel);
                    if (!NumFieldsEnabled)
                    {
                        OnGUIWM.fireBurstLength = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(guardLines, guardLabelWidth), OnGUIWM.fireBurstLength, 0, 10), 0.05f);
                        GUI.Label(RightLabelRect(guardLines), OnGUIWM.fireBurstLength.ToString(), leftLabel);
                    }
                    else
                    {
                        var field = textNumFields["fireBurstLength"];
                        field.TryParseValue(GUI.TextField(InputFieldRect(guardLines, guardLabelWidth), field.possibleValue, 4, field.style));
                        OnGUIWM.fireBurstLength = (float)field.CurrentValue;
                    }

                    // extension for feature_engagementenvelope: set the firing accuracy tolarance
                    var oldAutoFireCosAngleAdjustment = OnGUIWM.AutoFireCosAngleAdjustment;
                    string accuracyLabel = StringUtils.Localize("#LOC_BDArmory_WMWindow_FiringTolerance");//"Firing Angle"
                    GUI.Label(LabelRect(++guardLines, guardLabelWidth), accuracyLabel, leftLabel);
                    if (!NumFieldsEnabled)
                    {
                        OnGUIWM.AutoFireCosAngleAdjustment = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(guardLines, guardLabelWidth), OnGUIWM.AutoFireCosAngleAdjustment, 0, 4), 0.05f);
                        GUI.Label(RightLabelRect(guardLines), OnGUIWM.AutoFireCosAngleAdjustment.ToString(), leftLabel);
                    }
                    else
                    {
                        var field = textNumFields["AutoFireCosAngleAdjustment"];
                        field.TryParseValue(GUI.TextField(InputFieldRect(guardLines, guardLabelWidth), field.possibleValue, 4, field.style));
                        OnGUIWM.AutoFireCosAngleAdjustment = (float)field.CurrentValue;
                    }
                    if (OnGUIWM.AutoFireCosAngleAdjustment != oldAutoFireCosAngleAdjustment)
                        OnGUIWM.OnAFCAAUpdated(null, null);

                    GUI.Label(LabelRect(++guardLines, guardLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_FieldofView"),//"Field of View"
                        leftLabel);
                    if (!NumFieldsEnabled)
                    {
                        OnGUIWM.guardAngle = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(guardLines, guardLabelWidth), OnGUIWM.guardAngle, 10, 360), 0.1f);
                        GUI.Label(RightLabelRect(guardLines), OnGUIWM.guardAngle.ToString(), leftLabel);
                    }
                    else
                    {
                        var field = textNumFields["guardAngle"];
                        field.TryParseValue(GUI.TextField(InputFieldRect(guardLines, guardLabelWidth), field.possibleValue, 4, field.style));
                        OnGUIWM.guardAngle = (float)field.CurrentValue;
                    }

                    GUI.Label(LabelRect(++guardLines, guardLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_VisualRange"), leftLabel);//"Visual Range"
                    if (!NumFieldsEnabled)
                    {
                        OnGUIWM.guardRange = GUIUtils.HorizontalSemiLogSlider(SliderRect(guardLines, guardLabelWidth), OnGUIWM.guardRange, 100, BDArmorySettings.MAX_GUARD_VISUAL_RANGE, 1, false, false, ref cacheGuardRange);
                        GUI.Label(RightLabelRect(guardLines), OnGUIWM.guardRange < 1000 ? $"{OnGUIWM.guardRange:G4}m" : $"{OnGUIWM.guardRange / 1000:G4}km", leftLabel);
                    }
                    else
                    {
                        var field = textNumFields["guardRange"];
                        field.TryParseValue(GUI.TextField(InputFieldRect(guardLines, guardLabelWidth), field.possibleValue, 8, field.style));
                        OnGUIWM.guardRange = (float)field.CurrentValue;
                    }

                    GUI.Label(LabelRect(++guardLines, guardLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_GunsRange"), leftLabel);//"Guns Range"
                    if (!NumFieldsEnabled)
                    {
                        OnGUIWM.gunRange = GUIUtils.HorizontalPowerSlider(SliderRect(guardLines, guardLabelWidth), OnGUIWM.gunRange, 0, OnGUIWM.maxGunRange, 2, 2, ref cacheGunRange);
                        GUI.Label(RightLabelRect(guardLines), OnGUIWM.gunRange < 1000 ? $"{OnGUIWM.gunRange:G4}m" : $"{OnGUIWM.gunRange / 1000:G4}km", leftLabel);
                    }
                    else
                    {
                        var field = textNumFields["gunRange"];
                        field.TryParseValue(GUI.TextField(InputFieldRect(guardLines, guardLabelWidth), field.possibleValue, 8, field.style));
                        OnGUIWM.gunRange = (float)field.CurrentValue;
                    }

                    GUI.Label(LabelRect(++guardLines, guardLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_MultiTargetNum"), leftLabel);//"Max Turret targets "
                    if (!NumFieldsEnabled)
                    {
                        OnGUIWM.multiTargetNum = Mathf.Round(GUI.HorizontalSlider(SliderRect(guardLines, guardLabelWidth), OnGUIWM.multiTargetNum, 1, 10));
                        GUI.Label(RightLabelRect(guardLines), OnGUIWM.multiTargetNum.ToString(), leftLabel);
                    }
                    else
                    {
                        var field = textNumFields["multiTargetNum"];
                        field.TryParseValue(GUI.TextField(InputFieldRect(guardLines, guardLabelWidth), field.possibleValue, 2, field.style));
                        OnGUIWM.multiTargetNum = (float)field.CurrentValue;
                    }

                    GUI.Label(LabelRect(++guardLines, guardLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_MultiMissileNum"), leftLabel);//"Max Turret targets "
                    if (!NumFieldsEnabled)
                    {
                        OnGUIWM.multiMissileTgtNum = Mathf.Round(GUI.HorizontalSlider(SliderRect(guardLines, guardLabelWidth), OnGUIWM.multiMissileTgtNum, 1, 10));
                        GUI.Label(RightLabelRect(guardLines), OnGUIWM.multiMissileTgtNum.ToString(), leftLabel);
                    }
                    else
                    {
                        var field = textNumFields["multiMissileTgtNum"];
                        field.TryParseValue(GUI.TextField(InputFieldRect(guardLines, guardLabelWidth), field.possibleValue, 2, field.style));
                        OnGUIWM.multiMissileTgtNum = (float)field.CurrentValue;
                    }
                    if (!OnGUIWM.advancedMissileTargeting)
                    {
                        GUI.Label(LabelRect(++guardLines, guardLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_MissilesTgt"), leftLabel);//"Missiles/Tgt"
                        if (!NumFieldsEnabled)
                        {
                            OnGUIWM.maxMissilesOnTarget = Mathf.Round(GUI.HorizontalSlider(SliderRect(guardLines, guardLabelWidth), OnGUIWM.maxMissilesOnTarget, 1, MissileFire.maxAllowableMissilesOnTarget));
                            GUI.Label(RightLabelRect(guardLines), OnGUIWM.maxMissilesOnTarget.ToString(), leftLabel);
                        }
                        else
                        {
                            var field = textNumFields["maxMissilesOnTarget"];
                            field.TryParseValue(GUI.TextField(InputFieldRect(guardLines, guardLabelWidth), field.possibleValue, 2, field.style));
                            OnGUIWM.maxMissilesOnTarget = (float)field.CurrentValue;
                        }
                    }
                    else
                    {
                        if (!OnGUIWM.advancedMissileTgtByYield)
                        {
                            GUI.Label(LabelRect(++guardLines, guardLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_MissilesTgt") + ": " + StringUtils.Localize("#LOC_BDArmory_Air"), leftLabel);//"Missiles/Tgt"
                            if (!NumFieldsEnabled)
                            {
                                OnGUIWM.maxMissilesOnTargetAir = Mathf.Round(GUI.HorizontalSlider(SliderRect(guardLines, guardLabelWidth), OnGUIWM.maxMissilesOnTargetAir, 1, MissileFire.maxAllowableMissilesOnTarget));
                                GUI.Label(RightLabelRect(guardLines), OnGUIWM.maxMissilesOnTargetAir.ToString(), leftLabel);
                            }
                            else
                            {
                                var field = textNumFields["maxMissilesOnTargetAir"];
                                field.TryParseValue(GUI.TextField(InputFieldRect(guardLines, guardLabelWidth), field.possibleValue, 2, field.style));
                                OnGUIWM.maxMissilesOnTargetAir = (float)field.CurrentValue;
                            }
                            GUI.Label(LabelRect(++guardLines, guardLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_MissilesTgt") + ": " + StringUtils.Localize("#LOC_BDArmory_Surface"), leftLabel);//"Missiles/Tgt"
                            if (!NumFieldsEnabled)
                            {
                                OnGUIWM.maxMissilesOnTargetSrf = Mathf.Round(GUI.HorizontalSlider(SliderRect(guardLines, guardLabelWidth), OnGUIWM.maxMissilesOnTargetSrf, 1, MissileFire.maxAllowableMissilesOnTarget));
                                GUI.Label(RightLabelRect(guardLines), OnGUIWM.maxMissilesOnTargetSrf.ToString(), leftLabel);
                            }
                            else
                            {
                                var field = textNumFields["maxMissilesOnTargetSrf"];
                                field.TryParseValue(GUI.TextField(InputFieldRect(guardLines, guardLabelWidth), field.possibleValue, 2, field.style));
                                OnGUIWM.maxMissilesOnTargetSrf = (float)field.CurrentValue;
                            }
                            GUI.Label(LabelRect(++guardLines, guardLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_MissilesTgt") + ": " + StringUtils.Localize("#LOC_BDArmory_SLW"), leftLabel);//"Missiles/Tgt"
                            if (!NumFieldsEnabled)
                            {
                                OnGUIWM.maxMissilesOnTargetSea = Mathf.Round(GUI.HorizontalSlider(SliderRect(guardLines, guardLabelWidth), OnGUIWM.maxMissilesOnTargetSea, 1, MissileFire.maxAllowableMissilesOnTarget));
                                GUI.Label(RightLabelRect(guardLines), OnGUIWM.maxMissilesOnTargetSea.ToString(), leftLabel);
                            }
                            else
                            {
                                var field = textNumFields["maxMissilesOnTargetSea"];
                                field.TryParseValue(GUI.TextField(InputFieldRect(guardLines, guardLabelWidth), field.possibleValue, 2, field.style));
                                OnGUIWM.maxMissilesOnTargetSea = (float)field.CurrentValue;
                            }
                            GUI.Label(LabelRect(++guardLines, guardLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_MissilesTgt") + ": " + StringUtils.Localize("#LOC_BDArmory_Missile"), leftLabel);//"Missiles/Tgt"
                            if (!NumFieldsEnabled)
                            {
                                OnGUIWM.maxMissilesOnTargetMsl = Mathf.Round(GUI.HorizontalSlider(SliderRect(guardLines, guardLabelWidth), OnGUIWM.maxMissilesOnTargetMsl, 1, MissileFire.maxAllowableMissilesOnTarget));
                                GUI.Label(RightLabelRect(guardLines), OnGUIWM.maxMissilesOnTargetMsl.ToString(), leftLabel);
                            }
                            else
                            {
                                var field = textNumFields["maxMissilesOnTargetMsl"];
                                field.TryParseValue(GUI.TextField(InputFieldRect(guardLines, guardLabelWidth), field.possibleValue, 2, field.style));
                                OnGUIWM.maxMissilesOnTargetMsl = (float)field.CurrentValue;
                            }
                        }
                        else
                        {
                            GUI.Label(LabelRect(++guardLines, guardLabelWidth), StringUtils.Localize("#LOC_BDArmory_YieldPerTarget") + ": " + StringUtils.Localize("#LOC_BDArmory_Air"), leftLabel);//"Missiles/Tgt"
                            if (!NumFieldsEnabled)
                            {
                                OnGUIWM.maxTNTOnTargetAir = Mathf.Round(GUI.HorizontalSlider(SliderRect(guardLines, guardLabelWidth), OnGUIWM.maxTNTOnTargetAir, 1, 10000));
                                GUI.Label(RightLabelRect(guardLines), OnGUIWM.maxTNTOnTargetAir.ToString(), leftLabel);
                            }
                            else
                            {
                                var field = textNumFields["maxTNTOnTargetAir"];
                                field.TryParseValue(GUI.TextField(InputFieldRect(guardLines, guardLabelWidth), field.possibleValue, 2, field.style));
                                OnGUIWM.maxTNTOnTargetAir = (float)field.CurrentValue;
                            }
                            GUI.Label(LabelRect(++guardLines, guardLabelWidth), StringUtils.Localize("#LOC_BDArmory_YieldPerTarget") + ": " + StringUtils.Localize("#LOC_BDArmory_Surface"), leftLabel);//"Missiles/Tgt"
                            if (!NumFieldsEnabled)
                            {
                                OnGUIWM.maxTNTOnTargetSrf = Mathf.Round(GUI.HorizontalSlider(SliderRect(guardLines, guardLabelWidth), OnGUIWM.maxTNTOnTargetSrf, 1, 10000));
                                GUI.Label(RightLabelRect(guardLines), OnGUIWM.maxTNTOnTargetSrf.ToString(), leftLabel);
                            }
                            else
                            {
                                var field = textNumFields["maxTNTOnTargetSrf"];
                                field.TryParseValue(GUI.TextField(InputFieldRect(guardLines, guardLabelWidth), field.possibleValue, 2, field.style));
                                OnGUIWM.maxTNTOnTargetSrf = (float)field.CurrentValue;
                            }
                            GUI.Label(LabelRect(++guardLines, guardLabelWidth), StringUtils.Localize("#LOC_BDArmory_YieldPerTarget") + ": " + StringUtils.Localize("#LOC_BDArmory_SLW"), leftLabel);//"Missiles/Tgt"
                            if (!NumFieldsEnabled)
                            {
                                OnGUIWM.maxTNTOnTargetSea = Mathf.Round(GUI.HorizontalSlider(SliderRect(guardLines, guardLabelWidth), OnGUIWM.maxTNTOnTargetSea, 1, 10000));
                                GUI.Label(RightLabelRect(guardLines), OnGUIWM.maxTNTOnTargetSea.ToString(), leftLabel);
                            }
                            else
                            {
                                var field = textNumFields["maxTNTOnTargetSea"];
                                field.TryParseValue(GUI.TextField(InputFieldRect(guardLines, guardLabelWidth), field.possibleValue, 2, field.style));
                                OnGUIWM.maxTNTOnTargetSea = (float)field.CurrentValue;
                            }
                            GUI.Label(LabelRect(++guardLines, guardLabelWidth), StringUtils.Localize("#LOC_BDArmory_YieldPerTarget") + ": " + StringUtils.Localize("#LOC_BDArmory_Missile"), leftLabel);//"Missiles/Tgt"
                            if (!NumFieldsEnabled)
                            {
                                OnGUIWM.maxTNTOnTargetMsl = Mathf.Round(GUI.HorizontalSlider(SliderRect(guardLines, guardLabelWidth), OnGUIWM.maxTNTOnTargetMsl, 1, 1000));
                                GUI.Label(RightLabelRect(guardLines), OnGUIWM.maxTNTOnTargetMsl.ToString(), leftLabel);
                            }
                            else
                            {
                                var field = textNumFields["maxTNTOnTargetMsl"];
                                field.TryParseValue(GUI.TextField(InputFieldRect(guardLines, guardLabelWidth), field.possibleValue, 2, field.style));
                                OnGUIWM.maxTNTOnTargetMsl = (float)field.CurrentValue;
                            }
                        }
                        if (GUI.Button(ButtonRect(++guardLines), StringUtils.Localize("#LOC_BDArmory_MultiTargetMslYield_Config"), OnGUIWM.advancedMissileTgtByYield ? SelectedButtonStyle : ButtonStyle))//"Advanced Missile Targeting"
                        {
                            OnGUIWM.advancedMissileTgtByYield = !OnGUIWM.advancedMissileTgtByYield;
                        }
                        guardLines += 0.25f;
                    }
                    if (GUI.Button(ButtonRect(++guardLines), StringUtils.Localize("#LOC_BDArmory_MultiTargetMsl_Config"), OnGUIWM.advancedMissileTargeting ? SelectedButtonStyle : ButtonStyle))//"Advanced Missile Targeting"
                    {
                        OnGUIWM.advancedMissileTargeting = !OnGUIWM.advancedMissileTargeting;
                    }
                    guardLines += 0.25f; 
                    showTargetOptions = GUI.Toggle(ButtonRect(++guardLines), showTargetOptions, StringUtils.Localize("#LOC_BDArmory_Settings_Adv_Targeting"), showTargetOptions ? SelectedButtonStyle : ButtonStyle);//"Advanced Targeting"
                    guardLines += 0.25f;

                    float TargetLines = 0;
                    if (showTargetOptions && showGuardMenu && !toolMinimized)
                    {
                        contentWidth = columnWidth - 30;
                        guardLines += 0.25f;
                        GUI.BeginGroup(new Rect(10, contentTop + (guardLines * entryHeight), contentWidth, (TargetingHeight + 0.25f) * entryHeight), GUIContent.none, SelectedButtonStyle);
                        TargetLines += 0.25f;
                        string CoMlabel = StringUtils.Localize("#LOC_BDArmory_TargetCOM", (OnGUIWM.targetCoM ? StringUtils.Localize("#LOC_BDArmory_false") : StringUtils.Localize("#LOC_BDArmory_true")));//"Engage Air; True, False
                        if (GUI.Button(new Rect(leftIndent, TargetLines * entryHeight, contentWidth - 2 * leftIndent, entryHeight), CoMlabel, OnGUIWM.targetCoM ? SelectedButtonStyle : ButtonStyle))
                        {
                            OnGUIWM.targetCoM = !OnGUIWM.targetCoM;
                            OnGUIWM.StartGuardTurretFiring(); //reset weapon targeting assignments
                            if (OnGUIWM.targetCoM)
                            {
                                OnGUIWM.targetCommand = false;
                                OnGUIWM.targetEngine = false;
                                OnGUIWM.targetWeapon = false;
                                OnGUIWM.targetMass = false;
                                OnGUIWM.targetRandom = false;
                            }
                            if (!OnGUIWM.targetCoM && (!OnGUIWM.targetWeapon && !OnGUIWM.targetEngine && !OnGUIWM.targetCommand && !OnGUIWM.targetMass && !OnGUIWM.targetRandom))
                            {
                                OnGUIWM.targetRandom = true;
                            }
                        }
                        TargetLines += 1.1f;
                        string Commandlabel = StringUtils.Localize("#LOC_BDArmory_Command", (OnGUIWM.targetCommand ? StringUtils.Localize("#LOC_BDArmory_false") : StringUtils.Localize("#LOC_BDArmory_true")));//"Engage Air; True, False
                        if (GUI.Button(new Rect(leftIndent, TargetLines * entryHeight, (contentWidth - 2 * leftIndent) / 2, entryHeight), Commandlabel, OnGUIWM.targetCommand ? SelectedButtonStyle : ButtonStyle))
                        {
                            OnGUIWM.targetCommand = !OnGUIWM.targetCommand;
                            OnGUIWM.StartGuardTurretFiring();
                            if (OnGUIWM.targetCommand)
                            {
                                OnGUIWM.targetCoM = false;
                            }
                            if (!OnGUIWM.targetCoM && (!OnGUIWM.targetWeapon && !OnGUIWM.targetEngine && !OnGUIWM.targetCommand && !OnGUIWM.targetMass && !OnGUIWM.targetRandom))
                            {
                                OnGUIWM.targetCoM = true;
                            }
                        }
                        string Engineslabel = StringUtils.Localize("#LOC_BDArmory_Engines", (OnGUIWM.targetEngine ? StringUtils.Localize("#LOC_BDArmory_false") : StringUtils.Localize("#LOC_BDArmory_true")));//"Engage Missile; True, False
                        if (GUI.Button(new Rect(leftIndent + (contentWidth - 2 * leftIndent) / 2, TargetLines * entryHeight, (contentWidth - 2 * leftIndent) / 2, entryHeight), Engineslabel, OnGUIWM.targetEngine ? SelectedButtonStyle : ButtonStyle))
                        {
                            OnGUIWM.targetEngine = !OnGUIWM.targetEngine;
                            OnGUIWM.StartGuardTurretFiring();
                            if (OnGUIWM.targetEngine)
                            {
                                OnGUIWM.targetCoM = false;
                            }
                            if (!OnGUIWM.targetCoM && (!OnGUIWM.targetWeapon && !OnGUIWM.targetEngine && !OnGUIWM.targetCommand && !OnGUIWM.targetMass && !OnGUIWM.targetRandom))
                            {
                                OnGUIWM.targetCoM = true;
                            }
                        }
                        TargetLines += 1.1f;
                        string Weaponslabel = StringUtils.Localize("#LOC_BDArmory_Weapons", (OnGUIWM.targetWeapon ? StringUtils.Localize("#LOC_BDArmory_false") : StringUtils.Localize("#LOC_BDArmory_true")));//"Engage Surface; True, False
                        if (GUI.Button(new Rect(leftIndent, TargetLines * entryHeight, (contentWidth - 2 * leftIndent) / 2, entryHeight), Weaponslabel, OnGUIWM.targetWeapon ? SelectedButtonStyle : ButtonStyle))
                        {
                            OnGUIWM.targetWeapon = !OnGUIWM.targetWeapon;
                            OnGUIWM.StartGuardTurretFiring();
                            if (OnGUIWM.targetWeapon)
                            {
                                OnGUIWM.targetCoM = false;
                            }
                            if (!OnGUIWM.targetCoM && (!OnGUIWM.targetWeapon && !OnGUIWM.targetEngine && !OnGUIWM.targetCommand && !OnGUIWM.targetMass && !OnGUIWM.targetRandom))
                            {
                                OnGUIWM.targetCoM = true;
                            }
                        }
                        string Masslabel = StringUtils.Localize("#LOC_BDArmory_Mass", (OnGUIWM.targetMass ? StringUtils.Localize("#LOC_BDArmory_false") : StringUtils.Localize("#LOC_BDArmory_true")));//"Engage SLW; True, False
                        if (GUI.Button(new Rect(leftIndent + (contentWidth - 2 * leftIndent) / 2, TargetLines * entryHeight, (contentWidth - 2 * leftIndent) / 2, entryHeight), Masslabel, OnGUIWM.targetMass ? SelectedButtonStyle : ButtonStyle))
                        {
                            OnGUIWM.targetMass = !OnGUIWM.targetMass;
                            OnGUIWM.StartGuardTurretFiring();
                            if (OnGUIWM.targetMass)
                            {
                                OnGUIWM.targetCoM = false;
                            }
                            if (!OnGUIWM.targetCoM && (!OnGUIWM.targetWeapon && !OnGUIWM.targetEngine && !OnGUIWM.targetCommand && !OnGUIWM.targetMass && !OnGUIWM.targetRandom))
                            {
                                OnGUIWM.targetCoM = true;
                            }
                        }
                        TargetLines += 1.1f;
                        string Randomlabel = StringUtils.Localize("#LOC_BDArmory_Random", (OnGUIWM.targetRandom ? StringUtils.Localize("#LOC_BDArmory_false") : StringUtils.Localize("#LOC_BDArmory_true")));//"Engage Surface; True, False
                        if (GUI.Button(new Rect(leftIndent, TargetLines * entryHeight, (contentWidth - 2 * leftIndent) / 2, entryHeight), Randomlabel, OnGUIWM.targetRandom ? SelectedButtonStyle : ButtonStyle))
                        {
                            OnGUIWM.targetRandom = !OnGUIWM.targetRandom;
                            OnGUIWM.StartGuardTurretFiring();
                            if (OnGUIWM.targetRandom)
                            {
                                OnGUIWM.targetCoM = false;
                            }
                            if (!OnGUIWM.targetCoM && (!OnGUIWM.targetWeapon && !OnGUIWM.targetEngine && !OnGUIWM.targetCommand && !OnGUIWM.targetMass && !OnGUIWM.targetRandom))
                            {
                                OnGUIWM.targetCoM = true;
                            }
                        }
                        TargetLines += 1.1f;
                        OnGUIWM.targetingString = (OnGUIWM.targetCoM ? StringUtils.Localize("#LOC_BDArmory_TargetCOM") + "; " : "")
                            + (OnGUIWM.targetMass ? StringUtils.Localize("#LOC_BDArmory_Mass") + "; " : "")
                            + (OnGUIWM.targetCommand ? StringUtils.Localize("#LOC_BDArmory_Command") + "; " : "")
                            + (OnGUIWM.targetEngine ? StringUtils.Localize("#LOC_BDArmory_Engines") + "; " : "")
                            + (OnGUIWM.targetWeapon ? StringUtils.Localize("#LOC_BDArmory_Weapons") + "; " : "")
                            + (OnGUIWM.targetWeapon ? StringUtils.Localize("#LOC_BDArmory_Random") + "; " : "");
                        GUI.EndGroup();
                    }
                    TargetingHeight = Mathf.Lerp(TargetingHeight, TargetLines, 0.15f);
                    guardLines += TargetingHeight;

                    showEngageList = GUI.Toggle(ButtonRect(++guardLines), showEngageList, showEngageList ? StringUtils.Localize("#LOC_BDArmory_DisableEngageOptions") : StringUtils.Localize("#LOC_BDArmory_EnableEngageOptions"), showEngageList ? SelectedButtonStyle : ButtonStyle);//"Enable/Disable Engagement options"
                    guardLines += 0.25f;

                    float EngageLines = 0;
                    if (showEngageList && showGuardMenu && !toolMinimized)
                    {
                        contentWidth = columnWidth - 30;
                        guardLines += 0.25f;
                        GUI.BeginGroup(new Rect(10, contentTop + guardLines * entryHeight, contentWidth, (EngageHeight + 0.25f) * entryHeight), GUIContent.none, SelectedButtonStyle);
                        EngageLines += 0.25f;

                        string Airlabel = StringUtils.Localize("#LOC_BDArmory_EngageAir", (OnGUIWM.engageAir ? StringUtils.Localize("#LOC_BDArmory_false") : StringUtils.Localize("#LOC_BDArmory_true")));//"Engage Air; True, False
                        if (GUI.Button(new Rect(leftIndent, EngageLines * entryHeight, (contentWidth - 2 * leftIndent) / 2, entryHeight), Airlabel, OnGUIWM.engageAir ? SelectedButtonStyle : ButtonStyle))
                        {
                            OnGUIWM.ToggleEngageAir();
                        }
                        string Missilelabel = StringUtils.Localize("#LOC_BDArmory_EngageMissile", (OnGUIWM.engageMissile ? StringUtils.Localize("#LOC_BDArmory_false") : StringUtils.Localize("#LOC_BDArmory_true")));//"Engage Missile; True, False
                        if (GUI.Button(new Rect(leftIndent + (contentWidth - 2 * leftIndent) / 2, EngageLines * entryHeight, (contentWidth - 2 * leftIndent) / 2, entryHeight), Missilelabel, OnGUIWM.engageMissile ? SelectedButtonStyle : ButtonStyle))
                        {
                            OnGUIWM.ToggleEngageMissile();
                        }
                        EngageLines += 1.1f;
                        string Srflabel = StringUtils.Localize("#LOC_BDArmory_EngageSurface", (OnGUIWM.engageSrf ? StringUtils.Localize("#LOC_BDArmory_false") : StringUtils.Localize("#LOC_BDArmory_true")));//"Engage Surface; True, False
                        if (GUI.Button(new Rect(leftIndent, EngageLines * entryHeight, (contentWidth - 2 * leftIndent) / 2, entryHeight), Srflabel, OnGUIWM.engageSrf ? SelectedButtonStyle : ButtonStyle))
                        {
                            OnGUIWM.ToggleEngageSrf();
                        }

                        string SLWlabel = StringUtils.Localize("#LOC_BDArmory_EngageSLW", (OnGUIWM.engageSLW ? StringUtils.Localize("#LOC_BDArmory_false") : StringUtils.Localize("#LOC_BDArmory_true")));//"Engage SLW; True, False
                        if (GUI.Button(new Rect(leftIndent + (contentWidth - 2 * leftIndent) / 2, EngageLines * entryHeight, (contentWidth - 2 * leftIndent) / 2, entryHeight), SLWlabel, OnGUIWM.engageSLW ? SelectedButtonStyle : ButtonStyle))
                        {
                            OnGUIWM.ToggleEngageSLW();
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
                    GUI.BeginGroup(new Rect(5, contentTop + line * entryHeight, columnWidth - 10, priorityheight * entryHeight), GUIContent.none, SelectedButtonStyle);
                    priorityLines += 0.1f;

                    GUI.Label(LabelRect(++priorityLines, priorityLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_targetBias"), leftLabel);//"current target bias"                 
                    if (!NumFieldsEnabled)
                    {
                        OnGUIWM.targetBias = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(priorityLines, priorityLabelWidth), OnGUIWM.targetBias, 0, 10), 0.1f);
                        GUI.Label(RightLabelRect(priorityLines), OnGUIWM.targetBias.ToString(), leftLabel);
                    }
                    else
                    {
                        var field = textNumFields["targetBias"];
                        field.TryParseValue(GUI.TextField(InputFieldRect(priorityLines, priorityLabelWidth), field.possibleValue, 4, field.style));
                        OnGUIWM.targetBias = (float)field.CurrentValue;
                    }

                    GUI.Label(LabelRect(++priorityLines, priorityLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_targetProximity"), leftLabel); //target proximity"
                    if (!NumFieldsEnabled)
                    {
                        OnGUIWM.targetWeightRange = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(priorityLines, priorityLabelWidth), OnGUIWM.targetWeightRange, -10, 10), 0.1f);
                        GUI.Label(RightLabelRect(priorityLines), OnGUIWM.targetWeightRange.ToString(), leftLabel);
                    }
                    else
                    {
                        var field = textNumFields["targetWeightRange"];
                        field.TryParseValue(GUI.TextField(InputFieldRect(priorityLines, priorityLabelWidth), field.possibleValue, 4, field.style));
                        OnGUIWM.targetWeightRange = (float)field.CurrentValue;
                    }

                    GUI.Label(LabelRect(++priorityLines, priorityLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_targetPreference"), leftLabel); //target Air preference"
                    if (!NumFieldsEnabled)
                    {
                        OnGUIWM.targetWeightAirPreference = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(priorityLines, priorityLabelWidth), OnGUIWM.targetWeightAirPreference, -10, 10), 0.1f);
                        GUI.Label(RightLabelRect(priorityLines), OnGUIWM.targetWeightAirPreference.ToString(), leftLabel);
                    }
                    else
                    {
                        var field = textNumFields["targetWeightAirPreference"];
                        field.TryParseValue(GUI.TextField(InputFieldRect(priorityLines, priorityLabelWidth), field.possibleValue, 4, field.style));
                        OnGUIWM.targetWeightAirPreference = (float)field.CurrentValue;
                    }

                    GUI.Label(LabelRect(++priorityLines, priorityLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_targetAngletoTarget"), leftLabel); //target angle"
                    if (!NumFieldsEnabled)
                    {
                        OnGUIWM.targetWeightATA = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(priorityLines, priorityLabelWidth), OnGUIWM.targetWeightATA, -10, 10), 0.1f);
                        GUI.Label(RightLabelRect(priorityLines), OnGUIWM.targetWeightATA.ToString(), leftLabel);
                    }
                    else
                    {
                        var field = textNumFields["targetWeightATA"];
                        field.TryParseValue(GUI.TextField(InputFieldRect(priorityLines, priorityLabelWidth), field.possibleValue, 4, field.style));
                        OnGUIWM.targetWeightATA = (float)field.CurrentValue;
                    }

                    GUI.Label(LabelRect(++priorityLines, priorityLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_targetAngleDist"), leftLabel); //Angle over Distance"
                    if (!NumFieldsEnabled)
                    {
                        OnGUIWM.targetWeightAoD = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(priorityLines, priorityLabelWidth), OnGUIWM.targetWeightAoD, -10, 10), 0.1f);
                        GUI.Label(RightLabelRect(priorityLines), OnGUIWM.targetWeightAoD.ToString(), leftLabel);
                    }
                    else
                    {
                        var field = textNumFields["targetWeightAoD"];
                        field.TryParseValue(GUI.TextField(InputFieldRect(priorityLines, priorityLabelWidth), field.possibleValue, 4, field.style));
                        OnGUIWM.targetWeightAoD = (float)field.CurrentValue;
                    }

                    GUI.Label(LabelRect(++priorityLines, priorityLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_targetAccel"), leftLabel); //target accel"
                    if (!NumFieldsEnabled)
                    {
                        OnGUIWM.targetWeightAccel = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(priorityLines, priorityLabelWidth), OnGUIWM.targetWeightAccel, -10, 10), 0.1f);
                        GUI.Label(RightLabelRect(priorityLines), OnGUIWM.targetWeightAccel.ToString(), leftLabel);
                    }
                    else
                    {
                        var field = textNumFields["targetWeightAccel"];
                        field.TryParseValue(GUI.TextField(InputFieldRect(priorityLines, priorityLabelWidth), field.possibleValue, 4, field.style));
                        OnGUIWM.targetWeightAccel = (float)field.CurrentValue;
                    }

                    GUI.Label(LabelRect(++priorityLines, priorityLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_targetClosingTime"), leftLabel); //target closing time"
                    if (!NumFieldsEnabled)
                    {
                        OnGUIWM.targetWeightClosureTime = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(priorityLines, priorityLabelWidth), OnGUIWM.targetWeightClosureTime, -10, 10), 0.1f);
                        GUI.Label(RightLabelRect(priorityLines), OnGUIWM.targetWeightClosureTime.ToString(), leftLabel);
                    }
                    else
                    {
                        var field = textNumFields["targetWeightClosureTime"];
                        field.TryParseValue(GUI.TextField(InputFieldRect(priorityLines, priorityLabelWidth), field.possibleValue, 4, field.style));
                        OnGUIWM.targetWeightClosureTime = (float)field.CurrentValue;
                    }

                    GUI.Label(LabelRect(++priorityLines, priorityLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_targetgunNumber"), leftLabel); //target weapon num."
                    if (!NumFieldsEnabled)
                    {
                        OnGUIWM.targetWeightWeaponNumber = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(priorityLines, priorityLabelWidth), OnGUIWM.targetWeightWeaponNumber, -10, 10), 0.1f);
                        GUI.Label(RightLabelRect(priorityLines), OnGUIWM.targetWeightWeaponNumber.ToString(), leftLabel);
                    }
                    else
                    {
                        var field = textNumFields["targetWeightWeaponNumber"];
                        field.TryParseValue(GUI.TextField(InputFieldRect(priorityLines, priorityLabelWidth), field.possibleValue, 4, field.style));
                        OnGUIWM.targetWeightWeaponNumber = (float)field.CurrentValue;
                    }

                    GUI.Label(LabelRect(++priorityLines, priorityLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_targetMass"), leftLabel); //target mass"
                    if (!NumFieldsEnabled)
                    {
                        OnGUIWM.targetWeightMass = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(priorityLines, priorityLabelWidth), OnGUIWM.targetWeightMass, -10, 10), 0.1f);
                        GUI.Label(RightLabelRect(priorityLines), OnGUIWM.targetWeightMass.ToString(), leftLabel);
                    }
                    else
                    {
                        var field = textNumFields["targetWeightMass"];
                        field.TryParseValue(GUI.TextField(InputFieldRect(priorityLines, priorityLabelWidth), field.possibleValue, 4, field.style));
                        OnGUIWM.targetWeightMass = (float)field.CurrentValue;
                    }

                    GUI.Label(LabelRect(++priorityLines, priorityLabelWidth), StringUtils.Localize("#LOC_BDArmory_TargetPriority_TargetDmg"), leftLabel); //target Damage"
                    if (!NumFieldsEnabled)
                    {
                        OnGUIWM.targetWeightDamage = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(priorityLines, priorityLabelWidth), OnGUIWM.targetWeightDamage, -10, 10), 0.1f);
                        GUI.Label(RightLabelRect(priorityLines), OnGUIWM.targetWeightDamage.ToString(), leftLabel);
                    }
                    else
                    {
                        var field = textNumFields["targetWeightDamage"];
                        field.TryParseValue(GUI.TextField(InputFieldRect(priorityLines, priorityLabelWidth), field.possibleValue, 4, field.style));
                        OnGUIWM.targetWeightDamage = (float)field.CurrentValue;
                    }

                    GUI.Label(LabelRect(++priorityLines, priorityLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_targetAllies"), leftLabel); //target mass"
                    if (!NumFieldsEnabled)
                    {
                        OnGUIWM.targetWeightFriendliesEngaging = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(priorityLines, priorityLabelWidth), OnGUIWM.targetWeightFriendliesEngaging, -10, 10), 0.1f);
                        GUI.Label(RightLabelRect(priorityLines), OnGUIWM.targetWeightFriendliesEngaging.ToString(), leftLabel);
                    }
                    else
                    {
                        var field = textNumFields["targetWeightFriendliesEngaging"];
                        field.TryParseValue(GUI.TextField(InputFieldRect(priorityLines, priorityLabelWidth), field.possibleValue, 4, field.style));
                        OnGUIWM.targetWeightFriendliesEngaging = (float)field.CurrentValue;
                    }

                    GUI.Label(LabelRect(++priorityLines, priorityLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_targetThreat"), leftLabel); //target proximity"
                    if (!NumFieldsEnabled)
                    {
                        OnGUIWM.targetWeightThreat = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(priorityLines, priorityLabelWidth), OnGUIWM.targetWeightThreat, -10, 10), 0.1f);
                        GUI.Label(RightLabelRect(priorityLines), OnGUIWM.targetWeightThreat.ToString(), leftLabel);
                    }
                    else
                    {
                        var field = textNumFields["targetWeightThreat"];
                        field.TryParseValue(GUI.TextField(InputFieldRect(priorityLines, priorityLabelWidth), field.possibleValue, 4, field.style));
                        OnGUIWM.targetWeightThreat = (float)field.CurrentValue;
                    }

                    GUI.Label(LabelRect(++priorityLines, priorityLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_defendTeammate"), leftLabel); //defend teammate"
                    if (!NumFieldsEnabled)
                    {
                        OnGUIWM.targetWeightProtectTeammate = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(priorityLines, priorityLabelWidth), OnGUIWM.targetWeightProtectTeammate, -10, 10), 0.1f);
                        GUI.Label(RightLabelRect(priorityLines), OnGUIWM.targetWeightProtectTeammate.ToString(), leftLabel);
                    }
                    else
                    {
                        var field = textNumFields["targetWeightProtectTeammate"];
                        field.TryParseValue(GUI.TextField(InputFieldRect(priorityLines, priorityLabelWidth), field.possibleValue, 4, field.style));
                        OnGUIWM.targetWeightProtectTeammate = (float)field.CurrentValue;
                    }

                    GUI.Label(LabelRect(++priorityLines, priorityLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_defendVIP"), leftLabel); //target proximity"
                    if (!NumFieldsEnabled)
                    {
                        OnGUIWM.targetWeightProtectVIP = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(priorityLines, priorityLabelWidth), OnGUIWM.targetWeightProtectVIP, -10, 10), 0.1f);
                        GUI.Label(RightLabelRect(priorityLines), OnGUIWM.targetWeightProtectVIP.ToString(), leftLabel);
                    }
                    else
                    {
                        var field = textNumFields["targetWeightProtectVIP"];
                        field.TryParseValue(GUI.TextField(InputFieldRect(priorityLines, priorityLabelWidth), field.possibleValue, 4, field.style));
                        OnGUIWM.targetWeightProtectVIP = (float)field.CurrentValue;
                    }

                    GUI.Label(LabelRect(++priorityLines, priorityLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_targetVIP"), leftLabel); //target proximity"
                    if (!NumFieldsEnabled)
                    {
                        OnGUIWM.targetWeightAttackVIP = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(priorityLines, priorityLabelWidth), OnGUIWM.targetWeightAttackVIP, -10, 10), 0.1f);
                        GUI.Label(RightLabelRect(priorityLines), OnGUIWM.targetWeightAttackVIP.ToString(), leftLabel);
                    }
                    else
                    {
                        var field = textNumFields["targetWeightAttackVIP"];
                        field.TryParseValue(GUI.TextField(InputFieldRect(priorityLines, priorityLabelWidth), field.possibleValue, 4, field.style));
                        OnGUIWM.targetWeightAttackVIP = (float)field.CurrentValue;
                    }

                    GUI.Label(LabelRect(++priorityLines, priorityLabelWidth), StringUtils.Localize("#LOC_BDArmory_WMWindow_targetUncontrolled"), leftLabel); //target controllable
                    if (!NumFieldsEnabled)
                    {
                        OnGUIWM.targetWeightUncontrolled = BDAMath.RoundToUnit(GUI.HorizontalSlider(SliderRect(priorityLines, priorityLabelWidth), OnGUIWM.targetWeightUncontrolled, -10, 10), 0.1f);
                        GUI.Label(RightLabelRect(priorityLines), OnGUIWM.targetWeightUncontrolled.ToString(), leftLabel);
                    }
                    else
                    {
                        var field = textNumFields["targetWeightUncontrolled"];
                        field.TryParseValue(GUI.TextField(InputFieldRect(priorityLines, priorityLabelWidth), field.possibleValue, 4, field.style));
                        OnGUIWM.targetWeightUncontrolled = (float)field.CurrentValue;
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
                        GUIContent.none, SelectedButtonStyle);
                    moduleLines += 0.1f;

                    numberOfModules = 0;

                    if (OnGUIWM.radars.Count > 0)
                    {
                        numberOfModules++;
                        string Radarlabel = $"{StringUtils.Localize("#LOC_BDArmory_DynamicRadar")}: {(!OnGUIWM.DynamicRadarOverride ? StringUtils.Localize("#LOC_BDArmory_false") : StringUtils.Localize("#LOC_BDArmory_true"))}";//"Dynamic Radar vs ARMs: True, False
                        if (GUI.Button(new Rect(leftIndent, +(moduleLines * entryHeight), columnWidth - 2 * leftIndent, entryHeight), Radarlabel, OnGUIWM.DynamicRadarOverride ? ButtonStyle : SelectedButtonStyle))
                        {
                            OnGUIWM.DynamicRadarOverride = !OnGUIWM.DynamicRadarOverride;
                        }
                        moduleLines += 1.1f;
                    }

                    if (OnGUIWM.HMD && OnGUIWM.hasHMD)
                    {
                        numberOfModules++;
                        bool isEnabled = OnGUIWM.isHMDEnabled;
                        string label = StringUtils.Localize("#LOC_BDArmory_WMWindow_HMD");
                        Rect HMDRect = new Rect(leftIndent, +(moduleLines * entryHeight), columnWidth - 2 * leftIndent, entryHeight);
                        if (GUI.Button(HMDRect, label, isEnabled ? centerLabelOrange : centerLabel))
                        {
                            OnGUIWM.ToggleHMD();
                        }
                        moduleLines++;
                    }

                    //RWR
                    if (OnGUIWM.rwr)
                    {
                        numberOfModules++;
                        bool isEnabled = OnGUIWM.rwr.displayRWR;
                        string label = StringUtils.Localize("#LOC_BDArmory_WMWindow_RadarWarning");//"Radar Warning Receiver"
                        Rect rwrRect = new Rect(leftIndent, +(moduleLines * entryHeight), columnWidth - 2 * leftIndent, entryHeight);
                        if (GUI.Button(rwrRect, label, isEnabled ? centerLabelOrange : centerLabel))
                        {
                            if (isEnabled)
                            {
                                //OnGUIWM.rwr.DisableRWR();
                                OnGUIWM.rwr.displayRWR = false;
                            }
                            else
                            {
                                //OnGUIWM.rwr.EnableRWR();
                                OnGUIWM.rwr.displayRWR = true;
                            }
                        }
                        moduleLines++;
                    }

                    //TGP
                    using (List<ModuleTargetingCamera>.Enumerator mtc = OnGUIWM.targetingPods.GetEnumerator())
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
                    using (List<ModuleRadar>.Enumerator mr = OnGUIWM.radars.GetEnumerator())
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
                    using (List<ModuleIRST>.Enumerator mr = OnGUIWM.irsts.GetEnumerator())
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
                    using (List<ModuleECMJammer>.Enumerator jammer = OnGUIWM.jammers.GetEnumerator())
                        while (jammer.MoveNext())
                        {
                            if (jammer.Current == null) continue;
                            if (jammer.Current.isMissileECM) continue;
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
                    using (List<ModuleCloakingDevice>.Enumerator cloak = OnGUIWM.cloaks.GetEnumerator())
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
                    using (var module = OnGUIWM.wmModules.GetEnumerator())
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
                    if (OnGUIWM.wingCommander)
                    {
                        GUIStyle wingComStyle = OnGUIWM.wingCommander.showGUI
                            ? centerLabelBlue
                            : centerLabel;
                        numberOfModules++;
                        if (GUI.Button(new Rect(leftIndent, +(moduleLines * entryHeight), columnWidth - 2 * leftIndent, entryHeight),
                            StringUtils.Localize("#LOC_BDArmory_WMWindow_WingCommand"), wingComStyle))//"Wing Command"
                        {
                            OnGUIWM.wingCommander.ToggleGUI();
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
                   StringUtils.Localize("#LOC_BDArmory_WMWindow_NoWeaponManager"), SelectedButtonStyle);// "No Weapon Manager found."
                line++;
            }
#if DEBUG
            if (GUI.Button(new Rect(leftIndent, contentTop + (line++ * entryHeight), contentWidth, entryHeight * 1.25f), "Double click to QUIT", Time.realtimeSinceStartup - quitTimer > 1 ? ButtonStyle : SelectedButtonStyle)) // Big QUIT button for debug mode. Double click within 1s to quit.
            {
                if (Time.realtimeSinceStartup - quitTimer < 1)
                {
                    SaveConfig();
                    TournamentAutoResume.AutoQuit(0);
                }
                quitTimer = Time.realtimeSinceStartup;
            }
#endif
            var previousWindowHeight = toolWindowHeight;
            toolWindowWidth = Mathf.Lerp(toolWindowWidth, columnWidth * windowColumns, 0.15f);
            toolWindowHeight = Mathf.Lerp(toolWindowHeight, contentTop + (line * entryHeight) + 5, 1);
            WindowRectToolbar.height = toolWindowHeight;
            WindowRectToolbar.width = toolWindowWidth;
            numberOfButtons = buttonNumber + 1;
            GUIUtils.DragWindow();
            GUIUtils.RepositionWindow(ref WindowRectToolbar, previousWindowHeight);
        }
#if DEBUG
        float quitTimer = 0;
#endif

        bool validGPSName = true;

        //GPS window
        public void WindowGPS()
        {
            GUI.Box(WindowRectGps, GUIContent.none, SelectedButtonStyle);
            gpsEntryCount = 0;
            Rect listRect = new Rect(gpsBorder, gpsBorder, WindowRectGps.width - (2 * gpsBorder),
                WindowRectGps.height - (2 * gpsBorder));
            GUI.BeginGroup(listRect);
            string targetLabel = $"{StringUtils.Localize("#LOC_BDArmory_WMWindow_GPSTarget")}: {OnGUIWM.designatedGPSInfo.name}";//GPS Target
            GUI.Label(new Rect(0, 0, listRect.width, gpsEntryHeight), targetLabel, kspTitleLabel);

            // Expand/Collapse Target Toggle button
            if (GUI.Button(new Rect(listRect.width - gpsEntryHeight, 0, gpsEntryHeight, gpsEntryHeight), showTargets ? "-" : "+", ButtonStyle))
                showTargets = !showTargets;

            gpsEntryCount += 0.85f;
            if (OnGUIWM.designatedGPSCoords != Vector3d.zero)
            {
                GUI.Label(new Rect(0, gpsEntryCount * gpsEntryHeight, listRect.width - gpsEntryHeight, gpsEntryHeight),
                    BodyUtils.FormattedGeoPos(OnGUIWM.designatedGPSCoords, true), SelectedButtonStyle);
                if (
                    GUI.Button(
                        new Rect(listRect.width - gpsEntryHeight, gpsEntryCount * gpsEntryHeight, gpsEntryHeight,
                            gpsEntryHeight), " X", ButtonStyle))
                {
                    OnGUIWM.designatedGPSInfo = new GPSTargetInfo();
                }
            }
            else
            {
                GUI.Label(new Rect(0, gpsEntryCount * gpsEntryHeight, listRect.width - gpsEntryHeight, gpsEntryHeight),
                    StringUtils.Localize("#LOC_BDArmory_WMWindow_NoTarget"), SelectedButtonStyle);//"No Target"
            }

            gpsEntryCount += 1.35f;
            int indexToRemove = -1;
            int index = 0;
            BDTeam myTeam = OnGUIWM.Team;
            if (showTargets)
            {
                using (var coordinate = BDATargetManager.GPSTargetList(myTeam).GetEnumerator())
                    while (coordinate.MoveNext())
                    {
                        Color origWColor = GUI.color;
                        if (coordinate.Current.EqualsTarget(OnGUIWM.designatedGPSInfo))
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
                                  new Rect(0, gpsEntryCount * gpsEntryHeight, nameWidth, gpsEntryHeight), newGPSName, 12, textFieldStyle);
                                GUI.color = origColor;
                            }
                        }
                        else
                        {
                            if (GUI.Button(new Rect(0, gpsEntryCount * gpsEntryHeight, nameWidth, gpsEntryHeight),
                              coordinate.Current.name,
                              ButtonStyle))
                            {
                                editingGPSName = true;
                                editingGPSNameIndex = index;
                                newGPSName = coordinate.Current.name;
                            }
                        }

                        if (
                          GUI.Button(
                            new Rect(nameWidth, gpsEntryCount * gpsEntryHeight, listRect.width - gpsEntryHeight - nameWidth,
                              gpsEntryHeight), label, ButtonStyle))
                        {
                            OnGUIWM.designatedGPSInfo = coordinate.Current;
                            OnGUIWM.designatedGPSCoordsIndex = index;
                            editingGPSName = false;
                        }

                        if (
                          GUI.Button(
                            new Rect(listRect.width - gpsEntryHeight, gpsEntryCount * gpsEntryHeight, gpsEntryHeight,
                              gpsEntryHeight), "X", ButtonStyle))
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
                if (OnGUIWM.designatedGPSInfo.EqualsTarget(old))
                {
                    OnGUIWM.designatedGPSInfo.name = newGPSName;
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

        Rect SQuarterRect(float line, float pos, float span = 1)
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
        bool scalingUI = false;
        float oldUIScale = 1;
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

        (float, float)[] asteroidFieldAltitude;
        void WindowSettings(int windowID)
        {
            float line = 0.25f; // Top internal margin.
            GUI.Box(new Rect(0, 0, settingsWidth, settingsHeight), GUIContent.none);
            GUI.Label(new Rect(0, 2, settingsWidth, 22), $"{StringUtils.Localize("#LOC_BDArmory_Settings_Title")}   {Version}", settingsTitleStyle);//"BDArmory Settings"
            if (GUI.Button(new Rect(settingsWidth - 24, 2, 22, 22), " X", CloseButtonStyleDark))
            {
                windowSettingsEnabled = false;
            }
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
                GUI.Label(SQuarterRect(++line, 0), $"{StringUtils.Localize("#LOC_BDArmory_Settings_UIScale")}: {BDArmorySettings.UI_SCALE_ACTUAL:0.00}x", leftLabel); // UI Scale
                BDArmorySettings.UI_SCALE_FOLLOWS_STOCK = GUI.Toggle(SQuarterRect(line, 1), BDArmorySettings.UI_SCALE_FOLLOWS_STOCK, $"{StringUtils.Localize("#LOC_BDArmory_Settings_UIScaleFollowsStock")}");
                if (!BDArmorySettings.UI_SCALE_FOLLOWS_STOCK)
                {
                    if (BDArmorySettings.UI_SCALE != (BDArmorySettings.UI_SCALE = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.UI_SCALE, 0.5f, 2f), 0.05f)))
                    {
                        scalingUI = true;
                        BDACompetitionMode.Instance.UpdateGUIElements();
                    }
                }

                BDArmorySettings.DRAW_AIMERS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.DRAW_AIMERS, StringUtils.Localize("#LOC_BDArmory_Settings_DrawAimers"));//"Draw Aimers"

                if (!BDArmorySettings.ADVANCED_USER_SETTINGS)
                {
                    BDArmorySettings.BULLET_HITS = GUI.Toggle(SRightRect(line), BDArmorySettings.BULLET_HITS, StringUtils.Localize("#LOC_BDArmory_Settings_BulletFX"));//"Bullet Hits"
                    BDArmorySettings.BULLET_DECALS = BDArmorySettings.BULLET_HITS;
                    BDArmorySettings.EJECT_SHELLS = BDArmorySettings.BULLET_HITS;
                    BDArmorySettings.SHELL_COLLISIONS = BDArmorySettings.BULLET_HITS;
                    BDArmorySettings.WATER_HIT_FX = BDArmorySettings.BULLET_HITS;
                }
                else
                {
                    BDArmorySettings.BULLET_HITS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.BULLET_HITS, StringUtils.Localize("#LOC_BDArmory_Settings_BulletHits"));//"Bullet Hits"
                    if (BDArmorySettings.BULLET_HITS)
                    {
                        BDArmorySettings.BULLET_DECALS = GUI.Toggle(SLeftRect(++line, 1), BDArmorySettings.BULLET_DECALS, StringUtils.Localize("#LOC_BDArmory_Settings_BulletHoleDecals"));//"Bullet Hole Decals"
                        GUI.Label(SLeftSliderRect(++line, 1), $"{StringUtils.Localize("#LOC_BDArmory_Settings_MaxBulletHoles")}:  ({BDArmorySettings.MAX_NUM_BULLET_DECALS})", leftLabel); // Max Bullet Holes
                        if (BDArmorySettings.MAX_NUM_BULLET_DECALS != (BDArmorySettings.MAX_NUM_BULLET_DECALS = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.MAX_NUM_BULLET_DECALS, 1f, 999f))))
                            BulletHitFX.AdjustDecalPoolSizes(BDArmorySettings.MAX_NUM_BULLET_DECALS);
                    }
                    BDArmorySettings.EJECT_SHELLS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.EJECT_SHELLS, StringUtils.Localize("#LOC_BDArmory_Settings_EjectShells"));//"Eject Shells"
                    if (BDArmorySettings.EJECT_SHELLS)
                    {
                        BDArmorySettings.SHELL_COLLISIONS = GUI.Toggle(SLeftRect(++line, 1), BDArmorySettings.SHELL_COLLISIONS, StringUtils.Localize("#LOC_BDArmory_Settings_ShellCollisions"));//"Shell Collisions"}
                    }
                }

                BDArmorySettings.SHOW_AMMO_GAUGES = GUI.Toggle(SLeftRect(++line), BDArmorySettings.SHOW_AMMO_GAUGES, StringUtils.Localize("#LOC_BDArmory_Settings_AmmoGauges"));//"Ammo Gauges"

                if (BDArmorySettings.PERFORMANCE_OPTIONS != (BDArmorySettings.PERFORMANCE_OPTIONS = GUI.Toggle(SRightRect(line), BDArmorySettings.PERFORMANCE_OPTIONS, StringUtils.Localize("#LOC_BDArmory_Settings_PerfOptions"))))//"Enable FX"
                {
                    // Configure FX pools
                    if (CMDropper.flarePool != null) CMDropper.ResetFlarePool();
                    if (ExplosionFx.explosionFXPools != null) ExplosionFx.explosionFXPools.Clear();
                    if (NukeFX.nukePool != null) NukeFX.nukePool.Clear();
                }
                if (BDArmorySettings.ADVANCED_USER_SETTINGS && BDArmorySettings.PERFORMANCE_OPTIONS)
                {
                    BDArmorySettings.GAPLESS_PARTICLE_EMITTERS = GUI.Toggle(SLeftRect(++line, 1), BDArmorySettings.GAPLESS_PARTICLE_EMITTERS, StringUtils.Localize("#LOC_BDArmory_Settings_GaplessParticleEmitters"));//"Gapless Particle Emitters"
                    //BDArmorySettings.PERSISTENT_FX = GUI.Toggle(SRightRect(line, 1), BDArmorySettings.PERSISTENT_FX, StringUtils.Localize("#LOC_BDArmory_Settings_PersistentFX"));//"Persistent FX"
                    if (BDArmorySettings.FLARE_SMOKE != (BDArmorySettings.FLARE_SMOKE = GUI.Toggle(SRightRect(line, 1), BDArmorySettings.FLARE_SMOKE, StringUtils.Localize("#LOC_BDArmory_Settings_FlareSmoke"))))//"Flare Smoke"
                    {
                        if (CMDropper.flarePool != null) CMDropper.ResetFlarePool();
                    }
                    BDArmorySettings.WATER_HIT_FX = GUI.Toggle(SLeftRect(++line, 1), BDArmorySettings.WATER_HIT_FX, StringUtils.Localize("#LOC_BDArmory_Settings_WaterHitFX"));//"Water Hit FX"
                    //BDArmorySettings.LIGHTFX = GUI.Toggle(SRightRect(line, 1), BDArmorySettings.LIGHTFX, StringUtils.Localize("#LOC_BDArmory_Settings_LightFX"));//Light FX"
                    //comment out below and uncomment above if !LIGHTFX = light range/intensity = o, but LightFX components still added.
                    if (BDArmorySettings.LIGHTFX != (BDArmorySettings.LIGHTFX = GUI.Toggle(SRightRect(line, 1), BDArmorySettings.LIGHTFX, StringUtils.Localize("#LOC_BDArmory_Settings_LightFX"))))//"Light FX"
                    {
                        if (ExplosionFx.explosionFXPools != null)
                            ExplosionFx.explosionFXPools.Clear();
                        if (NukeFX.nukePool != null)
                            NukeFX.nukePool.Clear();
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
                    if (HighLogic.LoadedSceneIsFlight)
                    {
                        if (BDArmorySettings.VM_TOOLBAR_BUTTON)
                        { VesselMover.Instance.AddToolbarButton(); }
                        else
                        { VesselMover.Instance.RemoveToolbarButton(); }
                    }
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

                    { // Numeric Input config
                        BDArmorySettings.NUMERIC_INPUT_SELF_UPDATE = GUI.Toggle(SLeftRect(++line), BDArmorySettings.NUMERIC_INPUT_SELF_UPDATE, $"{StringUtils.Localize("#LOC_BDArmory_Settings_NumericInputSelfUpdate")}: {BDArmorySettings.NUMERIC_INPUT_DELAY:0.0}s"); // Numeric Input Self Update
                        BDArmorySettings.NUMERIC_INPUT_DELAY = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.NUMERIC_INPUT_DELAY, 0.1f, 2f), 0.1f);
                    }

                    if (HighLogic.LoadedSceneIsEditor) // Craft-browser thumbnails
                    {
                        if (GUI.Button(SLeftRect(++line), StringUtils.Localize("#LOC_BDArmory_CraftBrowser_GenerateMissingThumbnails"))) CraftBrowserMissingThumbnailGenerator.Instance.GenerateMissingThumbnails(EditorDriver.editorFacility);
                        CraftBrowserMissingThumbnailGenerator.recurse = GUI.Toggle(SRightRect(line), CraftBrowserMissingThumbnailGenerator.recurse, StringUtils.Localize("#LOC_BDArmory_CraftBrowser_GenerateMissingThumbnailsRecurse"));
                    }

                    if (GUI.Button(SLineRect(++line, 1, true), (BDArmorySettings.DEBUG_SETTINGS_TOGGLE ? "Disable " : "Enable ") + StringUtils.Localize("#LOC_BDArmory_Settings_DebugSettingsToggle")))//Enable/Disable Debugging.
                    {
                        BDArmorySettings.DEBUG_SETTINGS_TOGGLE = !BDArmorySettings.DEBUG_SETTINGS_TOGGLE;
                        if (!BDArmorySettings.DEBUG_SETTINGS_TOGGLE) // Disable all debugging when closing the debugging section.
                        {
                            BDArmorySettings.DEBUG_AI = false;
                            BDArmorySettings.DEBUG_ARMOR = false;
                            BDArmorySettings.DEBUG_HP = false;
                            BDArmorySettings.DEBUG_COMPETITION = false;
                            BDArmorySettings.DEBUG_DAMAGE = false;
                            BDArmorySettings.DEBUG_LINES = false;
                            BDArmorySettings.DEBUG_MISSILES = false;
                            BDArmorySettings.DEBUG_OTHER = false;
                            BDArmorySettings.DEBUG_RADAR = false;
                            BDArmorySettings.DEBUG_SPAWNING = false;
                            BDArmorySettings.DEBUG_TELEMETRY = false;
                            BDArmorySettings.DEBUG_WEAPONS = false;
                            BDArmorySettings.DEBUG_APS = false;
                        }
                    }
                    if (BDArmorySettings.DEBUG_SETTINGS_TOGGLE)
                    {
                        BDArmorySettings.DEBUG_TELEMETRY = GUI.Toggle(SQuarterRect(++line, 0, 2), BDArmorySettings.DEBUG_TELEMETRY, StringUtils.Localize("#LOC_BDArmory_Settings_DebugTelemetry"));//"On-Screen Telemetry"
                        BDArmorySettings.DEBUG_LINES = GUI.Toggle(SQuarterRect(line, 2), BDArmorySettings.DEBUG_LINES, StringUtils.Localize("#LOC_BDArmory_Settings_DebugLines"));//"Debug Lines"
                        if (GUI.Button(SQuarterRect(line, 3, 0.85f), StringUtils.Localize("#LOC_BDArmory_Settings_DebugMarker")))
                        {
                            Debug.LogWarning($"[BDArmory] DEBUG DEBUG DEBUG at time {Time.time}s (MET: {KSPUtil.PrintTimeStamp(FlightLogger.met, true, true)})");
                        }
                        BDArmorySettings.DEBUG_WEAPONS = GUI.Toggle(SQuarterRect(++line, 0), BDArmorySettings.DEBUG_WEAPONS, StringUtils.Localize("#LOC_BDArmory_Settings_DebugWeapons"));//"Debug Weapons"
                        BDArmorySettings.DEBUG_MISSILES = GUI.Toggle(SQuarterRect(line, 1), BDArmorySettings.DEBUG_MISSILES, StringUtils.Localize("#LOC_BDArmory_Settings_DebugMissiles"));//"Debug Missiles"
                        BDArmorySettings.DEBUG_ARMOR = GUI.Toggle(SQuarterRect(line, 2), BDArmorySettings.DEBUG_ARMOR, StringUtils.Localize("#LOC_BDArmory_Settings_DebugArmor"));//"Debug Armor"
                        BDArmorySettings.DEBUG_DAMAGE = GUI.Toggle(SQuarterRect(line, 3), BDArmorySettings.DEBUG_DAMAGE, StringUtils.Localize("#LOC_BDArmory_Settings_DebugDamage"));//"Debug Damage"
                        BDArmorySettings.DEBUG_AI = GUI.Toggle(SQuarterRect(++line, 0), BDArmorySettings.DEBUG_AI, StringUtils.Localize("#LOC_BDArmory_Settings_DebugAI"));//"Debug AI"
                        BDArmorySettings.DEBUG_COMPETITION = GUI.Toggle(SQuarterRect(line, 1), BDArmorySettings.DEBUG_COMPETITION, StringUtils.Localize("#LOC_BDArmory_Settings_DebugCompetition"));//"Debug Competition"
                        BDArmorySettings.DEBUG_RADAR = GUI.Toggle(SQuarterRect(line, 2), BDArmorySettings.DEBUG_RADAR, StringUtils.Localize("#LOC_BDArmory_Settings_DebugRadar"));//"Debug Detectors"
                        BDArmorySettings.DEBUG_SPAWNING = GUI.Toggle(SQuarterRect(line, 3), BDArmorySettings.DEBUG_SPAWNING, StringUtils.Localize("#LOC_BDArmory_Settings_DebugSpawning"));//"Debug Spawning"
                        BDArmorySettings.DEBUG_APS = GUI.Toggle(SQuarterRect(++line, 0), BDArmorySettings.DEBUG_APS, StringUtils.Localize("#LOC_BDArmory_Settings_DebugAPS"));//"Debug PointDefense"
                        BDArmorySettings.DEBUG_HP = GUI.Toggle(SQuarterRect(line, 1), BDArmorySettings.DEBUG_HP, StringUtils.Localize("#LOC_BDArmory_Settings_DebugHP"));//"Debug Armor"
                        BDArmorySettings.DEBUG_OTHER = GUI.Toggle(SQuarterRect(line, 2), BDArmorySettings.DEBUG_OTHER, StringUtils.Localize("#LOC_BDArmory_Settings_DebugOther"));//"Debug Other"

                        if (BDArmorySettings.DEBUG_OTHER && GUI.Button(SLineRect(++line), StringUtils.Localize("#LOC_BDArmory_Settings_ResetScrollZoom"))) GUIUtils.ResetScrollRate(); // Reset scroll-zoom.
                        if (BDArmorySettings.DEBUG_AI && GUI.Button(SLineRect(++line), "Debug Extending")) // Debug why a vessel is stuck in extending.
                        {
                            var pilotAI = FlightGlobals.ActiveVessel.ActiveController().PilotAI;
                            if (pilotAI != null && pilotAI.pilotEnabled) pilotAI.DebugExtending();
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

                        // if (GUI.Button(SLineRect(++line), "Test ActiveController")) TestActiveController();
                        // if (BDArmorySettings.DEBUG_OTHER && GUI.Button(SLineRect(++line), "Dump VesselModuleRegistry") && FlightGlobals.ActiveVessel != null) { VesselModuleRegistry.Instance.DumpRegistriesFor(FlightGlobals.ActiveVessel); }
                        // GUI.Label(SLeftSliderRect(++line), $"Initial correction: {(TestNumericalMethodsIC == 0 ? "None" : TestNumericalMethodsIC == 1 ? "All" : TestNumericalMethodsIC == 2 ? "Local" : "Gravity")}");
                        // TestNumericalMethodsIC = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), TestNumericalMethodsIC, 0, 3));
                        // if (GUI.Button(SLineRect(++line), $"Test Forward Euler vs Semi-Implicit Euler vs Leap-frog ({PROF_N * Time.fixedDeltaTime}s, {PROF_N / Math.Min(PROF_N / 2, PROF_n)} steps)")) StartCoroutine(TestNumericalMethods(PROF_N * Time.fixedDeltaTime, PROF_N / Math.Min(PROF_N / 2, PROF_n)));
                        // if (GUI.Button(SLineRect(++line), "Take Radar Snapshot"))
                        // {
                        //     if (HighLogic.LoadedSceneIsFlight)
                        //     {
                        //         var v = FlightGlobals.ActiveVessel;
                        //         var ti = RadarUtils.RenderVesselRadarSnapshot(v, v.transform);
                        //     }
                        //     else
                        //     {
                        //         Vessel v = new Vessel();
                        //         v.parts = EditorLogic.fetch.ship.Parts;
                        //         v.vesselType = VesselType.Plane;
                        //         var ti = RadarUtils.RenderVesselRadarSnapshot(v, EditorLogic.RootPart.transform);
                        //     }
                        // }
                        // if (GUI.Button(SLineRect(++line), "Test Bounds"))
                        // {
                        //     var v = FlightGlobals.ActiveVessel;
                        //     var c = FlightCamera.fetch;
                        //     var b = v.ViewBoundsFrom(v.GetColliderBounds(), c.transform);
                        //     Debug.Log($"DEBUG Bounds viewed from Camera: {b}");
                        //     // TestBounds();
                        // }
                        // if (GUI.Button(SLineRect(++line), "Test Angle")) TestAngle();
                        // if (GUI.Button(SLineRect(++line), "Test Abs")) TestAbs();
                        // if (GUI.Button(SLineRect(++line), "Test \"up\"")) TestUp();
                        // if (GUI.Button(SLineRect(++line), "Test inside vs on unit sphere")) TestInOnUnitSphere();
                        // if (GUI.Button(SLineRect(++line), "Test Sqr vs x*x")) TestMaxRelSpeed();
                        // if (GUI.Button(SLineRect(++line), "Test Sqr vs x*x")) TestSqrVsSqr();
                        // if (GUI.Button(SLineRect(++line), "Test Order of Operations")) TestOrderOfOperations();
                        // if (GUI.Button(SLineRect(++line), "Test GetMass vs Size performance")) TestMassVsSizePerformance();
                        // if (GUI.Button(SLineRect(++line), "Test DotNorm performance")) TestDotNormPerformance();
                        // if (GUI.Button(SLineRect(++line), "Vessel Naming"))
                        // {
                        //     var v = FlightGlobals.ActiveVessel;
                        //     Debug.Log($"DEBUG vesselName: {v.vesselName}, GetName: {v.GetName()}, GetDisplayName: {v.GetDisplayName()}");
                        // }
                        // if (GUI.Button(SLineRect(++line), "Test name performance")) TestNamePerformance();
                        // if (GUI.Button(SLineRect(++line), "Test rand performance")) TestRandPerformance();
                        // if (GUI.Button(SLineRect(++line), "Test ProjectOnPlane and PredictPosition")) TestProjectOnPlaneAndPredictPosition();
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
                        //     float μsResolution = 1e6f / System.Diagnostics.Stopwatch.Frequency;
                        //     watch.Start();
                        //     for (int i = 0; i < N; ++i)
                        //     {
                        //         for (int j = 0; j < debug_numRaycasts; ++j)
                        //             proximityRaycastCommands[j] = new RaycastCommand(vesselPosition, vesselSrfVelDir + relativeVelocityDownDirection, terrainAlertDetectionRadius, (int)LayerMasks.Scenery);
                        //         var job = RaycastCommand.ScheduleBatch(proximityRaycastCommands, proximityRaycastHits, 1, default(Unity.Jobs.JobHandle));
                        //         job.Complete(); // Wait for the job to complete.
                        //     }
                        //     watch.Stop();
                        //     Debug.Log($"DEBUG Batch RaycastCommand[{debug_numRaycasts}] took {watch.ElapsedTicks * μsResolution / N:G3}μs");
                        //     RaycastHit rayHit;
                        //     watch.Reset(); watch.Start();
                        //     for (int i = 0; i < N; ++i)
                        //         for (int j = 0; j < debug_numRaycasts; ++j)
                        //             Physics.Raycast(new Ray(vesselPosition, (vesselSrfVelDir + relativeVelocityDownDirection).normalized), out rayHit, terrainAlertDetectionRadius, (int)LayerMasks.Scenery);
                        //     watch.Stop();
                        //     Debug.Log($"DEBUG {debug_numRaycasts} Raycasts took {watch.ElapsedTicks * μsResolution / N:G3}μs");
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
                        //     float μsResolution = 1e6f / System.Diagnostics.Stopwatch.Frequency;
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
                        //         string raycastString = $"Raycast took {raycastTicks * μsResolution:G3}μs";
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
                        //         Debug.Log($"DEBUG {raycastString}, {(isNonConvexMeshCollider ? "ClosestPointOnBounds" : "ClosestPoint")} ({closestPoint}) on{(isMeshCollider ? $" {(isNonConvexMeshCollider ? "non-" : "")}convex mesh" : "")} collider {hit.collider} from camera ({ray.origin}) took {watch.ElapsedTicks * μsResolution / N:G3}μs{(partHit != null ? $", offset from part ({partHit.name}): {closestPoint - partHit.transform.position}" : "")}, offset from hit: {hit.point - closestPoint}");
                        //     }
                        // }
                        // if (GUI.Button(SLineRect(++line), "Test 2x Raycast vs RaycastNonAlloc"))
                        // {
                        //     var watch = new System.Diagnostics.Stopwatch();
                        //     float μsResolution = 1e6f / System.Diagnostics.Stopwatch.Frequency;
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
                        //     Debug.Log($"DEBUG Raycast 2x (hit? {didHit}) took {watch.ElapsedTicks * μsResolution / N:G3}μs");
                        //     int hitCount = 0;
                        //     watch.Reset(); watch.Start();
                        //     for (int i = 0; i < N; ++i)
                        //         hitCount = Physics.RaycastNonAlloc(ray, hits, dist, layerMask);
                        //     watch.Stop();
                        //     Debug.Log($"DEBUG RaycastNonAlloc ({hitCount} hits) took {watch.ElapsedTicks * μsResolution / N:G3}μs");
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
                        // if (GUI.Button(SLineRect(++line), $"Min Safe Altitudes"))
                        // {
                        //     foreach (var body in FlightGlobals.Bodies) Debug.Log($"DEBUG Min Safe Altitude for {body.GetName()} is {body.MinSafeAltitude()}m");
                        // }
                        // if (HighLogic.LoadedSceneIsEditor && GUI.Button(SLineRect(++line), "Test GetConnectedResourceTotals"))
                        // {
                        //     EditorLogic.fetch.ship.UpdateResourceSets();
                        //     float dMass = 0;
                        //     foreach (var res in EditorLogic.fetch.ship.parts.SelectMany(p => p.Resources, (p, r) => r.info).ToHashSet()) // Unique resource infos on the ship.
                        //     {
                        //         EditorLogic.fetch.ship.GetConnectedResourceTotals(res.id, true, out double fuelCurrent, out double fuelMax);
                        //         dMass -= (float)fuelCurrent * res.density;
                        //         Debug.Log($"DEBUG res {res.name}, ID: {res.id}, current: {fuelCurrent}, max: {fuelMax}, mass: {(float)fuelCurrent * res.density}");
                        //     }
                        //     Debug.Log($"DEBUG dMass: {dMass}");
                        //     dMass = -EditorLogic.fetch.ship.parts.SelectMany(p => p.Resources, (p, r) => r.info).ToHashSet().Select(res => { EditorLogic.fetch.ship.GetConnectedResourceTotals(res.id, true, out double fuelCurrent, out double fuelMax); return (float)fuelCurrent * res.density; }).Sum();
                        //     Debug.Log($"DEBUG dMass: {dMass}");
                        //     dMass = 0;
                        //     foreach (var part in EditorLogic.fetch.ship.parts)
                        //         foreach (var res in part.Resources)
                        //         {
                        //             dMass -= (float)res.amount * res.info.density;
                        //         }
                        //     Debug.Log($"DEBUG dMass: {dMass}");
                        //     dMass = -EditorLogic.fetch.ship.parts.SelectMany(p => p.Resources, (p, r) => r).Select(res => (float)res.amount * res.info.density).Sum();
                        //     Debug.Log($"DEBUG dMass: {dMass}");
                        // }
                        //if (GUI.Button(SLineRect(++line), "Test GetWorldAlignedVector")) // Test TransformDirection vs. Rotation
                        //{
                        //    TargetInfo test = FlightGlobals.ActiveVessel.gameObject.GetComponent<TargetInfo>();
                        //    Vector3 oneVec = Vector3.one;
                        //    Vector3 testVec;
                        //    float testFloat = 0;
                        //    int iters = 10000000;
                        //    var watch = new System.Diagnostics.Stopwatch();
                        //    float μsResolution = 1e6f / System.Diagnostics.Stopwatch.Frequency;
                        //    var tic = Time.realtimeSinceStartup;
                        //    watch.Reset(); watch.Start();
                        //    for (int i = 0; i < iters; ++i)
                        //    {
                        //        testVec = test.GetWorldAlignedVector(UnityEngine.Random.onUnitSphere);
                        //        testFloat += Vector3.Dot(oneVec, testVec);
                        //    }
                        //    watch.Stop();
                        //    var dt = Time.realtimeSinceStartup - tic;
                        //    Debug.Log($"DEBUG WorldAligned TransformDirection:  {dt / iters:G3}s/iter {watch.ElapsedTicks * μsResolution / iters:G3}ns, testFloat: {testFloat}");
                        //    tic = Time.realtimeSinceStartup;
                        //    watch.Reset(); watch.Start();
                        //    for (int i = 0; i < iters; ++i)
                        //    {
                        //        testVec = test.GetWorldAlignedVectorAlt(UnityEngine.Random.onUnitSphere);
                        //        testFloat += Vector3.Dot(oneVec, testVec);
                        //    }
                        //    watch.Stop();
                        //    dt = Time.realtimeSinceStartup - tic;
                        //    Debug.Log($"DEBUG WorldAligned Rot. Cached:         {dt / iters:G3}s/iter {watch.ElapsedTicks * μsResolution / iters:G3}ns, testFloat: {testFloat}");
                        //    tic = Time.realtimeSinceStartup;
                        //    watch.Reset(); watch.Start();
                        //    for (int i = 0; i < iters; ++i)
                        //    {
                        //        testVec = test.GetWorldAlignedVectorAlt2(UnityEngine.Random.onUnitSphere);
                        //        testFloat += Vector3.Dot(oneVec, testVec);
                        //    }
                        //    watch.Stop();
                        //    dt = Time.realtimeSinceStartup - tic;
                        //    Debug.Log($"DEBUG WorldAlignedAlt Rot. Non-Cached:  {dt / iters:G3}s/iter {watch.ElapsedTicks * μsResolution / iters:G3}ns, testFloat: {testFloat}");
                        //}
                        //if (GUI.Button(SLineRect(++line), "Test Gaussian")) // Test TransformDirection vs. Rotation
                        //{
                        //    float testDev = 0;
                        //    int iters = 10000000;
                        //    var watch = new System.Diagnostics.Stopwatch();
                        //    float μsResolution = 1e6f / System.Diagnostics.Stopwatch.Frequency;
                        //    var tic = Time.realtimeSinceStartup;
                        //    watch.Reset(); watch.Start();
                        //    for (int i = 0; i < iters; ++i)
                        //    {
                        //        float curr = VectorUtils.GaussianQuant();
                        //        testDev += curr * curr;
                        //    }
                        //    watch.Stop();
                        //    var dt = Time.realtimeSinceStartup - tic;
                        //    Debug.Log($"DEBUG GaussianQuant:    {dt / iters:G3}s/iter {watch.ElapsedTicks * μsResolution / iters:G3}ns, testDev: {BDAMath.Sqrt(testDev / iters)}");
                        //    testDev = 0f;
                        //    tic = Time.realtimeSinceStartup;
                        //    watch.Reset(); watch.Start();
                        //    for (int i = 0; i < iters; ++i)
                        //    {
                        //        float curr = VectorUtils.Gaussian();
                        //        testDev += curr * curr;
                        //    }
                        //    watch.Stop();
                        //    dt = Time.realtimeSinceStartup - tic;
                        //    Debug.Log($"DEBUG Gaussian:         {dt / iters:G3}s/iter {watch.ElapsedTicks * μsResolution / iters:G3}ns, testDev: {BDAMath.Sqrt(testDev / iters)}");
                        //    testDev = 0f;
                        //    tic = Time.realtimeSinceStartup;
                        //    watch.Reset(); watch.Start();
                        //    for (int i = 0; i < iters; ++i)
                        //    {
                        //        float curr = VectorUtils.GaussianQuant();
                        //        testDev += curr * curr;
                        //    }
                        //    watch.Stop();
                        //    dt = Time.realtimeSinceStartup - tic;
                        //    Debug.Log($"DEBUG GaussianQuant:    {dt / iters:G3}s/iter {watch.ElapsedTicks * μsResolution / iters:G3}ns, testDev: {BDAMath.Sqrt(testDev / iters)}");
                        //}
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
                    BDArmorySettings.DISABLE_GUARDMODE_ON_SPAWN = GUI.Toggle(SRightRect(line), BDArmorySettings.DISABLE_GUARDMODE_ON_SPAWN, StringUtils.Localize("#LOC_BDArmory_Settings_DisableGuardModeOnSpawn")); // Disable Guard Mode on Spawn
                    BDArmorySettings.BULLET_WATER_DRAG = GUI.Toggle(SLeftRect(++line), BDArmorySettings.BULLET_WATER_DRAG, StringUtils.Localize("#LOC_BDArmory_Settings_waterDrag"));// Underwater bullet drag
                    BDArmorySettings.RESET_HP = GUI.Toggle(SRightRect(line), BDArmorySettings.RESET_HP, StringUtils.Localize("#LOC_BDArmory_Settings_ResetHP"));
                    BDArmorySettings.VESSEL_RELATIVE_BULLET_CHECKS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.VESSEL_RELATIVE_BULLET_CHECKS, StringUtils.Localize("#LOC_BDArmory_Settings_VesselRelativeBulletChecks"));//"Vessel-Relative Bullet Checks"
                    BDArmorySettings.RESET_ARMOUR = GUI.Toggle(SRightRect(line), BDArmorySettings.RESET_ARMOUR, StringUtils.Localize("#LOC_BDArmory_Settings_ResetArmor"));
                    if (BDArmorySettings.RESTORE_KAL != (BDArmorySettings.RESTORE_KAL = GUI.Toggle(SLeftRect(++line), BDArmorySettings.RESTORE_KAL, StringUtils.Localize("#LOC_BDArmory_Settings_RestoreKAL")))) //Restore KAL
                    { SpawnUtils.RestoreKALGlobally(BDArmorySettings.RESTORE_KAL); }
                    BDArmorySettings.RESET_HULL = GUI.Toggle(SRightRect(line), BDArmorySettings.RESET_HULL, StringUtils.Localize("#LOC_BDArmory_Settings_ResetHull")); //Reset Hull
                    BDArmorySettings.AUTO_LOAD_TO_KSC = GUI.Toggle(SLeftRect(++line), BDArmorySettings.AUTO_LOAD_TO_KSC, StringUtils.Localize("#LOC_BDArmory_Settings_AutoLoadToKSC")); // Auto-Load To KSC
                    BDArmorySettings.GENERATE_CLEAN_SAVE = GUI.Toggle(SRightRect(line), BDArmorySettings.GENERATE_CLEAN_SAVE, StringUtils.Localize("#LOC_BDArmory_Settings_GenerateCleanSave")); // Generate Clean Save
                    BDArmorySettings.AUTO_RESUME_TOURNAMENT = GUI.Toggle(SLeftRect(++line), BDArmorySettings.AUTO_RESUME_TOURNAMENT, StringUtils.Localize("#LOC_BDArmory_Settings_AutoResumeTournaments")); // Auto-Resume Tournaments
                    BDArmorySettings.AUTO_RESUME_CONTINUOUS_SPAWN = GUI.Toggle(SRightRect(line), BDArmorySettings.AUTO_RESUME_CONTINUOUS_SPAWN, StringUtils.Localize("#LOC_BDArmory_Settings_AutoResumeContinuousSpawn")); // Auto-Resume Continuous Spawn
                    if (BDArmorySettings.AUTO_RESUME_TOURNAMENT || BDArmorySettings.AUTO_RESUME_CONTINUOUS_SPAWN)
                    {
                        BDArmorySettings.AUTO_QUIT_AT_END_OF_TOURNAMENT = GUI.Toggle(SLeftRect(++line), BDArmorySettings.AUTO_QUIT_AT_END_OF_TOURNAMENT, StringUtils.Localize("#LOC_BDArmory_Settings_AutoQuitAtEndOfTournament")); // Auto Quit At End Of Tournament
                    }
                    if (BDArmorySettings.AUTO_RESUME_TOURNAMENT)
                    {
                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_AutoQuitMemoryUsage")}:  ({(BDArmorySettings.QUIT_MEMORY_USAGE_THRESHOLD > SystemMaxMemory ? StringUtils.Localize("#LOC_BDArmory_Generic_Off") : $"{BDArmorySettings.QUIT_MEMORY_USAGE_THRESHOLD}GB")})", leftLabel); // Auto-Quit Memory Threshold
                        BDArmorySettings.QUIT_MEMORY_USAGE_THRESHOLD = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.QUIT_MEMORY_USAGE_THRESHOLD, 1f, SystemMaxMemory + 1));
                        if (BDArmorySettings.QUIT_MEMORY_USAGE_THRESHOLD <= SystemMaxMemory)
                        {
                            GUI.Label(SLineRect(++line, 1), $"{StringUtils.Localize("#LOC_BDArmory_Settings_CurrentMemoryUsageEstimate")}: {TournamentAutoResume.memoryUsage:F1}GB / {SystemMaxMemory}GB", leftLabel);
                        }
                    }
                    if (BDArmorySettings.TIME_OVERRIDE != (BDArmorySettings.TIME_OVERRIDE = GUI.Toggle(SLeftRect(++line), BDArmorySettings.TIME_OVERRIDE, $"{StringUtils.Localize("#LOC_BDArmory_Settings_TimeOverride")}:  ({BDArmorySettings.TIME_SCALE:G2}x)"))) // Time override.
                    {
                        OtherUtils.SetTimeOverride(BDArmorySettings.TIME_OVERRIDE);
                    }
                    if (BDArmorySettings.TIME_SCALE != (BDArmorySettings.TIME_SCALE = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.TIME_SCALE, 0f, BDArmorySettings.TIME_SCALE_MAX), BDArmorySettings.TIME_SCALE > 5f ? 1f : 0.1f)))
                    {
                        if (BDArmorySettings.TIME_OVERRIDE) Time.timeScale = BDArmorySettings.TIME_SCALE;
                    }
                    BDArmorySettings.MISSILE_CM_SETTING_TOGGLE = GUI.Toggle(SLineRect(++line), BDArmorySettings.MISSILE_CM_SETTING_TOGGLE, StringUtils.Localize("#LOC_BDArmory_Settings_MissileCMToggle"));
                    if (BDArmorySettings.MISSILE_CM_SETTING_TOGGLE)
                    {
                        BDArmorySettings.ASPECTED_RCS = GUI.Toggle(SLineRect(++line, 1), BDArmorySettings.ASPECTED_RCS, StringUtils.Localize("#LOC_BDArmory_Settings_AspectedRCS"));
                        if (BDArmorySettings.ASPECTED_RCS)
                        {
                            GUI.Label(SLeftSliderRect(++line, 1), $"{StringUtils.Localize("#LOC_BDArmory_Settings_AspectedRCSOverallRCSWeight")}:  ({BDArmorySettings.ASPECTED_RCS_OVERALL_RCS_WEIGHT})", leftLabel);
                            BDArmorySettings.ASPECTED_RCS_OVERALL_RCS_WEIGHT = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.ASPECTED_RCS_OVERALL_RCS_WEIGHT, 0f, 1f), 0.05f);
                        }

                        GUI.Label(SLeftSliderRect(++line, 1), $"{StringUtils.Localize("#LOC_BDArmory_Settings_FlareFactor")}:  ({BDArmorySettings.FLARE_FACTOR})", leftLabel);
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
                    BDArmorySettings.IGNORE_TERRAIN_CHECK = GUI.Toggle(SLeftRect(++line), BDArmorySettings.IGNORE_TERRAIN_CHECK, StringUtils.Localize("#LOC_BDArmory_Settings_IgnoreTerrainCheck")); // Ignore Terrain Check
                    BDArmorySettings.ALLOW_RETREAT_IF_ORBITING = GUI.Toggle(SRightRect(line), BDArmorySettings.ALLOW_RETREAT_IF_ORBITING, StringUtils.Localize("#LOC_BDArmory_Settings_RetreatIfOrbiting")); // Allow retreat if orbiting.
                    BDArmorySettings.CHECK_WATER_TERRAIN = GUI.Toggle(SLeftRect(++line), BDArmorySettings.CHECK_WATER_TERRAIN, StringUtils.Localize("#LOC_BDArmory_Settings_CheckWaterTerrain")); // Check Water
                    BDArmorySettings.WEAPONS_RESPECT_CROSSFEED = GUI.Toggle(SRightRect(line), BDArmorySettings.WEAPONS_RESPECT_CROSSFEED, StringUtils.Localize("#LOC_BDArmory_Settings_WeaponsRespectCrossfeed")); // Weapons Respect Crossfeed.
                    BDArmorySettings.RADAR_NOTCHING = GUI.Toggle(SLeftRect(++line), BDArmorySettings.RADAR_NOTCHING, StringUtils.Localize("#LOC_BDArmory_Settings_RadarNotching")); // Radar Notching Toggle
                    if (BDArmorySettings.RADAR_NOTCHING)
                    {
                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_Notching_Factor")}:  ({BDArmorySettings.RADAR_NOTCHING_FACTOR})", leftLabel); // Notch Effectiveness Multiplier
                        BDArmorySettings.RADAR_NOTCHING_FACTOR = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.RADAR_NOTCHING_FACTOR, 0f, 1f), 0.05f);

                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_Notching_SCR_Factor")}:  ({BDArmorySettings.RADAR_NOTCHING_SCR_FACTOR})", leftLabel); // Notch SCR Multiplier, should be set to 0.01 as default though it's adjustable
                        BDArmorySettings.RADAR_NOTCHING_SCR_FACTOR = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.RADAR_NOTCHING_SCR_FACTOR, 0f, 0.5f), 0.005f);
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

                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_HEATExplosiveDamageMultiplier")}:  ({BDArmorySettings.EXP_DMG_MOD_HEAT})", leftLabel);
                        BDArmorySettings.EXP_DMG_MOD_HEAT = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.EXP_DMG_MOD_HEAT, 0f, 10f), 0.25f);

                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_ExplosionImpulseMultiplier")}:  ({BDArmorySettings.EXP_IMP_MOD})", leftLabel);
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
                        BDArmorySettings.BALLISTIC_TRAJECTORY_SIMULATION_MULTIPLIER = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.BALLISTIC_TRAJECTORY_SIMULATION_MULTIPLIER, 1f, 128f));

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
                BDArmorySettings.CAMERA_SWITCH_FREQUENCY = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.CAMERA_SWITCH_FREQUENCY, 1f, 15f));

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
                GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_Max_Armor_Limit")}:  {(BDArmorySettings.MAX_ARMOR_LIMIT >= 0 ? $"{BDArmorySettings.MAX_ARMOR_LIMIT:0}" : "Unclamped")}", leftLabel); // Armor Limit
                if (BDArmorySettings.MAX_ARMOR_LIMIT != (BDArmorySettings.MAX_ARMOR_LIMIT = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.MAX_ARMOR_LIMIT, -1, 100))))
                {
                    if (HighLogic.LoadedSceneIsEditor && EditorLogic.fetch.ship is not null) GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
                }

                if (BDArmorySettings.ADVANCED_USER_SETTINGS)
                {
                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_DebrisCleanUpDelay")}:  ({BDArmorySettings.DEBRIS_CLEANUP_DELAY}s)", leftLabel); // Debris Clean-up delay
                    BDArmorySettings.DEBRIS_CLEANUP_DELAY = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.DEBRIS_CLEANUP_DELAY, 1f, 60f));

                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_CompetitionNonCompetitorRemovalDelay")}:  ({(BDArmorySettings.COMPETITION_NONCOMPETITOR_REMOVAL_DELAY > 60 ? StringUtils.Localize("#LOC_BDArmory_Generic_Off") : BDArmorySettings.COMPETITION_NONCOMPETITOR_REMOVAL_DELAY + "s")})", leftLabel); // Non-competitor removal frequency
                    BDArmorySettings.COMPETITION_NONCOMPETITOR_REMOVAL_DELAY = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.COMPETITION_NONCOMPETITOR_REMOVAL_DELAY, 1f, 61f));
                    if (BDACompetitionMode.Instance is not null)
                    {
                        if (GUI.Button(SLineThirdRect(++line, 0), StringUtils.Localize("#LOC_BDArmory_Settings_ClearDebrisNow"))) BDACompetitionMode.Instance.RemoveDebrisNow();
                        if (GUI.Button(SLineThirdRect(line, 1, 2), StringUtils.Localize(BDACompetitionMode.Instance.CleanDebrisAlways ? "#LOC_BDArmory_Settings_ClearDebrisAlways" : "#LOC_BDArmory_Settings_ClearDebrisDuringCompetitions"))) BDACompetitionMode.Instance.CleanDebrisAlways = !BDACompetitionMode.Instance.CleanDebrisAlways;
                    }
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

                if (BDArmorySettings.ADVANCED_USER_SETTINGS)
                {
                    // Moving this stuff higher up as dragging the RWP slider changes the layout and can switch which slider is being dragged, causing unintended settings changes.
                    if (BDArmorySettings.RUNWAY_PROJECT != (BDArmorySettings.RUNWAY_PROJECT = GUI.Toggle(SLeftRect(++line), BDArmorySettings.RUNWAY_PROJECT, StringUtils.Localize("#LOC_BDArmory_Settings_RunwayProject"))))//Runway Project
                    {
                        RWPSettings.SetRWP(BDArmorySettings.RUNWAY_PROJECT, BDArmorySettings.RUNWAY_PROJECT_ROUND);
                        if (HighLogic.LoadedSceneIsFlight)
                        {
                            SpawnUtils.HackActuatorsOnNewVessels(BDArmorySettings.RUNWAY_PROJECT);

                            foreach (var vessel in FlightGlobals.Vessels)
                            {
                                if (vessel == null || !vessel.loaded) continue;
                                SpawnUtils.HackActuators(vessel, BDArmorySettings.RUNWAY_PROJECT);
                            }
                        }
                        if (HighLogic.LoadedSceneIsEditor)
                        {
                            if (BDArmorySettings.RUNWAY_PROJECT && BDAEditorArmorWindow.Instance) BDAEditorArmorWindow.Instance.SetupLegalityValues();
                            if (EditorLogic.fetch.ship is not null) GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
                        }
                    }
                    if (CompSettings.CompOverridesEnabled)
                    {
                        if (BDArmorySettings.COMP_CONVENIENCE_CHECKS != (BDArmorySettings.COMP_CONVENIENCE_CHECKS = GUI.Toggle(SRightRect(line), BDArmorySettings.COMP_CONVENIENCE_CHECKS, StringUtils.Localize("#LOC_BDArmory_Settings_CompChecks"))))//Runway Project
                        {
                            if (HighLogic.LoadedSceneIsEditor)
                            {
                                if (BDArmorySettings.COMP_CONVENIENCE_CHECKS && BDAEditorArmorWindow.Instance) BDAEditorArmorWindow.Instance.SetupLegalityValues();
                                if (EditorLogic.fetch.ship is not null) GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
                            }
                        }
                    }
                    if (BDArmorySettings.RUNWAY_PROJECT)
                    {
                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_RunwayProjectRound")}: ({(BDArmorySettings.RUNWAY_PROJECT_ROUND > 10 ? $"S{(BDArmorySettings.RUNWAY_PROJECT_ROUND - 1) / 10}R{(BDArmorySettings.RUNWAY_PROJECT_ROUND - 1) % 10 + 1}" : "—")})", leftLabel); // RWP round
                        if (BDArmorySettings.RUNWAY_PROJECT_ROUND != (BDArmorySettings.RUNWAY_PROJECT_ROUND = RWPSettings.RWPIndexToRound.GetValueOrDefault(Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), RWPSettings.RWPRoundToIndex.GetValueOrDefault(BDArmorySettings.RUNWAY_PROJECT_ROUND), 0, RWPSettings.RWPRoundToIndex.Count - 1)))))
                            // if (BDArmorySettings.RUNWAY_PROJECT_ROUND != (BDArmorySettings.RUNWAY_PROJECT_ROUND = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.RUNWAY_PROJECT_ROUND, 10f, 70f))))
                            RWPSettings.SetRWP(BDArmorySettings.RUNWAY_PROJECT, BDArmorySettings.RUNWAY_PROJECT_ROUND);

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
                        //TODO - convert these to gamemode types - e.g. Firerate Increase on Kill, Rapid Deployment, Spacemode, etc, and clear out all the empty round values
                        if (CheatCodeGUI != (CheatCodeGUI = GUI.TextField(SLeftRect(++line, 1, true), CheatCodeGUI, textFieldStyle))) //if we need super-secret stuff
                        {
                            switch (CheatCodeGUI)
                            {
                                case "ZombieMode":
                                    {
                                        BDArmorySettings.ZOMBIE_MODE = !BDArmorySettings.ZOMBIE_MODE; //sticking this here until we figure out a better home for it
                                        CheatCodeGUI = "";
                                        break;
                                    }
                                /* //Announcer
                                case "UTDeathMatch":
                                    {
                                        BDArmorySettings.GG_ANNOUNCER = !BDArmorySettings.GG_ANNOUNCER;
                                        CheatCodeGUI = "";
                                        break;
                                    }
                                */
                                case "DiscoInferno":
                                    {
                                        BDArmorySettings.DISCO_MODE = !BDArmorySettings.DISCO_MODE;
                                        CheatCodeGUI = "";
                                        break;
                                    }
                                case "NoEngines":
                                    {
                                        BDArmorySettings.NO_ENGINES = !BDArmorySettings.NO_ENGINES;
                                        CheatCodeGUI = "";
                                        break;
                                    }
                                case "HallOfShame":
                                    {
                                        BDArmorySettings.ENABLE_HOS = !BDArmorySettings.ENABLE_HOS;
                                        CheatCodeGUI = "";
                                        break;
                                    }
                                case "altitudehack": //until we figure out where to put this
                                    {
                                        BDArmorySettings.ALTITUDE_HACKS = !BDArmorySettings.ALTITUDE_HACKS;
                                        CheatCodeGUI = "";
                                        break;
                                    }
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
                            HoSString = GUI.TextField(SLeftRect(++line, 1, true), HoSString, textFieldStyle);
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
                            BDArmorySettings.HOS_FIRE = GUI.HorizontalSlider(SRightSliderRect(line), (float)Math.Round(BDArmorySettings.HOS_FIRE, 1), 0, 10);
                            GUI.Label(SLeftSliderRect(++line, 2f), $"{StringUtils.Localize("Mass")}:  ({(float)Math.Round(BDArmorySettings.HOS_MASS, 1)} ton deadweight)", leftLabel);
                            BDArmorySettings.HOS_MASS = GUI.HorizontalSlider(SRightSliderRect(line), (float)Math.Round(BDArmorySettings.HOS_MASS, 1), -10, 10);
                            GUI.Label(SLeftSliderRect(++line, 2f), $"{StringUtils.Localize("Frailty")}:  ({(float)Math.Round(BDArmorySettings.HOS_DMG * 100, 2)}%) Dmg taken", leftLabel);
                            BDArmorySettings.HOS_DMG = GUIUtils.HorizontalSemiLogSlider(SRightSliderRect(line), (float)Math.Round(BDArmorySettings.HOS_DMG, 2), 0.1f, 10, 2f, false, true, ref hosDmgCache);
                            GUI.Label(SLeftSliderRect(++line, 2f), $"{StringUtils.Localize("Thrust")}:  ({(float)Math.Round(BDArmorySettings.HOS_THRUST, 1)}%) Engine Thrust", leftLabel);
                            BDArmorySettings.HOS_THRUST = GUI.HorizontalSlider(SRightSliderRect(line), (float)Math.Round(BDArmorySettings.HOS_THRUST), 0, 200);
                            BDArmorySettings.HOS_SAS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.HOS_SAS, "Remove Reaction Wheels");
                            BDArmorySettings.HOS_ASTEROID = GUI.Toggle(SLeftRect(++line), BDArmorySettings.HOS_ASTEROID, "Hunted by Asteroids");
                            GUI.Label(SLeftRect(++line), StringUtils.Localize("--Shame badge--"));
                            HoSTag = GUI.TextField(SLeftRect(++line, 1, true), HoSTag, textFieldStyle);
                            BDArmorySettings.HOS_BADGE = HoSTag;
                        }
                        else
                        {
                            BDArmorySettings.HOS_FIRE = 0;
                            BDArmorySettings.HOS_MASS = 0;
                            BDArmorySettings.HOS_DMG = 100;
                            BDArmorySettings.HOS_THRUST = 100;
                            BDArmorySettings.HOS_SAS = false;
                            BDArmorySettings.HOS_ASTEROID = false;
                            //partloss = false; //- would need special module, but could also be a mutator mode
                            //timebomb = false //same
                            //might be more elegant to simply have this use Mutator framework and load the HoS craft with a select mutator(s) instead... Something to look into later, maybe, but ideally this shouldn't need to be used in the first place.
                        }
                    }
                    /*
                    if (BDArmorySettings.PS_CONVENIENCE_CHECKS)
                    {
                        if (CheatCodeGUI != (CheatCodeGUI = GUI.TextField(SRightRect(++line, 1, true), CheatCodeGUI, textFieldStyle))) 
                        {
                            switch (CheatCodeGUI)
                            {
                                case "PSSettings": //until we figure out where to put this
                                    {
                                        PSSettings = !PSSettings;
                                        CheatCodeGUI = "";
                                        break;
                                    }                            
                            }
                        }
                        if (PSSettings) //rushjob hack. Look into implementing something akin to the RWPSettings/hooking into that, for a more modular approach?
                        {
                            GUI.Label(SLeftSliderRect(++line, 1f), $"{StringUtils.Localize("Extend Time Out")}:  ({BDArmorySettings.PS_EXTEND_TIMEOUT})", leftLabel);
                            BDArmorySettings.PS_EXTEND_TIMEOUT = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.PS_EXTEND_TIMEOUT, 0, 60));

                            GUI.Label(SLeftSliderRect(++line, 1f), $"{StringUtils.Localize("Max Extend Dist")}:  ({BDArmorySettings.PS_EXTEND_DIST})", leftLabel);
                            BDArmorySettings.PS_EXTEND_DIST = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.PS_EXTEND_DIST, 0, 30000), 100);

                            GUI.Label(SLeftSliderRect(++line, 1f), $"{StringUtils.Localize("Max View Range (1 seat)")}:  ({BDArmorySettings.PS_MONOCOCKPIT_VIEWRANGE})", leftLabel);
                            BDArmorySettings.PS_MONOCOCKPIT_VIEWRANGE = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.PS_MONOCOCKPIT_VIEWRANGE, 0, 30000), 250);

                            GUI.Label(SLeftSliderRect(++line, 1f), $"{StringUtils.Localize("Max View Range (2 seat)")}:  ({BDArmorySettings.PS_DUALCOCKPIT_VIEWRANGE})", leftLabel);
                            BDArmorySettings.PS_DUALCOCKPIT_VIEWRANGE = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.PS_DUALCOCKPIT_VIEWRANGE, 0, 30000), 250);

                            GUI.Label(SLeftSliderRect(++line, 1f), $"{StringUtils.Localize("Cockpit FOV (1 seat)")}:  ({BDArmorySettings.PS_COCKPIT_FOV})", leftLabel);
                            BDArmorySettings.PS_COCKPIT_FOV = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.PS_COCKPIT_FOV, 1, 360));

                            GUI.Label(SLeftSliderRect(++line, 1f), $"{StringUtils.Localize("Avoid Threshold Min")}:  ({BDArmorySettings.PS_AVOID_THRESH})", leftLabel);
                            BDArmorySettings.PS_AVOID_THRESH = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.PS_AVOID_THRESH, 1, 30));

                            GUI.Label(SLeftSliderRect(++line, 1f), $"{StringUtils.Localize("Avoid LookAhead Min")}:  ({BDArmorySettings.PS_AVOID_LA})", leftLabel);
                            BDArmorySettings.PS_AVOID_LA = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.PS_AVOID_LA, 0, 3) * 10f) / 10f;

                            GUI.Label(SLeftSliderRect(++line, 1f), $"{StringUtils.Localize("Avoid Strength Min")}:  ({BDArmorySettings.PS_AVOID_STR})", leftLabel);
                            BDArmorySettings.PS_AVOID_STR = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.PS_AVOID_STR, 0, 4) * 10f) / 10f;

                            GUI.Label(SLeftSliderRect(++line, 1f), $"{StringUtils.Localize("Idle Speed Min")}:  ({BDArmorySettings.PS_IDLE_SPEED})", leftLabel);
                            BDArmorySettings.PS_IDLE_SPEED = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.PS_IDLE_SPEED, 100, 500), 10);
                            BDArmorySettings.PS_DISABLE_SAS = GUI.Toggle(SLeftRect(++line, 1), BDArmorySettings.PS_DISABLE_SAS, StringUtils.Localize("Disable non-Cockpit SAS"));
                            line++;
                            //min/max Altitude setters?
                        }
                    }
                    */
                }

                if (BDArmorySettings.BATTLEDAMAGE != (BDArmorySettings.BATTLEDAMAGE = GUI.Toggle(SLeftRect(++line), BDArmorySettings.BATTLEDAMAGE, StringUtils.Localize("#LOC_BDArmory_Settings_BattleDamage"))))
                {
                    BDArmorySettings.PAINTBALL_MODE = false;
                }
                BDArmorySettings.INFINITE_AMMO = GUI.Toggle(SRightRect(line), BDArmorySettings.INFINITE_AMMO, StringUtils.Localize("#LOC_BDArmory_Settings_InfiniteAmmo"));
                if (BDArmorySettings.PAINTBALL_MODE != (BDArmorySettings.PAINTBALL_MODE = GUI.Toggle(SLeftRect(++line), BDArmorySettings.PAINTBALL_MODE, StringUtils.Localize("#LOC_BDArmory_Settings_PaintballMode"))))//"Paintball Mode"
                {
                    BulletHitFX.SetupShellPool();
                    BDArmorySettings.BATTLEDAMAGE = false;
                }
                BDArmorySettings.INFINITE_ORDINANCE = GUI.Toggle(SRightRect(line), BDArmorySettings.INFINITE_ORDINANCE, StringUtils.Localize("#LOC_BDArmory_Settings_InfiniteMissiles"));
                if (BDArmorySettings.PEACE_MODE != (BDArmorySettings.PEACE_MODE = GUI.Toggle(SLeftRect(++line), BDArmorySettings.PEACE_MODE, StringUtils.Localize("#LOC_BDArmory_Settings_PeaceMode"))))//"Peace Mode"
                {
                    BDATargetManager.ClearDatabase();
                    if (OnPeaceEnabled != null)
                    {
                        OnPeaceEnabled();
                    }
                    CheatOptions.InfinitePropellant = BDArmorySettings.PEACE_MODE || BDArmorySettings.INFINITE_FUEL;
                }
                BDArmorySettings.INFINITE_COUNTERMEASURES = GUI.Toggle(SRightRect(line), BDArmorySettings.INFINITE_COUNTERMEASURES, StringUtils.Localize("#LOC_BDArmory_Settings_InfiniteCountermeasures"));
                BDArmorySettings.TAG_MODE = GUI.Toggle(SLeftRect(++line), BDArmorySettings.TAG_MODE, StringUtils.Localize("#LOC_BDArmory_Settings_TagMode"));//"Tag Mode"
                BDArmorySettings.INFINITE_FUEL = CheatOptions.InfinitePropellant; // Sync with the Alt-F12 window if the checkbox was toggled there.
                if (BDArmorySettings.INFINITE_FUEL != (BDArmorySettings.INFINITE_FUEL = GUI.Toggle(SRightRect(line), BDArmorySettings.INFINITE_FUEL, StringUtils.Localize("#autoLOC_900349"))))//"Infinite Propellant"
                {
                    CheatOptions.InfinitePropellant = BDArmorySettings.INFINITE_FUEL;
                }
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
                BDArmorySettings.INFINITE_EC = CheatOptions.InfiniteElectricity; // Sync with the Alt-F12 window if the checkbox was toggled there.
                if (BDArmorySettings.INFINITE_EC != (BDArmorySettings.INFINITE_EC = GUI.Toggle(SRightRect(line), BDArmorySettings.INFINITE_EC, StringUtils.Localize("#autoLOC_900361"))))//"Infinite Electricity"
                {
                    CheatOptions.InfiniteElectricity = BDArmorySettings.INFINITE_EC;
                }
                //Mutators
                var oldMutators = BDArmorySettings.MUTATOR_MODE;
                BDArmorySettings.MUTATOR_MODE = GUI.Toggle(SLeftRect(++line), BDArmorySettings.MUTATOR_MODE, StringUtils.Localize("#LOC_BDArmory_Settings_Mutators"));
                {
                    if (BDArmorySettings.MUTATOR_MODE)
                    {
                        if (!oldMutators)  // Add missing modules when Space Hacks is toggled.
                        {
                            foreach (var vessel in FlightGlobals.Vessels)
                            {
                                if (vessel.ActiveController().WM != null && vessel.rootPart.FindModuleImplementing<BDAMutator>() == null)
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
                            BDArmorySettings.MUTATOR_APPLY_GUNGAME = GUI.Toggle(SLeftRect(++line, 1f), BDArmorySettings.MUTATOR_APPLY_GUNGAME, StringUtils.Localize("#LOC_BDArmory_Settings_MutatorGungame"));
                            BDArmorySettings.MUTATOR_APPLY_GLOBAL = false;
                            BDArmorySettings.MUTATOR_APPLY_TIMER = false;
                            if (BDArmorySettings.MUTATOR_APPLY_GUNGAME)
                            {
                                BDArmorySettings.GG_PERSISTANT_PROGRESSION = GUI.Toggle(SLeftRect(++line, 1f), BDArmorySettings.GG_PERSISTANT_PROGRESSION, StringUtils.Localize("#LOC_BDArmory_settings_gungame_progression"));
                                BDArmorySettings.GG_CYCLE_LIST = GUI.Toggle(SRightRect(line, 1f), BDArmorySettings.GG_CYCLE_LIST, StringUtils.Localize("#LOC_BDArmory_settings_gungame_cycle"));
                            }
                            if (GUI.Button(SLeftRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_reset")} {StringUtils.Localize("#LOC_BDArmory_Weapons")}")) SpawnUtilsInstance.Instance.gunGameProgress.Clear(); // Clear gun-game progress.
                            if (!MutatorInfo.gunGameConfigured) MutatorInfo.SetupGunGame();
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
                        BDArmorySettings.MUTATOR_DURATION = (float)Math.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.MUTATOR_DURATION, 0f, BDArmorySettings.COMPETITION_DURATION > 0 ? BDArmorySettings.COMPETITION_DURATION : 15), 1);
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
                    else if (oldMutators && HighLogic.LoadedSceneIsFlight && !(BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 61))
                    {
                        foreach (var vessel in FlightGlobals.VesselsLoaded) SpawnUtils.ApplyMutators(vessel, false); // Clear mutators on existing vessels when disabling this.
                        SpawnUtils.ApplyMutatorsOnNewVessels(false); // And prevent any new ones.
                    }
                }
                // Heartbleed
                BDArmorySettings.HEART_BLEED_ENABLED = GUI.Toggle(SLeftRect(++line), BDArmorySettings.HEART_BLEED_ENABLED, StringUtils.Localize("#LOC_BDArmory_Settings_HeartBleed"));//"Heart Bleed"
                if (BDArmorySettings.HEART_BLEED_ENABLED)
                {
                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_HeartBleedRate")}:  ({BDArmorySettings.HEART_BLEED_RATE})", leftLabel);//Heart Bleed Rate
                    BDArmorySettings.HEART_BLEED_RATE = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.HEART_BLEED_RATE, 0f, 0.1f) * 1000f) / 1000f;
                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_HeartBleedInterval")}:  ({BDArmorySettings.HEART_BLEED_INTERVAL})", leftLabel);//Heart Bleed Interval
                    BDArmorySettings.HEART_BLEED_INTERVAL = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.HEART_BLEED_INTERVAL, 1f, 60f));
                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_HeartBleedThreshold")}:  ({BDArmorySettings.HEART_BLEED_THRESHOLD})", leftLabel);//Heart Bleed Threshold
                    BDArmorySettings.HEART_BLEED_THRESHOLD = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.HEART_BLEED_THRESHOLD, 1f, 100f));
                }
                if (BDArmorySettings.G_LIMITS != (BDArmorySettings.G_LIMITS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.G_LIMITS, StringUtils.Localize("#LOC_BDArmory_Settings_GLimitsMode"))))//G-Force Limits
                {
                    if (BDArmorySettings.G_LIMITS) // Sync the initial override values to the current ones.
                    {
                        RWPSettings.SyncWithGameSettings(toKSP: false); // Update the current backing values from the Game Difficulty.
                        RWPSettings.SyncWithGameSettings(); // Then override them.
                    }
                    else
                    {
                        RWPSettings.SyncWithGameSettings(); // Sync so that the Game Difficulty values get reverted to the backing values.
                    }
                }
                if (BDArmorySettings.G_LIMITS)
                {
                    if (HighLogic.CurrentGame != null)
                    {
                        // Sync with the Game Difficulty window if the checkbox was toggled there and update the backing values. (Note: this only happens if the menu is open.)
                        advancedParams = HighLogic.CurrentGame.Parameters.CustomParams<GameParameters.AdvancedParams>(); // Grab the latest values, in case KSP has changed the instance.
                        if (BDArmorySettings.PART_GLIMIT != (BDArmorySettings.PART_GLIMIT = advancedParams.GPartLimits)) BDArmorySettings._PART_GLIMIT = BDArmorySettings.PART_GLIMIT;
                        if (BDArmorySettings.KERB_GLIMIT != (BDArmorySettings.KERB_GLIMIT = advancedParams.GKerbalLimits)) BDArmorySettings._KERB_GLIMIT = BDArmorySettings.KERB_GLIMIT;
                        if (BDArmorySettings.G_TOLERANCE != (BDArmorySettings.G_TOLERANCE = BDAMath.RoundToUnit(advancedParams.KerbalGToleranceMult * 20.5f, 0.5f))) BDArmorySettings._G_TOLERANCE = BDArmorySettings.G_TOLERANCE;
                    }

                    bool advParamsChanged = false;
                    if (BDArmorySettings.PART_GLIMIT != (BDArmorySettings.PART_GLIMIT = GUI.Toggle(SLeftRect(++line, 1), BDArmorySettings.PART_GLIMIT, StringUtils.Localize("#autoLOC_140950"))))//Part G-Force Limits
                        advParamsChanged = true;
                    if (BDArmorySettings.KERB_GLIMIT != (BDArmorySettings.KERB_GLIMIT = GUI.Toggle(SRightRect(line, 1), BDArmorySettings.KERB_GLIMIT, StringUtils.Localize("#autoLOC_140953"))))//Kerbal G-Force Limits
                        advParamsChanged = true;
                    if (BDArmorySettings.KERB_GLIMIT)
                    {
                        //G-Limit is point at which Kerbs begin to fill the G-meter; e.g. a G_TOLERANCE of 8 would result in Kerbs indefinately tolerating a 8g turn, but a 9g sustained turn would *very* slowly fill the meter, a 13g sustained turn would KO them after 5-6 seconds, a 20g turn would KO them instantly, etc.
                        GUI.Label(SLeftSliderRect(++line, 1), $"{StringUtils.Localize("#autoLOC_140956")}:  ({BDArmorySettings.G_TOLERANCE:0.0}g)", leftLabel);//Kerbal G-Force Tolerance
                        if (BDArmorySettings.G_TOLERANCE != (BDArmorySettings.G_TOLERANCE = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.G_TOLERANCE, 1f, 40f), 0.5f)))
                            advParamsChanged = true;
                    }
                    if (advParamsChanged)
                    {
                        RWPSettings.SyncWithGameSettings();
                    }
                }
                GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_AimingVisualMalus")}: ({BDArmorySettings.AIMING_VISUAL_MALUS:G2})", leftLabel); // Aiming Visual Malus
                BDArmorySettings.AIMING_VISUAL_MALUS = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.AIMING_VISUAL_MALUS, 0f, 1f), 0.01f);
                // Resource steal
                BDArmorySettings.RESOURCE_STEAL_ENABLED = GUI.Toggle(SLeftRect(++line), BDArmorySettings.RESOURCE_STEAL_ENABLED, StringUtils.Localize("#LOC_BDArmory_Settings_ResourceSteal"));//"Resource Steal"
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
                    BDArmorySettings.SF_DRAGMULT = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.SF_DRAGMULT, 0f, 50));
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
                    var altitudeString = BDArmorySettings.ASTEROID_FIELD_ALTITUDE < 1000f ? $"{BDArmorySettings.ASTEROID_FIELD_ALTITUDE:G3}m" : $"{BDArmorySettings.ASTEROID_FIELD_ALTITUDE / 1000f:G5}km";
                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_AsteroidFieldAltitude")}:  ({altitudeString})", leftLabel);
                    BDArmorySettings.ASTEROID_FIELD_ALTITUDE = GUIUtils.HorizontalSemiLogSlider(SRightSliderRect(line), BDArmorySettings.ASTEROID_FIELD_ALTITUDE, 100f, Mathf.Max(100000f, BDArmorySettings.VESSEL_SPAWN_ALTITUDE), 1.5f, false, false, ref asteroidFieldAltitude); // Asteroid Field Altitude
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

                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_BD_Proc_Pen")}: ({BDArmorySettings.BD_DAMAGE_PENETRATION})", leftLabel); //Proc Chance Penetration
                    BDArmorySettings.BD_DAMAGE_PENETRATION = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.BD_DAMAGE_PENETRATION, 0f, 1f), 0.01f);

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

                GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_TriggerHold")}: {BDArmorySettings.TRIGGER_HOLD_TIME:0.00} s", leftLabel);//Trigger Hold
                BDArmorySettings.TRIGGER_HOLD_TIME = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.TRIGGER_HOLD_TIME, 0.02f, 1f), 0.02f);

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
                weaponVol = GUI.HorizontalSlider(SRightSliderRect(line), weaponVol, 0f, 2f);
                if (uiVol != BDArmorySettings.BDARMORY_WEAPONS_VOLUME && OnVolumeChange != null)
                {
                    OnVolumeChange();
                }
                BDArmorySettings.BDARMORY_WEAPONS_VOLUME = weaponVol;

                if (BDArmorySettings.ADVANCED_USER_SETTINGS)
                {
                    BDArmorySettings.TRACE_VESSELS_DURING_COMPETITIONS = GUI.Toggle(SLineThirdRect(++line, 0, 2), BDArmorySettings.TRACE_VESSELS_DURING_COMPETITIONS, StringUtils.Localize("#LOC_BDArmory_Settings_TraceVessels"));// Trace Vessels (custom 2/3 width)
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
                    BDArmorySettings.AUTO_LOG_TIME_SYNC = GUI.Toggle(SLineThirdRect(++line, 0, 2), BDArmorySettings.AUTO_LOG_TIME_SYNC, StringUtils.Localize("#LOC_BDArmory_Settings_AutoLogTimeSync"));
                    if (GUI.Button(SLineThirdRect(line, 2), StringUtils.Localize(logTimeSyncEnabled ? "#LOC_BDArmory_Settings_LogTimeSyncStop" : "#LOC_BDArmory_Settings_LogTimeSyncStart"))) SetTimeSyncLogging(!logTimeSyncEnabled);
                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_LogTimeSyncInterval")}: {BDArmorySettings.LOG_TIME_SYNC_INTERVAL:G2}s", leftLabel);
                    BDArmorySettings.LOG_TIME_SYNC_INTERVAL = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.LOG_TIME_SYNC_INTERVAL, Time.fixedDeltaTime, 1f), Time.fixedDeltaTime);
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
                        startNowAfter = StringUtils.Localize("#LOC_BDArmory_Generic_Off");
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

                GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_CompetitionKillTimer")}: (" + (BDArmorySettings.COMPETITION_KILL_TIMER > 0 ? (BDArmorySettings.COMPETITION_KILL_TIMER + "s") : StringUtils.Localize("#LOC_BDArmory_Generic_Off")) + ")", leftLabel); // FIXME the toggle and this slider could be merged
                BDArmorySettings.COMPETITION_KILL_TIMER = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.COMPETITION_KILL_TIMER, 0, 60f));

                GUI.Label(SLeftRect(++line), StringUtils.Localize("#LOC_BDArmory_Settings_CompetitionIntraTeamSeparation")); // Intra-team separation.
                var intraTeamSepRect = SRightRect(line, 1, true);
                compIntraTeamSeparationBase = GUI.TextField(new Rect(intraTeamSepRect.x, intraTeamSepRect.y, intraTeamSepRect.width / 4 + 2, intraTeamSepRect.height), compIntraTeamSeparationBase, 6, textFieldStyle);
                GUI.Label(new Rect(intraTeamSepRect.x + intraTeamSepRect.width / 4 + 2, intraTeamSepRect.y, 15, intraTeamSepRect.height), " + ");
                compIntraTeamSeparationPerMember = GUI.TextField(new Rect(intraTeamSepRect.x + intraTeamSepRect.width / 4 + 17, intraTeamSepRect.y, intraTeamSepRect.width / 4 + 2, intraTeamSepRect.height), compIntraTeamSeparationPerMember, 6, textFieldStyle);
                GUI.Label(new Rect(intraTeamSepRect.x + intraTeamSepRect.width / 2 + 25, intraTeamSepRect.y, intraTeamSepRect.width / 2 - 25, intraTeamSepRect.height), StringUtils.Localize("#LOC_BDArmory_Settings_CompetitionIntraTeamSeparationPerMember"));
                if (float.TryParse(compIntraTeamSeparationBase, out float cIntraBase) && BDArmorySettings.COMPETITION_INTRA_TEAM_SEPARATION_BASE != cIntraBase)
                {
                    cIntraBase = Mathf.Round(cIntraBase); BDArmorySettings.COMPETITION_INTRA_TEAM_SEPARATION_BASE = cIntraBase;
                    if (cIntraBase != 0) compIntraTeamSeparationBase = cIntraBase.ToString();
                }
                if (float.TryParse(compIntraTeamSeparationPerMember, out float cIntraPerMember) && BDArmorySettings.COMPETITION_INTRA_TEAM_SEPARATION_PER_MEMBER != cIntraPerMember)
                {
                    cIntraPerMember = Mathf.Round(cIntraPerMember); BDArmorySettings.COMPETITION_INTRA_TEAM_SEPARATION_PER_MEMBER = cIntraPerMember;
                    if (cIntraPerMember != 0) compIntraTeamSeparationPerMember = cIntraPerMember.ToString();
                }

                GUI.Label(SLeftRect(++line), StringUtils.Localize("#LOC_BDArmory_Settings_CompetitionDistance"));//"Competition Distance"
                compDistGui = GUI.TextField(SRightRect(line, 1, true), compDistGui, textFieldStyle);
                if (float.TryParse(compDistGui, out float cDist) && BDArmorySettings.COMPETITION_DISTANCE != cDist)
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

                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_CompetitionKillerGMFrequency")}: ({(BDArmorySettings.COMPETITION_KILLER_GM_FREQUENCY > 60 ? StringUtils.Localize("#LOC_BDArmory_Generic_Off") : BDArmorySettings.COMPETITION_KILLER_GM_FREQUENCY + "s")}, {(BDACompetitionMode.Instance != null && BDACompetitionMode.Instance.killerGMenabled ? StringUtils.Localize("#LOC_BDArmory_Generic_On") : StringUtils.Localize("#LOC_BDArmory_Generic_Off"))})", leftLabel);
                        BDArmorySettings.COMPETITION_KILLER_GM_FREQUENCY = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.COMPETITION_KILLER_GM_FREQUENCY / 10f, 1, 6)) * 10f; // For now, don't control the killerGMEnabled flag (it's controlled by right clicking M).
                    }
                    // Craft autokill criteria
                    BDArmorySettings.COMPETITION_GM_KILL_WEAPON = GUI.Toggle(SLineRect(++line), BDArmorySettings.COMPETITION_GM_KILL_WEAPON, StringUtils.Localize("#LOC_BDArmory_Settings_CompetitionGMWeaponKill"));
                    BDArmorySettings.COMPETITION_GM_KILL_ENGINE = GUI.Toggle(SLineRect(++line), BDArmorySettings.COMPETITION_GM_KILL_ENGINE, StringUtils.Localize("#LOC_BDArmory_Settings_CompetitionGMEngineKill"));
                    BDArmorySettings.COMPETITION_GM_KILL_DISABLED = GUI.Toggle(SLineRect(++line), BDArmorySettings.COMPETITION_GM_KILL_DISABLED, StringUtils.Localize("#LOC_BDArmory_Settings_CompetitionGMDisableKill"));
                    string GMKillHP;
                    if (BDArmorySettings.COMPETITION_GM_KILL_HP <= 0f) GMKillHP = StringUtils.Localize("#LOC_BDArmory_Generic_Off");
                    else GMKillHP = $"<{Mathf.RoundToInt((BDArmorySettings.COMPETITION_GM_KILL_HP))}%";
                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_CompetitionGMHPKill")}: {GMKillHP}", leftLabel);
                    BDArmorySettings.COMPETITION_GM_KILL_HP = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.COMPETITION_GM_KILL_HP, 0, 99));

                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_CompetitionGMKillDelay")}: {(BDArmorySettings.COMPETITION_GM_KILL_TIME > -1 ? (BDArmorySettings.COMPETITION_GM_KILL_TIME + "s") : StringUtils.Localize("#LOC_BDArmory_Generic_Off"))}", leftLabel);
                    BDArmorySettings.COMPETITION_GM_KILL_TIME = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.COMPETITION_GM_KILL_TIME, -1, 60));

                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_CompetitionWaypointTimeThreshold")}: ({(BDArmorySettings.COMPETITION_WAYPOINTS_GM_KILL_PERIOD > 0 ? $"{BDArmorySettings.COMPETITION_WAYPOINTS_GM_KILL_PERIOD:0}s" : StringUtils.Localize("#LOC_BDArmory_Generic_Off"))})", leftLabel); // Waypoint threshold
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
                        BDArmorySettings.COMPETITION_HASH = GUI.TextField(SRightRect(line, 1, true), BDArmorySettings.COMPETITION_HASH, textFieldStyle);
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
                            string vesselPath = Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath, "AutoSpawn"));
                            if (!Directory.Exists(vesselPath)) Directory.CreateDirectory(vesselPath);
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
                                    case 67:
                                        BDACompetitionMode.Instance.StartRapidDeployment(0);
                                        break;
                                    case 77:
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
            if (!BDKeyBinder.current)
            {
                if (GUI.Button(SQuarterRect(++line, 0), StringUtils.Localize("#LOC_BDArmory_Generic_Reload"))) // Reload
                {
                    LoadConfig();
                }
                if (GUI.Button(SQuarterRect(line, 1, 3), StringUtils.Localize("#LOC_BDArmory_Generic_SaveandClose")))//"Save and Close"
                {
                    SaveConfig();
                    windowSettingsEnabled = false;
                }
            }

            line += 1.5f; // Bottom internal margin
            settingsHeight = (line * settingsLineHeight);
            WindowRectSettings.height = settingsHeight;
            GUIUtils.DragWindow();
            if (!scalingUI) GUIUtils.RepositionWindow(ref WindowRectSettings);
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
            Rect scrollerRect = new Rect(0, 0, settingsWidth - GUI.skin.verticalScrollbar.fixedWidth - 1, inputFields != null ? (inputFields.Length + 2 * 9) * settingsLineHeight : settingsHeight);

            _displayViewerPosition = GUI.BeginScrollView(viewRect, _displayViewerPosition, scrollerRect, false, true);

#if DEBUG
            GUI.Label(SLineRect(line++), $"- {StringUtils.Localize("#LOC_BDArmory_Settings_DebugSettingsToggle")} -", centerLabel); //Debugging
            InputSettingsList("DEBUG_", ref inputID, ref line);
            ++line;
#endif

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
            ++line;

            GUI.Label(SLineRect(line++), $"- {StringUtils.Localize("#LOC_BDArmory_InputSettings_TemporarilyShowMouse")} -", centerLabel);//Temporarily Show Mouse
            InputSettingsList("TEMPORARILY_SHOW_MOUSE", ref inputID, ref line);
            ++line;

            GUI.Label(SLineRect(line++), $"- {StringUtils.Localize("#LOC_BDArmory_InputSettings_MWC_SelectionBox")} -", centerLabel);//WingCommander Bounding Box
            InputSettingsList("MWC_", ref inputID, ref line);
            ++line;
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
            string label = string.Empty;
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
            if (GAME_UI_ENABLED)
            {
                // Switch visible/hidden window rects
                _WindowRectScoresUIVisible = WindowRectScores;
                if (_WindowRectScoresUIHidden != default) WindowRectScores = _WindowRectScoresUIHidden;
                if (ScoreWindow.Instance.autoResizingWindow) WindowRectScores.height = _WindowRectScoresUIVisible.height;
                _WindowRectVesselSwitcherUIVisible = WindowRectVesselSwitcher;
                if (_WindowRectVesselSwitcherUIHidden != default) WindowRectVesselSwitcher = _WindowRectVesselSwitcherUIHidden;
            }

            GAME_UI_ENABLED = false;
            BDACompetitionMode.Instance.UpdateGUIElements();
            UpdateCursorState();
        }

        void ShowGameUI()
        {
            if (!GAME_UI_ENABLED)
            {
                // Switch visible/hidden window rects
                _WindowRectScoresUIHidden = WindowRectScores;
                if (_WindowRectScoresUIVisible != default) WindowRectScores = _WindowRectScoresUIVisible;
                if (ScoreWindow.Instance.autoResizingWindow) WindowRectScores.height = _WindowRectScoresUIHidden.height;
                _WindowRectVesselSwitcherUIHidden = WindowRectVesselSwitcher;
                if (_WindowRectVesselSwitcherUIVisible != default) WindowRectVesselSwitcher = _WindowRectVesselSwitcherUIVisible;
            }

            GAME_UI_ENABLED = true;
            BDACompetitionMode.Instance.UpdateGUIElements();
            UpdateCursorState();
        }

        internal void OnDestroy()
        {
            if (saveWindowPosition)
            {
                if (GAME_UI_ENABLED)
                {
                    _WindowRectScoresUIVisible = WindowRectScores;
                    _WindowRectVesselSwitcherUIVisible = WindowRectVesselSwitcher;
                }
                else
                {
                    _WindowRectScoresUIHidden = WindowRectScores;
                    _WindowRectVesselSwitcherUIHidden = WindowRectVesselSwitcher;
                }
                BDAWindowSettingsField.Save();
            }
            if (windowSettingsEnabled || showVesselSpawnerGUI)
                SaveConfig();

            SetTimeSyncLogging(false);
            GameEvents.onHideUI.Remove(HideGameUI);
            GameEvents.onShowUI.Remove(ShowGameUI);
            GameEvents.onVesselGoOffRails.Remove(OnVesselGoOffRails);
            GameEvents.OnGameSettingsApplied.Remove(SaveVolumeSettings);
            GameEvents.onVesselChange.Remove(VesselChange);
            GameEvents.onGameSceneSwitchRequested.Remove(OnGameSceneSwitchRequested);
            GameEvents.onGameStateSave.Remove(OnGameStateSave);
            GameEvents.onGameStateSaved.Remove(OnGameStateSaved);
        }

        void OnVesselGoOffRails(Vessel v)
        {
            if (BDArmorySettings.DEBUG_OTHER)
            {
                Debug.Log("[BDArmory.BDArmorySetup]: Loaded vessel: " + v.vesselName + ", Velocity: " + v.Velocity() + ", packed: " + v.packed);
            }
        }

        public void SaveVolumeSettings()
        {
            SeismicChargeFX.originalShipVolume = GameSettings.SHIP_VOLUME;
            SeismicChargeFX.originalMusicVolume = GameSettings.MUSIC_VOLUME;
            SeismicChargeFX.originalAmbienceVolume = GameSettings.AMBIENCE_VOLUME;
        }

        #region TimeSyncLogging
        public bool logTimeSyncEnabled = false;
        readonly List<(float, float)> timeSyncLog = []; // List of time-sync timestamps.
        /// <summary>
        /// Enable time-sync logging.
        /// Game timestamps are made during the normal LateUpdate timing stage.
        /// </summary>
        /// <param name="enable">Enable or disable time-sync logging.</param>
        /// <param name="tag">A tag to use when writing the file when disabling time-sync logging.</param>
        public void SetTimeSyncLogging(bool enable, string tag = null)
        {
            if (enable)
            {
                if (!logTimeSyncEnabled) TimingManager.LateUpdateAdd(TimingManager.TimingStage.Normal, LogTimeSync);
            }
            else
            {
                TimingManager.LateUpdateRemove(TimingManager.TimingStage.Normal, LogTimeSync);
                DumpTimeSyncLog(tag);
                timeSyncLog.Clear();
            }
            logTimeSyncEnabled = enable;
        }
        /// <summary>
        /// Register a time-sync timestamp if the time-sync interval has passed (approx).
        /// </summary>
        void LogTimeSync()
        {
            if (Time.time - timeSyncLog.LastOrDefault().Item1 < BDArmorySettings.LOG_TIME_SYNC_INTERVAL - Time.fixedDeltaTime / 2f) return; // Round to the nearest physics frame.
            timeSyncLog.Add((Time.time, Time.realtimeSinceStartup));
        }
        /// <summary>
        /// Dump the time-sync timestamps to a compressed log.
        /// </summary>
        /// <param name="tag">The name of the file to dump to (without extension).</param>
        void DumpTimeSyncLog(string tag)
        {
            if (timeSyncLog.Count == 0) return;
            var folder = Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "BDArmory", "Logs", "TimeSync"));
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            using FileStream fileStream = File.Create(Path.Combine(folder, string.IsNullOrEmpty(tag) ? "time-sync.csv.gz" : $"{tag}.csv.gz"));
            using GZipStream gzStream = new(fileStream, CompressionMode.Compress);
            var startTime = timeSyncLog.FirstOrDefault();
            var tsLogBytes = Encoding.ASCII.GetBytes("Game-Time,Real-Time\n" + string.Join("\n", timeSyncLog.Select(ts => $"{ts.Item1 - startTime.Item1:F2},{ts.Item2 - startTime.Item2:F2}")));
            gzStream.Write(tsLogBytes, 0, tsLogBytes.Length);
        }
        #endregion
#if DEBUG
        // static int PROF_N_pow = 3, PROF_n_pow = 4;
        static int PROF_N = 1000, PROF_n = 10000;
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
            yield return wait; Debug.Log($"DEBUG {Time.time} WaitForFixedUpdate");
            yield return wait; Debug.Log($"DEBUG {Time.time} WaitForFixedUpdate");
            yield return wait; Debug.Log($"DEBUG {Time.time} WaitForFixedUpdate");
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
        void ObscenelyEarly() { Debug.Log($"DEBUG {Time.time} ObscenelyEarly, active vessel position: {FlightGlobals.ActiveVessel.CoM.ToString("G6")}, KbFV: {Krakensbane.GetFrameVelocityV3f()}"); }
        void Early() { Debug.Log($"DEBUG {Time.time} Early, active vessel position: {FlightGlobals.ActiveVessel.CoM.ToString("G6")}, KbFV: {Krakensbane.GetFrameVelocityV3f()}"); }
        void Precalc() { Debug.Log($"DEBUG {Time.time} Precalc, active vessel position: {FlightGlobals.ActiveVessel.CoM.ToString("G6")}, KbFV: {Krakensbane.GetFrameVelocityV3f()}"); }
        void Earlyish() { Debug.Log($"DEBUG {Time.time} Earlyish, active vessel position: {FlightGlobals.ActiveVessel.CoM.ToString("G6")}, KbFV: {Krakensbane.GetFrameVelocityV3f()}"); }
        void Normal() { Debug.Log($"DEBUG {Time.time} Normal, active vessel position: {FlightGlobals.ActiveVessel.CoM.ToString("G6")}, KbFV: {Krakensbane.GetFrameVelocityV3f()}"); }
        void FashionablyLate() { Debug.Log($"DEBUG {Time.time} FashionablyLate, active vessel position: {FlightGlobals.ActiveVessel.CoM.ToString("G6")}, KbFV: {Krakensbane.GetFrameVelocityV3f()}"); }
        void FlightIntegrator() { Debug.Log($"DEBUG {Time.time} FlightIntegrator, active vessel position: {FlightGlobals.ActiveVessel.CoM.ToString("G6")}, KbFV: {Krakensbane.GetFrameVelocityV3f()}"); }
        void Late() { Debug.Log($"DEBUG {Time.time} Late, active vessel position: {FlightGlobals.ActiveVessel.CoM.ToString("G6")}, KbFV: {Krakensbane.GetFrameVelocityV3f()}"); }
        void BetterLateThanNever() { Debug.Log($"DEBUG {Time.time} BetterLateThanNever, active vessel position: {FlightGlobals.ActiveVessel.CoM.ToString("G6")}, KbFV: {Krakensbane.GetFrameVelocityV3f()}"); }

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

        int TestNumericalMethodsIC = 0;
        IEnumerator TestNumericalMethods(float duration, int steps)
        {
            var vessel = FlightGlobals.ActiveVessel;
            var wait = new WaitForFixedUpdate();
            yield return wait; // Wait for the next fixedUpdate to synchronise with the physics.
            if (vessel == null) yield break;
            var dt = duration / steps;
            if (dt < Time.fixedDeltaTime)
            {
                dt = Time.fixedDeltaTime;
                steps = (int)(duration / dt);
            }
            Vector3 x = vessel.transform.position, x0 = x, x1 = x, x2 = x;
            Vector3 a0 = vessel.acceleration - FlightGlobals.getGeeForceAtPosition(x0), a1 = a0, a2 = a0; // Separate acceleration into local and gravitational.
            // Note: acceleration is an averaged (or derived) value, we should use acceleration_immediate instead, but most of the rest of the code uses acceleration.
            Vector3 v = vessel.rb_velocity + BDKrakensbane.FrameVelocityV3f;
            switch (TestNumericalMethodsIC)
            {
                case 0: // No correction
                    break;
                case 1:
                    v += 0.5f * Time.fixedDeltaTime * vessel.acceleration; // Unity integration correction for all acceleration
                    break;
                case 2:
                    v += 0.5f * Time.fixedDeltaTime * a0; // Unity integration correction for local acceleration only — this seems to give the best results for leap-frog
                    break;
                case 3:
                    v += 0.5f * Time.fixedDeltaTime * FlightGlobals.getGeeForceAtPosition(x0); // Unity integration correction for gravity only
                    break;
            }
            Vector3 v0 = v, v1 = v, v2 = v;
            Debug.Log($"DEBUG {Time.time} IC: {TestNumericalMethodsIC}, Initial acceleration: {a0}m/s^2 constant local + {(Vector3)FlightGlobals.getGeeForceAtPosition(x0)}m/s^2 gravity");
            for (int i = 0; i < steps; ++i)
            {
                // Forward Euler (1st order, not symplectic)
                var g = FlightGlobals.getGeeForceAtPosition(x0); // Get the gravity at the current time
                x0 += dt * v0; // Update position based on velocity at the current time
                v0 += dt * (a0 + g); // Update the velocity based on the potential at the current time

                // Semi-implicit Euler (1st order, symplectic)
                v1 += dt * (a1 + FlightGlobals.getGeeForceAtPosition(x1)); // Update velocity based on the potential at the current time
                x1 += dt * v1; // Update position based on velocity at the future time

                // Leap-frog (2nd order, symplectic)
                // Note: combining the second half-update to v2 with the first one of the next step into a single evaluation is where the name of the method comes from.
                v2 += 0.5f * dt * (a2 + FlightGlobals.getGeeForceAtPosition(x2)); // Update velocity half a step based on the potential at the current time
                x2 += dt * v2; // Update the position based on the velocity half-way between the current and future times
                v2 += 0.5f * dt * (a2 + FlightGlobals.getGeeForceAtPosition(x2)); // Update velocity another half a step based on the potential at the future time
            }

            var tic = Time.time;
            while (Time.time - tic < duration - Time.fixedDeltaTime / 2)
            {
                yield return wait;
                if (vessel == null)
                {
                    Debug.Log($"DEBUG Active vessel disappeared, aborting.");
                    yield break;
                }

                if (BDKrakensbane.IsActive) // Correct for Krakensbane
                {
                    x0 -= BDKrakensbane.FloatingOriginOffsetNonKrakensbane;
                    x1 -= BDKrakensbane.FloatingOriginOffsetNonKrakensbane;
                    x2 -= BDKrakensbane.FloatingOriginOffsetNonKrakensbane;
                }
            }
            x = vessel.transform.position;
            Debug.Log($"DEBUG {Time.time}: After {duration}s ({steps}*{dt}={steps * dt}), Actual x = {x}, Forward Euler predicts x = {x0} (Δ = {(x - x0).magnitude}), Semi-implicit Euler predicts x = {x1} (Δ = {(x - x1).magnitude}), Leap-frog predicts x = {x2} (Δ = {(x - x2).magnitude})");
        }

        public static void TestActiveController()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            var watch = new System.Diagnostics.Stopwatch();
            float μsResolution = 1e6f / System.Diagnostics.Stopwatch.Frequency;
            Debug.Log($"DEBUG Clock resolution: {μsResolution}μs, {PROF_N} outer loops, {PROF_n} inner loops");
            ActiveController activeController = null;
            var func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { activeController = vessel.ActiveController(); } };
            Debug.Log($"DEBUG vessel.ActiveController() took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {activeController} with hash {(uint)activeController.GetHashCode()}");
            MissileFire wm = null;
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { wm = vessel.ActiveController().WM; } };
            Debug.Log($"DEBUG vessel.ActiveController().WM took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {wm} with hash {(uint)wm.GetHashCode()}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { wm = VesselModuleRegistry.GetMissileFire(vessel); } };
            Debug.Log($"DEBUG VesselModuleRegistry.GetMissileFire(vessel) took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {wm} with hash {(uint)wm.GetHashCode()}");
            bool same = false;
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { same = wm != null && wm.IsPrimaryWM && wm.vessel == vessel; } };
            Debug.Log($"DEBUG wm != null && wm.IsPrimaryWM && wm.vessel == vessel took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {same}");
            BDModulePilotAI pilotAI = null;
            BDModuleSurfaceAI surfaceAI = null;
            BDModuleVTOLAI vtolAI = null;
            BDModuleOrbitalAI orbitalAI = null;
            IBDAIControl AI = vessel.ActiveController().AI;
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () =>
            {
                for (int i = 0; i < PROF_n; ++i)
                {
                    pilotAI = null; surfaceAI = null; vtolAI = null; orbitalAI = null;
                    if (AI != null && AI.pilotEnabled)
                    {
                        switch (AI.aiType)
                        {
                            case AIType.PilotAI: pilotAI = AI as BDModulePilotAI; break;
                            case AIType.SurfaceAI: surfaceAI = AI as BDModuleSurfaceAI; break;
                            case AIType.VTOLAI: vtolAI = AI as BDModuleVTOLAI; break;
                            case AIType.OrbitalAI: orbitalAI = AI as BDModuleOrbitalAI; break;
                        }
                    }
                }
            };
            Debug.Log($"DEBUG Multiple AI type selection took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {pilotAI}, {surfaceAI}, {vtolAI}, {orbitalAI}");
        }

        public static void TestAbs()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            Vector3 pos = vessel.CoM;
            var watch = new System.Diagnostics.Stopwatch();
            float μsResolution = 1e6f / System.Diagnostics.Stopwatch.Frequency;
            Debug.Log($"DEBUG Clock resolution: {μsResolution}μs, {PROF_N} outer loops, {PROF_n} inner loops");
            float x = 1.234f, y = -1.234f, zx = 0, zy = 0;
            [MethodImpl(MethodImplOptions.AggressiveInlining)] float Abs(float x) { return x < 0 ? -x : x; }
            var func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { zx = Mathf.Abs(x); zy = Mathf.Abs(y); } };
            Debug.Log($"DEBUG Mathf.Abs took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {zx}, {zy}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { zx = Math.Abs(x); zy = Math.Abs(y); } };
            Debug.Log($"DEBUG Math.Abs(x) took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {zx}, {zy}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { zx = (float)Math.Abs((double)x); zy = (float)Math.Abs((double)y); } };
            Debug.Log($"DEBUG (float)Math.Abs((double)x) took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {zx}, {zy}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { zx = Abs(x); zy = Abs(y); } };
            Debug.Log($"DEBUG Abs(x) {{ return x < 0 ? -x : x; }} took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {zx}, {zy}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { zx = x < 0 ? -x : x; zy = y < 0 ? -y : y; } };
            Debug.Log($"DEBUG inline x<0?-x:x took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {zx}, {zy}");
        }

        public static void TestAngle()
        {
            Vector3 v = UnityEngine.Random.onUnitSphere, v2 = Vector3.zero;
            var ax = Vector3.Cross(Vector3.up, v);
            foreach (var angle in new List<float> { 0, 1e-8f, 1e-7f, 1e-6f, 1e-5f, 1e-4f, 1e-3f, 1e-2f, 2e-2f, 3e-2f, 4e-2f, 5e-2f, 1e-1f })
            {
                v2 = Quaternion.AngleAxis(angle, ax) * v;
                Debug.Log($"DEBUG angle from v={v} to v rotated by {angle} is {Vector3.Angle(v, v2)} vs {VectorUtils.Angle(v, v2)} vs {(float)Vector3d.Angle(v, v2)}");
            }
            var watch = new System.Diagnostics.Stopwatch();
            float μsResolution = 1e6f / System.Diagnostics.Stopwatch.Frequency;
            Debug.Log($"DEBUG Clock resolution: {μsResolution}μs, {PROF_N} outer loops, {PROF_n} inner loops");
            float a = 0;
            var func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { a = Vector3.Angle(v, v2); } };
            Debug.Log($"DEBUG Vector3.Angle took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {a}");
            a = 0;
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { a = VectorUtils.Angle(v, v2); } };
            Debug.Log($"DEBUG VectorUtils.Angle took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {a}");
            a = 0;
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { a = (float)Vector3d.Angle(v, v2); } };
            Debug.Log($"DEBUG (float)Vector3d.Angle took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {a}");
        }

        public static void TestBounds()
        {
            var watch = new System.Diagnostics.Stopwatch();
            float μsResolution = 1e6f / System.Diagnostics.Stopwatch.Frequency;
            Debug.Log($"DEBUG Clock resolution: {μsResolution}μs, {PROF_N} outer loops, {PROF_n} inner loops");
            var vessel = FlightGlobals.ActiveVessel;
            Bounds bounds = default;
            Vector3 size = default;
            // Current GetBounds
            var func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { size = vessel.GetBounds(false); } };
            Debug.Log($"DEBUG vessel.GetBounds(false) took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {size}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { size = vessel.GetBounds(true); } };
            Debug.Log($"DEBUG vessel.GetBounds(true) took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {size}");
            // Collider bounds
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { bounds = vessel.GetColliderBounds(); } };
            Debug.Log($"DEBUG vessel.GetColliderBounds() took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {bounds}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { size = vessel.GetColliderBounds().size; } };
            Debug.Log($"DEBUG vessel.GetColliderBounds().size took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {size}");
            // Renderer bounds, with and without LineRenderers/ParticleSystemRenderers
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { bounds = vessel.GetRendererBounds(); } };
            Debug.Log($"DEBUG vessel.GetRendererBounds() took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {bounds}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { size = vessel.GetRendererBounds().size; } };
            Debug.Log($"DEBUG vessel.GetRendererBounds().size took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {size}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { bounds = vessel.GetRendererBounds(ignoreLineRenderers: false); } };
            Debug.Log($"DEBUG vessel.GetRendererBounds(ignoreLineRenderers:false) took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {bounds}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { size = vessel.GetRendererBounds(ignoreLineRenderers: false).size; } };
            Debug.Log($"DEBUG vessel.GetRendererBounds(ignoreLineRenderers:false).size took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {size}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { bounds = vessel.GetRendererBounds(ignoreLineRenderers: false, ignoreParticleRenderers: false); } };
            Debug.Log($"DEBUG vessel.GetRendererBounds(ignoreLineRenderers:false,ignoreParticleRenderers:false) took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {bounds}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { size = vessel.GetRendererBounds(ignoreLineRenderers: false, ignoreParticleRenderers: false).size; } };
            Debug.Log($"DEBUG vessel.GetRendererBounds(ignoreLineRenderers:false,ignoreParticleRenderers:false).size took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {size}");
            // Using cached model renderers vs current renderers
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { bounds = vessel.GetRendererBounds(useCached: true); } };
            Debug.Log($"DEBUG vessel.GetRendererBounds(useCached:true) took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {bounds}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { size = vessel.GetRendererBounds(useCached: true).size; } };
            Debug.Log($"DEBUG vessel.GetRendererBounds(useCached:true).size took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {size}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { bounds = vessel.GetRendererBounds(useCached: false); } };
            Debug.Log($"DEBUG vessel.GetRendererBounds(useCached:false) took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {bounds}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { size = vessel.GetRendererBounds(useCached: false).size; } };
            Debug.Log($"DEBUG vessel.GetRendererBounds(useCached:false).size took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {size}");
        }

        public static void TestUp()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            Vector3 pos = vessel.CoM;
            var watch = new System.Diagnostics.Stopwatch();
            float μsResolution = 1e6f / System.Diagnostics.Stopwatch.Frequency;
            Debug.Log($"DEBUG Clock resolution: {μsResolution}μs, {PROF_N} outer loops, {PROF_n} inner loops");
            Vector3 up = default;
            var func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { up = vessel.upAxis; } };
            Debug.Log($"DEBUG vessel.upAxis took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {(Vector3d)up}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { up = VectorUtils.GetUpDirection(pos); } };
            Debug.Log($"DEBUG GetUpDirection took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {(Vector3d)up}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { up = vessel.up; } };
            Debug.Log($"DEBUG vessel.up took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {(Vector3d)up}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { up = vessel.transform.up; } };
            Debug.Log($"DEBUG vessel.transform.up took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {(Vector3d)up}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { up = -FlightGlobals.getGeeForceAtPosition(pos).normalized; } };
            Debug.Log($"DEBUG -getGeeForceAtPosition.normalized took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {(Vector3d)up}");
        }

        public static void TestInOnUnitSphere()
        {
            var watch = new System.Diagnostics.Stopwatch();
            float μsResolution = 1e6f / System.Diagnostics.Stopwatch.Frequency;
            Debug.Log($"DEBUG Clock resolution: {μsResolution}μs, {PROF_N} outer loops, {PROF_n} inner loops");
            Vector3 point = default;
            var func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { point = UnityEngine.Random.onUnitSphere; } };
            Debug.Log($"DEBUG onUnitSphere took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {point}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { point = UnityEngine.Random.insideUnitSphere; } };
            Debug.Log($"DEBUG insideUnitSphere took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {point}");
        }

        public static void TestMaxRelSpeed()
        {
            var watch = new System.Diagnostics.Stopwatch();
            float μsResolution = 1e6f / System.Diagnostics.Stopwatch.Frequency;
            Debug.Log($"DEBUG Clock resolution: {μsResolution}μs, {PROF_N} outer loops, {PROF_n} inner loops");
            var currentPosition = FlightGlobals.ActiveVessel.transform.position;
            var currentVelocity = FlightGlobals.ActiveVessel.rb_velocity + BDKrakensbane.FrameVelocityV3f;
            float maxRelSpeed = 0;
            var func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { maxRelSpeed = BDAMath.Sqrt((float)FlightGlobals.Vessels.Where(v => v != null && v.loaded).Max(v => (v.rb_velocity + BDKrakensbane.FrameVelocityV3f - currentVelocity).sqrMagnitude)); } };
            Debug.Log($"DEBUG Without Dot took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {maxRelSpeed}");
            maxRelSpeed = 0;
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { maxRelSpeed = BDAMath.Sqrt((float)FlightGlobals.Vessels.Where(v => v != null && v.loaded && Vector3.Dot(v.rb_velocity + BDKrakensbane.FrameVelocityV3f - currentVelocity, v.transform.position - currentPosition) < 0).Select(v => (v.rb_velocity + BDKrakensbane.FrameVelocityV3f - currentVelocity).sqrMagnitude).DefaultIfEmpty(0).Max()); } };
            Debug.Log($"DEBUG With Dot took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {maxRelSpeed}");
            maxRelSpeed = 0;
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () =>
            {
                for (int i = 0; i < PROF_n; ++i)
                {
                    float maxRelSpeedSqr = 0, relVelSqr;
                    Vector3 relVel;
                    using (var v = FlightGlobals.Vessels.GetEnumerator())
                        while (v.MoveNext())
                        {
                            if (v.Current == null || !v.Current.loaded) continue;
                            relVel = v.Current.rb_velocity + BDKrakensbane.FrameVelocityV3f - currentVelocity;
                            // if (Vector3.Dot(relVel, v.Current.transform.position - currentPosition) >= 0) continue;
                            relVelSqr = relVel.sqrMagnitude;
                            if (relVelSqr > maxRelSpeedSqr) maxRelSpeedSqr = relVelSqr;
                        }
                    maxRelSpeed = BDAMath.Sqrt(maxRelSpeedSqr);
                }
            };
            Debug.Log($"DEBUG Explicit without Dot took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {maxRelSpeed}");
            maxRelSpeed = 0;
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () =>
            {
                for (int i = 0; i < PROF_n; ++i)
                {
                    float maxRelSpeedSqr = 0, relVelSqr;
                    Vector3 relVel;
                    using (var v = FlightGlobals.Vessels.GetEnumerator())
                        while (v.MoveNext())
                        {
                            if (v.Current == null || !v.Current.loaded) continue;
                            relVel = v.Current.rb_velocity + BDKrakensbane.FrameVelocityV3f - currentVelocity;
                            if (Vector3.Dot(relVel, v.Current.transform.position - currentPosition) >= 0) continue;
                            relVelSqr = relVel.sqrMagnitude;
                            if (relVelSqr > maxRelSpeedSqr) maxRelSpeedSqr = relVelSqr;
                        }
                    maxRelSpeed = BDAMath.Sqrt(maxRelSpeedSqr);
                }
            };
            Debug.Log($"DEBUG Explicit with Dot took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {maxRelSpeed}");
        }

        public static void TestSqrVsSqr()
        {
            var watch = new System.Diagnostics.Stopwatch();
            float μsResolution = 1e6f / System.Diagnostics.Stopwatch.Frequency;
            Debug.Log($"DEBUG Clock resolution: {μsResolution}μs, {PROF_N} outer loops, {PROF_n} inner loops");
            float sqr = 0, value = 3.14159f;
            var func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { sqr = value.Sqr(); } };
            Debug.Log($"DEBUG value.Sqr() took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {sqr}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { sqr = value * value; } };
            Debug.Log($"DEBUG value * value took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {sqr}");
            Vector3 a = UnityEngine.Random.insideUnitSphere, b = UnityEngine.Random.insideUnitSphere;
            float distance = 0;
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { distance = Vector3.Distance(a, b); } };
            Debug.Log($"DEBUG Vector3.Distance took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {distance}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { distance = (a - b).magnitude; } };
            Debug.Log($"DEBUG magnitude took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {distance}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { distance = (a - b).sqrMagnitude; } };
            Debug.Log($"DEBUG sqrMagnitude took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {distance}");
            bool lessThan = false;
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { lessThan = Vector3.Distance(a, b) < 0.5f; } };
            Debug.Log($"DEBUG Vector3.Distance < v took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {distance}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { lessThan = (a - b).sqrMagnitude < 0.5f * 0.5f; } };
            Debug.Log($"DEBUG sqrMagnitude < v*v took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {distance}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { lessThan = (a - b).sqrMagnitude < 0.5f.Sqr(); } };
            Debug.Log($"DEBUG sqrMagnitude < v.Sqr() took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {distance}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { lessThan = a.CloserToThan(b, 0.5f); } };
            Debug.Log($"DEBUG a.CloserToThan(b,f) took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {lessThan}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { lessThan = a.FurtherFromThan(b, 0.5f); } };
            Debug.Log($"DEBUG a.FurtherFromThan(b,f) took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {lessThan}");
        }

        public static void TestOrderOfOperations()
        {
            var watch = new System.Diagnostics.Stopwatch();
            float μsResolution = 1e6f / System.Diagnostics.Stopwatch.Frequency;
            Debug.Log($"DEBUG Clock resolution: {μsResolution}μs, {PROF_N} outer loops, {PROF_n} inner loops");
            float x = UnityEngine.Random.Range(-1f, 1f);
            Vector3 X = UnityEngine.Random.insideUnitSphere;
            Vector3 result = default;
            var func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = x * X; } };
            Debug.Log($"DEBUG x*X took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = X * x; } };
            Debug.Log($"DEBUG X*x took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = x * x * X; } };
            Debug.Log($"DEBUG x*x*X took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = X * x * x; } };
            Debug.Log($"DEBUG X*x*x took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = x * X * x; } };
            Debug.Log($"DEBUG x*X*x took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = x * x * x * X; } };
            Debug.Log($"DEBUG x*x*x*X took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = X * x * x * x; } };
            Debug.Log($"DEBUG X*x*x*x took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs");
        }

        public static void TestMassVsSizePerformance()
        {
            var watch = new System.Diagnostics.Stopwatch();
            float μsResolution = 1e6f / System.Diagnostics.Stopwatch.Frequency;
            Debug.Log($"DEBUG Clock resolution: {μsResolution}μs, {PROF_N} outer loops, {PROF_n} inner loops");
            var vessel = FlightGlobals.ActiveVessel;
            float mass = 0, size = 0;
            var func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { mass = vessel.GetTotalMass(); } };
            Debug.Log($"DEBUG GetTotalMass took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {mass}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { size = vessel.vesselSize.sqrMagnitude; } };
            Debug.Log($"DEBUG Size took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {size}");
        }

        public static void TestDotNormPerformance()
        {
            var watch = new System.Diagnostics.Stopwatch();
            float μsResolution = 1e6f / System.Diagnostics.Stopwatch.Frequency;
            Debug.Log($"DEBUG Clock resolution: {μsResolution}μs, {PROF_N} outer loops, {PROF_n} inner loops");
            var v1 = UnityEngine.Random.insideUnitSphere;
            var v2 = UnityEngine.Random.insideUnitSphere;
            float dot = 0;
            var func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { dot = Vector3.Dot(v1.normalized, v2.normalized); } };
            Debug.Log($"DEBUG Dot(v1.norm, v2.norm) took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {dot}");
            dot = 0;
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { dot = v1.DotNormalized(v2); } };
            Debug.Log($"DEBUG v1.DotNorm(v2) took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {dot}");
        }

        public static void TestNamePerformance()
        {
            var watch = new System.Diagnostics.Stopwatch();
            float μsResolution = 1e6f / System.Diagnostics.Stopwatch.Frequency;
            Debug.Log($"DEBUG Clock resolution: {μsResolution}μs, {PROF_N} outer loops, {PROF_n} inner loops");
            var v = FlightGlobals.ActiveVessel;
            string vesselName, getName, getDisplayName;
            var func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { vesselName = v.vesselName; } };
            Debug.Log($"DEBUG vesselName took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {v.vesselName}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { getName = v.GetName(); } };
            Debug.Log($"DEBUG GetName took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {v.GetName()}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { getDisplayName = v.GetDisplayName(); } };
            Debug.Log($"DEBUG GetDisplayName took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {v.GetDisplayName()}");
        }

        public static void TestRandPerformance()
        {
            var watch = new System.Diagnostics.Stopwatch();
            float μsResolution = 1e6f / System.Diagnostics.Stopwatch.Frequency;
            Debug.Log($"DEBUG Clock resolution: {μsResolution}μs, {PROF_N} outer loops, {PROF_n} inner loops");
            Vector3 result = default;
            var func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = VectorUtils.GaussianVector3(); } };
            Debug.Log($"DEBUG VectorUtils.GaussianVector3() took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {(Vector3d)result}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = UnityEngine.Random.insideUnitSphere; } };
            Debug.Log($"DEBUG UnityEngine.Random.insideUnitSphere took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {(Vector3d)result}");
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = new Vector3(UnityEngine.Random.value * 2f - 1f, UnityEngine.Random.value * 2f - 1f, UnityEngine.Random.value * 2f - 1f); } };
            Debug.Log($"DEBUG new Vector3(UnityEngine.Random.value * 2f - 1f, UnityEngine.Random.value * 2f - 1f, UnityEngine.Random.value * 2f - 1f) took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {(Vector3d)result}");
            float value = 0;
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { value = UnityEngine.Random.value; } };
            Debug.Log($"DEBUG UnityEngine.Random.value took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {value}");
            value = 0;
            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { value += UnityEngine.Random.insideUnitSphere.magnitude; } };
            Debug.Log($"DEBUG UnityEngine.Random.insideUnitSphere.magnitude took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {value / PROF_N / PROF_n}");
        }

        public static void TestProjectOnPlaneAndPredictPosition()
        {
            var watch = new System.Diagnostics.Stopwatch();
            float μsResolution = 1e6f / System.Diagnostics.Stopwatch.Frequency;
            Debug.Log($"DEBUG Clock resolution: {μsResolution}μs, {PROF_N} outer loops, {PROF_n} inner loops");
            Vessel vessel = FlightGlobals.ActiveVessel;
            Vector3 p = vessel.CoM, v = vessel.srf_velocity, a = vessel.acceleration;
            Vector3 result = default;
            float time = 5f;
            var upNormal = VectorUtils.GetUpDirection(p);

            var func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = AIUtils.PredictPosition(p, v, a, time); } };
            Debug.Log($"DEBUG AIUtils.PredictPosition(p, v, a, time) took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {(Vector3d)result}");

            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = PredictPositionNoInline(p, v, a, time); } };
            Debug.Log($"DEBUG PredictPositionNoInline(p, v, a, time) took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {(Vector3d)result}");

            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = p + time * v + 0.5f * time * time * a; } };
            Debug.Log($"DEBUG p + time * v + 0.5f * time * time * a took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {(Vector3d)result}");

            func = [MethodImpl(MethodImplOptions.NoInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = p + time * v + 0.5f * time * time * a; } };
            Debug.Log($"DEBUG p + time * v + 0.5f * time * time * a no-inlining took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {(Vector3d)result}");

            watch.Reset(); watch.Start();
            for (int i = 0; i < PROF_N * PROF_n; ++i) { result = p + time * v + 0.5f * time * time * a; }
            watch.Stop();
            Debug.Log($"DEBUG fully inlined took {watch.ElapsedTicks * μsResolution / PROF_N / PROF_n:G3}μs to give {(Vector3d)result}");

            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = Vector3.ProjectOnPlane(v, upNormal); } };
            Debug.Log($"DEBUG Vector3.ProjectOnPlane(v, upNormal) took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {(Vector3d)result}");

            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = v.ProjectOnPlane(upNormal); } };
            Debug.Log($"DEBUG v.ProjectOnPlane(upNormal) took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {(Vector3d)result}");

            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = ProjectOnPlaneOpt(v, upNormal); } };
            Debug.Log($"DEBUG ProjectOnPlaneOpt(v, upNormal) took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {(Vector3d)result}");

            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = v.ProjectOnPlanePreNormalized(upNormal); } };
            Debug.Log($"DEBUG v.ProjectOnPlanePreNormalized(upNormal) took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {(Vector3d)result}");

            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = ProjectOnPlanePreNorm(v, upNormal); } };
            Debug.Log($"DEBUG ProjectOnPlanePreNorm(v, upNormal) took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {(Vector3d)result}");

            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = ProjectOnPlanePreNormNoInline(v, upNormal); } };
            Debug.Log($"DEBUG ProjectOnPlanePreNormNoInline(v, upNormal) took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {(Vector3d)result}");

            func = [MethodImpl(MethodImplOptions.AggressiveInlining)] () => { for (int i = 0; i < PROF_n; ++i) { result = v - upNormal * Vector3.Dot(v, upNormal); } };
            Debug.Log($"DEBUG v - upNormal * Vector3.Dot(v, upNormal) took {ProfileFunc(func, PROF_N) / PROF_n:G3}μs to give {(Vector3d)result}");

            watch.Reset(); watch.Start();
            for (int i = 0; i < PROF_N * PROF_n; ++i) { result = v - upNormal * Vector3.Dot(v, upNormal); }
            watch.Stop();
            Debug.Log($"DEBUG fully inlined took {watch.ElapsedTicks * μsResolution / PROF_N / PROF_n:G3}μs to give {(Vector3d)result}");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static float ProfileFunc(Action func, int N)
        {
            var watch = new System.Diagnostics.Stopwatch();
            float μsResolution = 1e6f / System.Diagnostics.Stopwatch.Frequency;
            func(); // Warm-up
            watch.Start();
            for (int i = 0; i < N; ++i) func();
            watch.Stop();
            return watch.ElapsedTicks * μsResolution / N;
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

        /// <summary>
        /// On leaving flight mode. Perform some clean-up of things that might still be active.
        /// Not everything gets cleaned up by default for some reason.
        /// </summary>
        void OnGameSceneSwitchRequested(GameEvents.FromToAction<GameScenes, GameScenes> fromTo)
        {
            if (windowSettingsEnabled) ToggleWindowSettings(); // Close the settings window so that the following settings changes don't propagate back into the settings window.
            if (BDArmorySettings.G_LIMITS && (fromTo.from == GameScenes.EDITOR || fromTo.from == GameScenes.FLIGHT))
            {
                RWPSettings.SyncWithGameSettings(restoreOverrides: true);
            }
            if (fromTo.from == GameScenes.FLIGHT && fromTo.to != GameScenes.FLIGHT)
            {
                DisableAllFXAndProjectiles();
            }
        }
        void OnGameStateSave(ConfigNode node)
        {
            if (BDArmorySettings.G_LIMITS)
            {
                RWPSettings.SyncWithGameSettings(restoreOverrides: true);
            }
        }
        void OnGameStateSaved(Game game)
        {
            if (BDArmorySettings.G_LIMITS)
            {
                RWPSettings.SyncWithGameSettings();
            }
        }
        public static void DisableAllFXAndProjectiles()
        {
            SpawnUtils.DisableAllBulletsAndRockets();
            ExplosionFx.DisableAllExplosionFX();
            NukeFX.DisableAllExplosionFX();
            FXEmitter.DisableAllFX();
            CMDropper.DisableAllCMs();
            BulletHitFX.DisableAllFX();
            // FIXME Add in any other things that may otherwise continue doing stuff on scene changes, e.g., various FX.
        }
    }
}
