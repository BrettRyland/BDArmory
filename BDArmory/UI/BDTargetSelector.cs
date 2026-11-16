using System.Collections;
using UnityEngine;

using BDArmory.Control;
using BDArmory.Settings;
using BDArmory.Utils;

namespace BDArmory.UI
{
    [KSPAddon(KSPAddon.Startup.FlightAndEditor, false)]
    public class BDTargetSelector : MonoBehaviour
    {
        public static BDTargetSelector Instance;

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

        private void TargetingSelectorWindow(int id)
        {
            height = margin;
            GUIStyle labelStyle = BDArmorySetup.BDGuiSkin.label;
            GUI.Label(new Rect(margin, height, width - 2 * margin, buttonHeight), StringUtils.Localize("#LOC_BDArmory_Selecttargeting"), labelStyle);
            if (GUI.Button(new Rect(width - 18, 2, 16, 16), " X", BDArmorySetup.CloseButtonStyle))
            {
                SetVisible(false);
            }
            height += buttonHeight;

            height += buttonGap;
            Rect CoMRect = new Rect(margin, height, width - 2 * margin, buttonHeight);
            GUIStyle CoMStyle = targetWeaponManager.targetCoM ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle;
            //FIXME - switch these over to toggles instead of buttons; identified issue with weapon/engine targeting no sawing?
            if (GUI.Button(CoMRect, StringUtils.Localize("#LOC_BDArmory_TargetCOM"), CoMStyle))
            {
                targetWeaponManager.targetCoM = !targetWeaponManager.targetCoM;
                if (targetWeaponManager.targetCoM)
                {
                    targetWeaponManager.targetCommand = false;
                    targetWeaponManager.targetEngine = false;
                    targetWeaponManager.targetWeapon = false;
                    targetWeaponManager.targetMass = false;
                    targetWeaponManager.targetRandom = false;
                }
                if (!targetWeaponManager.targetCoM && (!targetWeaponManager.targetWeapon && !targetWeaponManager.targetEngine && !targetWeaponManager.targetCommand && !targetWeaponManager.targetMass && !targetWeaponManager.targetRandom))
                {
                    targetWeaponManager.targetRandom = true;
                }
            }
            height += buttonHeight;

            height += buttonGap;
            Rect MassRect = new Rect(margin, height, width - 2 * margin, buttonHeight);
            GUIStyle MassStyle = targetWeaponManager.targetMass ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle;

            if (GUI.Button(MassRect, StringUtils.Localize("#LOC_BDArmory_Mass"), MassStyle))
            {
                targetWeaponManager.targetMass = !targetWeaponManager.targetMass;
                if (targetWeaponManager.targetMass)
                {
                    targetWeaponManager.targetCoM = false;
                }
                if (!targetWeaponManager.targetCoM && (!targetWeaponManager.targetWeapon && !targetWeaponManager.targetEngine && !targetWeaponManager.targetCommand && !targetWeaponManager.targetMass && !targetWeaponManager.targetRandom))
                {
                    targetWeaponManager.targetCoM = true;
                }
            }
            height += buttonHeight;

            height += buttonGap;
            Rect CommandRect = new Rect(margin, height, width - 2 * margin, buttonHeight);
            GUIStyle CommandStyle = targetWeaponManager.targetCommand ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle;

            if (GUI.Button(CommandRect, StringUtils.Localize("#LOC_BDArmory_Command"), CommandStyle))
            {
                targetWeaponManager.targetCommand = !targetWeaponManager.targetCommand;
                if (targetWeaponManager.targetCommand)
                {
                    targetWeaponManager.targetCoM = false;
                }
                if (!targetWeaponManager.targetCoM && (!targetWeaponManager.targetWeapon && !targetWeaponManager.targetEngine && !targetWeaponManager.targetCommand && !targetWeaponManager.targetMass && !targetWeaponManager.targetRandom))
                {
                    targetWeaponManager.targetCoM = true;
                }
            }
            height += buttonHeight;

            height += buttonGap;
            Rect EngineRect = new Rect(margin, height, width - 2 * margin, buttonHeight);
            GUIStyle EngineStyle = targetWeaponManager.targetEngine ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle;

            if (GUI.Button(EngineRect, StringUtils.Localize("#LOC_BDArmory_Engines"), EngineStyle))
            {
                targetWeaponManager.targetEngine = !targetWeaponManager.targetEngine;
                if (targetWeaponManager.targetEngine)
                {
                    targetWeaponManager.targetCoM = false;
                }
                if (!targetWeaponManager.targetCoM && (!targetWeaponManager.targetWeapon && !targetWeaponManager.targetEngine && !targetWeaponManager.targetCommand && !targetWeaponManager.targetMass && !targetWeaponManager.targetRandom))
                {
                    targetWeaponManager.targetCoM = true;
                }
            }
            height += buttonHeight;

            height += buttonGap;
            Rect weaponRect = new Rect(margin, height, width - 2 * margin, buttonHeight);
            GUIStyle WepStyle = targetWeaponManager.targetWeapon ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle;

            if (GUI.Button(weaponRect, StringUtils.Localize("#LOC_BDArmory_Weapons"), WepStyle))
            {
                targetWeaponManager.targetWeapon = !targetWeaponManager.targetWeapon;
                if (targetWeaponManager.targetWeapon)
                {
                    targetWeaponManager.targetCoM = false;
                }
                if (!targetWeaponManager.targetCoM && (!targetWeaponManager.targetWeapon && !targetWeaponManager.targetEngine && !targetWeaponManager.targetCommand && !targetWeaponManager.targetMass && !targetWeaponManager.targetRandom))
                {
                    targetWeaponManager.targetCoM = true;
                }
            }
            height += buttonHeight;

            height += buttonGap;
            Rect RNGRect = new Rect(margin, height, width - 2 * margin, buttonHeight);
            GUIStyle RNGStyle = targetWeaponManager.targetWeapon ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle;

            if (GUI.Button(RNGRect, StringUtils.Localize("#LOC_BDArmory_Random"), RNGStyle))
            {
                targetWeaponManager.targetRandom = !targetWeaponManager.targetRandom;
                if (targetWeaponManager.targetRandom)
                {
                    targetWeaponManager.targetCoM = false;
                }
                if (!targetWeaponManager.targetCoM && (!targetWeaponManager.targetWeapon && !targetWeaponManager.targetEngine && !targetWeaponManager.targetCommand && !targetWeaponManager.targetMass && !targetWeaponManager.targetRandom))
                {
                    targetWeaponManager.targetCoM = true;
                }
            }
            height += buttonHeight;

            height += margin;
            targetWeaponManager.targetingString = (targetWeaponManager.targetCoM ? StringUtils.Localize("#LOC_BDArmory_TargetCOM") + "; " : "")
                + (targetWeaponManager.targetMass ? StringUtils.Localize("#LOC_BDArmory_Mass") + "; " : "")
                + (targetWeaponManager.targetCommand ? StringUtils.Localize("#LOC_BDArmory_Command") + "; " : "")
                + (targetWeaponManager.targetEngine ? StringUtils.Localize("#LOC_BDArmory_Engines") + "; " : "")
                + (targetWeaponManager.targetWeapon ? StringUtils.Localize("#LOC_BDArmory_Weapons") + "; " : "")
                +(targetWeaponManager.targetRandom ? StringUtils.Localize("#LOC_BDArmory_Random") + "; " : "");
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
                    Math.Min(windowLocation.x, Screen.width - width),
                    Math.Min(windowLocation.y, Screen.height - height),
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
