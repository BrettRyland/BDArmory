using UnityEngine;

using System.IO;
using System.Collections.Generic;

namespace BDArmory.Settings
{
    public class BDArmorySettings
    {
        public static string oldSettingsConfigURL = Path.Combine(KSPUtil.ApplicationRootPath, "GameData/BDArmory/settings.cfg"); // Migrate from the old settings file to the new one in PluginData so that we don't invalidate the ModuleManager cache.
        public static string settingsConfigURL = Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath, "GameData/BDArmory/PluginData/settings.cfg"));
        public static bool ready = false;

        #region Settings section toggles
        [BDAPersistentSettingsField] public static bool GAMEPLAY_SETTINGS_TOGGLE = false;
        [BDAPersistentSettingsField] public static bool GRAPHICS_UI_SETTINGS_TOGGLE = false;
        [BDAPersistentSettingsField] public static bool GAME_MODES_SETTINGS_TOGGLE = false;
        [BDAPersistentSettingsField] public static bool SLIDER_SETTINGS_TOGGLE = false;
        [BDAPersistentSettingsField] public static bool RADAR_SETTINGS_TOGGLE = false;
        [BDAPersistentSettingsField] public static bool OTHER_SETTINGS_TOGGLE = false;
        [BDAPersistentSettingsField] public static bool DEBUG_SETTINGS_TOGGLE = false;
        [BDAPersistentSettingsField] public static bool COMPETITION_SETTINGS_TOGGLE = false;
        [BDAPersistentSettingsField] public static bool GM_SETTINGS_TOGGLE = false;
        [BDAPersistentSettingsField] public static bool ADVANCED_USER_SETTINGS = true;
        #endregion

        #region Window settings
        [BDAPersistentSettingsField] public static bool STRICT_WINDOW_BOUNDARIES = true;
        [BDAPersistentSettingsField] public static float REMOTE_ORCHESTRATION_WINDOW_WIDTH = 225f;
        [BDAPersistentSettingsField] public static bool VESSEL_SWITCHER_WINDOW_SORTING = false;
        [BDAPersistentSettingsField] public static bool VESSEL_SWITCHER_WINDOW_OLD_DISPLAY_STYLE = false;
        [BDAPersistentSettingsField] public static bool VESSEL_SWITCHER_PERSIST_UI = false;
        [BDAPersistentSettingsField] public static float VESSEL_SPAWNER_WINDOW_WIDTH = 480f;
        [BDAPersistentSettingsField] public static float VESSEL_WAYPOINT_WINDOW_WIDTH = 480f;
        [BDAPersistentSettingsField] public static float EVOLUTION_WINDOW_WIDTH = 350f;
        [BDAPersistentSettingsField] public static float GUI_OPACITY = 1f;                   // Modify the GUI opacity.
        [BDAPersistentSettingsField] public static float UI_SCALE = 1f; // Global UI scaling
        [BDAPersistentSettingsField] public static bool UI_SCALE_FOLLOWS_STOCK = true; // Global UI scaling follows stock
        public static float _UI_SCALE => UI_SCALE_FOLLOWS_STOCK ? GameSettings.UI_SCALE : UI_SCALE;
        public static float PREVIOUS_UI_SCALE = 1f; // For tracking changes
        #endregion

        #region General toggle settings
        //[BDAPersistentSettingsField] public static bool INSTAKILL = true; //Deprecated, only affects lasers; use an Instagib mutator instead
        [BDAPersistentSettingsField] public static bool AI_TOOLBAR_BUTTON = true;                 // Show or hide the BDA AI toolbar button.
        [BDAPersistentSettingsField] public static bool VM_TOOLBAR_BUTTON = true;                 // Show or hide the BDA VM toolbar button.
        [BDAPersistentSettingsField] public static bool INFINITE_AMMO = false;              //infinite Bullets/rockets/laserpower
        [BDAPersistentSettingsField] public static bool INFINITE_ORDINANCE = false;         //infinite missiles/bombs (on ordinance w/ Reload Module)
        [BDAPersistentSettingsField] public static bool LIMITED_ORDINANCE = false;         //MML ammo clamped to salvo size, no relaods
        [BDAPersistentSettingsField] public static bool INFINITE_FUEL = false;              //Infinite propellant
        [BDAPersistentSettingsField] public static bool INFINITE_EC = false;                          //Infinite electric charge
        [BDAPersistentSettingsField] public static bool PERFORMANCE_OPTIONS = true;
        [BDAPersistentSettingsField] public static bool BULLET_HITS = true;
        [BDAPersistentSettingsField] public static bool WATER_HIT_FX = true;
        [BDAPersistentSettingsField] public static bool WEAPONS_RESPECT_CROSSFEED = true;
        public static bool waterHitEffect => PERFORMANCE_OPTIONS && WATER_HIT_FX;
        [BDAPersistentSettingsField] public static bool EJECT_SHELLS = true;
        [BDAPersistentSettingsField] public static bool LIGHTFX = true;                       // explosions spawn a LightFX
        public static bool LightFX => PERFORMANCE_OPTIONS && LIGHTFX;
        [BDAPersistentSettingsField] public static bool VESSEL_RELATIVE_BULLET_CHECKS = false;
        [BDAPersistentSettingsField] public static bool AIM_ASSIST = true;
        [BDAPersistentSettingsField] public static bool AIM_ASSIST_MODE = true;              // true = reticle follows bullet CPA position, false = reticle follows aiming position.
        [BDAPersistentSettingsField] public static bool DRAW_AIMERS = true;
        [BDAPersistentSettingsField] public static bool RESTORE_KAL = true;                  // Restore the Part, Module and AxisField references on the KAL to make it work.

        [BDAPersistentSettingsField] public static bool REMOTE_SHOOTING = false;
        [BDAPersistentSettingsField] public static bool BOMB_CLEARANCE_CHECK = false;
        [BDAPersistentSettingsField] public static bool SHOW_AMMO_GAUGES = true;
        [BDAPersistentSettingsField] public static bool SHELL_COLLISIONS = true;
        [BDAPersistentSettingsField] public static bool BULLET_DECALS = true;
        [BDAPersistentSettingsField] public static bool GAPLESS_PARTICLE_EMITTERS = true;         // Use gapless particle emitters.
        public static bool GaplessParticleEmitters => PERFORMANCE_OPTIONS && GAPLESS_PARTICLE_EMITTERS;
        [BDAPersistentSettingsField] public static bool FLARE_SMOKE = true;                       // Flares leave a trail of smoke.
        public static bool FlareSmoke => PERFORMANCE_OPTIONS && FLARE_SMOKE;
        [BDAPersistentSettingsField] public static bool DISABLE_RAMMING = false;                  // Prevent craft from going into ramming mode when out of ammo.
        [BDAPersistentSettingsField] public static bool DEFAULT_FFA_TARGETING = false;            // Free-for-all combat style instead of teams (changes target selection behaviour). This could be removed now.
        [BDAPersistentSettingsField] public static bool RUNWAY_PROJECT = false;                    // Enable/disable Runway Project specific enhancements.
        //[BDAPersistentSettingsField] public static bool DISABLE_KILL_TIMER = true;                //disables the kill timers.
        [BDAPersistentSettingsField] public static bool AUTO_ENABLE_VESSEL_SWITCHING = false;     // Automatically enables vessel switching on competition start.
        [BDAPersistentSettingsField] public static bool AUTONOMOUS_COMBAT_SEATS = false;          // Enable/disable seats without kerbals.
        [BDAPersistentSettingsField] public static bool DESTROY_UNCONTROLLED_WMS = true;         // Automatically destroy the WM if there's no kerbal or drone core controlling it.
        [BDAPersistentSettingsField] public static bool RESET_HP = false;                         // Automatically reset HP of parts of vessels when they're spawned in flight mode.
        [BDAPersistentSettingsField] public static bool RESET_ARMOUR = false;                     // Automatically reset Armor material of parts of vessels when they're spawned in flight mode.
        [BDAPersistentSettingsField] public static bool RESET_HULL = false;                     // Automatically reset hull material of parts of vessels when they're spawned in flight mode.
        [BDAPersistentSettingsField] public static int KERBAL_SAFETY = 1;                         // Try to save kerbals by ejecting/leaving seats and deploying parachutes.
        [BDAPersistentSettingsField] public static bool TRACE_VESSELS_DURING_COMPETITIONS = false; // Trace vessel positions and rotations during competitions.
        [BDAPersistentSettingsField] public static bool AUTO_LOG_TIME_SYNC = false;               // Log time synchronisation info automatically during competitions.
        [BDAPersistentSettingsField] public static float LOG_TIME_SYNC_INTERVAL = 0.2f;           // Interval for logging time synchronisation information (approx).
        [BDAPersistentSettingsField] public static bool DRAW_VESSEL_TRAILS = true;                // Draw a trail to visualize vessel path during the heat
        [BDAPersistentSettingsField] public static int VESSEL_TRAIL_LENGTH = 300;                   //Max length of trails, in seconds. Defaults to competition length
        [BDAPersistentSettingsField] public static bool AUTOCATEGORIZE_PARTS = true;
        [BDAPersistentSettingsField] public static bool SHOW_CATEGORIES = true;
        [BDAPersistentSettingsField] public static bool IGNORE_TERRAIN_CHECK = false;
        [BDAPersistentSettingsField] public static bool CHECK_WATER_TERRAIN = false;
        [BDAPersistentSettingsField] public static bool RADAR_NOTCHING = false;
        [BDAPersistentSettingsField] public static bool RADAR_ALLOW_SURFACE_WARFARE = true;
        [BDAPersistentSettingsField] public static float RADAR_NOTCHING_FACTOR = 1f;
        [BDAPersistentSettingsField] public static float RADAR_NOTCHING_SCR_FACTOR = 0.01f;
        [BDAPersistentSettingsField] public static bool DISPLAY_PATHING_GRID = false;             //laggy when the grid gets large
        //[BDAPersistentSettingsField] public static bool ADVANCED_EDIT = true;                     //Used for debug fields not nomrally shown to regular users //SI - Only usage is a commented out function in BDExplosivePart
        [BDAPersistentSettingsField] public static bool DISPLAY_COMPETITION_STATUS = true;             //Display competition status
        [BDAPersistentSettingsField] public static bool DISPLAY_COMPETITION_STATUS_WITH_HIDDEN_UI = false; // Display the competition status when using the "hidden UI"
        [BDAPersistentSettingsField] public static bool SCROLL_ZOOM_PREVENTION = true;                 // Prevent scroll-zoom when over most BDA windows.
        [BDAPersistentSettingsField] public static bool BULLET_WATER_DRAG = true;                       // do bullets/rockets get slowed down if fired into/under water
        [BDAPersistentSettingsField] public static bool UNDERWATER_VISION = false;                       //If false, Subs and other submerged vessels fully visible to surface/air craft and vice versa without detectors?
        [BDAPersistentSettingsField] public static bool PERSISTENT_FX = false;
        [BDAPersistentSettingsField] public static bool LEGACY_ARMOR = false;
        [BDAPersistentSettingsField] public static bool HACK_INTAKES = false;
        [BDAPersistentSettingsField] public static bool COMPETITION_CLOSE_SETTINGS_ON_COMPETITION_START = false; // Close the settings window when clicking the start competition button.
        [BDAPersistentSettingsField] public static bool AUTO_LOAD_TO_KSC = false;                      // Automatically load the last used save and go to the KSC.
        [BDAPersistentSettingsField] public static bool GENERATE_CLEAN_SAVE = false;                   // Use a clean save instead of the persistent one when loading to the KSC.
        #endregion

        #region Debug Labels
        [BDAPersistentSettingsField] public static bool DEBUG_LINES = false;                 //AI/Weapon aim visualizers
        [BDAPersistentSettingsField] public static bool DEBUG_OTHER = false;                 //internal debugging
        [BDAPersistentSettingsField] public static bool DEBUG_ARMOR = false;                 //armor and HP
        [BDAPersistentSettingsField] public static bool DEBUG_WEAPONS = false;               //Debug messages for guns/rockets/lasers and their projectiles
        [BDAPersistentSettingsField] public static bool DEBUG_MISSILES = false;              //Missile launch, tracking and targeting debug labels
        [BDAPersistentSettingsField] public static bool DEBUG_DAMAGE = false;                //Explosions and battle damage logging
        [BDAPersistentSettingsField] public static bool DEBUG_AI = false;                    //AI debugging
        [BDAPersistentSettingsField] public static bool DEBUG_RADAR = false;                 //FLIR/Radar and RCS debugging
        [BDAPersistentSettingsField] public static bool DEBUG_TELEMETRY = false;             //AI/WM UI debug telemetry display
        [BDAPersistentSettingsField] public static bool DEBUG_SPAWNING = false;              //Spawning debugging
        [BDAPersistentSettingsField] public static bool DEBUG_COMPETITION = false;           //Competition debugging
        #endregion

        #region General slider settings
        [BDAPersistentSettingsField] public static int COMPETITION_DURATION = 0;                       // Competition duration in minutes (0=unlimited)
        [BDAPersistentSettingsField] public static float COMPETITION_INITIAL_GRACE_PERIOD = 10;        // Competition initial grace period in seconds.
        [BDAPersistentSettingsField] public static float COMPETITION_FINAL_GRACE_PERIOD = 10;          // Competition final grace period in seconds.
        [BDAPersistentSettingsField] public static float COMPETITION_KILL_TIMER = 15;                  // Competition kill timer in seconds.
        [BDAPersistentSettingsField] public static float COMPETITION_KILLER_GM_FREQUENCY = 60;         // Competition killer GM timer in seconds.
        [BDAPersistentSettingsField] public static float COMPETITION_KILLER_GM_GRACE_PERIOD = 150;     // Competition killer GM grace period in seconds.
        [BDAPersistentSettingsField] public static float COMPETITION_ALTITUDE_LIMIT_HIGH = 55;         // Altitude (high) in km at which to kill off craft.
        [BDAPersistentSettingsField] public static float COMPETITION_ALTITUDE_LIMIT_LOW = -39;          // Altitude (low) in km at which to kill off craft.
        [BDAPersistentSettingsField] public static bool COMPETITION_ALTITUDE__LIMIT_ASL = false;       // Does Killer GM use ASL or AGL for latitide ceiling/floor?
        [BDAPersistentSettingsField] public static bool COMPETITION_GM_KILL_WEAPON = false;             // Competition GM will kill weaponless craft?
        [BDAPersistentSettingsField] public static bool COMPETITION_GM_KILL_ENGINE = false;             // Competition GM will kill engineless craft?
        [BDAPersistentSettingsField] public static bool COMPETITION_GM_KILL_DISABLED = false;           // Competition GM will kill craft that are disabled (no weapons or ammo, no engine [Pilot/VTOL/Ship/Sub] or no wheels [Surface])
        [BDAPersistentSettingsField] public static float COMPETITION_GM_KILL_HP = 0;                    // Competition GM will kill craft with low HP craft?
        [BDAPersistentSettingsField] public static float COMPETITION_GM_KILL_TIME = 0;                  // CompetitionGM Kill time
        [BDAPersistentSettingsField] public static float COMPETITION_NONCOMPETITOR_REMOVAL_DELAY = 30; // Competition non-competitor removal delay in seconds.
        [BDAPersistentSettingsField] public static float COMPETITION_WAYPOINTS_GM_KILL_PERIOD = 60;    // Waypoint Competition GM kill period in seconds. Craft that don't pass a waypoint within this time are killed off.
        [BDAPersistentSettingsField] public static float COMPETITION_DISTANCE = 1000;                  // Competition distance.
        [BDAPersistentSettingsField] public static float COMPETITION_INTRA_TEAM_SEPARATION_BASE = 800; // Intra-team separation (base value).
        [BDAPersistentSettingsField] public static float COMPETITION_INTRA_TEAM_SEPARATION_PER_MEMBER = 100; // Intra-team separation (per member value).
        [BDAPersistentSettingsField] public static int COMPETITION_START_NOW_AFTER = 11;               // Competition auto-start now.
        [BDAPersistentSettingsField] public static bool COMPETITION_START_DESPITE_FAILURES = false;    // Start competition despite failures.
        [BDAPersistentSettingsField] public static float DEBRIS_CLEANUP_DELAY = 15f;                   // Clean up debris after 30s.
        [BDAPersistentSettingsField] public static int MAX_NUM_BULLET_DECALS = 200;
        [BDAPersistentSettingsField] public static int TERRAIN_ALERT_FREQUENCY = 1;                    // Controls how often terrain avoidance checks are made (gets scaled by 1+(radarAltitude/500)^2)
        [BDAPersistentSettingsField] public static int CAMERA_SWITCH_FREQUENCY = 10;                    // Controls the minimum time between automated camera switches
        [BDAPersistentSettingsField] public static int DEATH_CAMERA_SWITCH_INHIBIT_PERIOD = 2;         // Controls the delay before the next switch after the currently active vessel dies
        [BDAPersistentSettingsField] public static bool CAMERA_SWITCH_INCLUDE_MISSILES = false;        // Include missiles in the camera switching logic.
        [BDAPersistentSettingsField] public static int KERBAL_SAFETY_INVENTORY = 2;                    // Controls how Kerbal Safety adjusts the inventory of kerbals.
        [BDAPersistentSettingsField] public static float TRIGGER_HOLD_TIME = 0.2f;
        [BDAPersistentSettingsField] public static float BDARMORY_UI_VOLUME = 0.35f;
        [BDAPersistentSettingsField] public static float BDARMORY_WEAPONS_VOLUME = 0.45f;
        [BDAPersistentSettingsField] public static float MAX_GUARD_VISUAL_RANGE = 200000f;
        [BDAPersistentSettingsField] public static float MAX_ACTIVE_RADAR_RANGE = 200000f;        //NOTE: used ONLY for display range of radar windows! Actual radar range provided by part configs!
        [BDAPersistentSettingsField] public static bool LOGARITHMIC_RADAR_DISPLAY = true;                //NOTE: used ONLY for display range of radar windows! Actual radar range provided by part configs!
        [BDAPersistentSettingsField] public static float MAX_ENGAGEMENT_RANGE = 200000f;          //NOTE: used ONLY for missile dlz parameters!
        [BDAPersistentSettingsField] public static float IVA_LOWPASS_FREQ = 2500f;
        [BDAPersistentSettingsField] public static float BALLISTIC_TRAJECTORY_SIMULATION_MULTIPLIER = 128f;      // Multiplier of fixedDeltaTime for the large scale steps of ballistic trajectory simulations. Large values at extreme ranges may cause small inaccuracies. 128 with 1km/s bullets at their max range seems reasonable in most cases.
        [BDAPersistentSettingsField] public static float FIRE_RATE_OVERRIDE = 10f;
        [BDAPersistentSettingsField] public static float FIRE_RATE_OVERRIDE_CENTER = 20f;
        [BDAPersistentSettingsField] public static float FIRE_RATE_OVERRIDE_SPREAD = 5f;
        [BDAPersistentSettingsField] public static float FIRE_RATE_OVERRIDE_BIAS = 0.4f;
        [BDAPersistentSettingsField] public static float FIRE_RATE_OVERRIDE_HIT_MULTIPLIER = 2f;
        [BDAPersistentSettingsField] public static float HP_THRESHOLD = 2000;                    //HP above this value will be scaled to a logarithmic value
        [BDAPersistentSettingsField] public static float HP_CLAMP = 0;                           //HP will be clamped to this value
        [BDAPersistentSettingsField] public static float MAX_ARMOR_LIMIT = -1;                   //Armor will be clamped to this limit if non-negative.
        [BDAPersistentSettingsField] public static bool PWING_EDGE_LIFT = true;                  //Toggle lift on PWing edges for balance with stock wings/remove edge abuse
        [BDAPersistentSettingsField] public static bool PWING_THICKNESS_AFFECT_MASS_HP = false;  //pWing thickness contributes to its mass calc instead of a static LiftArea derived value
        [BDAPersistentSettingsField] public static float MAX_PWING_LIFT = 4.54f;                //Clamp pWing lift to this amount
        [BDAPersistentSettingsField] public static float MAX_SAS_TORQUE = 30;                   //Clamp vessel total non-cockpit torque to this
        [BDAPersistentSettingsField] public static bool NUMERIC_INPUT_SELF_UPDATE = true;             // Automatically update the display string in NumericInputField after attempting to parse the value.
        [BDAPersistentSettingsField] public static float NUMERIC_INPUT_DELAY = 0.5f;                  // Time before last input for "read and interpret" logic of NumericInputField.
        [BDAPersistentSettingsField] public static Vector2 PROC_ARMOR_ALT_LIMITS = new Vector2(0.01f, 100f); // Unclamped limits of proc armour panels.
        #endregion

        #region Physics constants
        [BDAPersistentSettingsField] public static float GLOBAL_LIFT_MULTIPLIER = 0.25f;
        [BDAPersistentSettingsField] public static float GLOBAL_DRAG_MULTIPLIER = 6f;
        [BDAPersistentSettingsField] public static float RECOIL_FACTOR = 0.75f;
        [BDAPersistentSettingsField] public static float DMG_MULTIPLIER = 100f;
        [BDAPersistentSettingsField] public static float BALLISTIC_DMG_FACTOR = 1.55f;
        [BDAPersistentSettingsField] public static float HITPOINT_MULTIPLIER = 3.0f;
        [BDAPersistentSettingsField] public static float EXP_DMG_MOD_BALLISTIC_NEW = 0.55f;     // HE bullet explosion damage multiplier
        [BDAPersistentSettingsField] public static float EXP_PEN_RESIST_MULT = 2.50f;           // Armor HE penetration resistance multiplier
        [BDAPersistentSettingsField] public static float EXP_DMG_MOD_MISSILE = 6.75f;           // Missile explosion damage multiplier
        [BDAPersistentSettingsField] public static float EXP_DMG_MOD_ROCKET = 1f;               // Rocket explosion damage multiplier (FIXME needs tuning; Note: rockets used Ballistic mod before, but probably ought to be more like missiles)
        [BDAPersistentSettingsField] public static float EXP_DMG_MOD_BATTLE_DAMAGE = 1f;        // Battle damage explosion damage multiplier (FIXME needs tuning; Note: CASE-0 explosions used Missile mod, while CASE-1, CASE-2 and fuel explosions used Ballistic mod)
        [BDAPersistentSettingsField] public static float EXP_IMP_MOD = 0.25f;
        [BDAPersistentSettingsField] public static float BUILDING_DMG_MULTIPLIER = 1f;
        [BDAPersistentSettingsField] public static bool EXTRA_DAMAGE_SLIDERS = false;
        [BDAPersistentSettingsField] public static float WEAPON_FX_DURATION = 15;               //how long do weapon secondary effects(EMP/choker/gravitic/etc) last
        [BDAPersistentSettingsField] public static float ZOMBIE_DMG_MULT = 0.1f;
        [BDAPersistentSettingsField] public static float ARMOR_MASS_MOD = 1f;                   //Armor mass multiplier
        #endregion

        #region FX
        [BDAPersistentSettingsField] public static bool FIRE_FX_IN_FLIGHT = false;
        [BDAPersistentSettingsField] public static int MAX_FIRES_PER_VESSEL = 10;                 //controls fx for penetration only for landed or splashed //this is only for physical missile collisons into fueltanks - SI
        [BDAPersistentSettingsField] public static float FIRELIFETIME_IN_SECONDS = 90f;           //controls fx for penetration only for landed or splashed 
        #endregion

        #region Radar settings
        [BDAPersistentSettingsField] public static float RWR_WINDOW_SCALE_MIN = 0.50f;
        [BDAPersistentSettingsField] public static float RWR_WINDOW_SCALE = 1f;
        [BDAPersistentSettingsField] public static float RWR_WINDOW_SCALE_MAX = 1.50f;
        [BDAPersistentSettingsField] public static float RADAR_WINDOW_SCALE_MIN = 0.50f;
        [BDAPersistentSettingsField] public static float RADAR_WINDOW_SCALE = 1f;
        [BDAPersistentSettingsField] public static float RADAR_WINDOW_SCALE_MAX = 1.50f;
        [BDAPersistentSettingsField] public static float TARGET_WINDOW_SCALE_MIN = 0.50f;
        [BDAPersistentSettingsField] public static float TARGET_WINDOW_SCALE = 1f;
        [BDAPersistentSettingsField] public static float TARGET_WINDOW_SCALE_MAX = 2f;
        [BDAPersistentSettingsField] public static float TARGET_CAM_RESOLUTION = 1024f;
        [BDAPersistentSettingsField] public static bool BW_TARGET_CAM = true;
        [BDAPersistentSettingsField] public static bool TARGET_WINDOW_INVERT_MOUSE_X = false;
        [BDAPersistentSettingsField] public static bool TARGET_WINDOW_INVERT_MOUSE_Y = false;
        #endregion

        #region Game modes
        [BDAPersistentSettingsField] public static bool PEACE_MODE = false;
        [BDAPersistentSettingsField] public static bool TAG_MODE = false;
        [BDAPersistentSettingsField] public static bool PAINTBALL_MODE = false;
        [BDAPersistentSettingsField] public static bool GRAVITY_HACKS = false;
        [BDAPersistentSettingsField] public static bool ALTITUDE_HACKS = false; //transfer to a RunWayRound number?
        [BDAPersistentSettingsField] public static bool BATTLEDAMAGE = true;
        [BDAPersistentSettingsField] public static bool HEART_BLEED_ENABLED = false;
        [BDAPersistentSettingsField] public static bool RESOURCE_STEAL_ENABLED = false;
        [BDAPersistentSettingsField] public static bool ASTEROID_FIELD = false;
        [BDAPersistentSettingsField] public static int ASTEROID_FIELD_NUMBER = 100; // Number of asteroids
        [BDAPersistentSettingsField] public static float ASTEROID_FIELD_ALTITUDE = 2f; // Km.
        [BDAPersistentSettingsField] public static float ASTEROID_FIELD_RADIUS = 5f; // Km.
        [BDAPersistentSettingsField] public static bool ASTEROID_FIELD_ANOMALOUS_ATTRACTION = false; // Asteroids are attracted to vessels.
        [BDAPersistentSettingsField] public static float ASTEROID_FIELD_ANOMALOUS_ATTRACTION_STRENGTH = 0.2f; // Strength of the effect.
        [BDAPersistentSettingsField] public static bool ASTEROID_RAIN = false;
        [BDAPersistentSettingsField] public static int ASTEROID_RAIN_NUMBER = 100; // Number of asteroids
        [BDAPersistentSettingsField] public static float ASTEROID_RAIN_DENSITY = 0.5f; // Arbitrary density scale.
        [BDAPersistentSettingsField] public static float ASTEROID_RAIN_ALTITUDE = 2f; // Km.k
        [BDAPersistentSettingsField] public static float ASTEROID_RAIN_RADIUS = 3f; // Km.
        [BDAPersistentSettingsField] public static bool ASTEROID_RAIN_FOLLOWS_CENTROID = true;
        [BDAPersistentSettingsField] public static bool ASTEROID_RAIN_FOLLOWS_SPREAD = true;
        [BDAPersistentSettingsField] public static bool MUTATOR_MODE = false;
        [BDAPersistentSettingsField] public static bool ZOMBIE_MODE = false;
        [BDAPersistentSettingsField] public static bool DISCO_MODE = false;
        [BDAPersistentSettingsField] public static bool NO_ENGINES = false;
        [BDAPersistentSettingsField] public static bool WAYPOINTS_MODE = false;         // Waypoint section of Vessel Spawner Window.
        [BDAPersistentSettingsField] public static string PINATA_NAME = "Pinata";
        [BDAPersistentSettingsField] public static bool G_LIMITS = false;
        [BDAPersistentSettingsField] public static bool PART_GLIMIT = false;
        [BDAPersistentSettingsField] public static bool KERB_GLIMIT = false;
        [BDAPersistentSettingsField] public static float G_TOLERANCE = 0.4f;                       // Adjust the GToleranceMult to set Max G endurance of all kerbs to a desired amount
        #endregion

        #region Battle Damage settings
        [BDAPersistentSettingsField] public static bool BATTLEDAMAGE_TOGGLE = false;    // Main battle damage toggle.
        [BDAPersistentSettingsField] public static float BD_DAMAGE_CHANCE = 5;          // Base chance per-hit to proc damage
        [BDAPersistentSettingsField] public static bool BD_SUBSYSTEMS = true;           // Non-critical module damage?
        [BDAPersistentSettingsField] public static bool BD_TANKS = true;                // Fuel tanks, batteries can leak/burn
        [BDAPersistentSettingsField] public static float BD_TANK_LEAK_TIME = 20;        // Leak duration
        [BDAPersistentSettingsField] public static float BD_TANK_LEAK_RATE = 1;         // Leak rate modifier
        [BDAPersistentSettingsField] public static bool BD_AMMOBINS = true;             // Can ammo bins explode?
        [BDAPersistentSettingsField] public static bool BD_VOLATILE_AMMO = false;       // Ammo bins guaranteed to explode when destroyed
        [BDAPersistentSettingsField] public static bool BD_PROPULSION = true;           // Engine thrust reduction, fires
        [BDAPersistentSettingsField] public static float BD_PROP_FLOOR = 20;            // Minimum thrust% damaged engines produce
        [BDAPersistentSettingsField] public static float BD_PROP_FLAMEOUT = 25;         // Remaining HP% engines flameout
        [BDAPersistentSettingsField] public static bool BD_PART_STRENGTH = false;        // Part strength - breakingForce/Torque - decreases as part takes damage
        [BDAPersistentSettingsField] public static float BD_PROP_DAM_RATE = 1;          // Rate multiplier, 0.1-2
        [BDAPersistentSettingsField] public static bool BD_INTAKES = true;              // Can intakes be damaged?
        [BDAPersistentSettingsField] public static bool BD_GIMBALS = true;              // Can gimbals be disabled?
        [BDAPersistentSettingsField] public static bool BD_AEROPARTS = true;            // Lift loss & added drag
        [BDAPersistentSettingsField] public static float BD_LIFT_LOSS_RATE = 1;         // Rate multiplier
        [BDAPersistentSettingsField] public static bool BD_CTRL_SRF = true;             // Disable ctrl srf actuatiors?
        [BDAPersistentSettingsField] public static bool BD_COCKPITS = false;            // Control degredation
        [BDAPersistentSettingsField] public static bool BD_PILOT_KILLS = false;         // Cockpit damage can kill pilots?
        [BDAPersistentSettingsField] public static bool BD_FIRES_ENABLED = true;        // Can fires occur
        [BDAPersistentSettingsField] public static bool BD_FIRE_DOT = true;             // Do fires do DoT
        [BDAPersistentSettingsField] public static float BD_FIRE_DAMAGE = 5;            // Do fires do DoT
        [BDAPersistentSettingsField] public static bool BD_FIRE_HEATDMG = true;         // Do fires add heat to parts/are fires able to cook off fuel/ammo?
        [BDAPersistentSettingsField] public static bool BD_INTENSE_FIRES = false;       // Do fuel tank fires DoT get bigger over time?
        [BDAPersistentSettingsField] public static bool BD_FIRE_FUELEX = true;          // Can fires detonate fuel tanks
        [BDAPersistentSettingsField] public static float BD_FIRE_CHANCE_TRACER = 10;
        [BDAPersistentSettingsField] public static float BD_FIRE_CHANCE_HE = 25;
        [BDAPersistentSettingsField] public static float BD_FIRE_CHANCE_INCENDIARY = 90;
        #endregion

        #region Hall of Shame Settings
        [BDAPersistentSettingsField] public static bool ALLOW_ZOMBIE_BD = false;          // Allow battle damage to proc when using zombie mode?
        [BDAPersistentSettingsField] public static bool ENABLE_HOS = false;
        [BDAPersistentSettingsField] public static List<string> HALL_OF_SHAME_LIST = new List<string>();
        [BDAPersistentSettingsField] public static float HOS_FIRE = 0;
        [BDAPersistentSettingsField] public static float HOS_MASS = 0;
        [BDAPersistentSettingsField] public static float HOS_DMG = 0;
        [BDAPersistentSettingsField] public static float HOS_THRUST = 0;
        [BDAPersistentSettingsField] public static bool HOS_SAS = false;
        [BDAPersistentSettingsField] public static bool HOS_ASTEROID = false;
        [BDAPersistentSettingsField] public static string HOS_MUTATOR = "";
        [BDAPersistentSettingsField] public static string HOS_BADGE = "";
        #endregion

        #region Remote logging
        [BDAPersistentSettingsField] public static bool REMOTE_LOGGING_VISIBLE = false;                                   // Show/hide the remote orchestration toggle
        [BDAPersistentSettingsField] public static bool REMOTE_LOGGING_ENABLED = false;                                   // Enable/disable remote orchestration
        [BDAPersistentSettingsField] public static string REMOTE_ORCHESTRATION_BASE_URL = "bdascores.herokuapp.com";      // Base URL used for orchestration (note: we can't include the https:// as it breaks KSP's serialisation routine)
        [BDAPersistentSettingsField] public static string REMOTE_CLIENT_SECRET = "";                                      // Token used to authorize remote orchestration client
        [BDAPersistentSettingsField] public static string COMPETITION_HASH = "";                                          // Competition hash used for orchestration
        [BDAPersistentSettingsField] public static float REMOTE_INTERHEAT_DELAY = 30;                                     // Delay between heats.
        [BDAPersistentSettingsField] public static int RUNWAY_PROJECT_ROUND = 10;                                         // RWP round index.
        [BDAPersistentSettingsField] public static string REMOTE_ORCHESTRATION_NPC_SWAPPER = "Rammer";
        [BDAPersistentSettingsField] public static string REMOTE_ORC_NPCS_TEAM = "";
        #endregion

        #region Spawner settings
        [BDAPersistentSettingsField] public static bool SHOW_SPAWN_OPTIONS = true;                 // Show spawn options.
        [BDAPersistentSettingsField] public static Vector2d VESSEL_SPAWN_GEOCOORDS = new Vector2d(0.05096, -74.8016); // Spawning coordinates on a planetary body; Lat, Lon
        [BDAPersistentSettingsField] public static int VESSEL_SPAWN_WORLDINDEX = 1;                // Spawning planetary body: world index
        [BDAPersistentSettingsField] public static float VESSEL_SPAWN_ALTITUDE = 5f;               // Spawning altitude above the surface.
        public static float VESSEL_SPAWN_ALTITUDE_ => !RUNWAY_PROJECT ? VESSEL_SPAWN_ALTITUDE : RUNWAY_PROJECT_ROUND == 33 ? 10 : RUNWAY_PROJECT_ROUND == 53 ? FlightGlobals.currentMainBody.atmosphere ? (float)(FlightGlobals.currentMainBody.atmosphereDepth + (FlightGlobals.currentMainBody.atmosphereDepth / 10)) : 50000 : VESSEL_SPAWN_ALTITUDE; // Getter for handling the various RWP cases.
        [BDAPersistentSettingsField] public static float VESSEL_SPAWN_DISTANCE_FACTOR = 20f;       // Scale factor for the size of the spawning circle.
        [BDAPersistentSettingsField] public static float VESSEL_SPAWN_DISTANCE = 100f;             // Radius of the size of the spawning circle.
        [BDAPersistentSettingsField] public static bool VESSEL_SPAWN_DISTANCE_TOGGLE = true;       // Toggle between scaling factor and absolute distance.
        [BDAPersistentSettingsField] public static float VESSEL_SPAWN_REF_HEADING = 0f;            // Reference heading for the first craft in circular spawns.
        [BDAPersistentSettingsField] public static bool VESSEL_SPAWN_REASSIGN_TEAMS = true;        // Reassign teams on spawn, overriding teams defined in the SPH.
        [BDAPersistentSettingsField] public static int VESSEL_SPAWN_CONCURRENT_VESSELS = 0;        // Maximum number of vessels to spawn in concurrently (continuous spawning mode).
        [BDAPersistentSettingsField] public static int VESSEL_SPAWN_LIVES_PER_VESSEL = 0;          // Maximum number of times to spawn a vessel (continuous spawning mode).
        [BDAPersistentSettingsField] public static float OUT_OF_AMMO_KILL_TIME = -1f;              // Out of ammo kill timer for continuous spawn mode.
        [BDAPersistentSettingsField] public static int VESSEL_SPAWN_FILL_SEATS = 1;                // Fill seats: 0 - minimal, 1 - full cockpits or the first combat seat, 2 - all ModuleCommand and KerbalSeat parts, 3 - also cabins.
        [BDAPersistentSettingsField] public static bool VESSEL_SPAWN_CONTINUE_SINGLE_SPAWNING = false; // Spawn craft again after single spawn competition finishes.
        [BDAPersistentSettingsField] public static bool VESSEL_SPAWN_DUMP_LOG_EVERY_SPAWN = false; // Dump competition scores every time a vessel spawns.
        [BDAPersistentSettingsField] public static bool SHOW_SPAWN_LOCATIONS = false;              // Show the interesting spawn locations.
        [BDAPersistentSettingsField] public static int VESSEL_SPAWN_NUMBER_OF_TEAMS = 0;           // Number of Teams: 0 - FFA, 1 - Folders, 2-10 specified directly
        [BDAPersistentSettingsField] public static string VESSEL_SPAWN_FILES_LOCATION = "";        // Spawn files location (under AutoSpawn).
        [BDAPersistentSettingsField] public static string VESSEL_SPAWN_GAUNTLET_OPPONENTS_FILES_LOCATION = "";        // Gauntlet opponents spawn files location (under AutoSpawn).
        [BDAPersistentSettingsField] public static bool VESSEL_SPAWN_RANDOM_ORDER = true;          // Shuffle vessels before spawning them.
        [BDAPersistentSettingsField] public static bool SHOW_WAYPOINTS_OPTIONS = true;             // Waypoint section of Vessel Spawner Window.
        [BDAPersistentSettingsField] public static bool VESSEL_SPAWN_START_COMPETITION_AUTOMATICALLY = false; // Automatically start a competition after spawning succeeds.
        [BDAPersistentSettingsField] public static bool VESSEL_SPAWN_INITIAL_VELOCITY = false;     // Set planes at their idle speed after dropping them at the start of a competition.
        [BDAPersistentSettingsField] public static bool VESSEL_SPAWN_CS_FOLLOWS_CENTROID = false;  // The continuous spawning spawn point follows the brawl centroid with bias back to the original spawn point.
        #endregion

        #region Vessel Mover settings
        [BDAPersistentSettingsField] public static bool VESSEL_MOVER_CHOOSE_CREW = false;          // Choose crew when spawning vessels.
        [BDAPersistentSettingsField] public static bool VESSEL_MOVER_CLASSIC_CRAFT_CHOOSER = false; // Use the built-in craft chooser instead of the custom one.
        [BDAPersistentSettingsField] public static bool VESSEL_MOVER_ENABLE_BRAKES = true;         // Enable brakes when spawning vessels.
        [BDAPersistentSettingsField] public static bool VESSEL_MOVER_ENABLE_SAS = true;            // Enable SAS when spawning vessels.
        [BDAPersistentSettingsField] public static float VESSEL_MOVER_MIN_LOWER_SPEED = 1f;        // Minimum speed to lower vessels.
        [BDAPersistentSettingsField] public static bool VESSEL_MOVER_LOWER_FAST = true;            // Skip lowering from high altitude.
        [BDAPersistentSettingsField] public static bool VESSEL_MOVER_BELOW_WATER = false;          // Lower below water (on planets that have water).
        [BDAPersistentSettingsField] public static bool VESSEL_MOVER_DONT_WORRY_ABOUT_COLLISIONS = false; // Don't prevent collisions.
        [BDAPersistentSettingsField] public static bool VESSEL_MOVER_CLOSE_ON_COMPETITION_START = true; // Close when starting a competition.
        [BDAPersistentSettingsField] public static bool VESSEL_MOVER_PLACE_AFTER_SPAWN = false;    // Immediately place vessels after spawning them.
        #endregion

        #region Scores
        [BDAPersistentSettingsField] public static bool SHOW_SCORE_WINDOW = false;
        [BDAPersistentSettingsField] public static bool SCORES_PERSIST_UI = false;
        [BDAPersistentSettingsField] public static int SCORES_FONT_SIZE = 12;
        #endregion

        #region Waypoints
        [BDAPersistentSettingsField] public static float WAYPOINTS_ALTITUDE = 0f;                // Altitude above ground of the waypoints.
        [BDAPersistentSettingsField] public static bool WAYPOINTS_ONE_AT_A_TIME = false;          // Send the craft one-at-a-time through the course.
        [BDAPersistentSettingsField] public static bool WAYPOINTS_VISUALIZE = true;               // Add Waypoint models to indicate the path
        [BDAPersistentSettingsField] public static bool WAYPOINTS_INFINITE_FUEL_AT_START = true;  // Don't consume fuel prior to the first waypoint.
        [BDAPersistentSettingsField] public static float WAYPOINTS_SCALE = 0f;                   // Have model(or maybe WP radius proper) scale?
        [BDAPersistentSettingsField] public static int WAYPOINT_COURSE_INDEX = 0;                 // Select from a set of courses
        [BDAPersistentSettingsField] public static int WAYPOINT_LOOP_INDEX = 1;                   // Number of loops to generate
        [BDAPersistentSettingsField] public static int WAYPOINT_GUARD_INDEX = -1;                 // Activate guard after index; -1 for no guard
        [BDAPersistentSettingsField] public static int WAYPOINT_MAX_LAPS = 5;                     // Configurable max number of laps for the laps slider
        #endregion

        #region Heartbleed
        [BDAPersistentSettingsField] public static float HEART_BLEED_RATE = 0.01f;
        [BDAPersistentSettingsField] public static float HEART_BLEED_INTERVAL = 10f;
        [BDAPersistentSettingsField] public static float HEART_BLEED_THRESHOLD = 10f;
        #endregion

        #region Resource steal
        [BDAPersistentSettingsField] public static bool RESOURCE_STEAL_RESPECT_FLOWSTATE_IN = true;     // Respect resource flow state in (stealing).
        [BDAPersistentSettingsField] public static bool RESOURCE_STEAL_RESPECT_FLOWSTATE_OUT = false;   // Respect resource flow state out (stolen).
        [BDAPersistentSettingsField] public static float RESOURCE_STEAL_FUEL_RATION = 0.2f;
        [BDAPersistentSettingsField] public static float RESOURCE_STEAL_AMMO_RATION = 0.2f;
        [BDAPersistentSettingsField] public static float RESOURCE_STEAL_CM_RATION = 0f;
        #endregion

        #region Space Friction
        [BDAPersistentSettingsField] public static bool SPACE_HACKS = false;
        [BDAPersistentSettingsField] public static bool SF_FRICTION = false;
        [BDAPersistentSettingsField] public static bool SF_GRAVITY = false;
        [BDAPersistentSettingsField] public static bool SF_REPULSOR = false;
        [BDAPersistentSettingsField] public static float SF_REPULSOR_STRENGTH = 5f;
        [BDAPersistentSettingsField] public static float SF_DRAGMULT = 2f;
        #endregion

        #region Mutator Mode
        [BDAPersistentSettingsField] public static bool MUTATOR_APPLY_GLOBAL = false;
        [BDAPersistentSettingsField] public static bool MUTATOR_APPLY_KILL = false;
        [BDAPersistentSettingsField] public static bool MUTATOR_APPLY_TIMER = false;
        [BDAPersistentSettingsField] public static float MUTATOR_DURATION = 0.5f;
        [BDAPersistentSettingsField] public static List<string> MUTATOR_LIST = new List<string>();
        [BDAPersistentSettingsField] public static int MUTATOR_APPLY_NUM = 1;
        [BDAPersistentSettingsField] public static bool MUTATOR_ICONS = false;
        [BDAPersistentSettingsField] public static bool MUTATOR_APPLY_GUNGAME = false;
        [BDAPersistentSettingsField] public static float VENGEANCE_DELAY = 2.5f;
        [BDAPersistentSettingsField] public static float VENGEANCE_YIELD = 1.5f;
        #endregion
        #region GunGame
        [BDAPersistentSettingsField] public static bool GG_PERSISTANT_PROGRESSION = false;
        [BDAPersistentSettingsField] public static bool GG_CYCLE_LIST = false;
        //[BDAPersistentSettingsField] public static bool GG_ANNOUNCER = false;

        #endregion
        #region Tournament settings
        [BDAPersistentSettingsField] public static bool SHOW_TOURNAMENT_OPTIONS = false;           // Show tournament options.
        [BDAPersistentSettingsField] public static int TOURNAMENT_STYLE = 0;                       // Tournament Style (Random, N-choose-K, Gauntlet, etc.)
        [BDAPersistentSettingsField] public static int TOURNAMENT_ROUND_TYPE = 0;                  // Tournament Style (Shuffled, Ranked, etc.)
        [BDAPersistentSettingsField] public static float TOURNAMENT_DELAY_BETWEEN_HEATS = 5;      // Delay between heats
        [BDAPersistentSettingsField] public static int TOURNAMENT_ROUNDS = 1;                      // Rounds
        [BDAPersistentSettingsField] public static int TOURNAMENT_ROUNDS_CUSTOM = 1000;            // Custom number of rounds at right end of slider.
        [BDAPersistentSettingsField] public static int TOURNAMENT_VESSELS_PER_HEAT = -1;           // Vessels Per Heat (Auto)
        [BDAPersistentSettingsField] public static Vector2Int TOURNAMENT_AUTO_VESSELS_PER_HEAT_RANGE = new Vector2Int(6, 10); // Automatic vessels per heat selection (inclusive range).
        [BDAPersistentSettingsField] public static int TOURNAMENT_NPCS_PER_HEAT = 0;               // NPCs Per Heat
        [BDAPersistentSettingsField] public static int TOURNAMENT_TEAMS_PER_HEAT = 2;              // Teams Per Heat
        [BDAPersistentSettingsField] public static int TOURNAMENT_OPPONENT_TEAMS_PER_HEAT = 1;     // Opponent Teams Per Heat (for gauntlets)
        [BDAPersistentSettingsField] public static int TOURNAMENT_VESSELS_PER_TEAM = 2;            // Vessels Per Team
        [BDAPersistentSettingsField] public static int TOURNAMENT_OPPONENT_VESSELS_PER_TEAM = 2;   // Opponent Vessels Per Team
        [BDAPersistentSettingsField] public static bool TOURNAMENT_FULL_TEAMS = true;              // Full Teams
        [BDAPersistentSettingsField] public static float TOURNAMENT_TIMEWARP_BETWEEN_ROUNDS = 0;   // Timewarp between rounds in minutes.
        [BDAPersistentSettingsField] public static bool AUTO_RESUME_TOURNAMENT = false;            // Automatically load the game the last incomplete tournament was running in and continue the tournament.
        [BDAPersistentSettingsField] public static bool AUTO_RESUME_CONTINUOUS_SPAWN = false;      // Automatically load the game the last continuous spawn was running in and start running continuous spawn again.
        [BDAPersistentSettingsField] public static float QUIT_MEMORY_USAGE_THRESHOLD = float.MaxValue; // Automatically quit KSP when memory usage is beyond this. (0 = disabled)
        [BDAPersistentSettingsField] public static bool AUTO_QUIT_AT_END_OF_TOURNAMENT = false;    // Automatically quit at the end of a tournament (for automation).
        [BDAPersistentSettingsField] public static bool AUTO_GENERATE_TOURNAMENT_ON_RESUME = false; // Automatically generate a tournament after loading the game if the last tournament was complete or missing.
        [BDAPersistentSettingsField] public static string LAST_USED_SAVEGAME = "";                 // Name of the last used savegame (for auto_generate_tournament_on_resume).
        [BDAPersistentSettingsField] public static bool TOURNAMENT_BACKUPS = false;                // Store backups of unfinished tournaments.
        [BDAPersistentSettingsField] public static bool AUTO_DISABLE_UI = false;                   // Automatically disable the UI when starting tournaments.
        #endregion

        #region Custom Spawn Template
        [BDAPersistentSettingsField] public static bool CUSTOM_SPAWN_TEMPLATE_SHOW_OPTIONS = false; // Custom Spawn Template options.
        [BDAPersistentSettingsField] public static bool CUSTOM_SPAWN_TEMPLATE_REPLACE_TEAM = false; // Replace all vessels on the team.
        #endregion

        #region Time override settings
        [BDAPersistentSettingsField] public static bool TIME_OVERRIDE = false;                     // Enable the time control slider.
        [BDAPersistentSettingsField] public static float TIME_SCALE = 1f;                          // Time scale factor (higher speeds up the game rate without adjusting the physics time-step).
        [BDAPersistentSettingsField] public static float TIME_SCALE_MAX = 10f;                     // Max time scale factor (to allow users to set custom max values).
        #endregion

        #region Scoring categories
        [BDAPersistentSettingsField] public static float SCORING_HEADSHOT = 3;                     // Head-Shot Time Limit
        [BDAPersistentSettingsField] public static float SCORING_KILLSTEAL = 5;                   // Kill-Steal Time Limit
        #endregion

        #region Evolution settings
        [BDAPersistentSettingsField] public static bool EVOLUTION_ENABLED = false;
        [BDAPersistentSettingsField] public static bool SHOW_EVOLUTION_OPTIONS = false;
        [BDAPersistentSettingsField] public static int EVOLUTION_ANTAGONISTS_PER_HEAT = 1;
        [BDAPersistentSettingsField] public static int EVOLUTION_MUTATIONS_PER_HEAT = 1;
        [BDAPersistentSettingsField] public static int EVOLUTION_HEATS_PER_GROUP = 1;
        [BDAPersistentSettingsField] public static bool AUTO_RESUME_EVOLUTION = false;             // Automatically load the game and start evolution with the last used settings/seeds. Note: this overrides the AUTO_RESUME_TOURNAMENT setting.
        #endregion

        #region Missile & Countermeasure Settings
        [BDAPersistentSettingsField] public static bool MISSILE_CM_SETTING_TOGGLE = false;
        [BDAPersistentSettingsField] public static bool VARIABLE_MISSILE_VISIBILITY = false;        //missile visual detection range dependant on boost/cruise/post-thrust state
        [BDAPersistentSettingsField] public static bool ASPECTED_RCS = false;                   //RCS evaluated in real-time based on aircraft's aspect
        [BDAPersistentSettingsField] public static float ASPECTED_RCS_OVERALL_RCS_WEIGHT = 0.25f;   //When ASPECTED_RCS = true, final aspected RCS will be = (1-ASPECTED_RCS_OVERALL_RCS_WEIGHT) * [Aspected RCS] + ASPECTED_RCS_OVERALL_RCS_WEIGHT * [Overall RCS]
        [BDAPersistentSettingsField] public static bool ASPECTED_IR_SEEKERS = false;                //IR Missiles will be subject to thermal occlusion mechanic
        [BDAPersistentSettingsField] public static bool DUMB_IR_SEEKERS = false;                  // IR missiles will go after hottest thing they can see
        [BDAPersistentSettingsField] public static float FLARE_FACTOR = 1.6f;                       // Change this to make flares more or less effective, values close to or below 1.0 will cause flares to fail to decoy often
        [BDAPersistentSettingsField] public static float CHAFF_FACTOR = 0.65f;                       // Change this to make chaff more or less effective. Higher values will make chaff batter, lower values will make chaff worse.
        [BDAPersistentSettingsField] public static float SMOKE_DEFLECTION_FACTOR = 10f;
        [BDAPersistentSettingsField] public static int APS_THRESHOLD = 60;                           // Threshold caliber that APS will register for intercepting hostile shells/rockets
        #endregion
    }
}
