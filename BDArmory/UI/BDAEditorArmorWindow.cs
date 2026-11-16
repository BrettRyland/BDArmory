using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System;
using KSP.UI.Screens;
using UnityEngine;

using BDArmory.Armor;
using BDArmory.Damage;
using BDArmory.Extensions;
using BDArmory.ModIntegration;
using BDArmory.Settings;
using BDArmory.Utils;

namespace BDArmory.UI
{
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    internal class BDAEditorArmorWindow : MonoBehaviour
    {
        public static BDAEditorArmorWindow Instance = null;
        private ApplicationLauncherButton toolbarButton = null;

        private bool showArmorWindow = false;
        private string windowTitle = StringUtils.Localize("#LOC_BDArmory_ArmorTool");
        private Rect windowRect = new Rect(300, 150, 300, 350);
        private float lineHeight = 20;
        private float height = 20;
        private GUIContent[] armorGUI;
        private GUIContent armorBoxText;
        private BDGUIComboBox armorBox;
        private int previous_index = -1;

        private GUIContent[] hullGUI;
        private GUIContent hullBoxText;
        private BDGUIComboBox hullBox;
        private int previous_mat = -1;
        private float oldLines = -1;

        GUIStyle listStyle;

        private float totalArmorMass;
        private float totalArmorCost;
        private float totalLift;
        private float totalLiftArea;
        private float totalLiftStackRatio;
        private float wingLoadingWet;
        private float wingLoadingDry;
        private float WLRatioWet;
        private float WLRatioDry;
        private List<PartResourceDefinition> vesselResources;
        private List<int> vesselResourceIDs;
        private Rect vesselResourceBoxRect = new(10, 0, 280, 0);
        private bool CalcArmor = false;
        private bool shipModifiedfromCalcArmor = false;
        private bool SetType = false;
        private bool SetThickness = false;
        private string selectedArmor = "None";
        private bool ArmorStats = false;
        private bool resourcePick = false;
        private float ArmorDensity = 0;
        private float ArmorStrength = 200;
        private float ArmorHardness = 300;
        private float ArmorDuctility = 0.6f;
        private float ArmorDiffusivity = 237;
        private float ArmorMaxTemp = 993;
        private float ArmorVfactor = 8.45001135e-07f;
        private float ArmorMu1 = 0.656060636f;
        private float ArmorMu2 = 1.20190930f;
        private float ArmorMu3 = 1.77791929f;
        private float ArmorCost = 0;

        private bool armorslist = false;
        private bool hullslist = false;
        private float Thickness = 10;
        private bool useNumField = false;
        private float oldThickness = 10;
        private float maxThickness = 60;
        private bool Visualizer = false;
        private bool HPvisualizer = false;
        private bool HullVisualizer = false;
        private bool LiftVisualizer = false;
        private bool TreeVisualizer = false;
        private bool oldVisualizer = false;
        private bool oldHPvisualizer = false;
        private bool oldHullVisualizer = false;
        private bool oldLiftVisualizer = false;
        private bool oldTreeVisualizer = false;
        private bool refreshVisualizer = false;
        private bool refreshHPvisualizer = false;
        private bool refreshHullvisualizer = true;
        private bool refreshLiftvisualizer = false;
        private bool refreshTreevisualizer = false;
        private string hullmat = "Aluminium";

        private float steelValue = 1;
        private float armorValue = 1;
        private float relValue = 1;
        private float exploValue;

        //comp rules compliance stuff
        float maxStacking = -1;
        int maxPartCount = -1;
        float maxLtW = -1;
        float maxTWR = -1;
        float maxMass = -1;
        int maxEngines = 999;
        int pointBuyBudget = -1;

        Dictionary<string, NumericInputField> thicknessField;
        void Awake()
        {
            if (Instance != null) Destroy(Instance);
            Instance = this;
        }

        void Start()
        {
            AddToolbarButton();
            thicknessField = new Dictionary<string, NumericInputField>
            {
                {"Thickness", gameObject.AddComponent<NumericInputField>().Initialise(0, 10, 0, 1500) }, // FIXME should use maxThickness instead of 1500 here.
            };
            vesselResourceIDs = new List<int>();
            vesselResources = new List<PartResourceDefinition>();
            GameEvents.onEditorShipModified.Add(OnEditorShipModifiedEvent);
            GameEvents.onEditorPartPlaced.Add(OnEditorPartPlacedEvent);
            GameEvents.onEditorPartDeleted.Add(OnEditorPartPlacedEvent);
            /*
            var modifiedCaliber = (15) + (15) * (2f * 0.15f * 0.15f);
            float bulletEnergy = ProjectileUtils.CalculateProjectileEnergy(0.388f, 1109);
            float yieldStrength = modifiedCaliber * modifiedCaliber * Mathf.PI / 100f * 940 * 30;
            if (ArmorDuctility > 0.25f)
            {
                yieldStrength *= 0.7f;
            }
            float newCaliber = ProjectileUtils.CalculateDeformation(yieldStrength, bulletEnergy, 30, 1109, 1176, 7850, 0.19f, 0.8f, false);
            */
            //steelValue = ProjectileUtils.CalculatePenetration(30, newCaliber, 0.388f, 1109, 0.15f, 7850, 940, 30, 0.8f, false);
            steelValue = ProjectileUtils.CalculatePenetration(30, 1109, 0.388f, 0.8f);
            exploValue = 940 * 1.15f * 7.85f;
            listStyle = new GUIStyle(BDArmorySetup.ButtonStyle)
            {
                fixedHeight = 18 //make list contents slightly smaller
            };
            SetupLegalityValues();
        }

        private void FillArmorList()
        {
            armorGUI = new GUIContent[ArmorInfo.armors.Count];
            for (int i = 0; i < ArmorInfo.armors.Count; i++)
            {
                GUIContent gui = new GUIContent(ArmorInfo.armors[i].localizedName.Length <= 17 ? ArmorInfo.armors[i].localizedName : ArmorInfo.armors[i].name.Remove(14) + "...");
                armorGUI[i] = gui;
            }
            armorBoxText = new GUIContent();
            armorBoxText.text = StringUtils.Localize("#LOC_BDArmory_ArmorSelect");
        }
        private void FillHullList()
        {
            hullGUI = new GUIContent[HullInfo.materials.Count];
            for (int i = 0; i < HullInfo.materials.Count; i++)
            {
                GUIContent gui = new GUIContent(HullInfo.materials[i].localizedName.Length <= 17 ? HullInfo.materials[i].localizedName : HullInfo.materials[i].localizedName.Remove(14) + "...");
                hullGUI[i] = gui;
            }

            hullBoxText = new GUIContent();
            hullBoxText.text = StringUtils.Localize("#LOC_BDArmory_Armor_HullType");
        }

        public void SetupLegalityValues()
        {
            if (BDArmorySettings.COMP_CONVENIENCE_CHECKS || BDArmorySettings.RUNWAY_PROJECT)
            {
                if (CompSettings.CompVesselChecksEnabled)
                {
                    if (CompSettings.vesselChecks.TryGetValue("maxStacking", out float ms) && ms > 0) maxStacking = ms;
                    if (CompSettings.vesselChecks.TryGetValue("maxPartCount", out float mpc) && mpc > 0) maxPartCount = Mathf.RoundToInt(mpc);
                    if (CompSettings.vesselChecks.TryGetValue("maxLtW", out float ltw) && mpc > 0) maxLtW = ltw;
                    if (CompSettings.vesselChecks.TryGetValue("maxTWR", out float twr) && mpc > 0) maxTWR = twr;
                    if (CompSettings.vesselChecks.TryGetValue("maxMass", out float m) && m > 0) maxMass = m;
                    if (CompSettings.vesselChecks.TryGetValue("maxEngines", out float me) && me != 999) maxEngines = Mathf.RoundToInt(me);
                }
                if (CompSettings.CompPriceChecksEnabled && CompSettings.vesselChecks.TryGetValue("pointBuyBudget", out float pb) && pb > 0) pointBuyBudget = Mathf.RoundToInt(pb);
            }
        }

        public void OnEditorShipModifiedEvent(ShipConstruct data)
        {
            if (data is null) return;
            delayedRefreshVisuals = true;
            if (!delayedRefreshVisualsInProgress)
                StartCoroutine(DelayedRefreshVisuals(data));
        }

        private bool delayedRefreshVisuals = false;
        private bool delayedRefreshVisualsInProgress = false;
        IEnumerator DelayedRefreshVisuals(ShipConstruct ship)
        {
            delayedRefreshVisualsInProgress = true;
            var wait = new WaitForFixedUpdate();
            int count = 0, countLimit = 50;
            while (delayedRefreshVisuals && ++count < countLimit) // Wait until ship modified events stop coming, or countLimit ticks.
            {
                delayedRefreshVisuals = false;
                yield return wait;
            }
            if (count == countLimit) Debug.LogWarning($"[BDArmory.BDAEditorArmorWindow]: Continuous stream of OnEditorShipModifiedEvents for over {countLimit} frames.");
            count = 0;
            yield return new WaitUntilFixed(() => ++count == countLimit ||
               ship == null || ship.Parts == null || ship.Parts.TrueForAll(p =>
               {
                   if (p == null) return true;
                   var hp = p.GetComponent<HitpointTracker>();
                   return hp == null || hp.Ready;
               })); // Wait for HP changes to delayed ship modified events in HitpointTracker
            if (count == countLimit)
            {
                string reason = "";
                if (ship != null && ship.Parts != null)
                    reason = string.Join("; ", ship.Parts.Select(p =>
                    {
                        if (p == null) return null;
                        var hp = p.GetComponent<Damage.HitpointTracker>();
                        if (hp == null || hp.Ready) return null;
                        return hp;
                    }).Where(hp => hp != null).Select(hp => $"{hp.part.name}: {hp.Why}"));
                if (BDArmorySettings.DEBUG_ARMOR) Debug.LogWarning($"[BDArmory.BDAEditorArmorWindow]: Ship HP failed to settle within {countLimit} frames.{(string.IsNullOrEmpty(reason) ? "" : $" {reason}")}");
            }
            delayedRefreshVisualsInProgress = false;

            if (showArmorWindow)
            {
                if (!shipModifiedfromCalcArmor)
                {
                    CalcArmor = true;
                }
                if (Visualizer || HPvisualizer || HullVisualizer || LiftVisualizer || TreeVisualizer)
                {
                    refreshVisualizer = true;
                    refreshHPvisualizer = true;
                    refreshHullvisualizer = true;
                    refreshLiftvisualizer = true;
                    refreshTreevisualizer = true;
                }
                shipModifiedfromCalcArmor = false;
                CalculateArmorMass();

                var oldResources = vesselResources.ToHashSet();
                vesselResources.Clear();
                using (var part = EditorLogic.fetch.ship.parts.GetEnumerator())
                    while (part.MoveNext())
                    {
                        foreach (PartResource res in part.Current.Resources)
                        {
                            if (!vesselResources.Contains(res.info))
                            {
                                vesselResources.Add(res.info);
                            }
                        }
                    }
                var newResources = vesselResources.ToHashSet();
                newResources.ExceptWith(oldResources); // Newly added resources.
                var resourceIDs = newResources.Select(res => res.id).ToHashSet(); //add all resources to VRID by default so default drymass is true drymass, until specific resouces filtered
                resourceIDs.ExceptWith(vesselResourceIDs.ToHashSet()); // Only newly added resources that aren't already added to the IDs list.
                if (resourceIDs.Count > 0)
                    vesselResourceIDs.AddRange(resourceIDs);

                if (!FerramAerospace.hasFAR)
                    CalculateTotalLift(); // Re-calculate lift and wing loading on armor change
                //Debug.Log("[ArmorTool] Recalculating mass/lift");
            }
            DoVesselLegalityChecks(false);
        }

        private void OnEditorPartPlacedEvent(Part data)
        {
            //DoVesselLegalityChecks(true); //these also get triggered in OnShipModified, which properly waits for HP, etc to settle; important since LegalityChecks also calls CalculateTotalLift, which needs craft mass settled
            if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 78)
            {
                data.sameVesselCollision = true;
            }
        }

        private void OnDestroy()
        {
            GameEvents.onEditorShipModified.Remove(OnEditorShipModifiedEvent);
            GameEvents.onEditorPartPlaced.Remove(OnEditorPartPlacedEvent);
            GameEvents.onEditorPartDeleted.Remove(OnEditorPartPlacedEvent);
            HideToolbarGUINow();
            if (toolbarButton)
            {
                ApplicationLauncher.Instance.RemoveModApplication(toolbarButton);
                toolbarButton = null;
            }
        }

        void AddToolbarButton()
        {
            if (!HighLogic.LoadedSceneIsEditor || BDArmorySettings.LEGACY_ARMOR) return;
            StartCoroutine(ToolbarButtonRoutine());
        }
        IEnumerator ToolbarButtonRoutine()
        {
            if (toolbarButton) // Update the callbacks for the current instance.
            {
                toolbarButton.onTrue = ShowToolbarGUI;
                toolbarButton.onFalse = HideToolbarGUI;
                yield break;
            }
            yield return new WaitUntil(() => ApplicationLauncher.Ready && BDArmorySetup.toolbarButtonAdded); // Wait until after the main BDA toolbar button.
            Texture buttonTexture = GameDatabase.Instance.GetTexture(BDArmorySetup.textureDir + "icon_Armor", false);
            toolbarButton = ApplicationLauncher.Instance.AddModApplication(ShowToolbarGUI, HideToolbarGUI, Dummy, Dummy, Dummy, Dummy, ApplicationLauncher.AppScenes.SPH | ApplicationLauncher.AppScenes.VAB, buttonTexture);
        }

        public void ShowToolbarGUI()
        {
            showArmorWindow = true;
            OnEditorShipModifiedEvent(EditorLogic.fetch.ship); // Trigger updating of stuff.
        }

        public void HideToolbarGUI() => StartCoroutine(HideToolbarGUIAtEndOfFrame());
        bool waitingForEndOfFrame = false;
        IEnumerator HideToolbarGUIAtEndOfFrame()
        {
            if (waitingForEndOfFrame) yield break;
            waitingForEndOfFrame = true;
            yield return new WaitForEndOfFrame();
            waitingForEndOfFrame = false;
            HideToolbarGUINow();
        }
        void HideToolbarGUINow()
        {
            showArmorWindow = false;
            CalcArmor = false;
            Visualizer = false;
            HPvisualizer = false;
            HullVisualizer = false;
            LiftVisualizer = false;
            TreeVisualizer = false;
            if (thicknessField != null && thicknessField.ContainsKey("Thickness")) thicknessField["Thickness"].tryParseValueNow();
            Visualize();
            GUIUtils.PreventClickThrough(windowRect, "BDAArmorLOCK", true);
        }

        void Dummy()
        { }

        void OnGUI()
        {
            if (showArmorWindow)
            {
                if (BDArmorySettings.UI_SCALE_ACTUAL != 1) GUIUtility.ScaleAroundPivot(BDArmorySettings.UI_SCALE_ACTUAL * Vector2.one, windowRect.position);
                windowRect = GUI.Window(GUIUtility.GetControlID(FocusType.Passive), windowRect, WindowArmor, windowTitle, BDArmorySetup.BDGuiSkin.window);
            }
            if (TreeVisualizer)
            {
                Part rootPart = EditorLogic.RootPart;
                if (rootPart == null) return;
                using (List<Part>.Enumerator parts = EditorLogic.fetch.ship.Parts.GetEnumerator())
                    while (parts.MoveNext())
                    {
                        if (parts.Current == rootPart)
                            GUIUtils.DrawTextureOnWorldPos(parts.Current.transform.position, BDArmorySetup.Instance.redDotTexture, new Vector2(48, 48), 0);
                        else
                        {
                            GUIUtils.DrawTextureOnWorldPos(parts.Current.transform.position, BDArmorySetup.Instance.redDotTexture, new Vector2(16, 16), 0);
                            Color VisualizerColor = Color.HSVToRGB(((1 - Mathf.Clamp(Mathf.Abs(Vector3.Distance(parts.Current.attPos, Vector3.zero)), 0.1f, 1)) / 1) / 3, 1, 1);
                            //will result in any part that has been offset more than a meter showing up with a red line
                            GUIUtils.DrawLineBetweenWorldPositions(parts.Current.transform.position, parts.Current.parent.transform.position, 3, VisualizerColor);
                        }
                    }
            }
        }

        void WindowArmor(int windowID)
        {
            GUIUtils.PreventClickThrough(windowRect, "BDAArmorLOCK");
            if (GUI.Button(new Rect(windowRect.width - 18, 2, 16, 16), " X", BDArmorySetup.CloseButtonStyle))
            {
                toolbarButton.SetFalse();
            }
            if (CalcArmor)
            {
                CalcArmor = false;
                SetType = false;
                CalculateArmorMass();
            }

            GUIStyle style = BDArmorySetup.BDGuiSkin.label;

            if (useNumField != (useNumField = GUI.Toggle(new Rect(windowRect.width - 36, 2, 16, 16), useNumField, "#", useNumField ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))
            {
                if (!useNumField && thicknessField != null && thicknessField.ContainsKey("Thickness")) thicknessField["Thickness"].tryParseValueNow();
            }

            float line = 1.5f;

            style.fontStyle = FontStyle.Normal;

            if (GUI.Button(new Rect(10, line * lineHeight, 280, lineHeight), StringUtils.Localize("#LOC_BDArmory_ArmorHPVisualizer"), HPvisualizer ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))
            {
                HPvisualizer = !HPvisualizer;
                if (HPvisualizer)
                {
                    Visualizer = false;
                    HullVisualizer = false;
                    LiftVisualizer = false;
                    TreeVisualizer = false;
                }
            }
            line += 1.25f;


            if (!BDArmorySettings.RESET_ARMOUR)
            {
                if (GUI.Button(new Rect(10, line * lineHeight, 280, lineHeight), StringUtils.Localize("#LOC_BDArmory_ArmorVisualizer"), Visualizer ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))
                {
                    Visualizer = !Visualizer;
                    if (Visualizer)
                    {
                        HPvisualizer = false;
                        HullVisualizer = false;
                        LiftVisualizer = false;
                        TreeVisualizer = false;
                    }
                }
                line += 1.25f;
            }

            if (!BDArmorySettings.RESET_HULL)
            {
                if (GUI.Button(new Rect(10, line * lineHeight, 280, lineHeight), StringUtils.Localize("#LOC_BDArmory_ArmorHullVisualizer"), HullVisualizer ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))
                {
                    HullVisualizer = !HullVisualizer;
                    if (HullVisualizer)
                    {
                        HPvisualizer = false;
                        Visualizer = false;
                        LiftVisualizer = false;
                        TreeVisualizer = false;
                    }
                }
                line += 1.25f;
            }

            if (!FerramAerospace.hasFAR)
            {
                if (GUI.Button(new Rect(10, line * lineHeight, 280, lineHeight), StringUtils.Localize("#LOC_BDArmory_ArmorLiftVisualizer"), LiftVisualizer ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))
                {
                    LiftVisualizer = !LiftVisualizer;
                    if (LiftVisualizer)
                    {
                        Visualizer = false;
                        HullVisualizer = false;
                        HPvisualizer = false;
                        TreeVisualizer = false;
                    }
                }
                line += 1.25f;
            }

            //if (BDArmorySettings.RUNWAY_PROJECT)
            {
                if (GUI.Button(new Rect(10, line * lineHeight, 280, lineHeight), StringUtils.Localize("#LOC_BDArmory_partTreeVisualizer"), TreeVisualizer ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))
                {
                    TreeVisualizer = !TreeVisualizer;
                    if (TreeVisualizer)
                    {
                        Visualizer = false;
                        HullVisualizer = false;
                        HPvisualizer = false;
                        LiftVisualizer = false;
                    }
                }
                line += 1.25f;
            }

            line += 0.25f;

            if ((refreshHPvisualizer || HPvisualizer != oldHPvisualizer) || (refreshVisualizer || Visualizer != oldVisualizer) || (refreshHullvisualizer || HullVisualizer != oldHullVisualizer) || (refreshLiftvisualizer || LiftVisualizer != oldLiftVisualizer) || (refreshTreevisualizer || TreeVisualizer != oldTreeVisualizer))
            {
                Visualize();
            }

            if (!BDArmorySettings.RESET_ARMOUR)
            {
                GUI.Label(new Rect(10, line * lineHeight, 300, lineHeight), $"{StringUtils.Localize("#LOC_BDArmory_ArmorThickness")}: {Thickness} mm", style);
                line++;
                if (!useNumField)
                {
                    Thickness = GUI.HorizontalSlider(new Rect(20, line * lineHeight, 260, lineHeight), Thickness, 0, maxThickness);
                    //Thickness /= 5;
                    Thickness = Mathf.Round(Thickness);
                    //Thickness *= 5;
                    line++;
                }
                else
                {
                    var field = thicknessField["Thickness"];
                    field.TryParseValue(GUI.TextField(new Rect(20, line * lineHeight, 260, lineHeight), field.possibleValue, 4, field.style));
                    Thickness = Mathf.Min((float)field.CurrentValue, maxThickness); // FIXME Mathf.Min shouldn't be necessary if the maxValue of the thicknessField has been updated for maxThickness
                    line++;
                }
                GUI.Label(new Rect(10, line * lineHeight, 300, lineHeight), $"{StringUtils.Localize("#LOC_BDArmory_ArmorTotalMass")}: {totalArmorMass:0.00}", style);
                line++;
                GUI.Label(new Rect(10, line * lineHeight, 300, lineHeight), $"{StringUtils.Localize("#LOC_BDArmory_ArmorTotalCost")}: {Mathf.Round(totalArmorCost)}", style);
                line++;
            }
            if (!FerramAerospace.hasFAR)
            {
                GUI.Label(new Rect(10, line * lineHeight, 300, lineHeight), $"{StringUtils.Localize("#LOC_BDArmory_ArmorTotalLift")}: {totalLift:0.00} ({totalLiftArea:F3} m2)", style);
                line++;
                GUI.Label(new Rect(10, line * lineHeight, 300, lineHeight), $"{StringUtils.Localize("#LOC_BDArmory_ArmorWingLoading")}:", style);
                line++;
                GUI.Label(new Rect(10, line * lineHeight, 300, lineHeight), $"   - {StringUtils.Localize("#autoLOC_6001895")}: {wingLoadingWet:0.00} ({WLRatioWet:F2} kg/m2)", style);
                line++;
                GUI.Label(new Rect(10, line * lineHeight, 300, lineHeight), $"   - {StringUtils.Localize("#autoLOC_6001896")}: {wingLoadingDry:0.00} ({WLRatioDry:F2} kg/m2)", style);
                line++;
                GUI.Label(new Rect(10, line * lineHeight, 300, lineHeight), $"{StringUtils.Localize("#LOC_BDArmory_ArmorLiftStacking")}: {totalLiftStackRatio:0.0%}", style);
                line++;
#if DEBUG
                line += 0.5f;
                if (GUI.Button(new Rect(10, line++ * lineHeight, 280, lineHeight), "Find Wings", BDArmorySetup.ButtonStyle))
                {
                    var wings = FindWings();
                    foreach (var wing in wings)
                    {
                        Debug.Log($"DEBUG Wing: {string.Join(", ", wing.Select(w => $"{w.name}:{w.persistentId}"))}");
                    }
                    // Total lift stacking is the combination of inter- and intra-wing lift stacking.
                    // Calculate inter-wing lift stacking by calculating stacking between wings.
                    var liftStacking = CalculateInterWingLiftStacking(wings);
                    // Calculate intra-wing lift stacking by descending down wing hierarchies and calculating the stacking between children of each node.
                    foreach (var wing in wings)
                        liftStacking += CalculateIntraWingLiftStacking(wing);
                    Debug.Log($"DEBUG Lift stacking: {liftStacking}");
                }
#endif
            }
            if (!FerramAerospace.hasFAR)
            {
                line += 0.5f;
                resourcePick = GUI.Toggle(new Rect(10, line++ * lineHeight, 280, lineHeight), resourcePick, StringUtils.Localize("#LOC_BDArmory_DryMassWhitelist"), resourcePick ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle);
                if (resourcePick)
                {
                    vesselResourceBoxRect.y = line * lineHeight - 2;
                    GUI.Box(vesselResourceBoxRect, "", BDArmorySetup.SelectedButtonStyle); // l,r,t,b = 3,3,3,3 with slight overlap of the toggle
                    int pos = 0;
                    using (var res = vesselResources.GetEnumerator())
                        while (res.MoveNext())
                        {
                            if (res.Current.density == 0) continue; //don't show massless resouces for drymass blacklist
                            if (res.Current.name.Contains("Intake")) continue; //don't include intake air, since that will always be present                            
                            int resID = res.Current.id;
                            var buttonName = res.Current.displayName.Length <= 17 ? res.Current.displayName : res.Current.displayName.Remove(14) + "...";
                            if (GUI.Button(new Rect(pos % 2 == 0 ? 13 : 152f, (line + (int)(pos / 2)) * lineHeight + 1, 135, lineHeight), $"{buttonName}", vesselResourceIDs.Contains(resID) ? BDArmorySetup.ButtonStyle : BDArmorySetup.SelectedButtonStyle)) // match BDGUIComboBox's layout
                            {
                                if (!vesselResourceIDs.Contains(resID))
                                {
                                    vesselResourceIDs.Add(resID); //resource counted as wet mass
                                }
                                else
                                {
                                    vesselResourceIDs.Remove(resID); //resouce to be counted as drymass
                                }
                                CalculateTotalLift();
                            }
                            pos++;
                        }
                    vesselResourceBoxRect.height = Mathf.CeilToInt(pos / 2f) * lineHeight + 6;
                    line += Mathf.CeilToInt(pos / 2f) + 0.25f;
                }
            }
            float StatLines = 0;
            float armorLines = 0;
            if (!BDArmorySettings.RESET_ARMOUR)
            {
                line += 0.5f;
                if (Thickness != oldThickness)
                {
                    oldThickness = Thickness;
                    SetThickness = true;
                    maxThickness = 10;
                    thicknessField["Thickness"].maxValue = maxThickness;
                    CalculateArmorMass();
                }
                //GUI.Label(new Rect(40, line * lineHeight, 300, lineHeight), StringUtils.Localize("#LOC_BDArmory_ArmorSelect"), style);
                if (!armorslist)
                {
                    FillArmorList();
                    armorBox = new BDGUIComboBox(new Rect(10, line * lineHeight, 280, lineHeight), new Rect(10, line * lineHeight, 280, lineHeight), armorBoxText, armorGUI, 120, listStyle);
                    armorslist = true;
                }
                armorBox.UpdateRect(new Rect(10, line * lineHeight, 280, lineHeight));
                int selected_index = armorBox.Show();
                armorLines++;
                if (armorBox.IsOpen)
                {
                    armorLines += armorBox.Height / lineHeight;
                }
                if (selected_index != previous_index)
                {
                    if (selected_index != -1)
                    {
                        selectedArmor = ArmorInfo.armors[selected_index].name;
                        SetType = true;
                        CalculateArmorMass();
                        CalculateArmorStats();
                    }
                    previous_index = selected_index;
                    CalculateArmorMass();
                }

                if (GameSettings.ADVANCED_TWEAKABLES)
                {
                    line += 0.5f;
                    ArmorStats = GUI.Toggle(new Rect(10, (line + armorLines) * lineHeight, 280, lineHeight), ArmorStats, StringUtils.Localize("#LOC_BDArmory_ArmorStats"), ArmorStats ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle);
                    StatLines++;
                    if (ArmorStats)
                    {
                        if (selectedArmor != "None")
                        {
                            GUI.Label(new Rect(15, (line + armorLines + StatLines) * lineHeight, 120, lineHeight), $"{StringUtils.Localize("#LOC_BDArmory_ArmorStrength")}: {ArmorStrength}", style);
                            //StatLines++;
                            GUI.Label(new Rect(135, (line + armorLines + StatLines) * lineHeight, 260, lineHeight), $"{StringUtils.Localize("#LOC_BDArmory_ArmorHardness")}: {ArmorHardness} ", style);
                            StatLines++;
                            GUI.Label(new Rect(15, (line + armorLines + StatLines) * lineHeight, 120, lineHeight), $"{StringUtils.Localize("#LOC_BDArmory_ArmorDuctility")}: {ArmorDuctility}", style);
                            //StatLines++;
                            GUI.Label(new Rect(135, (line + armorLines + StatLines) * lineHeight, 260, lineHeight), $"{StringUtils.Localize("#LOC_BDArmory_ArmorDiffusivity")}: {ArmorDiffusivity}", style);
                            StatLines++;
                            GUI.Label(new Rect(15, (line + armorLines + StatLines) * lineHeight, 120, lineHeight), $"{StringUtils.Localize("#LOC_BDArmory_ArmorMaxTemp")}: {ArmorMaxTemp} K", style);
                            //StatLines++;
                            GUI.Label(new Rect(135, (line + armorLines + StatLines) * lineHeight, 260, lineHeight), $"{StringUtils.Localize("#LOC_BDArmory_ArmorDensity")}: {ArmorDensity} kg/m3", style);
                            StatLines++;
                            GUI.Label(new Rect(15, (line + armorLines + StatLines) * lineHeight, 120, lineHeight), $"{StringUtils.Localize("#LOC_BDArmory_ArmorCost")}: {ArmorCost} /m3", style);
                            StatLines++;
                            GUI.Label(new Rect(15, (line + armorLines + StatLines) * lineHeight, 260, lineHeight), $"{StringUtils.Localize("#LOC_BDArmory_BulletResist")}:{(relValue < 1.2 ? (relValue < 0.5 ? "* * * * *" : "* * * *") : (relValue > 2.8 ? (relValue > 4 ? "*" : "* *") : "* * *"))}", style);
                            StatLines++;

                            GUI.Label(new Rect(15, (line + armorLines + StatLines) * lineHeight, 260, lineHeight), $"{StringUtils.Localize("#LOC_BDArmory_ExplosionResist")}: {((ArmorDuctility < 0.05f && ArmorHardness < 500) ? "* *" : (exploValue > 8000 ? (exploValue > 20000 ? "* * * * *" : "* * * *") : (exploValue < 4000 ? (exploValue < 2000 ? "*" : "* *") : "* * *")))}", style);
                            StatLines++;

                            GUI.Label(new Rect(15, (line + armorLines + StatLines) * lineHeight, 260, lineHeight), $"{StringUtils.Localize("#LOC_BDArmory_LaserResist")}: {(ArmorDiffusivity > 150 ? (ArmorDiffusivity > 199 ? "* * * * *" : "* * * *") : (ArmorDiffusivity < 50 ? (ArmorDiffusivity < 10 ? "*" : "* *") : "* * *"))}", style);
                            StatLines++;

                            if (ArmorDuctility < 0.05)
                            {
                                if (ArmorHardness > 500) GUI.Label(new Rect(15, (line + armorLines + StatLines) * lineHeight, 260, lineHeight), StringUtils.Localize("#LOC_BDArmory_ArmorShatterWarning"), style);
                                StatLines++;
                            }
                        }
                        if (selectedArmor != "Mild Steel" && selectedArmor != "None")
                        {
                            GUI.Label(new Rect(10, (line + armorLines + StatLines) * lineHeight, 300, lineHeight), $"{StringUtils.Localize("#LOC_BDArmory_EquivalentThickness")}: {Thickness / relValue:G3} mm", style);
                            line++;
                        }
                    }
                }
            }
            float HullLines = 0;
            if (!BDArmorySettings.RESET_HULL)
            {
                line += 0.5f;
                if (!hullslist)
                {
                    FillHullList();
                    hullBox = new BDGUIComboBox(new Rect(10, (line + armorLines + StatLines) * lineHeight, 280, lineHeight), new Rect(10, (line + armorLines + StatLines) * lineHeight, 280, lineHeight), hullBoxText, hullGUI, 120, listStyle);
                    hullslist = true;
                }
                hullBox.UpdateRect(new Rect(10, (line + armorLines + StatLines) * lineHeight, 280, lineHeight));
                if (armorLines + StatLines != oldLines)
                {
                    oldLines = armorLines + StatLines;
                }
                int selected_mat = hullBox.Show();
                HullLines++;
                if (hullBox.IsOpen)
                {
                    HullLines += hullBox.Height / lineHeight;
                }
                if (selected_mat != previous_mat)
                {
                    if (selected_mat != -1)
                    {
                        hullmat = HullInfo.materials[selected_mat].name;
                        CalculateArmorMass(true);
                    }
                    previous_mat = selected_mat;
                }
            }
            line += 0.5f;
            if ((BDArmorySettings.RUNWAY_PROJECT || BDArmorySettings.COMP_CONVENIENCE_CHECKS) && (CompSettings.CompBanChecksEnabled || CompSettings.CompPriceChecksEnabled || CompSettings.CompVesselChecksEnabled))
            {
                if (GUI.Button(new Rect(10, (line + armorLines + StatLines + HullLines) * lineHeight, 280, lineHeight), StringUtils.Localize("#LOC_BDArmory_checkVessel"), BDArmorySetup.ButtonStyle))
                {
                    DoVesselLegalityChecks(true, true);
                }
                line += 1.5f;
            }
            GUIUtils.DragWindow();
            height = Mathf.Lerp(height, (line + armorLines + StatLines + HullLines) * lineHeight, 0.15f);
            windowRect.height = height;
            GUIUtils.RepositionWindow(ref windowRect);
        }

        void CalculateArmorMass(bool vesselmass = false)
        {
            if (EditorLogic.RootPart == null)
                return;

            bool modified = false;
            var selectedArmorIndex = ArmorInfo.armors.FindIndex(t => t.name == selectedArmor);
            if (selectedArmorIndex < 0)
                return;
            using (List<Part>.Enumerator parts = EditorLogic.fetch.ship.Parts.GetEnumerator())
                while (parts.MoveNext())
                {
                    if (parts.Current.IsMissile()) continue;
                    HitpointTracker armor = parts.Current.GetComponent<HitpointTracker>();
                    if (armor != null)
                    {
                        if (!vesselmass)
                        {
                            if (armor.maxSupportedArmor > maxThickness)
                            {
                                maxThickness = armor.maxSupportedArmor;
                                thicknessField["Thickness"].maxValue = maxThickness;
                            }
                            if (SetType || SetThickness)
                            {
                                if (SetThickness)
                                {
                                    if (armor.ArmorTypeNum > 1)
                                    {
                                        armor.Armor = Mathf.Clamp(Thickness, 0, armor.maxSupportedArmor);
                                    }
                                }
                                if (SetType)
                                {
                                    armor.ArmorTypeNum = selectedArmorIndex + 1;
                                    if (armor.ArmorThickness > 10)
                                    {
                                        if (armor.ArmorTypeNum < 2)
                                        {
                                            armor.ArmorTypeNum = 2; //don't set armor type "none" for armor panels
                                        }
                                        if (armor.maxSupportedArmor > maxThickness)
                                        {
                                            maxThickness = armor.maxSupportedArmor;
                                            thicknessField["Thickness"].maxValue = maxThickness;
                                        }
                                    }
                                }
                                armor.ArmorModified(null, null);
                                modified = true;
                            }
                            StartCoroutine(calcArmorMassAndCost());
                            //totalArmorMass += armor.armorMass; //these aren't updating due to ArmorModified getting called next Update tick, so armorMass/Cost hasn't updated yet for grabbing the new value
                            //totalArmorCost += armor.armorCost;
                        }
                        else
                        {
                            armor.HullTypeNum = HullInfo.materials.FindIndex(t => t.name == hullmat) + 1;
                            armor.HullModified(null, null);
                            modified = true;
                        }

                    }
                }
            CalcArmor = false;
            if ((SetType || SetThickness) && (Visualizer || HPvisualizer))
            {
                refreshVisualizer = true;
            }
            SetType = false;
            SetThickness = false;
            ArmorCost = ArmorInfo.armors[selectedArmorIndex].Cost;
            ArmorDensity = ArmorInfo.armors[selectedArmorIndex].Density;
            ArmorDiffusivity = ArmorInfo.armors[selectedArmorIndex].Diffusivity;
            ArmorDuctility = ArmorInfo.armors[selectedArmorIndex].Ductility;
            ArmorHardness = ArmorInfo.armors[selectedArmorIndex].Hardness;
            ArmorMaxTemp = ArmorInfo.armors[selectedArmorIndex].SafeUseTemp;
            ArmorStrength = ArmorInfo.armors[selectedArmorIndex].Strength;
            ArmorVfactor = ArmorInfo.armors[selectedArmorIndex].vFactor;
            ArmorMu1 = ArmorInfo.armors[selectedArmorIndex].muParam1;
            ArmorMu2 = ArmorInfo.armors[selectedArmorIndex].muParam2;
            ArmorMu3 = ArmorInfo.armors[selectedArmorIndex].muParam3;

            if (modified)
            {
                shipModifiedfromCalcArmor = true;
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
            }

            if (!FerramAerospace.hasFAR)
                CalculateTotalLift(); // Re-calculate lift and wing loading on armor change
        }

        void CalculateTotalLift()
        {
            if (EditorLogic.RootPart == null)
                return;

            var totalMass = EditorLogic.fetch.ship.GetTotalMass();
            totalLift = 0;
            using (List<Part>.Enumerator parts = EditorLogic.fetch.ship.Parts.GetEnumerator())
                while (parts.MoveNext())
                {
                    if (parts.Current.IsMissile()) continue;
                    ModuleLiftingSurface wing = parts.Current.GetComponent<ModuleLiftingSurface>();
                    if (wing != null)
                    {
                        totalLift += wing.deflectionLiftCoeff * Vector3.Project(wing.transform.forward, Vector3.up).sqrMagnitude; // Only return vertically oriented lift components
                    }
                }
            wingLoadingWet = totalLift / totalMass; //convert to kg/m2. 1 LiftingArea is ~ 3.51m2, or ~285kg/m2
            totalLiftArea = totalLift * 3.52f;
            WLRatioWet = totalMass * 1000 / totalLiftArea;
            float dMass = totalMass - EditorLogic.fetch.ship.parts.SelectMany(p => p.Resources, (p, r) => r).Where(res => vesselResourceIDs.Contains(res.info.id)).Select(res => (float)res.amount * res.info.density).Sum();

            wingLoadingDry = totalLift / dMass;
            WLRatioDry = (dMass * 1000) / totalLiftArea;
            CalculateTotalLiftStacking();
        }

        void CalculateTotalLiftStacking()
        {
            if (EditorLogic.RootPart == null)
                return;

            float liftStackedAll = 0;
            float liftStackedAllEval = 0;
            List<Part> evaluatedParts = new List<Part>(); ;
            totalLiftStackRatio = 0;
            using (List<Part>.Enumerator parts1 = EditorLogic.fetch.ship.Parts.GetEnumerator())
                while (parts1.MoveNext())
                {
                    if (parts1.Current.IsMissile()) continue;
                    if (IsAeroBrake(parts1.Current)) continue;
                    ModuleLiftingSurface wing1 = parts1.Current.GetComponent<ModuleLiftingSurface>();
                    if (wing1 != null)
                    {
                        evaluatedParts.Add(parts1.Current);
                        float lift1area = wing1.deflectionLiftCoeff * Vector3.Project(wing1.transform.forward, Vector3.up).sqrMagnitude; // Only return vertically oriented lift components
                        float lift1rad = BDAMath.Sqrt(lift1area / Mathf.PI);
                        Vector3 col1Pos = wing1.part.partTransform.TransformPoint(wing1.part.CoLOffset);
                        Vector3 col1PosProj = col1Pos.ProjectOnPlanePreNormalized(Vector3.up);
                        liftStackedAllEval += lift1area; // Add up total lift areas

                        using (List<Part>.Enumerator parts2 = EditorLogic.fetch.ship.Parts.GetEnumerator())
                            while (parts2.MoveNext())
                            {
                                if (evaluatedParts.Contains(parts2.Current)) continue;
                                if (parts1.Current == parts2.Current) continue;
                                if (parts2.Current.IsMissile()) continue;
                                if (IsAeroBrake(parts2.Current)) continue;
                                ModuleLiftingSurface wing2 = parts2.Current.GetComponent<ModuleLiftingSurface>();
                                if (wing2 != null)
                                {
                                    float lift2area = wing2.deflectionLiftCoeff * Vector3.Project(wing2.transform.forward, Vector3.up).sqrMagnitude; // Only return vertically oriented lift components
                                    float lift2rad = BDAMath.Sqrt(lift2area / Mathf.PI);
                                    Vector3 col2Pos = wing2.part.partTransform.TransformPoint(wing2.part.CoLOffset);
                                    Vector3 col2PosProj = col2Pos.ProjectOnPlanePreNormalized(Vector3.up);

                                    float d = Vector3.Distance(col1PosProj, col2PosProj);
                                    float R = lift1rad;
                                    float r = lift2rad;

                                    float a = 0;

                                    // Calc overlapping area between two circles
                                    if (d >= R + r) // Circles not overlapping
                                        a = 0;
                                    else if (R >= (d + r)) // Circle 2 inside Circle 1
                                        a = Mathf.PI * r * r;
                                    else if (r >= (d + R)) // Circle 1 inside Circle 2
                                        a = Mathf.PI * R * R;
                                    else if (d < R + r) // Circles overlapping
                                        a = r * r * Mathf.Acos((d * d + r * r - R * R) / (2 * d * r)) + R * R * Mathf.Acos((d * d + R * R - r * r) / (2 * d * R)) -
                                            0.5f * BDAMath.Sqrt((-d + r + R) * (d + r - R) * (d - r + R) * (d + r + R));

                                    // Calculate vertical spacing factor (0 penalty if surfaces are spaced sqrt(2*lift) apart)
                                    float v_dist = Vector3.Distance(Vector3.Project(col1Pos, Vector3.up), Vector3.Project(col2Pos, Vector3.up));
                                    float l_spacing = Mathf.Round(Mathf.Max(lift1area, lift2area, 0.25f) * 100f) / 100f; // Round lift to nearest 0.01
                                    float v_factor = Mathf.Pow(Mathf.Clamp01((BDAMath.Sqrt(2 * l_spacing) - v_dist) / (BDAMath.Sqrt(2 * l_spacing) - BDAMath.Sqrt(l_spacing))), 0.1f);

                                    // Add overlapping area
                                    liftStackedAll += a * v_factor;
                                }
                            }
                    }
                }
            // Look at total overlapping lift area as a percentage of total lift area. Since overlapping lift area for multiple parts can potentially be greater than the total lift area, cap 
            // the stacking at 100%. Also, multiply stacked lift by two for the edge case where only two parts are evaluated.
            liftStackedAll *= (evaluatedParts.Count == 2) ? 2 : 1;
            totalLiftStackRatio = Mathf.Clamp01(liftStackedAll / Math.Max(liftStackedAllEval, 0.01f));
        }

        /// <summary>
        /// Get a list of all the logical wings (hierarchically connected) on a vessel beginning at (but not including) the given part.
        /// </summary>
        /// <param name="part">The part to start at or the root part if not specified.</param>
        /// <param name="checkedParts"></param>
        /// <param name="wings"></param>
        /// <returns>A list of the logical wings where each wing is a hashset of parts with lifting surfaces.</returns>
        List<HashSet<Part>> FindWings(Part part = null, HashSet<Part> checkedParts = null, List<HashSet<Part>> wings = null)
        {
            if (part == null) part = EditorLogic.RootPart;
            if (wings == null) wings = new List<HashSet<Part>>();
            if (part == null) return wings;
            if (checkedParts == null) checkedParts = new HashSet<Part> { part };

            foreach (var child in part.children)
            {
                if (child == null) continue;
                if (child.IsMissile()) continue;
                if (!checkedParts.Contains(child)) // If the part hasn't been checked, check it for being the start of a wing.
                {
                    var liftingSurface = child.GetComponent<ModuleLiftingSurface>();
                    if (liftingSurface != null) // Start of a wing.
                    {
                        var wing = FindWingDescendants(child);
                        wings.Add(wing);
                        checkedParts.UnionWith(wing); // Mark all the wing segments as being checked already.
                    }
                }
                checkedParts.Add(child);
                FindWings(child, checkedParts, wings); // We still need to check all the children in case there's another wing lower in the hierarchy.
            }

            return wings;
        }

        /// <summary>
        /// Find connected wing segments that are direct descendants of a part.
        /// </summary>
        /// <param name="wing"></param>
        /// <returns>The parts that form the segments of the wing.</returns>
        HashSet<Part> FindWingDescendants(Part wing)
        {
            HashSet<Part> segments = new HashSet<Part> { wing };
            foreach (var child in wing.children)
            {
                if (child == null) continue;
                if (child.IsMissile()) continue;
                var liftingSurface = child.GetComponent<ModuleLiftingSurface>();
                if (liftingSurface != null) // If the child is a lifting surface, add it and its descendants.
                {
                    segments.Add(child);
                    segments.UnionWith(FindWingDescendants(child));
                }
            }
            return segments;
        }

        /// <summary>
        /// Calculate the amount of lift stacking between the wings.
        /// </summary>
        /// <param name="wings">The wings, each consisting of a hashset of parts.</param>
        /// <param name="baseWing">The base part of the wing (leave as null if the base isn't a wing).</param>
        /// <returns>The amount of stacking between the wings.</returns>
        float CalculateInterWingLiftStacking(List<HashSet<Part>> wings, Part baseWing = null)
        {
            if (wings.Count < (baseWing == null ? 2 : 1)) return 0; // Not enough segments for an overlap.
            var wingRoots = wings.Select(wing => wing.Where(p => p.parent == null || !wing.Contains(p.parent)).FirstOrDefault()).Where(p => p != null).ToList();
            Debug.Log($"DEBUG Checking lift stacking between wings with{(baseWing != null ? $" base {baseWing.name}:{baseWing.persistentId} and" : "")} roots: {string.Join(", ", wingRoots.Select(w => $"{w.name}:{w.persistentId}"))}");
            return 0; // FIXME Compute the lift of the base and each wing and the amount they overlap. This could potentially include non-vertical lift too.
        }

        /// <summary>
        /// Calculate the amount of lift stacking between segments of a wing.
        /// </summary>
        /// <param name="wing">The parts in the wing.</param>
        /// <returns>The amount of stacking within the wing.</returns>
        float CalculateIntraWingLiftStacking(HashSet<Part> wing)
        {
            var wingRoot = wing.Where(p => p.parent == null || !wing.Contains(p.parent)).FirstOrDefault(); // The root of the wing either has no parent or the parent isn't part of the wing.
            if (wingRoot == null) return 0;
            var subWings = FindWings(wingRoot);
            float liftStacking = CalculateInterWingLiftStacking(subWings, wingRoot); // Include the lift stacking between this wing segment and its sub-wings.
            foreach (var subWing in subWings) liftStacking += CalculateIntraWingLiftStacking(subWing); // Then go deeper in the tree.
            return liftStacking;
        }

        bool IsAeroBrake(Part part)
        {
            if (part.GetComponent<ModuleLiftingSurface>() is not null)
            {
                if (part.GetComponent<ModuleAeroSurface>() is not null)
                    return true;
                else
                    return false;
            }
            else
                return false;
        }

        IEnumerator calcArmorMassAndCost()
        {
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            if (!HighLogic.LoadedSceneIsEditor) yield break;
            totalArmorMass = 0;
            totalArmorCost = 0;
            using (List<Part>.Enumerator parts = EditorLogic.fetch.ship.Parts.GetEnumerator())
                while (parts.MoveNext())
                {
                    if (parts.Current.IsMissile()) continue;
                    HitpointTracker armor = parts.Current.GetComponent<HitpointTracker>();
                    if (armor != null)
                    {
                        if (armor.ArmorTypeNum == 1 && !armor.ArmorPanel) continue;

                        totalArmorMass += armor.armorMass;
                        totalArmorCost += armor.armorCost;
                    }
                }
        }

        void Visualize()
        {
            if (EditorLogic.RootPart == null)
                return;
            if (Visualizer || HPvisualizer || HullVisualizer || LiftVisualizer)
            {
                using (List<Part>.Enumerator parts = EditorLogic.fetch.ship.Parts.GetEnumerator())
                    while (parts.MoveNext())
                    {
                        if (parts.Current.name.Contains("conformaldecals")) continue;
                        HitpointTracker a = parts.Current.GetComponent<HitpointTracker>();
                        if (a != null)
                        {
                            Color VisualizerColor = Color.HSVToRGB((Mathf.Clamp(a.Hitpoints, 100, 1600) / 1600) / 3, 1, 1);
                            if (Visualizer)
                            {
                                VisualizerColor = Color.HSVToRGB(a.ArmorTypeNum / (ArmorInfo.armors.Count + 1), (a.Armor / maxThickness), 1f);
                            }
                            if (HullVisualizer)
                            {
                                VisualizerColor = Color.HSVToRGB(a.HullTypeNum / (HullInfo.materials.Count + 1), 1, 1f);
                            }
                            if (LiftVisualizer)
                            {
                                ModuleLiftingSurface wing = parts.Current.GetComponent<ModuleLiftingSurface>();
                                if (wing != null && wing.deflectionLiftCoeff > 0f)
                                {
                                    VisualizerColor = Color.HSVToRGB(Mathf.Clamp01(Mathf.Log10(wing.deflectionLiftCoeff + 1f)) / 3, 1, 1);
                                    if (BDArmorySettings.MAX_PWING_LIFT > 0 && parts.Current.name.Contains("B9.Aero.Wing.Procedural") && wing.deflectionLiftCoeff > BDArmorySettings.MAX_PWING_LIFT)
                                    {
                                        VisualizerColor = Color.magenta;
                                    }
                                }
                                else
                                    VisualizerColor = Color.HSVToRGB(0, 0, 0.5f);
                            }
                            var r = parts.Current.GetComponentsInChildren<Renderer>();
                            {
                                if (!a.RegisterProcWingShader && parts.Current.name.Contains("B9.Aero.Wing.Procedural")) //procwing defaultshader left null on start so current shader setup can be grabbed at visualizer runtime
                                {
                                    for (int s = 0; s < r.Length; s++)
                                    {
                                        if (r[s].GetComponentInParent<Part>() != parts.Current) continue; // Don't recurse to child parts.
                                        int key = r[s].material.GetInstanceID();
                                        a.defaultShader.Add(key, r[s].material.shader);
                                        //Debug.Log("[Visualizer] " + parts.Current.name + " shader is " + r[s].material.shader.name);
                                        if (r[s].material.HasProperty("_Color"))
                                        {
                                            a.defaultColor.Add(key, r[s].material.color);
                                        }
                                    }
                                    a.RegisterProcWingShader = true;
                                }
                                for (int i = 0; i < r.Length; i++)
                                {
                                    if (r[i].GetComponentInParent<Part>() != parts.Current) continue; // Don't recurse to child parts.
                                    if (!a.defaultShader.ContainsKey(r[i].material.GetInstanceID())) continue; // Don't modify shaders that we don't have defaults for as we can't then replace them.
                                    if (r[i].material.shader.name.Contains("Alpha")) continue;
                                    r[i].material.shader = Shader.Find("KSP/Unlit");
                                    if (r[i].material.HasProperty("_Color"))
                                    {
                                        r[i].material.SetColor("_Color", VisualizerColor);
                                    }
                                }
                            }
                            //Debug.Log("[VISUALIZER] modding shaders on " + parts.Current.name);//can confirm that procwings aren't getting shaders applied, yet they're still getting applied. 
                            //at least this fixes the procwings widgets getting colored
                        }
                    }
            }
            if (!Visualizer && !HPvisualizer && !HullVisualizer && !LiftVisualizer)
            {
                using (List<Part>.Enumerator parts = EditorLogic.fetch.ship.Parts.GetEnumerator())
                    while (parts.MoveNext())
                    {
                        HitpointTracker armor = parts.Current.GetComponent<HitpointTracker>();
                        if (parts.Current.name.Contains("conformaldecals")) continue;
                        //so, this gets called when GUI closed, without touching the hp/armor visualizer at all.
                        //Now, on GUI close, it runs the latter half of visualize to shut off any visualizer effects and reset stuff.
                        //Procs wings turn orange at this point... oh. That's why: The visualizer reset is grabbing a list of shaders and colors at *part spawn!*
                        //pWings use dynamic shaders to paint themselves, so it's not reapplying the latest shader /color config, but the initial one, the one from the part icon  
                        var r = parts.Current.GetComponentsInChildren<Renderer>();
                        if (!armor.RegisterProcWingShader && parts.Current.name.Contains("B9.Aero.Wing.Procedural")) //procwing defaultshader left null on start so current shader setup can be grabbed at visualizer runtime
                        {
                            for (int s = 0; s < r.Length; s++)
                            {
                                if (r[s].GetComponentInParent<Part>() != parts.Current) continue; // Don't recurse to child parts.
                                int key = r[s].material.GetInstanceID();
                                armor.defaultShader.Add(key, r[s].material.shader);
                                //Debug.Log("[Visualizer] " + parts.Current.name + " shader is " + r[s].material.shader.name);
                                if (r[s].material.HasProperty("_Color"))
                                {
                                    armor.defaultColor.Add(key, r[s].material.color);
                                }
                            }
                            armor.RegisterProcWingShader = true;
                        }
                        //Debug.Log("[VISUALIZER] applying shader to " + parts.Current.name);
                        for (int i = 0; i < r.Length; i++)
                        {
                            try
                            {
                                if (r[i].GetComponentInParent<Part>() != parts.Current) continue; // Don't recurse to child parts.
                                int key = r[i].material.GetInstanceID();
                                if (!armor.defaultShader.ContainsKey(key))
                                {
                                    if (BDArmorySettings.DEBUG_RADAR) Debug.Log($"[BDArmory.BDAEditorArmorWindow]: {r[i].material.name} ({key}) not found in defaultShader for part {parts.Current.partInfo.name} on {parts.Current.vessel.vesselName}"); // Enable this to see what materials aren't getting RCS shaders applied to them.
                                    continue;
                                }
                                if (r[i].material.shader != armor.defaultShader[key])
                                {
                                    if (armor.defaultShader[key] != null)
                                    {
                                        r[i].material.shader = armor.defaultShader[key];
                                    }
                                    if (armor.defaultColor.ContainsKey(key))
                                    {
                                        if (armor.defaultColor[key] != null)
                                        {
                                            if (parts.Current.name.Contains("B9.Aero.Wing.Procedural"))
                                            {
                                                r[i].material.SetColor("_MainTex", armor.defaultColor[key]);
                                                //LayeredSpecular has _MainTex, _Emissive, _SpecColor,_RimColor, _TemperatureColor, and _BurnColor
                                                // source: https://github.com/tetraflon/B9-PWings-Modified/blob/master/B9%20PWings%20Fork/shaders/SpecularLayered.shader
                                                //This works.. occasionally. Sometimes it will properly reset pwing tex/color, most of the time it doesn't. need to test later
                                            }
                                            else
                                            {
                                                r[i].material.SetColor("_Color", armor.defaultColor[key]);
                                            }
                                        }
                                        else
                                        {
                                            if (parts.Current.name.Contains("B9.Aero.Wing.Procedural"))
                                            {
                                                //r[i].material.SetColor("_Emissive", Color.white);
                                                r[i].material.SetColor("_MainTex", Color.white);
                                            }
                                            else
                                            {
                                                r[i].material.SetColor("_Color", Color.white);
                                            }
                                        }
                                    }
                                }
                            }
                            catch
                            {
                                //Debug.Log("[BDAEditorArmorWindow]: material on " + parts.Current.name + "could not find default shader/color");
                            }
                        }
                    }
            }
            oldVisualizer = Visualizer;
            oldHPvisualizer = HPvisualizer;
            oldHullVisualizer = HullVisualizer;
            oldLiftVisualizer = LiftVisualizer;
            oldTreeVisualizer = TreeVisualizer;
            refreshVisualizer = false;
            refreshHPvisualizer = false;
            refreshHullvisualizer = false;
        }

        float priceCkeckoout = 0;
        Dictionary<string, List<Part>> partLimitCheck = new Dictionary<string, List<Part>>();
        string boughtParts = "";
        string engineparts = "";
        int engineCount = 0;
        string blacklistedParts = "";
        bool nonCockpitWM = false;
        bool nonCockpitAI = false;
        //bool nonRootCockpit = false;
        bool notOnPriceList = false;
        int oversizedPWings = 0;
        float maxThrust = 0;
        int weaponmanagers = 0;
        int AIs = 0;
        ScreenMessage vessellegality = new ScreenMessage("", 7.0f, ScreenMessageStyle.LOWER_CENTER);

        void DoVesselLegalityChecks(bool refreshParts, bool buttonTest = false)
        {
            if ((BDArmorySettings.RUNWAY_PROJECT || BDArmorySettings.COMP_CONVENIENCE_CHECKS) && (CompSettings.CompBanChecksEnabled || CompSettings.CompPriceChecksEnabled || CompSettings.CompVesselChecksEnabled))
            {
                if (refreshParts)
                {
                    priceCkeckoout = 0;
                    partLimitCheck.Clear();
                    boughtParts = "";
                    engineparts = "";
                    engineCount = 0;
                    maxThrust = 0;
                    blacklistedParts = "";
                    nonCockpitWM = false;
                    nonCockpitAI = false;
                    //nonRootCockpit = false;
                    weaponmanagers = 0;
                    AIs = 0;
                    oversizedPWings = 0;

                    foreach (var part in EditorLogic.fetch.ship.Parts) //grab a list of parts and their quantity
                    {
                        if (partLimitCheck.TryGetValue(part.name, out var qty))
                            qty.Add(part);
                        else
                            partLimitCheck.Add(part.name, new List<Part> { part });
                    }
                    //begin evaluation
                    if (CompSettings.CompBanChecksEnabled) //do we have more limited parts than allowed?
                    {
                        foreach (var part in CompSettings.partBlacklist)
                        {
                            string partName = part.Key;
                            int listedpartCount = 0;
                            if (partName.Contains("*"))
                            {
                                partName = partName.Trim('*');

                                foreach (var kvp in partLimitCheck)
                                {
                                    if (kvp.Key.Contains(partName))
                                        listedpartCount += kvp.Value.Count;
                                }
                            }
                            else
                                if (partLimitCheck.TryGetValue(part.Key, out var qty))
                                    listedpartCount = qty.Count;
                            if (CompSettings.partBlacklist.TryGetValue(part.Key, out float bQ))
                            {
                                if (bQ >= 0 && listedpartCount > bQ)
                                {
                                    if (!string.IsNullOrEmpty(blacklistedParts)) blacklistedParts += " | ";
                                    blacklistedParts += $"{partName} parts({listedpartCount}/{bQ})"; //is the part on the black list? if so, add to string for messaging illegal parts
                                }
                                if (bQ < 0 && listedpartCount < Mathf.Abs(bQ))
                                {
                                    if (!string.IsNullOrEmpty(blacklistedParts)) blacklistedParts += " | ";
                                    blacklistedParts += $"{partName} missing({listedpartCount}/{Mathf.Abs(bQ)})"; //is the part on the white list? if so, add to string for messaging missing parts
                                }
                            }
                        }
                    }
                    //could just eval the placed part, but that doesn't cover symmetry or subassumblies
                    foreach (var kvp in partLimitCheck)
                    {
                        notOnPriceList = false;

                        if (CompSettings.CompPriceChecksEnabled && pointBuyBudget > 0)// budget check 
                        {
                            if (CompSettings.partPointCosts.TryGetValue(kvp.Key, out float pb)) //if the part is in the pricing list
                            {
                                if (!string.IsNullOrEmpty(boughtParts)) boughtParts += " | ";
                                boughtParts += $"{kvp.Value.Count}x {kvp.Value[0].partInfo.title}({kvp.Value.Count * pb})"; //make a note for later
                                priceCkeckoout += (kvp.Value.Count * pb); //and tally total budget spent so far
                            }
                            else
                            {
                                notOnPriceList = true;
                            }
                        }
                        foreach (var partModule in kvp.Value[0].Modules) //weapon whitelist/engine count
                        {
                            if (partModule == null) continue;
                            switch (partModule.moduleName)
                            {
                                case "ModuleEngines":
                                case "ModuleEnginesFX":
                                    {
                                        if (engineparts.Contains(kvp.Value[0].partInfo.title)) break; //don't grab both moduleEngines for dual-mode engines and double-count them
                                        if (CompSettings.CompVesselChecksEnabled && maxEngines < 999 || maxTWR > 0)
                                        {
                                            if (maxEngines < 999)
                                            {
                                                if (!string.IsNullOrEmpty(engineparts)) engineparts += " | ";
                                                engineparts += $"{kvp.Value.Count}x {kvp.Value[0].partInfo.title}";
                                                engineCount += kvp.Value.Count;
                                                Debug.Log($"[VesselCheckDebug] found {kvp.Value.Count} {kvp.Value[0].partInfo.title}");
                                            }
                                            if (maxTWR > 0) maxThrust += (kvp.Value[0].FindModuleImplementing<ModuleEngines>().maxThrust * kvp.Value[0].FindModuleImplementing<ModuleEngines>().thrustPercentage) * kvp.Value.Count;
                                        }
                                        break;
                                    }
                                case "ModuleWeapon":
                                case "MissileBase":
                                case "MissileLauncher":
                                    {
                                        if (pointBuyBudget > 0 && notOnPriceList) //if a weapon isn't on the price list, it's banned
                                        {
                                            if (!string.IsNullOrEmpty(blacklistedParts)) blacklistedParts += " | ";
                                            blacklistedParts += $"{kvp.Value[0].partInfo.title}({kvp.Value.Count}/0)";
                                        }
                                        break;
                                    }
                                case "MissileFire":
                                    {
                                        weaponmanagers += kvp.Value.Count;
                                        if (weaponmanagers > 1) //only 1 WM per vessel. TODO - remember to change this out if Doc ever gets mothership sub-WMs implemented fully
                                        {
                                            if (!string.IsNullOrEmpty(blacklistedParts)) blacklistedParts += " | ";
                                            blacklistedParts += $"{kvp.Value[0].partInfo.title}(WMs: {kvp.Value.Count}/1)";
                                        }
                                        /*
                                        if (kvp.Value[0].parent != EditorLogic.fetch.ship.Parts[0] || kvp.Value[0] != EditorLogic.fetch.ship.Parts[0])
                                        {
                                            nonCockpitWM = true;
                                        }
                                        */
                                        var isChair = kvp.Value[0].FindModuleImplementing<KerbalSeat>();
                                        if (isChair != null)
                                        {
                                            break;
                                        }
                                        ModuleCommand AIParent = null;
                                        if (kvp.Value[0].parent) AIParent = kvp.Value[0].parent.FindModuleImplementing<ModuleCommand>();
                                        if (AIParent == null)
                                        {
                                            nonCockpitWM = true;
                                        }
                                        break;
                                    }
                                case "BDModulePilotAI":
                                case "BDModuleSurfaceAI":
                                case "BDModuleVTOLAI":
                                case "BDModuleOrbitalAI":
                                    {
                                        AIs += kvp.Value.Count;
                                        if (AIs > 1) //only 1 WM per vessel. TODO - remember to change this out if Doc ever gets mothership sub-WMs implemented fully
                                        {
                                            if (!string.IsNullOrEmpty(blacklistedParts)) blacklistedParts += " | ";
                                            blacklistedParts += $"{kvp.Value[0].partInfo.title}(AI: {kvp.Value.Count}/1)";
                                        }
                                        //editorLogic.fetch.ship.parts[0] doesn't account for re-rooting the craft. fetch.ship also doesn't support .rootpart
                                        //if (kvp.Value[0].parent != EditorLogic.fetch.ship.Parts[0] || kvp.Value[0] != EditorLogic.fetch.ship.Parts[0])
                                        var isChair = kvp.Value[0].FindModuleImplementing<KerbalSeat>();
                                        if (isChair != null)
                                        {
                                            break;
                                        }
                                        ModuleCommand AIParent = null;
                                        if (kvp.Value[0].parent) AIParent = kvp.Value[0].parent.FindModuleImplementing<ModuleCommand>();
                                        if (AIParent == null)
                                        {
                                            nonCockpitAI = true;
                                        }
                                        break;
                                    }
                                case "ModuleCommand":
                                    {
                                        int crewCount = kvp.Value[0].FindModuleImplementing<ModuleCommand>().minimumCrew;
                                        if (crewCount <= 0)
                                        {
                                            if (!string.IsNullOrEmpty(blacklistedParts)) blacklistedParts += " | ";
                                            blacklistedParts += $"{kvp.Value[0].partInfo.title}(Probecore: {kvp.Value.Count}/0)";
                                        }
                                        /*
                                        if (kvp.Value[0] != EditorLogic.fetch.ship.Parts[0])
                                        {
                                            nonRootCockpit = true;
                                        }
                                        */
                                        break;
                                    }
                            }
                        }
                    }
                    if (BDArmorySettings.MAX_PWING_LIFT > 0)
                    {
                        foreach (var part in EditorLogic.fetch.ship.Parts) //not ideal, but this needs to fire onVesselModified, not onPartPlaced, but linking this to Visualizer's parts eval only updates when that does
                        {
                            if (part.name.Contains("B9.Aero.Wing.Procedural.Type"))
                            {
                                ModuleLiftingSurface wing = part.GetComponent<ModuleLiftingSurface>();
                                if (wing != null && wing.deflectionLiftCoeff > 0f)
                                {
                                    if (wing.deflectionLiftCoeff > BDArmorySettings.MAX_PWING_LIFT)
                                        oversizedPWings++;
                                }
                            }
                        }
                    }
                }
                StringBuilder evaluationstring = new StringBuilder();
                if (CompSettings.CompVesselChecksEnabled)
                {
                    CalculateTotalLift(); //update wing lading/lift stack values if GUI not open
                    if (maxPartCount > 0 && EditorLogic.fetch.ship.Parts.Count > maxPartCount)
                        evaluationstring.AppendLine($"{StringUtils.Localize("#LOC_BDArmory_ArmorToolPartCount")} ({EditorLogic.fetch.ship.Parts.Count}/{maxPartCount})"); //"Part count exceeded!"
                    if (engineCount > 0)
                    {
                        if (maxEngines >= 0 && engineCount > maxEngines)
                            evaluationstring.AppendLine($"{StringUtils.Localize("#LOC_BDArmory_ArmorToolEngineCount")} ({engineCount}/{maxEngines}) - {engineparts}"); //Too Many Engines:"
                        if (maxEngines < 0 && engineCount < Mathf.Abs(maxEngines))
                            evaluationstring.AppendLine($"{StringUtils.Localize("#LOC_BDArmory_ArmorToolEngineCountFloor")} ({engineCount}/{Mathf.Abs(maxEngines)})"); //"Too Few Engines:"
                    }
                    if (maxTWR > 0 && Math.Round(((maxThrust / (PhysicsGlobals.GravitationalAcceleration * FlightGlobals.GetHomeBody().GeeASL) * EditorLogic.fetch.ship.GetTotalMass())), 2) > maxLtW)
                        evaluationstring.AppendLine($"{StringUtils.Localize("#LOC_BDArmory_ArmorToolTWR")} {Math.Round(maxThrust / (EditorLogic.fetch.ship.GetTotalMass() * (PhysicsGlobals.GravitationalAcceleration * FlightGlobals.GetHomeBody().GeeASL)), 2)}/{maxTWR}"); //"TWR Exceeded:"
                    if (maxLtW > 0 && wingLoadingWet > maxLtW)
                        evaluationstring.AppendLine($"{StringUtils.Localize("#LOC_BDArmory_ArmorToolLTW")} {wingLoadingWet}/{maxLtW}"); //"LTW Exceeded:"
                    if (maxStacking > 0 && totalLiftStackRatio * 100 > maxStacking)
                        evaluationstring.AppendLine($"{StringUtils.Localize("#LOC_BDArmory_ArmorLiftStacking")}: {Mathf.RoundToInt(totalLiftStackRatio * 100)}/{maxStacking}%"); //"Lift Stacking"
                    if (maxMass > 0 && EditorLogic.fetch.ship.GetTotalMass() > maxMass)
                        evaluationstring.AppendLine($"{StringUtils.Localize("#LOC_BDArmory_ArmorToolMaxMass")} {EditorLogic.fetch.ship.GetTotalMass()}/{maxMass}"); //"Maxx Limit Exceeded:"
                    //max Dimensions?
                }
                if (CompSettings.CompPriceChecksEnabled && pointBuyBudget > 0)
                {
                    if (priceCkeckoout > pointBuyBudget)
                        evaluationstring.AppendLine($"{StringUtils.Localize("#LOC_BDArmory_ArmorToolMaxPoints")} ({priceCkeckoout}/{pointBuyBudget}) - {boughtParts}"); //Point Limit Exceeded:
                }
                if (CompSettings.CompVesselChecksEnabled || CompSettings.CompBanChecksEnabled)
                {
                    if (!string.IsNullOrEmpty(blacklistedParts))
                        evaluationstring.AppendLine($"{StringUtils.Localize("#LOC_BDArmory_ArmorToolIllegalParts")} - {blacklistedParts}"); //"Illegal Parts:"
                }

                if (nonCockpitAI || nonCockpitWM) // || nonRootCockpit)
                {
                    string commandStatus = "";
                    if (nonCockpitAI) commandStatus += StringUtils.Localize("#LOC_BDArmory_Settings_DebugAI"); //"AI"
                    if (nonCockpitWM)
                    {
                        if (!string.IsNullOrEmpty(commandStatus)) commandStatus += ", ";
                        commandStatus += StringUtils.Localize("#LOC_BDArmory_WMWindow_title"); //"BDA Weapon Manager"
                    }
                    commandStatus += $" {(StringUtils.Localize("#LOC_BDArmory_ArmorToolNonCockpit"))}"; //"not attached to cockpit"
                    //if (nonRootCockpit)
                    //{
                    //    commandStatus += ", which is not a cockpit.";
                    //}
                    evaluationstring.AppendLine(commandStatus);
                }

                if (buttonTest)
                {
                    if (evaluationstring.Length == 0)
                        evaluationstring.AppendLine(StringUtils.Localize("#LOC_BDArmory_ArmorToolVesselLegal")); //"Vessel Legal!"
                }
                if (oversizedPWings > 0)
                    evaluationstring.AppendLine($"{oversizedPWings} {StringUtils.Localize("#LOC_BDArmory_ArmorToolOversizedPWings")}"); //"pWings exceedeing max Lift - check Lift Visualize"
                ScreenMessages.RemoveMessage(vessellegality);
                vessellegality.textInstance = null;
                vessellegality.message = evaluationstring.ToString();
                vessellegality.style = ScreenMessageStyle.UPPER_CENTER;

                ScreenMessages.PostScreenMessage(vessellegality);
                //todo - draw a GUI line to each illegal part?
            }
        }

        private void CalculateArmorStats()
        {
            if (selectedArmor == "Mild Steel")
            {
                relValue = 1;
            }
            else
            {
                /*float bulletEnergy = ProjectileUtils.CalculateProjectileEnergy(0.388f, 1109);
                var modifiedCaliber = (15) + (15) * (2f * ArmorDuctility * ArmorDuctility);
                float yieldStrength = modifiedCaliber * modifiedCaliber * Mathf.PI / 100f * ArmorStrength * (ArmorDensity / 7850f) * 30;
                if (ArmorDuctility > 0.25f)
                {
                    yieldStrength *= 0.7f;
                }
                float newCaliber = ProjectileUtils.CalculateDeformation(yieldStrength, bulletEnergy, 30, 1109, 1176, 7850, 0.19f, 0.8f, false);
                */
                //armorValue = ProjectileUtils.CalculatePenetration(30, newCaliber, 0.388f, 1109, ArmorDuctility, ArmorDensity, ArmorStrength, 30, 0.8f, false);
                armorValue = ProjectileUtils.CalculatePenetration(30, 1109, 0.388f, 0.8f, ArmorStrength, ArmorVfactor, ArmorMu1, ArmorMu2, ArmorMu3); //why is this hardcoded? it needs to be the selected armor mat's vars
                relValue = BDAMath.RoundToUnit(armorValue / steelValue, 0.1f);
                exploValue = ArmorStrength * (1 + ArmorDuctility) * (ArmorDensity / 1000);
            }
        }
    }
}