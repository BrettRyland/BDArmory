using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.GUILayout;

using BDArmory.Bullets;
using BDArmory.Settings;
using BDArmory.Utils;
using BDArmory.Weapons;
using System.Collections;

namespace BDArmory.UI
{
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class BDAmmoSelector : MonoBehaviour
    {
        public static BDAmmoSelector Instance;
        private Rect windowRect = new Rect(350, 100, 350, 20);

        const float width = 350;
        const float margin = 5;
        const float buttonHeight = 20;

        private bool open = false;
        private bool save = false;
        private float height = 20;

        private string beltString = string.Empty;
        private string GUIstring = string.Empty;
        private string lastGUIstring = string.Empty;
        string countString = string.Empty;
        private int roundCounter = 0;
        int labelLines = 1;

        private Vector2 windowLocation;
        private ModuleWeapon selectedWeapon;

        public string SelectedAmmoType; //presumably Aubranium can use this to filter allowed/banned ammotypes

        public List<string> AList = new List<string>();
        public List<string> BList = new List<string>();
        public List<string> ammoDesc = new List<string>();
        private BulletInfo bulletInfo;
        public string guiAmmoTypeString = StringUtils.Localize("#LOC_BDArmory_Ammo_Slug");

        GUIStyle labelStyle;
        GUIStyle titleStyle;
        private Vector2 scrollInfoVector;
        void Start()
        {
            labelStyle = new GUIStyle(BDArmorySetup.BDGuiSkin.label)
            {
                alignment = TextAnchor.UpperLeft
            };
            labelStyle.normal.textColor = Color.white;

            titleStyle = new GUIStyle
            {
                font = BDArmorySetup.BDGuiSkin.window.font,
                fontSize = BDArmorySetup.BDGuiSkin.window.fontSize,
                fontStyle = BDArmorySetup.BDGuiSkin.window.fontStyle,
                alignment = TextAnchor.UpperCenter,
                normal = new GUIStyleState { textColor = BDArmorySetup.BDGuiSkin.window.normal.textColor }
            };
        }

        public void Open(ModuleWeapon weapon, Vector2 position)
        {
            open = true;
            selectedWeapon = weapon;
            windowLocation = position;
            beltString = string.Empty;
            GUIstring = string.Empty;
            countString = string.Empty;
            lastGUIstring = string.Empty;
            roundCounter = 0;
            applyWeaponGroupTo = new string[] { StringUtils.Localize("#LOC_BDArmory_thisWeapon"), StringUtils.Localize("#LOC_BDArmory_SymmetricWeapons"), $"{StringUtils.Localize("#autoLOC_900712")} {weapon.part.partInfo.title}" };
            _applyWeaponGroupTo = applyWeaponGroupTo[_applyWeaponGroupToIndex];
            if (weapon.ammoBelt != "def")
            {
                beltString = weapon.ammoBelt;
                BList = BDAcTools.ParseNames(beltString);
                for (int i = 0; i < BList.Count; i++)
                {
                    BulletInfo binfo = BulletInfo.bullets[BList[i].ToString()];
                    if (BList[i] != lastGUIstring)
                    {
                        GUIstring += countString.ToString();
                        GUIstring += binfo.DisplayName;
                        lastGUIstring = binfo.DisplayName;
                        roundCounter = 1;
                        countString = "; ";
                    }
                    else
                    {
                        roundCounter++;
                        countString = " X" + roundCounter + "; ";
                    }
                }
            }
            AList = BDAcTools.ParseNames(weapon.bulletType);

            for (int a = 0; a < AList.Count; a++)
            {
                bulletInfo = BulletInfo.bullets[AList[a].ToString()];
                guiAmmoTypeString = "";
                if (bulletInfo.projectileCount >= 2)
                {
                    guiAmmoTypeString = StringUtils.Localize("#LOC_BDArmory_Ammo_Shot") + " ";
                }
                if (bulletInfo.apBulletMod >= 1.1)
                {
                    guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_AP") + " ";
                }
                if (bulletInfo.apBulletMod < 1.1 && bulletInfo.apBulletMod > 0.8f)
                {
                    guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_SAP") + " ";
                }
                if (bulletInfo.nuclear)
                {
                    guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_Nuclear") + " ";
                }
                if (bulletInfo.tntMass > 0 && !bulletInfo.nuclear)
                {
                    if (bulletInfo.fuzeType.ToLower() == "flak" || bulletInfo.fuzeType.ToLower() == "proximity")
                    {
                        guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_Flak") + " ";
                    }
                    else if (bulletInfo.explosive.ToLower() == "Shaped")
                    {
                        guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_Shaped") + " ";
                    }
                    guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_Explosive") + " ";
                }
                if (bulletInfo.incendiary)
                {
                    guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_Incendiary") + " ";
                }
                if (bulletInfo.EMP && !bulletInfo.nuclear)
                {
                    guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_EMP") + " ";
                }
                if (bulletInfo.beehive)
                {
                    guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_Beehive") + " ";
                }
                if (bulletInfo.tntMass <= 0 && bulletInfo.apBulletMod <= 0.8)
                {
                    guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_Slug");
                }
                ammoDesc.Add(guiAmmoTypeString);
            }
        }

        // Doing it this way prevents OnGUI events from below the window from being triggered by the window disappearing.
        void CloseWindow() => StartCoroutine(CloseWindowAtEndOfFrame());
        bool waitingForEndOfFrame = false;
        IEnumerator CloseWindowAtEndOfFrame()
        {
            if (waitingForEndOfFrame) yield break;
            waitingForEndOfFrame = true;
            yield return new WaitForEndOfFrame();
            waitingForEndOfFrame = false;
            CloseWindowNow();
        }
        void CloseWindowNow()
        {
            open = false;
            GUIUtils.PreventClickThrough(windowRect, "BDABELTLOCK", true);
        }

        string[] applyWeaponGroupTo;
        string _applyWeaponGroupTo;
        int _applyWeaponGroupToIndex = 1; // Default to symmetric belts.
        protected virtual void OnGUI()
        {
            if (save)
            {
                save = false;

                switch (_applyWeaponGroupToIndex)
                {
                    case 0:
                        SetBeltInfo(selectedWeapon);
                        break;
                    case 1: // symmetric parts
                        SetBeltInfo(selectedWeapon);
                        foreach (Part p in selectedWeapon.part.symmetryCounterparts)
                        {
                            var wpn = p.GetComponent<ModuleWeapon>();
                            if (wpn == null) continue;
                            if (wpn.GetShortName() != selectedWeapon.GetShortName()) continue;
                            SetBeltInfo(wpn);
                        }
                        break;
                    case 2: // all weapons of the same type
                        foreach (Part p in EditorLogic.fetch.ship.parts)
                        {
                            if (p.name == selectedWeapon.part.name)
                            {
                                var wpn = p.GetComponent<ModuleWeapon>();
                                if (wpn == null) continue;
                                if (wpn.GetShortName() != selectedWeapon.GetShortName()) continue;
                                SetBeltInfo(wpn);
                            }
                        }
                        break;
                }
            }
            if (open)
            {
                if (BDArmorySettings.UI_SCALE_ACTUAL != 1) GUIUtility.ScaleAroundPivot(BDArmorySettings.UI_SCALE_ACTUAL * Vector2.one, windowRect.position);
                windowRect = GUI.Window(GUIUtility.GetControlID(FocusType.Passive), windowRect, AmmoSelectorWindow, "", BDArmorySetup.BDGuiSkin.window);
            }
        }
        private void SetBeltInfo(ModuleWeapon weapon)
        {
            weapon.ammoBelt = beltString;
            if (!string.IsNullOrEmpty(beltString))
                weapon.useCustomBelt = true;
            else
                weapon.useCustomBelt = false;
        }
        private void AmmoSelectorWindow(int id)
        {
            GUIUtils.PreventClickThrough(windowRect, "BDABELTLOCK");
            float line = 0.5f;
            string labelString = GUIstring.ToString() + countString.ToString();
            GUI.Label(new Rect(margin, 0.5f * buttonHeight, width - 2 * margin, buttonHeight), StringUtils.Localize("#LOC_BDArmory_Ammo_Setup"), titleStyle);
            if (GUI.Button(new Rect(width - 26, 2, 24, 24), " X", BDArmorySetup.CloseButtonStyle))
            {
                beltString = string.Empty;
                GUIstring = string.Empty;
                countString = string.Empty;
                lastGUIstring = string.Empty;
                CloseWindow();
            }
            line++;
            GUI.Label(new Rect(margin, line * buttonHeight, width - 2 * margin, buttonHeight), StringUtils.Localize("#LOC_BDArmory_Ammo_Weapon") + " " + selectedWeapon.part.partInfo.title, labelStyle);
            line++;
            GUI.Label(new Rect(margin, line * buttonHeight, width - 2 * margin, buttonHeight), StringUtils.Localize("#LOC_BDArmory_Ammo_Belt"), labelStyle);
            line += 1.2f;
            labelLines = Mathf.Clamp(Mathf.CeilToInt(labelString.Length / 50), 1, 4);
            BeginArea(new Rect(margin, line * buttonHeight, width - 2 * margin, labelLines * buttonHeight));
            using (var scrollViewScope = new ScrollViewScope(scrollInfoVector, Width(width - 2 * margin), Height(labelLines * buttonHeight)))
            {
                scrollInfoVector = scrollViewScope.scrollPosition;
                GUILayout.Label(labelString, labelStyle, Width(width - 50 - 2 * margin));
            }
            EndArea();
            line++;

            float ammolines = 0.1f;
            for (int i = 0; i < AList.Count; i++)
            {
                string ammoname = string.IsNullOrEmpty(BulletInfo.bullets[AList[i]].DisplayName) ? BulletInfo.bullets[AList[i]].name : BulletInfo.bullets[AList[i]].DisplayName;
                if (GUI.Button(new Rect(margin * 2, (line + labelLines + ammolines) * buttonHeight, (width - 4 * margin), buttonHeight), ammoname, BDArmorySetup.ButtonStyle))
                {
                    beltString += BulletInfo.bullets[AList[i]].name;
                    beltString += "; ";
                    if (lastGUIstring != ammoname)
                    {
                        GUIstring += countString.ToString();
                        GUIstring += ammoname;
                        lastGUIstring = ammoname;
                        roundCounter = 1;
                        countString = "; ";
                    }
                    else
                    {
                        roundCounter++;
                        countString = " X" + roundCounter + "; ";
                    }
                }
                ammolines++;
                if (ammoDesc[i] != null)
                {
                    GUI.Label(new Rect(margin * 4, (line + labelLines + ammolines) * buttonHeight, (width - 8 * margin), buttonHeight), ammoDesc[i], labelStyle);
                    ammolines += 1.1f;
                }
            }
            if (GUI.Button(new Rect(margin * 5, (line + labelLines + ammolines) * buttonHeight, (width - (10 * margin)) / 2, buttonHeight), StringUtils.Localize("#LOC_BDArmory_reset"), BDArmorySetup.ButtonStyle))
            {
                beltString = string.Empty;
                GUIstring = string.Empty;
                countString = string.Empty;
                lastGUIstring = string.Empty;
                labelLines = 1;
                roundCounter = 1;
            }
            if (GUI.Button(new Rect((margin * 5) + ((width - (10 * margin)) / 2), (line + labelLines + ammolines) * buttonHeight, (width - (10 * margin)) / 2, buttonHeight), StringUtils.Localize("#LOC_BDArmory_save"), BDArmorySetup.ButtonStyle))
            {
                save = true;
                CloseWindow();
            }
            line += 1.5f;

            GUI.Label(new Rect(margin * 5, (line + labelLines + ammolines) * buttonHeight, (width - (10 * margin)) / 2, buttonHeight), $"{StringUtils.Localize("#LOC_BDArmory_applyTo")} {_applyWeaponGroupTo}");
            line += 0.2f;
            if (_applyWeaponGroupToIndex != (_applyWeaponGroupToIndex = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect((margin * 5) + (width - (10 * margin)) / 2, (line + labelLines + ammolines) * buttonHeight, (width - (10 * margin)) / 2, buttonHeight),
                            _applyWeaponGroupToIndex, 0, 2)))) _applyWeaponGroupTo = applyWeaponGroupTo[_applyWeaponGroupToIndex];
            line += 1.5f;
            height = Mathf.Lerp(height, (line + labelLines + ammolines) * buttonHeight, 0.15f);
            windowRect.height = height;

            GUIUtils.DragWindow();
            GUIUtils.RepositionWindow(ref windowRect);
        }

        private void Awake()
        {
            if (Instance) Destroy(Instance);
            Instance = this;
            windowRect = new Rect((Screen.width / 2) - (width / 2), (Screen.height / 2) - (height / 2), width, height);
        }

        private void OnDestroy()
        {
            CloseWindowNow();
        }
    }
}
