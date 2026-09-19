using System.Reflection;

using BDArmory.Utils;

namespace BDArmory.Settings
{
    public class BDInputSettingsFields
    { // Note: order here determines order in input settings GUI within each section (based on prefix).
        //MAIN
        public static BDInputInfo WEAP_FIRE_KEY = new BDInputInfo("mouse 0", "Fire");
        public static BDInputInfo WEAP_FIRE_MISSILE_KEY = new BDInputInfo("Fire Missile");
        public static BDInputInfo WEAP_NEXT_KEY = new BDInputInfo("Next Weapon");
        public static BDInputInfo WEAP_PREV_KEY = new BDInputInfo("Prev Weapon");
        public static BDInputInfo WEAP_TOGGLE_ARMED_KEY = new BDInputInfo("Toggle Armed");
        public static BDInputInfo WEAP_TOGGLE_HMD_KEY = new BDInputInfo("Toggle HMD");

        //TGP
        public static BDInputInfo TGP_SLEW_RIGHT = new BDInputInfo("[6]", "Slew Right");
        public static BDInputInfo TGP_SLEW_LEFT = new BDInputInfo("[4]", "Slew Left");
        public static BDInputInfo TGP_SLEW_UP = new BDInputInfo("[5]", "Slew Up");
        public static BDInputInfo TGP_SLEW_DOWN = new BDInputInfo("[8]", "Slew Down");
        public static BDInputInfo TGP_LOCK = new BDInputInfo("[9]", "Lock/Unlock");
        public static BDInputInfo TGP_IN = new BDInputInfo("[0]", "Zoom In");
        public static BDInputInfo TGP_OUT = new BDInputInfo("[.]", "Zoom Out");
        public static BDInputInfo TGP_RADAR = new BDInputInfo("[3]", "To Radar");
        public static BDInputInfo TGP_SEND_GPS = new BDInputInfo("[7]", "Send GPS");
        public static BDInputInfo TGP_TO_GPS = new BDInputInfo("[2]", "Slave to GPS");
        public static BDInputInfo TGP_TURRETS = new BDInputInfo("[1]", "Slave Turrets");
        public static BDInputInfo TGP_COM = new BDInputInfo("CoM-Track");
        public static BDInputInfo TGP_NV = new BDInputInfo("Toggle NV");
        public static BDInputInfo TGP_RESET = new BDInputInfo("Reset");
        public static BDInputInfo TGP_SELECT_NEXT_GPS_TARGET = new BDInputInfo("Select Next GPS Target");

        //RADAR
        public static BDInputInfo RADAR_LOCK = new BDInputInfo("Lock/Unlock");
        public static BDInputInfo RADAR_CYCLE_LOCK = new BDInputInfo("Cycle Lock");
        public static BDInputInfo RADAR_SLEW_RIGHT = new BDInputInfo("Slew Right");
        public static BDInputInfo RADAR_SLEW_LEFT = new BDInputInfo("Slew Left");
        public static BDInputInfo RADAR_SLEW_UP = new BDInputInfo("Slew Up");
        public static BDInputInfo RADAR_SLEW_DOWN = new BDInputInfo("Slew Down");
        public static BDInputInfo RADAR_SCAN_MODE = new BDInputInfo("Scan Mode");
        public static BDInputInfo RADAR_TURRETS = new BDInputInfo("Slave Turrets");
        public static BDInputInfo RADAR_RANGE_UP = new BDInputInfo("Range +");
        public static BDInputInfo RADAR_RANGE_DN = new BDInputInfo("Range -");
        public static BDInputInfo RADAR_TARGET_NEXT = new BDInputInfo("Next Target");
        public static BDInputInfo RADAR_TARGET_PREV = new BDInputInfo("Prev Target");

        // VESSEL SWITCHER
        public static BDInputInfo VS_SWITCH_NEXT = new BDInputInfo("page down", "Next Vessel");
        public static BDInputInfo VS_SWITCH_PREV = new BDInputInfo("page up", "Prev Vessel");

        // TOURNAMENT
        public static BDInputInfo TOURNAMENT_SETUP = new BDInputInfo("Setup Tournament");
        public static BDInputInfo TOURNAMENT_RUN = new BDInputInfo("Run Tournament");

        //GUI
        public static BDInputInfo GUI_WM_TOGGLE = new BDInputInfo("[*]", "Toggle WM GUI");
        public static BDInputInfo GUI_AI_TOGGLE = new BDInputInfo("[/]", "Toggle AI GUI");

        //WingManager
        public static BDInputInfo MWC_SELECTIONBOX = new BDInputInfo("WingCommander Batch Select");

        //DEBUG
        public static BDInputInfo DEBUG_CLEAR_DEV_CONSOLE = new BDInputInfo("Clear Development Console");

        // TIME SCALING
        public static BDInputInfo TIME_SCALING = new BDInputInfo("Toggle Time Scaling");

        public static BDInputInfo TEMPORARILY_SHOW_MOUSE = new BDInputInfo("right shift", "Temporarily Show Mouse");

        public static void SaveSettings()
        {
            ConfigNode fileNode = ConfigNode.Load(BDArmorySettings.settingsConfigURL);
            if (!fileNode.HasNode("BDAInputSettings"))
            {
                fileNode.AddNode("BDAInputSettings");
            }

            ConfigNode cfg = fileNode.GetNode("BDAInputSettings");

            FieldInfo[] fields = typeof(BDInputSettingsFields).GetFields();
            for (int i = 0; i < fields.Length; i++)
            {
                string fieldName = fields[i].Name;
                string valueString = ((BDInputInfo)fields[i].GetValue(null)).inputString;
                cfg.SetValue(fieldName, valueString, true);
            }

            fileNode.Save(BDArmorySettings.settingsConfigURL);
        }

        public static void LoadSettings()
        {
            ConfigNode fileNode = ConfigNode.Load(BDArmorySettings.settingsConfigURL);
            if (!fileNode.HasNode("BDAInputSettings"))
            {
                fileNode.AddNode("BDAInputSettings");
            }

            ConfigNode cfg = fileNode.GetNode("BDAInputSettings");

            FieldInfo[] fields = typeof(BDInputSettingsFields).GetFields();
            for (int i = 0; i < fields.Length; i++)
            {
                string fieldName = fields[i].Name;
                if (!cfg.HasValue(fieldName)) continue;
                BDInputInfo orig = (BDInputInfo)fields[i].GetValue(null);
                BDInputInfo loaded = new BDInputInfo(cfg.GetValue(fieldName), orig.description);
                fields[i].SetValue(null, loaded);
            }

            fileNode.Save(BDArmorySettings.settingsConfigURL);
        }
    }
}
