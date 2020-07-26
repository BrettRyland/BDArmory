namespace BDArmory.Core
{
    public class BDArmorySettings
    {
        public static string settingsConfigURL = "GameData/BDArmory/settings.cfg";

        [BDAPersistantSettingsField] public static bool INSTAKILL = false;
        [BDAPersistantSettingsField] public static bool BULLET_HITS = true;
        [BDAPersistantSettingsField] public static bool BULLET_DECALS = true;
        [BDAPersistantSettingsField] public static int MAX_NUM_BULLET_DECALS = 200;
        [BDAPersistantSettingsField] public static bool SHOW_AMMO_GAUGES = false;
        [BDAPersistantSettingsField] public static bool EJECT_SHELLS = true;
        [BDAPersistantSettingsField] public static bool SHELL_COLLISIONS = true;
        [BDAPersistantSettingsField] public static bool INFINITE_AMMO = false;
        [BDAPersistantSettingsField] public static bool DRAW_DEBUG_LINES = false;
        [BDAPersistantSettingsField] public static bool DRAW_DEBUG_LABELS = false;
        [BDAPersistantSettingsField] public static bool DRAW_AIMERS = true;
        [BDAPersistantSettingsField] public static bool AIM_ASSIST = true;
        [BDAPersistantSettingsField] public static bool REMOTE_SHOOTING = false;
        [BDAPersistantSettingsField] public static bool BOMB_CLEARANCE_CHECK = true;
        [BDAPersistantSettingsField] public static float MAX_BULLET_RANGE = 8000f;               //TODO: remove all references to this so it can be deprecated! all ranges should be supplied in part config!
        [BDAPersistantSettingsField] public static float TRIGGER_HOLD_TIME = 0.3f;
        [BDAPersistantSettingsField] public static float TARGET_CAM_RESOLUTION = 1024f;
        [BDAPersistantSettingsField] public static bool BW_TARGET_CAM = true;
        [BDAPersistantSettingsField] public static float SMOKE_DEFLECTION_FACTOR = 10f;
        [BDAPersistantSettingsField] public static float RWR_WINDOW_SCALE_MIN = 0.50f;
        [BDAPersistantSettingsField] public static float RWR_WINDOW_SCALE = 1f;
        [BDAPersistantSettingsField] public static float RWR_WINDOW_SCALE_MAX = 1.50f;
        [BDAPersistantSettingsField] public static float RADAR_WINDOW_SCALE_MIN = 0.50f;
        [BDAPersistantSettingsField] public static float RADAR_WINDOW_SCALE = 1f;
        [BDAPersistantSettingsField] public static float RADAR_WINDOW_SCALE_MAX = 1.50f;
        [BDAPersistantSettingsField] public static float TARGET_WINDOW_SCALE_MIN = 0.50f;
        [BDAPersistantSettingsField] public static float TARGET_WINDOW_SCALE = 1f;
        [BDAPersistantSettingsField] public static float TARGET_WINDOW_SCALE_MAX = 2f;
        [BDAPersistantSettingsField] public static float BDARMORY_UI_VOLUME = 0.35f;
        [BDAPersistantSettingsField] public static float BDARMORY_WEAPONS_VOLUME = 0.32f;
        [BDAPersistantSettingsField] public static float MAX_GUARD_VISUAL_RANGE = 40000f;
        [BDAPersistantSettingsField] public static float MAX_ACTIVE_RADAR_RANGE = 40000f;        //NOTE: used ONLY for display range of radar windows! Actual radar range provided by part configs!
        [BDAPersistantSettingsField] public static float MAX_ENGAGEMENT_RANGE = 40000f;          //NOTE: used ONLY for missile dlz parameters!
        [BDAPersistantSettingsField] public static float GLOBAL_LIFT_MULTIPLIER = 0.20f;
        [BDAPersistantSettingsField] public static float GLOBAL_DRAG_MULTIPLIER = 4f;
        [BDAPersistantSettingsField] public static float IVA_LOWPASS_FREQ = 2500f;
        [BDAPersistantSettingsField] public static bool PEACE_MODE = false;
        [BDAPersistantSettingsField] public static bool DISABLE_RAMMING = false;
        [BDAPersistantSettingsField] public static bool FFA_COMBAT_STYLE = false;                // Free-for-all combat style instead of teams (changes target selection behaviour)
        [BDAPersistantSettingsField] public static bool IGNORE_TERRAIN_CHECK = false;
        [BDAPersistantSettingsField] public static bool DISPLAY_PATHING_GRID = false;             //laggy when the grid gets large
        [BDAPersistantSettingsField] public static bool ADVANCED_EDIT = false;                    //Used for debug fields not nomrally shown to regular users

        [BDAPersistantSettingsField] public static float RECOIL_FACTOR = 0.75f;
        [BDAPersistantSettingsField] public static float DMG_MULTIPLIER = 300f;
        [BDAPersistantSettingsField] public static float BALLISTIC_DMG_FACTOR = 1f;
        [BDAPersistantSettingsField] public static float HITPOINT_MULTIPLIER = 2.0f;
        [BDAPersistantSettingsField] public static float EXP_DMG_MOD_BALLISTIC;
        [BDAPersistantSettingsField] public static float EXP_DMG_MOD_MISSILE;
        [BDAPersistantSettingsField] public static float EXP_IMP_MOD;
        [BDAPersistantSettingsField] public static bool FIRE_FX_IN_FLIGHT = false;
        [BDAPersistantSettingsField] public static int MAX_FIRES_PER_VESSEL = 10;                   //controls fx for penetration only for landed or splashed
        [BDAPersistantSettingsField] public static float FIRELIFETIME_IN_SECONDS = 90f;             //controls fx for penetration only for landed or splashed
        [BDAPersistantSettingsField] public static bool PERFORMANCE_LOGGING = false;
        [BDAPersistantSettingsField] public static bool AUTOCATEGORIZE_PARTS = true;
        [BDAPersistantSettingsField] public static bool SHOW_CATEGORIES = false;
        [BDAPersistantSettingsField] public static int TERRAIN_ALERT_FREQUENCY = 1;               // Controls how often terrain avoidance checks are made (gets scaled by 1+(radarAltitude/500)^2)
        [BDAPersistantSettingsField] public static bool DEBUG_RAMMING_LOGGING = false;                // Controls whether ramming logging debug information is printed to the Debug.Log
        [BDAPersistantSettingsField] public static bool DISABLE_KILL_TIMER = false;                 //disables the kill timers.
    }
}
