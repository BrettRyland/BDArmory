using System.Collections;
using UnityEngine;

using BDArmory.Competition;
using BDArmory.Control;
using BDArmory.Settings;
using BDArmory.Utils;
using System;

namespace BDArmory.UI
{
    [KSPAddon(KSPAddon.Startup.FlightAndEditor, false)]
    public class BDTeamSelector : MonoBehaviour
    {
        public static BDTeamSelector Instance;

        const float width = 250;
        const float margin = 5;
        const float buttonHeight = 20;
        const float buttonGap = 2;
        const float newTeamButtonWidth = 40;
        const float scrollWidth = 20;

        private static int guiCheckIndex = -1;
        private bool ready = false;
        private bool open = false;
        private Rect window;
        private float height;
        private bool scrollable;
        private Vector2 scrollPosition = Vector2.zero;
        Rect alliesRect;
        GUIStyle alliesStyle;

        private Vector2 windowLocation;
        private MissileFire targetWeaponManager;
        private string newTeamName = string.Empty;

        public void Open(MissileFire weaponManager, Vector2 position)
        {
            targetWeaponManager = weaponManager;
            newTeamName = string.Empty;
            windowLocation = position;
            SetVisible(true);
        }

        public void SetVisible(bool visible)
        {
            open = visible;
            GUIUtils.SetGUIRectVisible(guiCheckIndex, visible);
            if (visible)
            {
                window = new Rect(
                 Math.Min(windowLocation.x, Screen.width - (scrollable ? width + scrollWidth : width)),
                 Math.Min(windowLocation.y, Screen.height - height),
                 width,
                 scrollable ? Screen.height / 2 + buttonHeight + buttonGap + 2 * margin : height
                 );
                alliesStyle ??= new GUIStyle(BDArmorySetup.BDGuiSkin.label) { wordWrap = true, alignment = TextAnchor.UpperLeft, fixedWidth = width };
            }
        }

        private void TeamSelectorWindow(int id)
        {
            height = margin;
            GUI.Label(new Rect(margin, height, width - 2 * margin, buttonHeight), StringUtils.Localize("#LOC_BDArmory_SelectTeam"), BDArmorySetup.BDGuiSkin.label);
            if (GUI.Button(new Rect(width - 18, 2, 18, 18), " X", BDArmorySetup.CloseButtonStyle))
            {
                SetVisible(false);
            }
            height += buttonHeight;
            // Team input field
            newTeamName = GUI.TextField(new Rect(margin, height, width - buttonGap - 2 * margin - newTeamButtonWidth, buttonHeight), newTeamName, 30);

            // New team button
            Rect newTeamButtonRect = new Rect(width - margin - newTeamButtonWidth, height, newTeamButtonWidth, buttonHeight);
            if (GUI.Button(newTeamButtonRect, StringUtils.Localize("#LOC_BDArmory_Generic_New"), BDArmorySetup.ButtonStyle))//"New"
            {
                if (!string.IsNullOrEmpty(newTeamName.Trim()))
                {
                    targetWeaponManager.SetTeam(BDTeam.Get(newTeamName.Trim()));
                    newTeamName = string.Empty;
                }
            }

            height += buttonHeight;

            // Scrollable list of existing teams
            scrollable = (BDArmorySetup.Instance.Teams.Count * (buttonHeight + buttonGap) * 2 > Screen.height);

            if (scrollable)
                scrollPosition = GUI.BeginScrollView(
                    new Rect(margin, height, width - margin * 2 + scrollWidth, Screen.height / 2),
                    scrollPosition,
                    new Rect(margin, height, width - margin * 2, BDArmorySetup.Instance.Teams.Count * (buttonHeight + buttonGap)),
                    false, true);

            using (var teams = BDArmorySetup.Instance.Teams.Values.GetEnumerator())
                while (teams.MoveNext())
                {
                    if (teams.Current == null || !teams.Current.Name.ToLowerInvariant().StartsWith(newTeamName.ToLowerInvariant().Trim())) continue;

                    height += buttonGap;
                    Rect buttonRect = new Rect(margin, height, width - buttonHeight - 4 * margin, buttonHeight);
                    GUIStyle buttonStyle = (teams.Current == targetWeaponManager.Team) ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle;

                    if (GUI.Button(buttonRect, teams.Current.Name + (teams.Current.Neutral ? (teams.Current.Name != "Neutral" ? "(Neutral)" : "") : ""), buttonStyle))
                    {
                        switch (Event.current.button)
                        {
                            case 1: // right click
                                if (teams.Current.Name != "Neutral" && teams.Current.Name != "A" && teams.Current.Name != "B")
                                    teams.Current.Neutral = !teams.Current.Neutral;
                                break;
                            default:
                                targetWeaponManager.SetTeam(teams.Current);
                                if (targetWeaponManager.Team.Allies.Contains(teams.Current.Name))
                                    targetWeaponManager.Team.Allies.Remove(teams.Current.Name);
                                break;
                        }
                    }
                    if (teams.Current.Name != "Neutral" && teams.Current.Name != "A" && teams.Current.Name != "B" && !teams.Current.Neutral && teams.Current != targetWeaponManager.Team)
                    {
                        if (GUI.Button(new Rect(width - buttonHeight, height, buttonHeight - 2 * margin, buttonHeight), "[A]", (targetWeaponManager.Team.Allies.Contains(teams.Current.Name)) ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))
                        {
                            switch (Event.current.button)
                            {
                                default:
                                    targetWeaponManager.SetTeam(targetWeaponManager.Team);
                                    if (targetWeaponManager.Team.Allies.Contains(teams.Current.Name))
                                        targetWeaponManager.Team.Allies.Remove(teams.Current.Name);
                                    else targetWeaponManager.Team.Allies.Add(teams.Current.Name);
                                    break;
                            }
                        }
                    }
                    height += buttonHeight;
                }
            GUI.Label(new Rect(margin, height, width - 2 * margin, buttonHeight), StringUtils.Localize("#LOC_BDArmory_Allies"));
            height += buttonHeight + buttonGap;
            string allies = string.Join("; ", targetWeaponManager.Team.Allies);
            alliesRect = new Rect(margin, height, width - 2 * margin, buttonHeight)
            { height = alliesStyle.CalcHeight(new GUIContent(allies), width) };
            GUI.Label(alliesRect, allies, alliesStyle);
            height += alliesRect.height;
            if (scrollable)
                GUI.EndScrollView();

            // Buttons
            if (Event.current.type == EventType.KeyUp)
            {
                if ((Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) && !string.IsNullOrEmpty(newTeamName.Trim()))
                {
                    targetWeaponManager.SetTeam(BDTeam.Get(newTeamName.Trim()));
                    newTeamName = string.Empty;
                }
                else if (Event.current.keyCode == KeyCode.Escape)
                {
                    SetVisible(false);
                }
            }

            height += margin;
            window.height = scrollable ? Screen.height / 2 + buttonHeight + buttonGap + 2 * margin : height;
            GUIUtils.DragWindow();
            GUIUtils.RepositionWindow(ref window);
            GUIUtils.UseMouseEventInRect(window);
        }

        protected virtual void OnGUI()
        {
            if (ready)
            {
                if (open && BDArmorySetup.GAME_UI_ENABLED
                    && Event.current.type == EventType.MouseDown
                    && !window.Contains(Event.current.mousePosition))
                {
                    SetVisible(false);
                }

                if (open && BDArmorySetup.GAME_UI_ENABLED)
                {
                    var clientRect = new Rect(
                        Math.Min(windowLocation.x, Screen.width - (scrollable ? width + scrollWidth : width)),
                        Math.Min(windowLocation.y, Screen.height - height),
                        width,
                        scrollable ? Screen.height / 2 + buttonHeight + buttonGap + 2 * margin : height);
                    if (BDArmorySettings.UI_SCALE_ACTUAL != 1) GUIUtility.ScaleAroundPivot(BDArmorySettings.UI_SCALE_ACTUAL * Vector2.one, clientRect.position);
                    window = GUI.Window(10591029, clientRect, TeamSelectorWindow, "", BDArmorySetup.BDGuiSkin.window);
                    GUIUtils.UpdateGUIRect(window, guiCheckIndex);
                }
                else
                {
                    GUIUtils.UpdateGUIRect(new Rect(), guiCheckIndex);
                }
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
