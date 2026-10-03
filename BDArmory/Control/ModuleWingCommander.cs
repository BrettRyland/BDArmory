using BDArmory.Competition;
using BDArmory.Extensions;
using BDArmory.ModIntegration;
using BDArmory.Settings;
using BDArmory.Targeting;
using BDArmory.UI;
using BDArmory.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


namespace BDArmory.Control
{
    public class ModuleWingCommander : BDAPartModule
    {
        public MissileFire WeaponManager
        {
            get
            {
                if (field == null || !field.IsPrimaryWM || field.vessel != vessel)
                    field = vessel && vessel.loaded ? vessel.ActiveController().WM : null;
                return field;
            }
        }

        public List<IBDAIControl> friendlies = []; // All the available wingmen.
        List<IBDAIControl> selectedWingmen = []; // The friendlies that are selected pending orders.
        public List<IBDAIControl> wingmen = []; // Wingmen are those that we have commanded to follow.
        Dictionary<int, List<IBDAIControl>> CtrlGroup; //TODO: save/load for persistent ctrlgroups
        Dictionary<string, bool> craftFilters = [];
        // [KSPField(isPersistant = true)] public string savedWingmen = string.Empty;

        public string guiTitle = "WingCommander:";

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_WingCommander_FormationSpread"), UI_FloatRange(minValue = 20f, maxValue = 200f, stepIncrement = 1, scene = UI_Scene.Editor)]//Formation Spread
        public float spread = 100;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_WingCommander_FormationLag"), UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 1, scene = UI_Scene.Editor)]//Formation Lag
        public float lag = 50;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_WingCommander_FormationLag"), UI_FloatSemiLogRange(minValue = 1000f, maxValue = 100000f, sigFig = 1.5f, withZero = false, reducedPrecisionAtMin = true, scene = UI_Scene.Editor)]//Formation Lag
        public float wingAttackRange = 10000;

        [KSPField(isPersistant = true)] public bool commandSelf;

        List<GPSTargetInfo> commandedPositions = [];
        bool drawMouseDiamond;
        ScreenMessage screenMessage;
        [KSPField(isPersistant = true)] public bool showSelectionBoxes = true;
        static int _guiCheckIndex = -1;

        [KSPEvent(guiActive = true, guiName = "#LOC_BDArmory_WingCommander_ToggleGUI")]//ToggleGUI
        public void ToggleGUI()
        {
            showGUI = !showGUI;
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (!HighLogic.LoadedSceneIsFlight) return;
            part.force_activate();

            StartCoroutine(StartupRoutine());

            // GameEvents.onGameStateSave.Add(SaveWingmen);
            GameEvents.onVesselLoaded.Add(OnVesselLoaded);
            GameEvents.onVesselDestroy.Add(OnVesselLoaded);
            GameEvents.onVesselGoOnRails.Add(OnVesselLoaded);
            GameEvents.onVesselPartCountChanged.Add(OnVesselPartCountChanged);
            MissileFire.OnChangeTeam += OnToggleTeam;

            screenMessage = new ScreenMessage("", 2, ScreenMessageStyle.LOWER_CENTER);
            //CtrlGroup = new Dictionary<int, List<IBDAIControl>>();
            CtrlGroup = [];
            for (int d = 0; d < 10; d++)
            {
                CtrlGroup.Add(d, []);
            }
            craftFilters.Add("Generic", false);
        }

        /// <summary>
        /// Stuff to do when *any* vessel changes team.
        /// </summary>
        void OnToggleTeam(MissileFire mf, BDTeam team)
        {
            RefreshFriendlies();
            var ac = ActiveController.GetActiveController(vessel);
            if (ac == null || mf != ac.WM) return; // It wasn't us that changed team.

            // While technically, if the leader switched to the same team at the same time, we'd still be on the same team, the simplest solution is to release anyway. They can issue a new command if needed.
            if (ac.AI != null) ac.AI.ReleaseCommand(); // Stop doing whatever the traitorous leader told us.

            // Release anyone we were commanding - they're now our enemies.
            foreach (var wingman in wingmen.Where(w => w != null).ToList())
                wingman.ReleaseCommand();
            wingmen.Clear();
        }

        IEnumerator StartupRoutine()
        {
            while (vessel.packed)
            {
                yield return null;
            }

            RefreshFriendlies();
            RefreshWingmen();
            // LoadWingmen();
        }

        void OnDestroy()
        {
            // GameEvents.onGameStateSave.Remove(SaveWingmen);
            GameEvents.onVesselLoaded.Remove(OnVesselLoaded);
            GameEvents.onVesselDestroy.Remove(OnVesselLoaded);
            GameEvents.onVesselGoOnRails.Remove(OnVesselLoaded);
            GameEvents.onVesselPartCountChanged.Remove(OnVesselPartCountChanged);
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

        void OnVesselPartCountChanged(Vessel v)
        {
            // Check if the vessel lost its WM and if so drop it.
            if (v != vessel) return;
            var ac = ActiveController.GetActiveController(v);
            if (ac.AI == null) return;
            if (ac.WM == null && ac.AI.commandLeader != null)
            {
                ac.AI.commandLeader.RefreshFriendlies();
                ac.AI.ReleaseCommand();
            }
        }

        void RefreshFriendlies()
        {
            var wm = WeaponManager;
            if (wm == null) // We're dead, abort!
            {
                friendlies.Clear();
                wingmen.Clear();
                selectedWingmen.Clear();
                return;
            }
            var previouslySelected = selectedWingmen.Where(ai => ai != null).ToList();
            friendlies.Clear();
            selectedWingmen.Clear();
            foreach (var v in BDATargetManager.LoadedVessels)
            {
                if (v == null) continue;
                if (!v.loaded || v == vessel || VesselModuleRegistry.IgnoredVesselTypes.Contains(v.vesselType)) continue;

                var ac = v.ActiveController();
                if (ac == null) continue; // Since this is called on vessel destroy, we need to check that the vessel module isn't null.
                if (ac.AI == null) continue;
                if (ac.WM == null || ac.WM.Team != wm.Team) continue;
                friendlies.Add(ac.AI);
                if (previouslySelected.Contains(ac.AI)) selectedWingmen.Add(ac.AI);
            }
        }

        /// <summary>
        /// Refresh the wingmen by removing any that are dead or traitors.
        /// Note: this doesn't add any new wingmen - they haven't been commanded to follow yet.
        /// </summary>
        void RefreshWingmen()
        {
            var wm = WeaponManager;
            if (wm == null) // We're dead, abort!
            {
                wingmen.Clear();
                return;
            }

            // Filter out dead allies and traitors.
            wingmen = [.. wingmen.Where(ai => ai != null && friendlies.Contains(ai))];
        }

        public void AddWingman(IBDAIControl ai)
        {
            if (ai == null) return;
            if (!wingmen.Contains(ai)) wingmen.Add(ai);
        }
        public void RemoveWingman(IBDAIControl ai)
        {
            if (ai == null) return;
            if (wingmen.Contains(ai)) wingmen.Remove(ai);
        }

        /* FIXME Temporarily disable load/save of wingmen (this doesn't seem to be functional anyway).
        void SaveWingmen(ConfigNode cfg)
        {
            if (wingmen == null)
            {
                return;
            }

            savedWingmen = string.Empty;
            using List<IBDAIControl>.Enumerator pilots = wingmen.GetEnumerator();
            while (pilots.MoveNext())
            {
                if (pilots.Current == null) continue;
                savedWingmen += pilots.Current.vessel.id + ",";
            }
        }

        void LoadWingmen()
        {
            wingmen = [];

            if (savedWingmen == string.Empty) return;
            using IEnumerator<string> wingIDs = savedWingmen.Split([',']).AsEnumerable().GetEnumerator();
            while (wingIDs.MoveNext())
            {
                using var vs = BDATargetManager.LoadedVessels.GetEnumerator();
                while (vs.MoveNext())
                {
                    if (vs.Current == null || !vs.Current.loaded || VesselModuleRegistry.IgnoredVesselTypes.Contains(vs.Current.vesselType)) continue;

                    if (vs.Current.id.ToString() != wingIDs.Current) continue;
                    var pilot = vs.Current.ActiveController().AI;
                    if (pilot != null) wingmen.Add(pilot);
                }
            }
        }
        */

        Coroutine boundingBoxRoutine = null;
        bool selectionBoxEnabled = false;

        void Update()
        {
            if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && !vessel.packed)
            {
                if (!MapView.MapIsEnabled && showGUI)
                {
                    if (boundingBoxRoutine == null)
                    {
                        if (selectionBoxEnabled && Input.GetMouseButtonDown(0) && !GUIUtils.CheckMouseIsOnGui()) //left-click
                        {
                            boundingBoxRoutine = StartCoroutine(BoundingBox());
                        }
                    }
                    if (selectionBoxEnabled && Input.GetMouseButtonDown(1)) //right-click
                    {
                        if (boundingBoxRoutine != null) StopCoroutine(boundingBoxRoutine);
                        selectionBoxEnabled = false;
                        selectionBoxActive = false;
                    }
                }
            }
        }
        Vector2 boxStartPos = Vector2.zero;
        Vector2 boxEndPos = Vector2.zero;
        bool selectionBoxActive = false;
        IEnumerator BoundingBox()
        {
            boxStartPos = new(Input.mousePosition.x / Screen.width, Input.mousePosition.y / Screen.height);
            boxEndPos = boxStartPos;
            selectionBoxActive = true;
            while (Input.GetMouseButton(0))
            {
                boxEndPos = new(Input.mousePosition.x / Screen.width, Input.mousePosition.y / Screen.height);
                yield return null;
            }
            selectionBoxActive = false;
            selectedWingmen.Clear();
            foreach (var friend in friendlies)
            {
                Vector3 screenPos = GUIUtils.GetMainCamera().WorldToViewportPoint(friend.vessel.CoM);
                if (screenPos.z < 0) continue; //behind camera
                if (screenPos.x != Mathf.Clamp01(screenPos.x) || screenPos.y != Mathf.Clamp01(screenPos.y)) continue; //off-screen
                if (screenPos.x < Mathf.Min(boxStartPos.x, boxEndPos.x) || screenPos.x > Mathf.Max(boxStartPos.x, boxEndPos.x)) continue;
                if (screenPos.y < Mathf.Min(boxStartPos.y, boxEndPos.y) || screenPos.y > Mathf.Max(boxStartPos.y, boxEndPos.y)) continue;
                if (!selectedWingmen.Contains(friend)) selectedWingmen.Add(friend);
            }
            selectionBoxEnabled = false;
            boundingBoxRoutine = null;
        }

        public bool showGUI
        {
            get; private set
            {
                field = value;
                if (_guiCheckIndex < 0) _guiCheckIndex = GUIUtils.RegisterGUIRect(new Rect());
                if (value)
                {
                    if (!guiInit)
                    {
                        windowSize = BDArmorySetup.WindowRectWingCommander.size;
                        wingmanButtonStyle = new(BDArmorySetup.ButtonStyle)
                        {
                            alignment = TextAnchor.MiddleLeft,
                            wordWrap = false,
                            fontSize = 11
                        };
                        wingmanButtonSelectedStyle = new(BDArmorySetup.SelectedButtonStyle)
                        {
                            alignment = wingmanButtonStyle.alignment,
                            wordWrap = wingmanButtonStyle.wordWrap,
                            fontSize = wingmanButtonStyle.fontSize
                        };
                        labelStyle = new(BDArmorySetup.BDGuiSkin.label) { alignment = TextAnchor.MiddleLeft };
                        formationLabelStyle = new(labelStyle) { alignment = TextAnchor.LowerCenter, wordWrap = false, clipping = TextClipping.Overflow };
                        sliderStyle = new(BDArmorySetup.BDGuiSkin.horizontalSlider) { margin = new(0, 0, 10, 0) }; // This centres the slider vertically for GUILayout.
                        sliderThumbStyle = new(BDArmorySetup.BDGuiSkin.horizontalSliderThumb);
                        guiInit = true;
                    }

                    RefreshFriendlies();
                }
                else
                {
                    GUIUtils.UpdateGUIRect(new Rect(), _guiCheckIndex);
                    showAGWindow = false;
                    showFormationWindow = false;
                    selectionBoxEnabled = false;
                    if (boundingBoxRoutine != null) StopCoroutine(boundingBoxRoutine);
                }
            }
        } = false;
        bool guiInit = false;
        float buttonHeight = 24;
        float margin = 6;
        bool resizingWindow = false;
        public bool autoResizingWindow = true;
        const float windowMinWidth = 308, windowMinHeight = 594; // The height needs to be set just right for the auto-resizing to adjust properly. FIXME Find a way to set this automatically.
        Vector2 windowSize = new(windowMinWidth, windowMinHeight);
        GUIStyle wingmanButtonStyle;
        GUIStyle wingmanButtonSelectedStyle;
        GUIStyle labelStyle, formationLabelStyle;
        GUIStyle sliderStyle, sliderThumbStyle;

        void OnGUI()
        {
            if (!HighLogic.LoadedSceneIsFlight || !vessel || !vessel.isActiveVessel || vessel.packed) return;
            if (!BDArmorySetup.GAME_UI_ENABLED) return;
            if (showGUI)
            {
                if (Event.current.type == EventType.MouseUp)
                {
                    if (resizingWindow) resizingWindow = false;
                    else if (resizingFormationWindow) resizingFormationWindow = false;
                    else if (formationDragIndex >= 0) formationDragIndex = -1;
                }
                BDArmorySetup.SetGUIOpacity();
                if (resizingWindow)
                {
                    windowSize.x = Mathf.Clamp(windowSize.x, windowMinWidth, Screen.width - BDArmorySetup.WindowRectWingCommander.x);
                    windowSize.y = Mathf.Clamp(windowSize.y, windowMinHeight, Screen.height - BDArmorySetup.WindowRectWingCommander.y);
                    BDArmorySetup.WindowRectWingCommander.size = windowSize;
                }
                else if (autoResizingWindow)
                {
                    BDArmorySetup.WindowRectWingCommander.height = Mathf.Min(windowMinHeight - 70 + Mathf.Clamp(Mathf.Min(friendlies.Count, filteredFriendlies) * 30, 30, 300), Screen.height - BDArmorySetup.WindowRectWingCommander.y);
                    windowSize.y = BDArmorySetup.WindowRectWingCommander.height;
                }
                var guiMatrix = GUI.matrix;
                if (BDArmorySettings.UI_SCALE_ACTUAL != 1) GUIUtility.ScaleAroundPivot(BDArmorySettings.UI_SCALE_ACTUAL * Vector2.one, BDArmorySetup.WindowRectWingCommander.position);
                BDArmorySetup.WindowRectWingCommander = GUI.Window(
                    GUIUtility.GetControlID(FocusType.Passive),
                    BDArmorySetup.WindowRectWingCommander,
                    WingmenWindow,
                    StringUtils.Localize("#LOC_BDArmory_WingCommander_Title"),//"WingCommander"
                    BDArmorySetup.BDGuiSkin.window);
                GUIUtils.RepositionWindow(ref BDArmorySetup.WindowRectWingCommander);
                GUIUtils.UpdateGUIRect(BDArmorySetup.WindowRectWingCommander, _guiCheckIndex);
                GUIUtils.UseMouseEventInRect(BDArmorySetup.WindowRectWingCommander);

                if (showAGWindow)
                {
                    if (BDArmorySettings.UI_SCALE_ACTUAL != 1) { GUI.matrix = guiMatrix; GUIUtility.ScaleAroundPivot(BDArmorySettings.UI_SCALE_ACTUAL * Vector2.one, agWindowRect.position); }
                    agWindowRect = GUILayout.Window(
                        GUIUtility.GetControlID(FocusType.Passive),
                        agWindowRect,
                        AGWindow,
                        StringUtils.Localize("#LOC_BDArmory_WingCommander_ActionGroups"), // "Action Groups"
                        BDArmorySetup.BDGuiSkin.window
                    );
                    GUIUtils.RepositionWindow(ref agWindowRect);
                    GUIUtils.UpdateGUIRect(agWindowRect, _agGuiCheckIndex);
                    GUIUtils.UseMouseEventInRect(agWindowRect);
                }
                if (showFormationWindow)
                {
                    if (BDArmorySettings.UI_SCALE_ACTUAL != 1) { GUI.matrix = guiMatrix; GUIUtility.ScaleAroundPivot(BDArmorySettings.UI_SCALE_ACTUAL * Vector2.one, formationWindowRect.position); }
                    formationWindowRect = GUI.Window(
                        GUIUtility.GetControlID(FocusType.Passive),
                        formationWindowRect,
                        FormationWindow,
                        StringUtils.Localize("#LOC_BDArmory_WingCommander_FormationWindow"), // "Formation Window"
                        BDArmorySetup.BDGuiSkin.window
                    );
                    if (resizingFormationWindow)
                    {
                        formationWindowSize.x = Mathf.Clamp(formationWindowSize.x, 200, Screen.width - formationWindowRect.x);
                        formationWindowSize.y = Mathf.Clamp(formationWindowSize.y, 100, Screen.height - formationWindowRect.y);
                    }
                    formationWindowRect.size = formationWindowSize;
                    GUIUtils.RepositionWindow(ref formationWindowRect);
                    GUIUtils.UpdateGUIRect(formationWindowRect, _formationGuiCheckIndex);
                    GUIUtils.UseMouseEventInRect(formationWindowRect);
                }
                BDArmorySetup.SetGUIOpacity(false);

                if (selectionBoxActive)
                {
                    float xPos = Mathf.Min(boxStartPos.x * Screen.width, boxEndPos.x * Screen.width);
                    float yPos = Mathf.Min((1 - boxStartPos.y) * Screen.height, (1 - boxEndPos.y) * Screen.height);
                    float x2Pos = Mathf.Max(boxStartPos.x * Screen.width, boxEndPos.x * Screen.width);
                    float y2Pos = Mathf.Max((1 - boxStartPos.y) * Screen.height, (1 - boxEndPos.y) * Screen.height);
                    Rect topRect = new Rect(xPos, yPos, x2Pos - xPos, 2);
                    Rect leftRect = new Rect(xPos, yPos, 2, y2Pos - yPos);
                    Rect bottomRect = new Rect(xPos, y2Pos, x2Pos - xPos, 2);
                    Rect rightRect = new Rect(x2Pos, yPos, 2, y2Pos - yPos);
                    GUIUtils.DrawRectangle(topRect, Color.white);
                    GUIUtils.DrawRectangle(leftRect, Color.white);
                    GUIUtils.DrawRectangle(bottomRect, Color.white);
                    GUIUtils.DrawRectangle(rightRect, Color.white);
                }
                if (showSelectionBoxes)
                {
                    float size = 30 * BDTISettings.ICONSCALE;
                    if (selectedWingmen.Count > 0)
                    {
                        foreach (var unit in selectedWingmen)
                        {
                            bool offscreen = false;
                            Vector3 screenPos = GUIUtils.GetMainCamera().WorldToViewportPoint(unit.vessel.CoM);
                            if (screenPos.z < 0 || screenPos.x != Mathf.Clamp01(screenPos.x) || screenPos.y != Mathf.Clamp01(screenPos.y))
                            {
                                offscreen = true;
                            }
                            float xPos = (screenPos.x * Screen.width) - (0.5f * size);
                            float yPos = ((1 - screenPos.y) * Screen.height) - (0.5f * size);

                            if (!offscreen)
                            {
                                Rect unitRect = new Rect(xPos, yPos, size, size);
                                GUI.DrawTexture(unitRect, BDArmorySetup.Instance.openGreenSquare, ScaleMode.StretchToFill, true);
                            }
                        }
                    }
                    if (commandSelf)
                    {
                        bool offscreen = false;
                        Vector3 screenPos = GUIUtils.GetMainCamera().WorldToViewportPoint(vessel.CoM);
                        if (screenPos.z < 0 || screenPos.x != Mathf.Clamp01(screenPos.x) || screenPos.y != Mathf.Clamp01(screenPos.y))
                        {
                            offscreen = true;
                        }
                        float xPos = (screenPos.x * Screen.width) - (0.5f * size);
                        float yPos = ((1 - screenPos.y) * Screen.height) - (0.5f * size);

                        if (!offscreen)
                        {
                            Rect unitRect = new Rect(xPos, yPos, size, size);
                            GUI.DrawTexture(unitRect, BDArmorySetup.Instance.openGreenSquare, ScaleMode.StretchToFill, true);
                        }
                    }
                }
            }

            //command position diamonds
            float diamondSize = 24;
            using List<GPSTargetInfo>.Enumerator comPos = commandedPositions.GetEnumerator();
            while (comPos.MoveNext())
            {
                GUIUtils.DrawTextureOnWorldPos(comPos.Current.worldPos, BDArmorySetup.Instance.greenDiamondTexture,
                    new Vector2(diamondSize, diamondSize), 0);
                if (!GUIUtils.WorldToGUIPos(comPos.Current.worldPos, out Vector2 labelPos)) continue;
                labelPos.x += diamondSize / 2;
                labelPos.y -= 10;
                GUI.Label(new Rect(labelPos.x, labelPos.y, 300, 20), comPos.Current.name);
            }

            if (!drawMouseDiamond) return;
            Vector2 mouseDiamondPos = Input.mousePosition;
            Rect mouseDiamondRect = new Rect(mouseDiamondPos.x - (diamondSize / 2),
                Screen.height - mouseDiamondPos.y - (diamondSize / 2), diamondSize, diamondSize);
            GUI.DrawTexture(mouseDiamondRect, BDArmorySetup.Instance.greenDiamondTexture,
                ScaleMode.StretchToFill, true);
        }
        delegate void CommandFunction(IBDAIControl wingman, object data);

        Vector2 wingmenScrollPos = default;
        int filteredFriendlies = 0;
        void WingmenWindow(int windowID)
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandHeight(autoResizingWindow));
            if (GUI.Button(new Rect(windowSize.x - buttonHeight, margin, buttonHeight - margin, buttonHeight - margin), " X", BDArmorySetup.CloseButtonStyle))
            {
                showGUI = false;
            }
            //command buttons
            GUILayout.BeginHorizontal(); // Two columns of equal width
            GUILayout.BeginVertical(GUILayout.MaxWidth(windowSize.x / 2 - 2 * margin));
            CommandButton(CommandFlyTo, StringUtils.Localize("#LOC_BDArmory_WingCommander_FlyToPos"), true, waitingForFlytoPos);//"Fly To Pos"
            CommandButton(CommandFollow, StringUtils.Localize("#LOC_BDArmory_WingCommander_Follow"), true, false);//"Follow"
            CommandButton(CommandTakeOff, StringUtils.Localize("#LOC_BDArmory_WingCommander_TakeOff"), true, false);//"Take Off"
            GUILayout.EndVertical();
            GUILayout.BeginVertical(GUILayout.MaxWidth(windowSize.x / 2 - 2 * margin));
            CommandButton(CommandAttack, StringUtils.Localize("#LOC_BDArmory_WingCommander_AttackPos"), true, waitingForAttackPos);//"Attack Pos"
            CommandButton(CommandWingAttack, StringUtils.Localize("#LOC_BDArmory_WingCommander_WingAttackPos"), true, waitingForAttackPos);//"Wing Attack Pos"
            CommandButton(CommandRelease, StringUtils.Localize("#LOC_BDArmory_WingCommander_Release"), true, false);//"Release"
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            var guiMargin = GUI.skin.button.margin;
            GUILayout.Space(-guiMargin.bottom - guiMargin.top); // Trim the extra margin from using two layers of GUILayout.

            GUILayout.Space(buttonHeight / 2f);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{StringUtils.Localize("#LOC_BDArmory_WingCommander_FormationSettings")}:", labelStyle, GUILayout.MinWidth(120));//Formation Settings
            if (GUILayout.Button(StringUtils.Localize("#LOC_BDArmory_WingCommander_FormationWindow"), showFormationWindow ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))
            {
                showFormationWindow = !showFormationWindow;
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{StringUtils.Localize("#LOC_BDArmory_WingCommander_Spread")}: {spread:0}", labelStyle, GUILayout.Width(80));//Spread
            spread = GUILayout.HorizontalSlider(spread, 1f, 200f, sliderStyle, sliderThumbStyle);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{StringUtils.Localize("#LOC_BDArmory_WingCommander_Lag")}: {lag:0}", labelStyle, GUILayout.Width(80));//Lag
            lag = GUILayout.HorizontalSlider(lag, 0f, 100f, sliderStyle, sliderThumbStyle);
            GUILayout.EndHorizontal();
            GUILayout.Label($"{StringUtils.Localize("#LOC_BDArmory_WingCommander_WingAttackSettings")}:", labelStyle, GUILayout.ExpandWidth(true));//Wing Attack Settings
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{StringUtils.Localize("#LOC_BDArmory_WingCommander_WingAttackRange")}: {wingAttackRange / 1000:0}km", labelStyle, GUILayout.Width(100));//Engage
            wingAttackRange = Mathf.Round(GUILayout.HorizontalSlider(wingAttackRange / 1000, 1f, 100f, sliderStyle, sliderThumbStyle)) * 1000f;
            GUILayout.EndHorizontal();
            if (GUILayout.Button(StringUtils.Localize("#LOC_BDArmory_WingCommander_FormationWindow"), showFormationWindow ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))
            {
                showFormationWindow = !showFormationWindow;
            }
            CommandButton(OpenAGWindow, StringUtils.Localize("#LOC_BDArmory_WingCommander_ActionGroup"), false, showAGWindow);//"Action Group"
            GUILayout.Space(buttonHeight / 2f);
            GUILayout.Label($"{StringUtils.Localize("#LOC_BDArmory_WingCommander_CtrlGroups")}:", labelStyle, GUILayout.ExpandWidth(true));//Control Groups
            GUILayout.BeginHorizontal();
            GroupButton("1", 0);
            GroupButton("2", 1);
            GroupButton("3", 2);
            GroupButton("4", 3);
            GroupButton("5", 4);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GroupButton("6", 5);
            GroupButton("7", 6);
            GroupButton("8", 7);
            GroupButton("9", 8);
            GroupButton("10", 9);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            commandSelf = GUILayout.Toggle(commandSelf, StringUtils.Localize("#LOC_BDArmory_WingCommander_CommandSelf"), BDArmorySetup.BDGuiSkin.toggle, GUILayout.Width(125));//"Command Self"
            GUILayout.FlexibleSpace();
            if (filteredFriendlies == selectedWingmen.Count) CommandButton(SelectNone, StringUtils.Localize("#LOC_BDArmory_WingCommander_SelectNone"), false, false);
            else CommandButton(SelectAll, StringUtils.Localize("#LOC_BDArmory_WingCommander_SelectAll"), false, false);//"Select All"
            if (GUILayout.Button(StringUtils.Localize("#LOC_BDArmory_WingCommander_BoxSelect"), selectionBoxEnabled ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))
            {
                selectionBoxEnabled = !selectionBoxEnabled;
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            showSelectionBoxes = GUILayout.Toggle(showSelectionBoxes, StringUtils.Localize("#LOC_BDArmory_WingCommander_ShowSelectionBoxes"));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            FilterButton(FormationTextures.Plane, "Plane");
            FilterButton(FormationTextures.Tank, "Tank");
            FilterButton(FormationTextures.Boat, "Boat");
            FilterButton(FormationTextures.Vtol, "VTOL");
            FilterButton(FormationTextures.Base, "Emplacement");
            FilterButton(FormationTextures.Generic, "Generic");
            //spacecraft filter button?
            GUILayout.EndHorizontal();

            bool useScroll = !autoResizingWindow || filteredFriendlies > 10;
            if (useScroll)
            {
                wingmenScrollPos = GUILayout.BeginScrollView(wingmenScrollPos, GUI.skin.box);
            }
            filteredFriendlies = 0;
            foreach (var wingman in friendlies)
            {
                if (wingman != null)
                {
                    string VeeType = GetVeeType(wingman);
                    if (!craftFilters[VeeType]) continue;
                    filteredFriendlies++;
                    if (GUILayout.Button($"{wingman.vessel.vesselName} ({wingman.currentStatus}) - {VeeType}", selectedWingmen.Contains(wingman) ? wingmanButtonSelectedStyle : wingmanButtonStyle))
                    {
                        if (selectedWingmen.Contains(wingman))
                        {
                            selectedWingmen.Remove(wingman);
                        }
                        else
                        {
                            selectedWingmen.Add(wingman);
                        }
                    }
                }
            }
            if (useScroll) GUILayout.EndScrollView(); //needs to be based on windowHeight
            GUILayout.EndVertical();

            var resizeRect = new Rect(windowSize.x - 16, windowSize.y - 16, 16, 16);
            GUI.DrawTexture(resizeRect, GUIUtils.resizeTexture, ScaleMode.StretchToFill, true);
            if (Event.current.type == EventType.MouseDown && resizeRect.Contains(Event.current.mousePosition))
            {
                if (Event.current.button == 1) // Right click - reset to auto-resizing the height.
                {
                    resizingWindow = false;
                    autoResizingWindow = true;
                }
                else
                {
                    autoResizingWindow = false;
                    resizingWindow = true;
                }
            }
            else GUIUtils.DragWindow();
            if (resizingWindow && Event.current.type == EventType.Repaint) windowSize += Mouse.delta / BDArmorySettings.UI_SCALE_ACTUAL;
        }

        string GetVeeType(IBDAIControl ai)
        {
            return ai.aiType switch
            {
                AIType.PilotAI => "Plane",
                AIType.VTOLAI => "VTOL",
                AIType.SurfaceAI => (ai as BDModuleSurfaceAI).SurfaceType switch
                {
                    AIUtils.VehicleMovementType.Land or AIUtils.VehicleMovementType.Amphibious => "Tank",
                    AIUtils.VehicleMovementType.Water => "Boat",
                    AIUtils.VehicleMovementType.Submarine => "Boat", //technically this should be VehicleType 'Sub', but we currently aren't making a distinction between surface/subsurface watercraft
                    AIUtils.VehicleMovementType.Stationary => "Emplacement",
                    _ => "Generic"
                },
                _ => "Generic"
            };
        }
        bool IsVisibleInFilter(IBDAIControl ai)
        {
            if (ai == null) return false;
            string VeeType = GetVeeType(ai);
            //return craftFilters[VeeType == "Sub" ? "Boat" : VeeType]; //uncomment if we add a submarines filter
            return craftFilters[VeeType];
        }

        void CommandButton(CommandFunction func, string buttonLabel, bool sendToWingmen, bool pressed, object data = null)
        {
            if (GUILayout.Button(buttonLabel, pressed ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))
            {
                var ai = ActiveController.GetActiveController(vessel).AI;
                if (ai != null && ai.currentCommand == PilotCommands.Follow) // Avoid follow loops.
                {
                    ai.ReleaseCommand();
                }
                if (sendToWingmen)
                {
                    foreach (var index in selectedWingmen)
                    {
                        func(index, data);
                    }

                    if (func == CommandWingAttack) commandSelf = true;
                    if (commandSelf && ai != null && func != CommandFollow) // Don't chase your own tail!
                    {
                        func(ai, data);
                    }
                }
                else
                {
                    func(null, null);
                }
            }
        }
        void GroupButton(string buttonLabel, int index)
        {
            bool getter = CtrlGroup[index].Count > 0;
            //if nothing in the group, l-click to fill it; if group has stuff in it, l-click to select, r-click to clear
            if (GUILayout.Button(buttonLabel, getter ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))
            {
                if (!getter)
                {
                    CtrlGroup[index].Clear();
                    if (Event.current.button != 1)
                    {
                        foreach (var craft in selectedWingmen)
                            CtrlGroup[index].Add(craft);
                    }
                }
                else
                {
                    switch (Event.current.button)
                    {
                        case 1: // right click
                            CtrlGroup[index].Clear();
                            break;
                        default:
                            foreach (var wingman in CtrlGroup[index])
                            {
                                if (wingman == null) continue;
                                string VeeType = GetVeeType(wingman);
                                //craftFilters[VeeType == "Sub" ? "Boat" : VeeType] = true; // uncomment if adding "Sub" filter
                                craftFilters[VeeType] = true; // Re-enable relevant vehicle type filters for craft in the ctrl group.
                            }
                            selectedWingmen = [.. CtrlGroup[index]];
                            break;
                    }
                }
            }
        }
        void FilterButton(Texture image, string index)
        {
            if (!craftFilters.ContainsKey(index)) craftFilters.Add(index, true);

            if (GUILayout.Button(image, (craftFilters[index] == true ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle), GUILayout.MaxHeight(buttonHeight * 1.5f)))
            {
                craftFilters[index] = !craftFilters[index];
            }
            if (!craftFilters[index]) //if filter off, remove craft of that type from selectedWingman list
            {
                selectedWingmen = [.. selectedWingmen.Where(IsVisibleInFilter)];
            }
        }
        void CommandRelease(IBDAIControl wingman, object data)
        {
            wingman.ReleaseCommand();
        }

        void CommandFollow(IBDAIControl wingman, object data)
        {
            wingman.CommandFollow(this, wingman.commandFollowIndex < 0 ? GetFreeWingIndex(false) : wingman.commandFollowIndex); // Get a new index or reuse an existing one.
        }

        public void CommandAllFollow()
        {
            RefreshFriendlies();
            int i = 0;
            foreach (var wingman in friendlies)
            {
                if (wingman == null) continue;
                wingman.CommandFollow(this, i++);
            }
        }
        public int GetFreeWingIndex(bool refresh = true)
        {
            if (refresh) RefreshFriendlies();
            int freeIndex = 0;
            var usedIndices = friendlies.Select(f => f.commandFollowIndex).ToList();
            while (usedIndices.Contains(freeIndex)) ++freeIndex;
            return freeIndex;
        }

        void CommandAG(IBDAIControl wingman, object ag)
        {
            KSPActionGroup actionGroup = (KSPActionGroup)ag;
            wingman.CommandAG(actionGroup);
        }

        void CommandTakeOff(IBDAIControl wingman, object data)
        {
            wingman.CommandTakeOff();
        }

        void OpenAGWindow(IBDAIControl wingman, object data)
        {
            showAGWindow = !showAGWindow;
        }

        bool showAGWindow
        {
            get; set
            {
                field = value;
                if (_agGuiCheckIndex < 0) _agGuiCheckIndex = GUIUtils.RegisterGUIRect(new Rect());
                if (value)
                {
                    agWindowRect.position = BDArmorySetup.WindowRectWingCommander.position + BDArmorySettings.UI_SCALE_ACTUAL * new Vector2(BDArmorySetup.WindowRectWingCommander.width, 0);
                }
                else
                {
                    GUIUtils.UpdateGUIRect(new Rect(), _agGuiCheckIndex);
                }
            }
        } = false;
        Rect agWindowRect;
        static int _agGuiCheckIndex = -1;

        void AGWindow(int id)
        {
            GUILayout.BeginVertical(GUILayout.MinWidth(100)); // Width of title
            foreach (var actionGroup in Enum.GetValues(typeof(KSPActionGroup)).Cast<KSPActionGroup>())
            {
                if (actionGroup <= KSPActionGroup.None) continue;
                CommandButton(CommandAG, actionGroup.ToString(), true, false, actionGroup);
            }
            GUILayout.EndVertical();
            GUIUtils.DragWindow();
        }

        void SelectAll(IBDAIControl wingman, object data)
        {
            switch (Event.current.button)
            {
                case 1: // right click
                    selectedWingmen.Clear();
                    break;
                default:
                    selectedWingmen.Clear();
                    foreach (var visibleWingmen in friendlies)
                    {
                        if (IsVisibleInFilter(visibleWingmen))
                            selectedWingmen.Add(visibleWingmen);
                    }
                    break;
            }
        }

        void SelectNone(IBDAIControl wingman, object data)
        {
            selectedWingmen.Clear();
        }

        void CommandFlyTo(IBDAIControl wingman, object data)
        {
            StartCoroutine(CommandPosition(wingman, PilotCommands.FlyTo));
        }

        void CommandAttack(IBDAIControl wingman, object data)
        {
            StartCoroutine(CommandPosition(wingman, PilotCommands.Attack));
        }

        void CommandWingAttack(IBDAIControl wingman, object data)
        {
            StartCoroutine(CommandPosition(wingman, PilotCommands.WingAttack));
        }

        bool waitingForFlytoPos;
        bool waitingForAttackPos;

        IEnumerator CommandPosition(IBDAIControl wingman, PilotCommands command)
        {
            if (selectedWingmen.Count == 0 && !commandSelf)
            {
                yield break;
            }

            DisplayScreenMessage(StringUtils.Localize("#LOC_BDArmory_WingCommander_ScreenMessage"));//"Select target coordinates.\nRight-click to cancel."

            if (command == PilotCommands.FlyTo)
            {
                waitingForFlytoPos = true;
            }
            else if (command == PilotCommands.Attack || command == PilotCommands.WingAttack)
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
                    Vector3 mousePos = new Vector3(Input.mousePosition.x / Screen.width, Input.mousePosition.y / Screen.height, 0);
                    Ray ray = FlightCamera.fetch.mainCamera.ViewportPointToRay(mousePos);
                    // Stage 1: check for colliders (other than this vessel) and adjust for current radar altitude
                    // Stage 2: check for ray-sphere intersection and adjust for terrain and current radar altitude
                    // Stage 3: fall back to a point near the horizon and adjust for terrain and current radar altitude
                    bool haveGPS = false;
                    Vector3d gps = default;
                    if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Max(30000f, PhysicsRangeExtender.GetPRERange()), (int)(LayerMasks.Scenery | LayerMasks.Parts | LayerMasks.EVA | LayerMasks.Wheels)))
                    {
                        var hitPart = hit.collider.gameObject.GetComponentInParent<Part>();
                        if (hitPart == null)
                        {
                            var hitEVA = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                            if (hitEVA != null) hitPart = hitEVA.part;
                        }
                        if (hitPart == null || hitPart.vessel != vessel) // Hit someone or something other than us.
                        {
                            if (hitPart == null || hitPart.vessel.LandedOrSplashed)
                            {
                                hit.point += (float)vessel.radarAltitude * VectorUtils.GetUpDirection(hit.point); // Adjust to maintain radar altitude over the target, unless it's airborne.
                            }
                            gps = VectorUtils.WorldPositionToGeoCoords(hit.point, vessel.mainBody);
                            haveGPS = true;
                        }
                    }
                    else if (VectorUtils.SphereRayIntersect(ray, vessel.mainBody.position, vessel.mainBody.Radius, out double distance) && distance > 0)
                    {
                        Vector3 worldPoint = ray.GetPoint((float)distance);
                        worldPoint += (float)(vessel.radarAltitude + BodyUtils.GetTerrainAltitudeAtPos(worldPoint)) * VectorUtils.GetUpDirection(worldPoint); // Adjust to maintain radar altitude.
                        gps = VectorUtils.WorldPositionToGeoCoords(worldPoint, vessel.mainBody);
                        haveGPS = true;
                    }
                    else // Aim for a point near the horizon
                    {
                        var horizonDist = BDAMath.Sqrt(2f * (float)vessel.mainBody.Radius * FlightGlobals.getAltitudeAtPos(FlightCamera.fetch.mainCamera.transform.position));
                        Vector3 worldPoint = ray.GetPoint(horizonDist);
                        worldPoint += (float)(vessel.radarAltitude + BodyUtils.GetTerrainAltitudeAtPos(worldPoint) - FlightGlobals.getAltitudeAtPos(worldPoint)) * VectorUtils.GetUpDirection(worldPoint); // Adjust to maintain radar altitude.
                        gps = VectorUtils.WorldPositionToGeoCoords(worldPoint, vessel.mainBody);
                        haveGPS = true;
                    }
                    if (haveGPS)
                    {
                        switch (command)
                        {
                            case PilotCommands.FlyTo:
                                wingman.CommandFlyTo(gps);
                                break;
                            case PilotCommands.Attack:
                                wingman.CommandAttack(gps);
                                break;
                            case PilotCommands.WingAttack:
                                wingman.CommandWingAttack(
                                    this,
                                    wingman == ActiveController.GetActiveController(vessel).AI ? -1 : // We're the leader
                                        wingman.commandFollowIndex < 0 ? GetFreeWingIndex(false) : wingman.commandFollowIndex, // Get a new index or reuse an existing one.
                                    gps,
                                    wingAttackRange
                                );
                                break;
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
            while (Vector3d.Distance(wingman.commandGPS, tInfo.gpsCoordinates) < 0.01f && (
                wingman.currentCommand == PilotCommands.Attack ||
                wingman.currentCommand == PilotCommands.WingAttack ||
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

        #region Formation Position
        bool showFormationWindow
        {
            get; set
            {
                field = value;
                if (_formationGuiCheckIndex < 0) _formationGuiCheckIndex = GUIUtils.RegisterGUIRect(new Rect());
                if (value)
                {
                    if (formationWindowRect == default) formationWindowRect = new Rect(
                        BDArmorySetup.WindowRectWingCommander.position + BDArmorySettings.UI_SCALE_ACTUAL * new Vector2(BDArmorySetup.WindowRectWingCommander.width, 0),
                        formationWindowSize
                    );
                }
                else
                {
                    GUIUtils.UpdateGUIRect(new Rect(), _formationGuiCheckIndex);
                }
            }
        } = false;
        static int _formationGuiCheckIndex = -1;
        Rect formationWindowRect = default;
        bool resizingFormationWindow = false;
        Vector2 formationWindowSize = new(500, 300);
        float formationWindowScale = 2f;
        const float formationIconScale = 32;
        Vector2 formationIconOffset = new(-formationIconScale / 2, 50);
        int formationDragIndex = -1;
        bool numericFields = false;

        readonly Dictionary<int, Vector3> formationIndexToPosition = []; // Formation index => formation position (right, ahead, above) relative to commander.
        readonly Dictionary<int, NumericInputFieldVector3> formationIndexToPositionNumericFields = [];
        /// <summary>
        /// Get the formation position in local coordinates.
        /// </summary>
        /// <param name="index">Formation index</param>
        /// <returns></returns>
        public Vector3 GetFormationPosition(int index)
        {
            if (formationIndexToPosition.TryGetValue(index, out Vector3 position))
            {
                return position;
            }
            else // Fall back to the default formation position.
            {
                float indexF = index;
                indexF++;

                float rightSign = indexF % 2 == 0 ? -1 : 1;
                float positionFactor = Mathf.Ceil(indexF / 2);
                float right = rightSign * positionFactor * spread;
                float back = -positionFactor * lag;
                return new Vector3(right, back, 0);
            }
        }
        public void SetFormationPosition(int index, Vector3 position)
        {
            formationIndexToPosition[index] = position;
            if (numericFields) formationIndexToPositionNumericFields[index].SetCurrentValue(position);
        }

        void FormationWindow(int id)
        {
            if (GUI.Button(new Rect(formationWindowSize.x - buttonHeight, margin, buttonHeight - margin, buttonHeight - margin), " X", BDArmorySetup.CloseButtonStyle))
            { showFormationWindow = false; }
            if (GUI.Button(new Rect(formationWindowSize.x - 2 * buttonHeight, margin, buttonHeight - margin, buttonHeight - margin), "#", numericFields ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))
            {
                numericFields = !numericFields;
                if (!numericFields)
                {
                    foreach (var field in formationIndexToPositionNumericFields.Values) Destroy(field);
                    formationIndexToPositionNumericFields.Clear();
                }
            }
            if (GUI.Button(new Rect(formationWindowSize.x - 4 * buttonHeight, margin, 2 * buttonHeight - margin, buttonHeight - margin), "Reset", BDArmorySetup.ButtonStyle))
            { // Start fresh with the default spread and lag.
                formationIndexToPosition.Clear();
                foreach (var field in formationIndexToPositionNumericFields.Values) Destroy(field);
                formationIndexToPositionNumericFields.Clear();
            }
            if (GUI.Button(new Rect(formationWindowSize.x - buttonHeight, margin + buttonHeight, buttonHeight - margin, buttonHeight - margin), "-", BDArmorySetup.ButtonStyle))
            { formationWindowScale *= 2f; }
            if (GUI.Button(new Rect(formationWindowSize.x - 2 * buttonHeight, margin + buttonHeight, buttonHeight - margin, buttonHeight - margin), "+", BDArmorySetup.ButtonStyle))
            { formationWindowScale = Mathf.Max(0.25f, formationWindowScale / 2f); }

            var leaderAC = ActiveController.GetActiveController(vessel);
            var teamColor = BDATeamIcons.GetTeamColor(leaderAC.WM);
            var dragRect = new Rect(formationIconOffset.x + formationWindowSize.x / 2, formationIconOffset.y, formationIconScale, formationIconScale);
            FormationTextures.DrawFormationIcon(leaderAC.AI, Color.black, dragRect); // Command leader in black
            if (Event.current.type == EventType.MouseDown && dragRect.Contains(Event.current.mousePosition)) formationDragIndex = 0;
            foreach (var wingman in wingmen)
            {
                if (wingman == null) continue;
                int wingmanIndex = wingman.commandFollowIndex;
                var formationPosition = GetFormationPosition(wingmanIndex);
                dragRect = new Rect(formationIconOffset.x + formationPosition.x / formationWindowScale + formationWindowSize.x / 2, formationIconOffset.y - formationPosition.y / formationWindowScale, formationIconScale, formationIconScale);
                if (numericFields)
                {
                    if (!formationIndexToPositionNumericFields.TryGetValue(wingmanIndex, out var field))
                    {
                        field = formationIndexToPositionNumericFields[wingmanIndex] = gameObject.AddComponent<NumericInputFieldVector3>().Initialise(Time.time, formationPosition);
                    }
                    field.TryParseValue(GUI.TextField(new Rect(dragRect.position + new Vector2(formationIconScale / 2 - 50, formationIconScale / 2 + 18), new(100, 20)), field.PossibleValue, 16, field.Style));
                    formationIndexToPosition[wingmanIndex] = field.CurrentValue;
                    GUI.Label(new Rect(dragRect.position + new Vector2(formationIconScale / 2 - 100, formationIconScale / 2 + 36), new(200, 20)), $"{wingmanIndex + 1}: {wingman.vessel.vesselName}", formationLabelStyle);
                }
                else
                {
                    GUI.Label(
                        new Rect(dragRect.position + new Vector2(formationIconScale / 2 - 100, formationIconScale / 2), new(200, 50)),
                        $"({formationPosition.x:0}, {formationPosition.y:0}{(formationPosition.z != 0 ? $", {formationPosition.z:0}" : "")})\n{wingmanIndex + 1}: {wingman.vessel.vesselName}",
                        formationLabelStyle);
                }
                FormationTextures.DrawFormationIcon(wingman, teamColor, dragRect); // Wingmen in team colors
                if (Event.current.type == EventType.MouseDown && dragRect.Contains(Event.current.mousePosition)) formationDragIndex = wingmanIndex + 1;
            }

            var resizeRect = new Rect(formationWindowSize.x - 16, formationWindowSize.y - 16, 16, 16);
            GUI.DrawTexture(resizeRect, GUIUtils.resizeTexture, ScaleMode.StretchToFill, true);
            if (Event.current.type == EventType.MouseDown && resizeRect.Contains(Event.current.mousePosition)) resizingFormationWindow = true;
            else if (formationDragIndex < 0) GUIUtils.DragWindow();

            if (Event.current.type == EventType.Repaint)
            {
                if (resizingFormationWindow) formationWindowSize += Mouse.delta / BDArmorySettings.UI_SCALE_ACTUAL;
                else if (formationDragIndex >= 0)
                {
                    if (formationDragIndex == 0)
                    {
                        if (GameSettings.MODIFIER_KEY.GetKey())
                        {
                            if (Mouse.delta.y != 0)
                            {
                                float vertAdjust = Mouse.delta.y > 0 ? -1 : 1;
                                foreach (var wingman in wingmen)
                                {
                                    if (wingman == null) continue;
                                    int wingmanIndex = wingman.commandFollowIndex;
                                    var position = GetFormationPosition(wingmanIndex);
                                    position.z += vertAdjust;
                                    SetFormationPosition(wingmanIndex, position);
                                }
                            }
                        }
                        else
                        {
                            formationIconOffset += Mouse.delta / BDArmorySettings.UI_SCALE_ACTUAL;
                        }
                    }
                    else
                    {
                        if (GameSettings.MODIFIER_KEY.GetKey())
                        {
                            if (Mouse.delta.y != 0)
                            {
                                var position = GetFormationPosition(formationDragIndex - 1);
                                position.z += Mouse.delta.y > 0 ? -1 : 1;
                                SetFormationPosition(formationDragIndex - 1, position);
                            }
                        }
                        else
                        {
                            SetFormationPosition(formationDragIndex - 1, GetFormationPosition(formationDragIndex - 1) + (Vector3)(new Vector2(formationWindowScale, -formationWindowScale) * Mouse.delta / BDArmorySettings.UI_SCALE_ACTUAL));
                        }
                    }
                }
            }
        }

        static class FormationTextures
        {
            public static void DrawFormationIcon(IBDAIControl ai, Color color, Rect rect)
            {
                var texture = ai.aiType switch // Matches the classifications in ActiveController.
                {
                    AIType.PilotAI => Plane,
                    AIType.VTOLAI => Vtol,
                    AIType.SurfaceAI => (ai as BDModuleSurfaceAI).SurfaceType switch
                    {
                        AIUtils.VehicleMovementType.Land or AIUtils.VehicleMovementType.Amphibious or AIUtils.VehicleMovementType.Stationary => Tank,
                        AIUtils.VehicleMovementType.Water or AIUtils.VehicleMovementType.Submarine => Boat,
                        _ => Generic
                    },
                    _ => Generic
                };
                GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true, 1, color, 0, 0);
            }
            // Note: Use white and transparent PNGs for these textures to allow blending to any color. Also, we can't use Path.Combine for GameDatabase queries as it's not portable.
            public static Texture2D Boat { get { return field ? field : field = GameDatabase.Instance.GetTexture(BDArmorySetup.textureDir + "Formation/boat", false); } } = null;
            public static Texture2D Plane { get { return field ? field : field = GameDatabase.Instance.GetTexture(BDArmorySetup.textureDir + "Formation/plane", false); } } = null;
            public static Texture2D Tank { get { return field ? field : field = GameDatabase.Instance.GetTexture(BDArmorySetup.textureDir + "Formation/tank", false); } } = null;
            public static Texture2D Vtol { get { return field ? field : field = GameDatabase.Instance.GetTexture(BDArmorySetup.textureDir + "Formation/vtol", false); } } = null;
            public static Texture2D Base { get { return field ? field : field = GameDatabase.Instance.GetTexture(BDArmorySetup.textureDir + "Formation/base", false); } } = null;
            public static Texture2D Generic { get { return field ? field : field = GameDatabase.Instance.GetTexture(BDArmorySetup.textureDir + "Formation/generic", false); } } = null;
        }
        #endregion
    }
}
