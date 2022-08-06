using System;
using System.Collections;
using System.Collections.Generic;
using UniLinq;
using UnityEngine;
using KSP.Localization;

using BDArmory.Competition;
using BDArmory.Targeting;
using BDArmory.UI;
using BDArmory.Utils;

namespace BDArmory.Control
{
    public class ModuleWingCommander : PartModule
    {
        public MissileFire weaponManager;

        public List<IBDAIControl> friendlies;

        List<IBDAIControl> wingmen;
        [KSPField(isPersistant = true)] public string savedWingmen = string.Empty;

        //[KSPField(guiActive = false, guiActiveEditor = false, guiName = "")]
        public string guiTitle = "WingCommander:";

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_WingCommander_Guiname1"), UI_FloatRange(minValue = 20f, maxValue = 200f, stepIncrement = 1, scene = UI_Scene.Editor)]//Formation Spread
        public float spread = 100;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_WingCommander_Guiname2"), UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 1, scene = UI_Scene.Editor)]//Formation Lag
        public float lag = 7;

        [KSPField(isPersistant = true)] public bool commandSelf;

        List<GPSTargetInfo> commandedPositions;
        bool drawMouseDiamond;

        ScreenMessage screenMessage;

        //int focusIndex = 0;
        List<int> focusIndexes;

        [KSPEvent(guiActive = true, guiName = "#LOC_BDArmory_WingCommander_Guiname3")]//ToggleGUI
        public void ToggleGUI()
        {
            showGUI = !showGUI;
            if (!showGUI) return;
            RefreshFriendlies();

            //TEMPORARY
            wingmen = new List<IBDAIControl>();
            List<IBDAIControl>.Enumerator ps = friendlies.GetEnumerator();
            while (ps.MoveNext())
            {
                if (ps.Current == null) continue;
                wingmen.Add(ps.Current);
            }
            ps.Dispose();
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            if (HighLogic.LoadedSceneIsFlight)
            {
                focusIndexes = new List<int>();
                commandedPositions = new List<GPSTargetInfo>();
                part.force_activate();

                StartCoroutine(StartupRoutine());

                GameEvents.onGameStateSave.Add(SaveWingmen);
                GameEvents.onVesselLoaded.Add(OnVesselLoaded);
                GameEvents.onVesselDestroy.Add(OnVesselLoaded);
                GameEvents.onVesselGoOnRails.Add(OnVesselLoaded);
                MissileFire.OnChangeTeam += OnToggleTeam;

                screenMessage = new ScreenMessage("", 2, ScreenMessageStyle.LOWER_CENTER);
            }
        }

        void OnToggleTeam(MissileFire mf, BDTeam team)
        {
            RefreshFriendlies();
            RefreshWingmen();
        }

        IEnumerator StartupRoutine()
        {
            while (vessel.packed)
            {
                yield return null;
            }

            weaponManager = part.FindModuleImplementing<MissileFire>();

            RefreshFriendlies();
            RefreshWingmen();
            LoadWingmen();
        }

        void OnDestroy()
        {
            GameEvents.onGameStateSave.Remove(SaveWingmen);
            GameEvents.onVesselLoaded.Remove(OnVesselLoaded);
            GameEvents.onVesselDestroy.Remove(OnVesselLoaded);
            GameEvents.onVesselGoOnRails.Remove(OnVesselLoaded);
            MissileFire.OnChangeTeam -= OnToggleTeam;
        }

        void OnVesselLoaded(Vessel v)
        {
            if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && !vessel.packed)
            {
                RefreshFriendlies();
                RefreshWingmen();
            }
        }

        void RefreshFriendlies()
        {
            if (!weaponManager) return;
            friendlies = new List<IBDAIControl>();
            using (var vs = BDATargetManager.LoadedVessels.GetEnumerator())
                while (vs.MoveNext())
                {
                    if (vs.Current == null) continue;
                    if (!vs.Current.loaded || vs.Current == vessel || VesselModuleRegistry.ignoredVesselTypes.Contains(vs.Current.vesselType)) continue;

                    IBDAIControl pilot = VesselModuleRegistry.GetIBDAIControl(vs.Current, true);
                    if (pilot == null) continue;
                    MissileFire wm = VesselModuleRegistry.GetMissileFire(vs.Current, true);
                    if (wm == null || wm.Team != weaponManager.Team) continue;
                    friendlies.Add(pilot);
                }

            //TEMPORARY
            wingmen = new List<IBDAIControl>();
            using (var fs = friendlies.GetEnumerator())
                while (fs.MoveNext())
                {
                    if (fs.Current == null) continue;
                    wingmen.Add(fs.Current);
                }
        }

        void RefreshWingmen()
        {
            if (wingmen == null)
            {
                wingmen = new List<IBDAIControl>();
                //focusIndex = 0;
                focusIndexes.Clear();
                return;
            }
            wingmen.RemoveAll(w => w == null || (w.weaponManager && w.weaponManager.Team != weaponManager.Team));

            List<int> uniqueIndexes = new List<int>();
            List<int>.Enumerator fIndexes = focusIndexes.GetEnumerator();
            while (fIndexes.MoveNext())
            {
                int clampedIndex = Mathf.Clamp(fIndexes.Current, 0, wingmen.Count - 1);
                if (!uniqueIndexes.Contains(clampedIndex))
                {
                    uniqueIndexes.Add(clampedIndex);
                }
            }
            fIndexes.Dispose();
            focusIndexes = new List<int>(uniqueIndexes);
        }

        void SaveWingmen(ConfigNode cfg)
        {
            if (wingmen == null)
            {
                return;
            }

            savedWingmen = string.Empty;
            List<IBDAIControl>.Enumerator pilots = wingmen.GetEnumerator();
            while (pilots.MoveNext())
            {
                if (pilots.Current == null) continue;
                savedWingmen += pilots.Current.vessel.id + ",";
            }
            pilots.Dispose();
        }

        void LoadWingmen()
        {
            wingmen = new List<IBDAIControl>();

            if (savedWingmen == string.Empty) return;
            IEnumerator<string> wingIDs = savedWingmen.Split(new char[] { ',' }).AsEnumerable().GetEnumerator();
            while (wingIDs.MoveNext())
            {
                using (var vs = BDATargetManager.LoadedVessels.GetEnumerator())
                    while (vs.MoveNext())
                    {
                        if (vs.Current == null || !vs.Current.loaded || VesselModuleRegistry.ignoredVesselTypes.Contains(vs.Current.vesselType)) continue;

                        if (vs.Current.id.ToString() != wingIDs.Current) continue;
                        var pilot = VesselModuleRegistry.GetIBDAIControl(vs.Current, true);
                        if (pilot != null) wingmen.Add(pilot);
                    }
            }
            wingIDs.Dispose();
        }

        public bool showGUI;
        bool rectInit;
        float buttonStartY = 30;
        float buttonHeight = 24;
        float buttonGap = 3;
        float margin = 6;
        private float windowWidth = 240;
        private float windowHeight = 100;
        float buttonWidth;
        float buttonEndY;
        GUIStyle wingmanButtonStyle;
        GUIStyle wingmanButtonSelectedStyle;

        void OnGUI()
        {
            if (!HighLogic.LoadedSceneIsFlight || !vessel || !vessel.isActiveVessel || vessel.packed) return;
            if (!BDArmorySetup.GAME_UI_ENABLED) return;
            if (showGUI)
            {
                if (!rectInit)
                {
                    // this Rect initialization ensures any save issues with height or width of the window are resolved
                    BDArmorySetup.WindowRectWingCommander = new Rect(BDArmorySetup.WindowRectWingCommander.x, BDArmorySetup.WindowRectWingCommander.y, windowWidth, windowHeight);
                    buttonWidth = BDArmorySetup.WindowRectWingCommander.width - (2 * margin);
                    buttonEndY = buttonStartY;
                    wingmanButtonStyle = new GUIStyle(BDArmorySetup.BDGuiSkin.button);
                    wingmanButtonStyle.alignment = TextAnchor.MiddleLeft;
                    wingmanButtonStyle.wordWrap = false;
                    wingmanButtonStyle.fontSize = 11;
                    wingmanButtonSelectedStyle = new GUIStyle(BDArmorySetup.BDGuiSkin.box);
                    wingmanButtonSelectedStyle.alignment = TextAnchor.MiddleLeft;
                    wingmanButtonSelectedStyle.wordWrap = false;
                    wingmanButtonSelectedStyle.fontSize = 11;
                    rectInit = true;
                }
                // this Rect initialization ensures any save issues with height or width of the window are resolved
                //BDArmorySetup.WindowRectWingCommander = new Rect(BDArmorySetup.WindowRectWingCommander.x, BDArmorySetup.WindowRectWingCommander.y, windowWidth, windowHeight);
                BDArmorySetup.SetGUIOpacity();
                BDArmorySetup.WindowRectWingCommander = GUI.Window(1293293, BDArmorySetup.WindowRectWingCommander, WingmenWindow, Localizer.Format("#LOC_BDArmory_WingCommander_Title"),//"WingCommander"
                    BDArmorySetup.BDGuiSkin.window);
                BDArmorySetup.SetGUIOpacity(false);

                if (showAGWindow) AGWindow();
            }

            //command position diamonds
            float diamondSize = 24;
            List<GPSTargetInfo>.Enumerator comPos = commandedPositions.GetEnumerator();
            while (comPos.MoveNext())
            {
                GUIUtils.DrawTextureOnWorldPos(comPos.Current.worldPos, BDArmorySetup.Instance.greenDiamondTexture,
                    new Vector2(diamondSize, diamondSize), 0);
                Vector2 labelPos;
                if (!GUIUtils.WorldToGUIPos(comPos.Current.worldPos, out labelPos)) continue;
                labelPos.x += diamondSize / 2;
                labelPos.y -= 10;
                GUI.Label(new Rect(labelPos.x, labelPos.y, 300, 20), comPos.Current.name);
            }
            comPos.Dispose();

            if (!drawMouseDiamond) return;
            Vector2 mouseDiamondPos = Input.mousePosition;
            Rect mouseDiamondRect = new Rect(mouseDiamondPos.x - (diamondSize / 2),
                Screen.height - mouseDiamondPos.y - (diamondSize / 2), diamondSize, diamondSize);
            GUI.DrawTexture(mouseDiamondRect, BDArmorySetup.Instance.greenDiamondTexture,
                ScaleMode.StretchToFill, true);
        }

        delegate void CommandFunction(IBDAIControl wingman, int index, object data);

        void WingmenWindow(int windowID)
        {
            float height = buttonStartY;
            GUI.DragWindow(new Rect(0, 0, BDArmorySetup.WindowRectWingCommander.width - buttonStartY - margin - margin, buttonStartY));

            //close buttton
            float xSize = buttonStartY - margin - margin;
            if (GUI.Button(new Rect(buttonWidth + (2 * buttonGap) - xSize, margin, xSize, xSize), "X",
                BDArmorySetup.BDGuiSkin.button))
            {
                showGUI = false;
            }

            GUI.Box(
                new Rect(margin - buttonGap, buttonStartY - buttonGap, buttonWidth + (2 * buttonGap),
                    Mathf.Max(wingmen.Count * (buttonHeight + buttonGap), 10)), GUIContent.none, BDArmorySetup.BDGuiSkin.box);
            buttonEndY = buttonStartY;
            for (int i = 0; i < wingmen.Count; i++)
            {
                WingmanButton(i, out buttonEndY);
            }
            buttonEndY = Mathf.Max(buttonEndY, 15f);
            height += buttonEndY;

            //command buttons
            float commandButtonLine = 0;
            CommandButton(SelectAll, Localizer.Format("#LOC_BDArmory_WingCommander_SelectAll"), ref commandButtonLine, false, false);//"Select All"
            //commandButtonLine += 0.25f;

            commandSelf =
                GUI.Toggle(
                    new Rect(margin, margin + buttonEndY + (commandButtonLine * (buttonHeight + buttonGap)), buttonWidth,
                        buttonHeight), commandSelf, Localizer.Format("#LOC_BDArmory_WingCommander_CommandSelf"), BDArmorySetup.BDGuiSkin.toggle);//"Command Self"
            commandButtonLine++;

            commandButtonLine += 0.10f;

            CommandButton(CommandFollow, Localizer.Format("#LOC_BDArmory_WingCommander_Follow"), ref commandButtonLine, true, false);//"Follow"
            CommandButton(CommandFlyTo, Localizer.Format("#LOC_BDArmory_WingCommander_FlyToPos"), ref commandButtonLine, true, waitingForFlytoPos);//"Fly To Pos"
            CommandButton(CommandAttack, Localizer.Format("#LOC_BDArmory_WingCommander_AttackPos"), ref commandButtonLine, true, waitingForAttackPos);//"Attack Pos"
            CommandButton(OpenAGWindow, Localizer.Format("#LOC_BDArmory_WingCommander_ActionGroup"), ref commandButtonLine, false, showAGWindow);//"Action Group"
            CommandButton(CommandTakeOff, Localizer.Format("#LOC_BDArmory_WingCommander_TakeOff"), ref commandButtonLine, true, false);//"Take Off"
            commandButtonLine += 0.5f;
            CommandButton(CommandRelease, Localizer.Format("#LOC_BDArmory_WingCommander_Release"), ref commandButtonLine, true, false);//"Release"

            commandButtonLine += 0.5f;
            GUI.Label(
                new Rect(margin, buttonEndY + margin + (commandButtonLine * (buttonHeight + buttonGap)), buttonWidth, 20),
                Localizer.Format("#LOC_BDArmory_WingCommander_FormationSettings") + ":", BDArmorySetup.BDGuiSkin.label);//Formation Settings
            commandButtonLine++;
            GUI.Label(
                new Rect(margin, buttonEndY + margin + (commandButtonLine * (buttonHeight + buttonGap)), buttonWidth / 3, 20),
                Localizer.Format("#LOC_BDArmory_WingCommander_Spread") + ": " + spread.ToString("0"), BDArmorySetup.BDGuiSkin.label);//Spread
            spread =
                GUI.HorizontalSlider(
                    new Rect(margin + (buttonWidth / 3),
                        buttonEndY + margin + (commandButtonLine * (buttonHeight + buttonGap)), 2 * buttonWidth / 3, 20),
                    spread, 1f, 200f, BDArmorySetup.BDGuiSkin.horizontalSlider, BDArmorySetup.BDGuiSkin.horizontalSliderThumb);
            commandButtonLine++;
            GUI.Label(
                new Rect(margin, buttonEndY + margin + (commandButtonLine * (buttonHeight + buttonGap)), buttonWidth / 3, 20),
                Localizer.Format("#LOC_BDArmory_WingCommander_Lag") + ": " + lag.ToString("0"), BDArmorySetup.BDGuiSkin.label);//Lag
            lag =
                GUI.HorizontalSlider(
                    new Rect(margin + (buttonWidth / 3),
                        buttonEndY + margin + (commandButtonLine * (buttonHeight + buttonGap)), 2 * buttonWidth / 3, 20), lag,
                    0f, 100f, BDArmorySetup.BDGuiSkin.horizontalSlider, BDArmorySetup.BDGuiSkin.horizontalSliderThumb);
            commandButtonLine++;

            //resize window
            height += ((commandButtonLine - 1) * (buttonHeight + buttonGap));
            BDArmorySetup.WindowRectWingCommander.height = height;
            GUI.DragWindow(BDArmorySetup.WindowRectWingCommander);
            GUIUtils.RepositionWindow(ref BDArmorySetup.WindowRectWingCommander);
        }

        void WingmanButton(int index, out float buttonEndY)
        {
            int i = index;
            Rect buttonRect = new Rect(margin, buttonStartY + (i * (buttonHeight + buttonGap)), buttonWidth, buttonHeight);
            GUIStyle style = (focusIndexes.Contains(i)) ? wingmanButtonSelectedStyle : wingmanButtonStyle;
            string label = " " + wingmen[i].vessel.vesselName + " (" + wingmen[i].currentStatus + ")";
            if (GUI.Button(buttonRect, label, style))
            {
                if (focusIndexes.Contains(i))
                {
                    focusIndexes.Remove(i);
                }
                else
                {
                    focusIndexes.Add(i);
                }
            }
            buttonEndY = buttonStartY + ((i + 1.5f) * buttonHeight);
        }

        void CommandButton(CommandFunction func, string buttonLabel, ref float buttonLine, bool sendToWingmen,
            bool pressed, object data = null)
        {
            CommandButton(func, buttonLabel, ref buttonLine, buttonEndY, margin, buttonGap, buttonWidth, buttonHeight,
                sendToWingmen, pressed, data);
        }

        void CommandButton(CommandFunction func, string buttonLabel, ref float buttonLine, float startY, float margin,
            float buttonGap, float buttonWidth, float buttonHeight, bool sendToWingmen, bool pressed, object data)
        {
            float yPos = startY + margin + ((buttonHeight + buttonGap) * buttonLine);
            if (GUI.Button(new Rect(margin, yPos, buttonWidth, buttonHeight), buttonLabel,
                pressed ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))
            {
                if (sendToWingmen)
                {
                    if (wingmen.Count > 0)
                    {
                        List<int>.Enumerator index = focusIndexes.GetEnumerator();
                        while (index.MoveNext())
                        {
                            func(wingmen[index.Current], index.Current, data);
                        }
                        index.Dispose();
                    }

                    if (commandSelf)
                    {
                        using (var ai = VesselModuleRegistry.GetModules<IBDAIControl>(vessel).GetEnumerator())
                            while (ai.MoveNext())
                            {
                                func(ai.Current, -1, data); // Note: this commands *all* AIs on the vessel.
                            }
                    }
                }
                else
                {
                    func(null, -1, null);
                }
            }

            buttonLine++;
        }

        void CommandRelease(IBDAIControl wingman, int index, object data)
        {
            wingman.ReleaseCommand();
        }

        void CommandFollow(IBDAIControl wingman, int index, object data)
        {
            wingman.CommandFollow(this, index);
        }

        public void CommandAllFollow()
        {
            RefreshFriendlies();
            int i = 0;
            using (var wingman = friendlies.GetEnumerator())
                while (wingman.MoveNext())
                {
                    if (wingman.Current == null) continue;
                    wingman.Current.CommandFollow(this, i);
                    i++;
                }
        }

        void CommandAG(IBDAIControl wingman, int index, object ag)
        {
            //Debug.Log("object to string: "+ag.ToString());
            KSPActionGroup actionGroup = (KSPActionGroup)ag;
            //Debug.Log("ag to string: " + actionGroup.ToString());
            wingman.CommandAG(actionGroup);
        }

        void CommandTakeOff(IBDAIControl wingman, int index, object data)
        {
            wingman.CommandTakeOff();
        }

        void OpenAGWindow(IBDAIControl wingman, int index, object data)
        {
            showAGWindow = !showAGWindow;
        }

        public bool showAGWindow;
        float agWindowHeight = 10;
        public Rect agWindowRect;

        void AGWindow()
        {
            float width = 100;
            float buttonHeight = 20;
            float agMargin = 5;
            float newHeight = 0;
            agWindowRect = new Rect(BDArmorySetup.WindowRectWingCommander.x + BDArmorySetup.WindowRectWingCommander.width, BDArmorySetup.WindowRectWingCommander.y, width, agWindowHeight);
            GUI.Box(agWindowRect, string.Empty, BDArmorySetup.BDGuiSkin.window);
            GUI.BeginGroup(agWindowRect);
            newHeight += agMargin;
            GUIStyle titleStyle = new GUIStyle(BDArmorySetup.BDGuiSkin.label);
            titleStyle.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(agMargin, 5, width - (2 * agMargin), 20), Localizer.Format("#LOC_BDArmory_WingCommander_ActionGroups"), titleStyle);//"Action Groups"
            newHeight += 20;
            float startButtonY = newHeight;
            float buttonLine = 0;
            int i = -1;
            IEnumerator<KSPActionGroup> ag = Enum.GetValues(typeof(KSPActionGroup)).Cast<KSPActionGroup>().GetEnumerator();
            while (ag.MoveNext())
            {
                i++;
                if (i <= 1) continue;
                CommandButton(CommandAG, ag.Current.ToString(), ref buttonLine, startButtonY, agMargin, buttonGap,
                    width - (2 * agMargin), buttonHeight, true, false, ag.Current);
                newHeight += buttonHeight + buttonGap;
            }
            ag.Dispose();
            newHeight += agMargin;
            GUI.EndGroup();

            agWindowHeight = newHeight;
        }

        void SelectAll(IBDAIControl wingman, int index, object data)
        {
            for (int i = 0; i < wingmen.Count; i++)
            {
                if (!focusIndexes.Contains(i))
                {
                    focusIndexes.Add(i);
                }
            }
        }

        void CommandFlyTo(IBDAIControl wingman, int index, object data)
        {
            StartCoroutine(CommandPosition(wingman, PilotCommands.FlyTo));
        }

        void CommandAttack(IBDAIControl wingman, int index, object data)
        {
            StartCoroutine(CommandPosition(wingman, PilotCommands.Attack));
        }

        bool waitingForFlytoPos;
        bool waitingForAttackPos;

        IEnumerator CommandPosition(IBDAIControl wingman, PilotCommands command)
        {
            if (focusIndexes.Count == 0 && !commandSelf)
            {
                yield break;
            }

            DisplayScreenMessage(Localizer.Format("#LOC_BDArmory_WingCommander_ScreenMessage"));//"Select target coordinates.\nRight-click to cancel."

            if (command == PilotCommands.FlyTo)
            {
                waitingForFlytoPos = true;
            }
            else if (command == PilotCommands.Attack)
            {
                waitingForAttackPos = true;
            }

            yield return null;

            bool waitingForPos = true;
            drawMouseDiamond = true;
            while (waitingForPos)
            {
                if (Input.GetMouseButtonDown(1))
                {
                    break;
                }
                if (Input.GetMouseButtonDown(0))
                {
                    Vector3 mousePos = new Vector3(Input.mousePosition.x / Screen.width,
                        Input.mousePosition.y / Screen.height, 0);
                    Plane surfPlane = new Plane(vessel.upAxis,
                        vessel.transform.position - (vessel.altitude * vessel.upAxis));
                    Ray ray = FlightCamera.fetch.mainCamera.ViewportPointToRay(mousePos);
                    float dist;
                    if (surfPlane.Raycast(ray, out dist))
                    {
                        Vector3 worldPoint = ray.GetPoint(dist);
                        Vector3d gps = VectorUtils.WorldPositionToGeoCoords(worldPoint, vessel.mainBody);

                        if (command == PilotCommands.FlyTo)
                        {
                            wingman.CommandFlyTo(gps);
                        }
                        else if (command == PilotCommands.Attack)
                        {
                            wingman.CommandAttack(gps);
                        }

                        StartCoroutine(CommandPositionGUIRoutine(wingman, new GPSTargetInfo(gps, command.ToString())));
                    }

                    break;
                }
                yield return null;
            }

            waitingForAttackPos = false;
            waitingForFlytoPos = false;
            drawMouseDiamond = false;
            ScreenMessages.RemoveMessage(screenMessage);
        }

        IEnumerator CommandPositionGUIRoutine(IBDAIControl wingman, GPSTargetInfo tInfo)
        {
            //RemoveCommandPos(tInfo);
            commandedPositions.Add(tInfo);
            yield return new WaitForSeconds(0.25f);
            while (Vector3d.Distance(wingman.commandGPS, tInfo.gpsCoordinates) < 0.01f &&
                   (wingman.currentCommand == PilotCommands.Attack ||
                    wingman.currentCommand == PilotCommands.FlyTo))
            {
                yield return null;
            }
            RemoveCommandPos(tInfo);
        }

        void RemoveCommandPos(GPSTargetInfo tInfo)
        {
            commandedPositions.RemoveAll(t => t.EqualsTarget(tInfo));
        }

        void DisplayScreenMessage(string message)
        {
            if (BDArmorySetup.GAME_UI_ENABLED && vessel == FlightGlobals.ActiveVessel)
            {
                ScreenMessages.RemoveMessage(screenMessage);
                screenMessage.message = message;
                ScreenMessages.PostScreenMessage(screenMessage);
            }
        }
    }
}
