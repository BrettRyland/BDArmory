using BDArmory.Control;
using BDArmory.Settings;
using BDArmory.Utils;
using LibNoise.Models;
using System.Collections;
using UnityEngine;

namespace BDArmory.UI
{
    [KSPAddon(KSPAddon.Startup.FlightAndEditor, false)]
    public class BDMultiTargetingSelector : MonoBehaviour
    {
        public static BDMultiTargetingSelector Instance;

        const float width = 250;
        const float margin = 5;
        const float buttonHeight = 20;
        const float buttonGap = 2;

        private static int guiCheckIndex = -1;
        private bool ready = false;
        private bool open = false;
        private Rect window;
        private float height;

        private Vector2 windowLocation;
        private MissileFire targetWeaponManager;
        public void Open(MissileFire weaponManager, Vector2 position)
        {
            SetVisible(true);
            targetWeaponManager = weaponManager;
            windowLocation = position;
        }

        void SetVisible(bool visible)
        {
            open = visible;
            GUIUtils.SetGUIRectVisible(guiCheckIndex, visible);
        }
        Rect SRect(float line, float indent = 0, float margin = 20)
        {
            return new Rect(10 + indent, line, width - margin, 20);
        }
        private void TargetingSelectorWindow(int id)
        {
            height = margin;
            GUIStyle labelStyle = BDArmorySetup.BDGuiSkin.label;
            GUI.Label(new Rect(margin, height, width - 2 * margin, buttonHeight), StringUtils.Localize("#LOC_BDArmory_MultiTarget_Config"), labelStyle);
            if (GUI.Button(new Rect(width - 18, 2, 16, 16), " X", BDArmorySetup.CloseButtonStyle))
            {
                SetVisible(false);
            }
            height += buttonHeight;
            /*
            GUI.Toggle(SRect(height++), targetWeaponManager.advancedMultiTargeting, StringUtils.Localize("#LOC_BDArmory_MultiTargetTurret_Config"), labelStyle);
            if (targetWeaponManager.advancedMultiTargeting)
            {
                height += buttonGap;
                GUI.Label(SRect(height++), StringUtils.Localize("#LOC_BDArmory_WMWindow_MultiTargetNum") + ": " + StringUtils.Localize("#LOC_BDArmory_Air"), labelStyle);
                GUI.HorizontalSlider(SRect(height++, 40), targetWeaponManager.multiTargetNumAir, 0, 10);
                height += buttonGap;

                GUI.Label(SRect(height++), StringUtils.Localize("#LOC_BDArmory_WMWindow_MultiTargetNum") + ": " + StringUtils.Localize("#LOC_BDArmory_Surface"), labelStyle);
                GUI.HorizontalSlider(SRect(height++, 40), targetWeaponManager.multiTargetNumSrf, 0, 10);
                height += buttonGap;

                GUI.Label(SRect(height++), StringUtils.Localize("#LOC_BDArmory_WMWindow_MultiTargetNum") + ": " + StringUtils.Localize("#LOC_BDArmory_Sea"), labelStyle);
                GUI.HorizontalSlider(SRect(height++, 40), targetWeaponManager.multiTargetNumSea, 0, 10);
                height += buttonGap;

                GUI.Label(SRect(height++), StringUtils.Localize("#LOC_BDArmory_WMWindow_MultiTargetNum") + ": " + StringUtils.Localize("#LOC_BDArmory_Missile"), labelStyle);
                GUI.HorizontalSlider(SRect(height++, 40), targetWeaponManager.multiTargetNumMsl, 0, 10);
                height += buttonGap;
                targetWeaponManager.multiTargetNum = Mathf.Max(targetWeaponManager.multiTargetNumAir, targetWeaponManager.multiTargetNumSrf, targetWeaponManager.multiTargetNumSea, targetWeaponManager.multiTargetNumMsl);
            }
            */
            GUIStyle adVStyle = targetWeaponManager.advancedMissileTargeting ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle;
            if (GUI.Button(SRect(height += buttonHeight), StringUtils.Localize("#LOC_BDArmory_MultiTargetMsl_Config"), adVStyle))
                targetWeaponManager.advancedMissileTargeting = !targetWeaponManager.advancedMissileTargeting;
            if (targetWeaponManager.advancedMissileTargeting)
            {
                adVStyle = targetWeaponManager.advancedMissileTgtByYield ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle;
                if (GUI.Button(SRect(height += buttonHeight, 30, 60), StringUtils.Localize("#LOC_BDArmory_MultiTargetMslYield_Config"), adVStyle))
                    targetWeaponManager.advancedMissileTgtByYield = !targetWeaponManager.advancedMissileTgtByYield;
                if (targetWeaponManager.advancedMissileTgtByYield)
                {
                    height += buttonGap;
                    GUI.Label(SRect(height += buttonHeight), StringUtils.Localize("#LOC_BDArmory_YieldPerTarget") + ": " + StringUtils.Localize("#LOC_BDArmory_Air"), labelStyle);
                    GUI.HorizontalSlider(SRect(height, 100, 110), targetWeaponManager.maxTNTOnTargetAir, 0, 1000);
                    height += buttonGap;

                    GUI.Label(SRect(height += buttonHeight), StringUtils.Localize("#LOC_BDArmory_YieldPerTarget") + ": " + StringUtils.Localize("#LOC_BDArmory_Surface"), labelStyle);
                    GUI.HorizontalSlider(SRect(height, 100, 110), targetWeaponManager.maxTNTOnTargetSrf, 0, 1000);
                    height += buttonGap;

                    GUI.Label(SRect(height += buttonHeight), StringUtils.Localize("#LOC_BDArmory_YieldPerTarget") + ": " + StringUtils.Localize("#LOC_BDArmory_Sea"), labelStyle);
                    GUI.HorizontalSlider(SRect(height, 100, 110), targetWeaponManager.maxTNTOnTargetSea, 0, 1000);
                    height += buttonGap;

                    GUI.Label(SRect(height += buttonHeight), StringUtils.Localize("#LOC_BDArmory_YieldPerTarget") + ": " + StringUtils.Localize("#LOC_BDArmory_Missile"), labelStyle);
                    GUI.HorizontalSlider(SRect(height, 100, 110), targetWeaponManager.maxTNTOnTargetMsl, 0, 1000);
                    height += buttonGap;
                }
                else
                {
                    height += buttonGap;
                    GUI.Label(SRect(height += buttonHeight), StringUtils.Localize("#LOC_BDArmory_MissilesOnTarget") + ": " + StringUtils.Localize("#LOC_BDArmory_Air"), labelStyle);
                    GUI.HorizontalSlider(SRect(height, 100, 110), targetWeaponManager.maxMissilesOnTargetAir, 0, 10);
                    height += buttonGap;

                    GUI.Label(SRect(height += buttonHeight), StringUtils.Localize("#LOC_BDArmory_MissilesOnTarget") + ": " + StringUtils.Localize("#LOC_BDArmory_Surface"), labelStyle);
                    GUI.HorizontalSlider(SRect(height, 100, 110), targetWeaponManager.maxMissilesOnTargetSrf, 0, 10);
                    height += buttonGap;

                    GUI.Label(SRect(height += buttonHeight), StringUtils.Localize("#LOC_BDArmory_MissilesOnTarget") + ": " + StringUtils.Localize("#LOC_BDArmory_Sea"), labelStyle);
                    GUI.HorizontalSlider(SRect(height, 100, 110), targetWeaponManager.maxMissilesOnTargetSea, 0, 10);
                    height += buttonGap;

                    GUI.Label(SRect(height += buttonHeight), StringUtils.Localize("#LOC_BDArmory_MissilesOnTarget") + ": " + StringUtils.Localize("#LOC_BDArmory_Missile"), labelStyle);
                    GUI.HorizontalSlider(SRect(height, 100, 110), targetWeaponManager.maxMissilesOnTargetMsl, 0, 10);
                    height += buttonGap;
                    targetWeaponManager.maxMissilesOnTarget = Mathf.Max(targetWeaponManager.maxMissilesOnTargetAir, targetWeaponManager.maxMissilesOnTargetSrf, targetWeaponManager.maxMissilesOnTargetSea, targetWeaponManager.maxMissilesOnTargetMsl);
                }
            }
            GUIUtils.RepositionWindow(ref window);
            GUIUtils.UseMouseEventInRect(window);
        }

        protected virtual void OnGUI()
        {
            if (!BDArmorySetup.GAME_UI_ENABLED) return;
            if (ready)
            {
                if (!open) return;

                var clientRect = new Rect(
                    Mathf.Min(windowLocation.x, Screen.width - width),
                    Mathf.Min(windowLocation.y, Screen.height - height),
                    width,
                    height);
                BDArmorySetup.SetGUIOpacity();
                if (BDArmorySettings.UI_SCALE_ACTUAL != 1) GUIUtility.ScaleAroundPivot(BDArmorySettings.UI_SCALE_ACTUAL * Vector2.one, clientRect.position);
                window = GUI.Window(10591029, clientRect, TargetingSelectorWindow, "", BDArmorySetup.BDGuiSkin.window);
                BDArmorySetup.SetGUIOpacity(false);
                GUIUtils.UpdateGUIRect(window, guiCheckIndex);
            }
        }

        private void Awake()
        {
            if (Instance)
                Destroy(Instance);
            Instance = this;
        }

        private void Start()
        {
            StartCoroutine(WaitForBdaSettings());
        }

        private void OnDestroy()
        {
            ready = false;
        }

        private IEnumerator WaitForBdaSettings()
        {
            yield return new WaitUntil(() => BDArmorySetup.Instance is not null);

            ready = true;
            if (guiCheckIndex < 0) guiCheckIndex = GUIUtils.RegisterGUIRect(new Rect());
        }
    }
}
