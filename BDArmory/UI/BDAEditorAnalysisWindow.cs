using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using KSP.UI.Screens;
using UnityEngine;

using BDArmory.CounterMeasure;
using BDArmory.Radar;
using BDArmory.Settings;
using BDArmory.Utils;

namespace BDArmory.UI
{
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    internal class BDAEditorAnalysisWindow : MonoBehaviour
    {
        public static BDAEditorAnalysisWindow Instance = null;
        private ApplicationLauncherButton toolbarButton = null;

        private bool showRcsWindow = false;
        private Rect windowRect = new Rect(300, 150, !BDArmorySettings.ASPECTED_RCS ? 650 : 670, 500);

        private bool takeSnapshot = false;
        private float rcsReductionFactor;
        private float rcsOverride = -1;
        private float rcsGCF = 1.0f;
        private static int rcsElevationIndex = -1;
        private float[] rcsElevations = [-90f, -45f, -20f, -10f, -5f, -2.5f, 0f, 2.5f, 5f, 10f, 20f, 45f, 90f];

        private ModuleRadarSensorBase[] radars;
        private GUIContent[] radarsGUI;
        private GUIContent radarBoxText;
        private BDGUIComboBox radarBox;
        private int previous_index = -1;
        private string text_detection;
        private string text_locktrack;
        private string text_sonar;
        private bool bLandedSplashed;

        // Cache variables to avoid regenerating the texture every frame
        private Texture2D cachedPolarPlot = null;
        private float cachedPolarPlotElevation = float.MinValue;
        private float cachedMaxRCS = 1f;

        void Awake()
        {
            if (Instance != null) Destroy(Instance);
            Instance = this;
        }

        void Start()
        {
            AddToolbarButton();

            RadarUtils.SetupResources();
            GameEvents.onEditorShipModified.Add(OnEditorShipModifiedEvent);
        }

        private void FillRadarList()
        {
            radars = BDAEditorTools.getRadars().ToArray();

            // first pass, then sort
            for (int i = 0; i < radars.Length; i++)
            {
                if (string.IsNullOrEmpty(radars[i].sensorName)) radars[i].sensorName = (radars[i].part == null ? null : radars[i].part.partInfo == null ? null : radars[i].part.partInfo.title);
                GUIContent gui = new GUIContent(radars[i].sensorName);
            }
            Array.Sort(radars, delegate (ModuleRadarSensorBase r1, ModuleRadarSensorBase r2) { return r1.sensorName.CompareTo(r2.sensorName); });

            // second pass to copy
            radarsGUI = new GUIContent[radars.Length];
            for (int i = 0; i < radars.Length; i++)
            {
                GUIContent gui = new GUIContent(radars[i].sensorName);
                radarsGUI[i] = gui;
            }

            radarBoxText = new GUIContent();
            radarBoxText.text = "Select Radar... **";
        }

        private void OnEditorShipModifiedEvent(ShipConstruct data)
        {
            if (data is null) return;
            delayedTakeSnapShot = true;
            if (!delayedTakeSnapShotInProgress)
                StartCoroutine(DelayedTakeSnapShot(data));
        }

        private bool delayedTakeSnapShot = false;
        private bool delayedTakeSnapShotInProgress = false;
        IEnumerator DelayedTakeSnapShot(ShipConstruct ship)
        {
            delayedTakeSnapShotInProgress = true;
            var wait = new WaitForFixedUpdate();
            while (delayedTakeSnapShot) // Wait until ship modified events stop coming.
            {
                delayedTakeSnapShot = false;
                yield return wait;
            }
            yield return new WaitUntilFixed(() =>
                ship == null || ship.Parts == null || ship.Parts.TrueForAll(p =>
                {
                    if (p == null) return true;
                    var hp = p.GetComponent<Damage.HitpointTracker>();
                    return hp == null || hp.Ready;
                })); // Wait for HP changes to delayed ship modified events in HitpointTracker
            delayedTakeSnapShotInProgress = false;
            takeSnapshot = true;
            previous_index = -1;
        }

        private void OnDestroy()
        {
            GameEvents.onEditorShipModified.Remove(OnEditorShipModifiedEvent);
            RadarUtils.CleanupResources();
            HideToolbarGUINow();

            if (toolbarButton)
            {
                ApplicationLauncher.Instance.RemoveModApplication(toolbarButton);
                toolbarButton = null;
            }

            if (cachedPolarPlot != null) Destroy(cachedPolarPlot);
        }

        void AddToolbarButton()
        {
            if (!HighLogic.LoadedSceneIsEditor) return;
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
            Texture buttonTexture = GameDatabase.Instance.GetTexture(BDArmorySetup.textureDir + "icon_rcs", false);
            toolbarButton = ApplicationLauncher.Instance.AddModApplication(ShowToolbarGUI, HideToolbarGUI, Dummy, Dummy, Dummy, Dummy, ApplicationLauncher.AppScenes.SPH | ApplicationLauncher.AppScenes.VAB, buttonTexture);
        }

        public void ShowToolbarGUI()
        {
            showRcsWindow = true;
            takeSnapshot = true;
        }

        // Doing it this way prevents OnGUI events from below the window from being triggered by the window disappearing.
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
            showRcsWindow = false;
            takeSnapshot = false;
            GUIUtils.PreventClickThrough(windowRect, "BDARCSLOCK", true);
        }

        void Dummy()
        { }

        void OnGUI()
        {
            if (showRcsWindow)
            {
                string windowTitle = !BDArmorySettings.ASPECTED_RCS ? "BDArmory Radar Cross Section Analysis (Worst Three Aspects)" : "BDArmory Radar Cross Section Analysis (Front/Side/Polar Plot)";
                if (BDArmorySettings.UI_SCALE_ACTUAL != 1) GUIUtility.ScaleAroundPivot(BDArmorySettings.UI_SCALE_ACTUAL * Vector2.one, windowRect.position);
                windowRect = GUI.Window(GUIUtility.GetControlID(FocusType.Passive), windowRect, WindowRcs, windowTitle, BDArmorySetup.BDGuiSkin.window);
            }
        }
        void WindowRcs(int windowID)
        {
            GUIUtils.PreventClickThrough(windowRect, "BDARCSLOCK");
            if (GUI.Button(new Rect(windowRect.width - 18, 2, 16, 16), " X", BDArmorySetup.CloseButtonStyle))
            {
                toolbarButton.SetFalse();
            }

            GUI.Label(new Rect(10, 40, 200, 20), $"Az {RadarUtils.editorRCSAspects[0, 0].ToString("0")}, El {RadarUtils.editorRCSAspects[0, 1].ToString("0")}", BDArmorySetup.SelectedButtonStyle);
            GUI.Label(new Rect(220, 40, 200, 20), $"Az {RadarUtils.editorRCSAspects[1, 0].ToString("0")}, El {RadarUtils.editorRCSAspects[1, 1].ToString("0")}", BDArmorySetup.SelectedButtonStyle);
            GUI.Label(new Rect(430, 40, 200, 20), BDArmorySettings.ASPECTED_RCS ? "RCS Polar Plot" :
            $"Az {RadarUtils.editorRCSAspects[2, 0].ToString("0")}, El {RadarUtils.editorRCSAspects[2, 1].ToString("0")}", BDArmorySetup.SelectedButtonStyle);

            // Optimization: Check if we need to regenerate the plot data before resetting the flag
            bool needRegen = takeSnapshot;
            if (takeSnapshot)
                takeRadarSnapshot();

            // Draw renderings
            GUI.DrawTexture(new Rect(10, 70, 200, 200), RadarUtils.GetTexture1, ScaleMode.StretchToFill);
            GUI.DrawTexture(new Rect(220, 70, 200, 200), RadarUtils.GetTexture2, ScaleMode.StretchToFill);
            float selectedElevation = 0f;
            if (BDArmorySettings.ASPECTED_RCS)
            {
                int maxSliderIndex = rcsElevations.Length - 1;

                // Define the plot area
                Rect plotRect = new Rect(430, 70, 200, 200);

                // Define the slider area (to the right of the plot)
                Rect sliderRect = new Rect(plotRect.x + plotRect.width + 10, plotRect.y, 20, plotRect.height);

                // Initialize to the first valid index if rcsElevationIndex is still at its invalid default (-1)
                if (rcsElevationIndex == -1)
                {
                    if ((rcsElevationIndex = rcsElevations.IndexOf(0f)) == -1)
                        rcsElevationIndex = rcsElevations.Length / 2;
                }

                // Draw Vertical Slider
                rcsElevationIndex = Mathf.Clamp(Mathf.RoundToInt(GUI.VerticalSlider(sliderRect, rcsElevationIndex, maxSliderIndex, 0)), 0, maxSliderIndex);

                // Calculate selected elevation
                selectedElevation = rcsElevations[rcsElevationIndex];

                // Optimization: Only regenerate texture if necessary (data changed or elevation changed)
                float maxRcs = 1f;
                if (needRegen || cachedPolarPlot == null || Mathf.Abs(cachedPolarPlotElevation - selectedElevation) > 0.01f)
                {
                    if (cachedPolarPlot != null) Destroy(cachedPolarPlot);
                    cachedPolarPlot = GenerateRCSPolarPlot(RadarUtils.RCSMatrix, selectedElevation, RadarUtils.GetTexture3, out maxRcs);
                    cachedPolarPlotElevation = selectedElevation;
                    cachedMaxRCS = maxRcs;
                }

                GUI.DrawTexture(plotRect, cachedPolarPlot, ScaleMode.StretchToFill);

                // Draw Labels for Concentric Circles
                int numCircles = 5;
                float stepRcs = cachedMaxRCS / numCircles;
                float centerX = plotRect.x + plotRect.width / 2;
                float centerY = plotRect.y + plotRect.height / 2;
                float maxPlotRadius = (plotRect.width / 2) - 10;

                for (int i = 1; i <= numCircles; i++)
                {
                    float rcsVal = i * stepRcs;
                    float normalizedRcs = rcsVal / cachedMaxRCS;
                    float radius = normalizedRcs * maxPlotRadius;

                    string labelText = rcsVal.ToString("F1");

                    // Vertical Axis Labels (Y-Axis)
                    // Centered horizontally on the plot, positioned at radius distance vertically
                    float labelX = centerX + 2;

                    // Top Label (North/Up)
                    GUI.Label(new Rect(labelX, centerY - radius - 13, 40, 20), labelText);
                }
            }
            else
                GUI.DrawTexture(new Rect(430, 70, 200, 200), RadarUtils.GetTexture3, ScaleMode.StretchToFill);

            float editorUIRCS0 = (!BDArmorySettings.ASPECTED_RCS) ? RadarUtils.editorRCSAspects[0, 2] : (RadarUtils.editorRCSAspects[0, 2] * (1 - BDArmorySettings.ASPECTED_RCS_OVERALL_RCS_WEIGHT) + RadarUtils.rcsTotal * BDArmorySettings.ASPECTED_RCS_OVERALL_RCS_WEIGHT);
            float editorUIRCS1 = (!BDArmorySettings.ASPECTED_RCS) ? RadarUtils.editorRCSAspects[1, 2] : (RadarUtils.editorRCSAspects[1, 2] * (1 - BDArmorySettings.ASPECTED_RCS_OVERALL_RCS_WEIGHT) + RadarUtils.rcsTotal * BDArmorySettings.ASPECTED_RCS_OVERALL_RCS_WEIGHT);
            float editorUIRCS2 = (!BDArmorySettings.ASPECTED_RCS) ? RadarUtils.editorRCSAspects[2, 2] : (RadarUtils.editorRCSAspects[2, 2] * (1 - BDArmorySettings.ASPECTED_RCS_OVERALL_RCS_WEIGHT) + RadarUtils.rcsTotal * BDArmorySettings.ASPECTED_RCS_OVERALL_RCS_WEIGHT);

            GUI.Label(new Rect(10, 275, 200, 20), RadarUtils.RCSString(editorUIRCS0), BDArmorySetup.BDGuiSkin.label);
            GUI.Label(new Rect(220, 275, 200, 20), RadarUtils.RCSString(editorUIRCS1), BDArmorySetup.BDGuiSkin.label);
            GUI.Label(new Rect(430, 275, 200, 20), BDArmorySettings.ASPECTED_RCS ? $"RCS (m²) at {selectedElevation.ToString("F1")} deg El" : RadarUtils.RCSString(editorUIRCS2), BDArmorySetup.BDGuiSkin.label);


            GUIStyle style = BDArmorySetup.BDGuiSkin.label;
            style.fontStyle = FontStyle.Bold;
            GUI.Label(new Rect(10, 300, 600, 20), "Base radar cross section for vessel: " + RadarUtils.RCSString(RadarUtils.rcsTotal) + " (without ECM/countermeasures)", style);
            GUI.Label(new Rect(10, 320, 600, 20), "Total radar cross section for vessel: " + RadarUtils.RCSString(rcsOverride > 0 ? rcsOverride * rcsGCF : RadarUtils.rcsTotal * rcsReductionFactor * rcsGCF) + " (with RCS reduction/stealth/ground clutter)", style);

            style.fontStyle = FontStyle.Normal;
            GUI.Label(new Rect(10, 380, 600, 20), "** (Range evaluation not accounting for ECM/countermeasures)", style);
            GUI.Label(new Rect(10, 410, 600, 20), text_detection, style);
            GUI.Label(new Rect(10, 430, 600, 20), text_locktrack, style);
            GUI.Label(new Rect(10, 450, 600, 20), text_sonar, style);

            bool bNewValue = GUI.Toggle(new Rect(490, 348, 150, 20), bLandedSplashed, "Splashed/Landed", BDArmorySetup.BDGuiSkin.toggle);

            if (radars == null)
            {
                FillRadarList();
                GUIStyle listStyle = new(BDArmorySetup.ButtonStyle)
                {
                    fixedHeight = 18 //make list contents slightly smaller
                };
                radarBox = new BDGUIComboBox(new Rect(10, 350, 450, 20), new Rect(10, 350, 450, 20), radarBoxText, radarsGUI, 124, listStyle);
            }

            int selected_index = radarBox.Show();

            if ((selected_index != previous_index) || (bNewValue != bLandedSplashed))
            {
                text_sonar = "";
                bLandedSplashed = bNewValue;

                // selected radar changed - evaluate craft RCS against this radar
                if (selected_index != -1)
                {
                    var selected_radar = radars[selected_index];

                    // ground clutter factor from radar
                    if (bLandedSplashed)
                        rcsGCF = selected_radar.radarGroundClutterFactor;
                    else
                        rcsGCF = 1.0f;

                    if (selected_radar.CanScan)
                    {
                        for (float distance = selected_radar.radarMaxDistanceDetect; distance >= 0; distance--)
                        {
                            text_detection = $"Detection: undetectable by this radar.";
                            if (selected_radar.radarDetectionCurve.Evaluate(distance) <= (rcsOverride > 0 ? rcsOverride * rcsGCF : RadarUtils.rcsTotal * rcsReductionFactor * rcsGCF))
                            {
                                text_detection = $"Detection: detected at {distance} km and closer";
                                break;
                            }
                        }
                    }
                    else
                    {
                        text_detection = "Detection: This radar does not have detection capabilities.";
                    }

                    if (selected_radar.CanLock)
                    {
                        text_locktrack = $"Lock/Track: untrackable by this radar.";

                        ModuleRadar tempRad = selected_radar as ModuleRadar;

                        for (float distance = tempRad.radarMaxDistanceLockTrack; distance >= 0; distance--)
                        {
                            if (tempRad.radarLockTrackCurve.Evaluate(distance) <= (rcsOverride > 0 ? rcsOverride * rcsGCF : RadarUtils.rcsTotal * rcsReductionFactor * rcsGCF))
                            {
                                text_locktrack = $"Lock/Track: tracked at {distance} km and closer";
                                break;
                            }
                        }
                    }
                    else
                    {
                        text_locktrack = "Lock/Track: This radar does not have locking/tracking capabilities.";
                    }

                    if (selected_radar.getRWRType(selected_radar.rwrThreatType) == "SONAR")
                        text_sonar = "SONAR - will only be able to detect/track splashed or submerged vessels!";
                }
            }
            previous_index = selected_index;

            GUIUtils.DragWindow();
            GUIUtils.RepositionWindow(ref windowRect);
        }

        void takeRadarSnapshot()
        {
            if (EditorLogic.RootPart == null)
                return;

            // Encapsulate editor ShipConstruct into a vessel:
            Vessel v = new Vessel();
            v.parts = EditorLogic.fetch.ship.Parts;
            v.vesselType = VesselType.Plane; // Tell KSP that it's not debris (which we ignore in the snapshot).
            // RadarUtils.RenderVesselRadarSnapshot(v, EditorLogic.RootPart.transform);  //first rendering for true RCS
            RadarUtils.RenderVesselRadarSnapshot(v, EditorLogic.RootPart.transform, null, true);  //create renders
            takeSnapshot = false;

            // get RCS reduction measures (stealth/low observability)
            rcsReductionFactor = 1.0f;

            int rcsCount = 0;
            using List<Part>.Enumerator parts = EditorLogic.fetch.ship.Parts.GetEnumerator();
            while (parts.MoveNext())
            {
                ModuleECMJammer rcsJammer = parts.Current.GetComponent<ModuleECMJammer>();
                if (rcsJammer != null)
                {
                    if (rcsJammer.rcsReduction)
                    {
                        rcsReductionFactor *= rcsJammer.rcsReductionFactor;
                        rcsCount++;
                        if (rcsOverride < rcsJammer.rcsOverride) rcsOverride = rcsJammer.rcsOverride;
                    }
                }
            }

            if (rcsCount > 0)
                rcsReductionFactor = Mathf.Max((rcsReductionFactor * rcsCount), 0.0f);    //same formula as in VesselECMJInfo must be used here!
        }

        public static Texture2D GenerateRCSPolarPlot(float[,] rcsMatrix, float el, Texture2D aircraft, out float maxRcs)
        {
            // 1. Validate inputs
            if (rcsMatrix == null || aircraft == null)
            {
                Debug.LogError("Invalid input: rcsMatrix or aircraft is null.");
                maxRcs = 1f;
                return null;
            }

            int width = aircraft.width;
            int height = aircraft.height;

            // Ensure the texture is read/write enabled
            if (!aircraft.isReadable)
            {
                Debug.LogWarning("Aircraft texture is not readable. Generating a readable copy.");
                RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(aircraft, rt);
                Texture2D readableCopy = new Texture2D(width, height);
                readableCopy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                readableCopy.Apply();
                RenderTexture.ReleaseTemporary(rt);
                aircraft = readableCopy;
            }

            // 2. Process Aircraft Image (Invert Colors)
            Color[] aircraftPixels = aircraft.GetPixels();
            Color[] processedPixels = new Color[aircraftPixels.Length];

            for (int i = 0; i < aircraftPixels.Length; i++)
            {
                Color original = aircraftPixels[i];

                // Invert RGB
                float r = 1f - original.r;
                float g = 1f - original.g;
                float b = 1f - original.b;

                // Preserve detail in dark areas (make black medium grey)
                float brightness = (r + g + b) / 3f;
                if (brightness < 0.1f)
                {
                    r = g = b = 0.5f;
                }

                processedPixels[i] = new Color(r, g, b, 1f);
            }

            Texture2D processedAircraftTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            processedAircraftTex.SetPixels(processedPixels);
            processedAircraftTex.Apply();

            // 3. Generate RCS Data using RadarUtils.RCSMatrixEval from 0 to 360 degrees
            List<(float azimuth, float rcs)> validData = new List<(float azimuth, float rcs)>();

            // Iterate azimuth from -180 to 180 with a step of 5
            for (int az = -180; az <= 180; az += 5)
            {
                // Calculate RCS using the matrix eval assuming left/right symmetry
                float rcs = RadarUtils.RCSMatrixEval(rcsMatrix, RadarUtils.rcsTotal, Mathf.Abs(az), el);
                validData.Add((az, rcs));
            }

            // Note: Sorting is not strictly necessary as the loop generates sequential data,
            // but retained if future logic requires specific ordering guarantees.
            // validData.Sort((a, b) => a.azimuth.CompareTo(b.azimuth));

            // 4. Setup Plot Parameters
            int centerX = width / 2;
            int centerY = height / 2;

            maxRcs = validData.Max(d => d.rcs);
            float minRcs = 0f;
            float range = maxRcs - minRcs;
            if (range == 0) range = 1f;

            int maxPlotRadius = Mathf.Min(width, height) / 2 - 10;

            float aircraftScaleFactor = 0.3f;
            int aircraftMaxRadius = Mathf.RoundToInt(maxPlotRadius * aircraftScaleFactor);

            Texture2D finalTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color[] finalPixels = new Color[width * height];

            // Initialize with black background (can also do transparent)
            for (int i = 0; i < finalPixels.Length; i++)
            {
                finalPixels[i] = new Color(0f, 0f, 0f, 1f); // 0f on last for transparent, 1f for opaque
            }

            void DrawLineOnArray(int x0, int y0, int x1, int y1, Color color)
            {
                int dx = Mathf.Abs(x1 - x0);
                int dy = Mathf.Abs(y1 - y0);
                int sx = x0 < x1 ? 1 : -1;
                int sy = y0 < y1 ? 1 : -1;
                int err = dx - dy;

                while (true)
                {
                    if (x0 >= 0 && x0 < width && y0 >= 0 && y0 < height)
                    {
                        finalPixels[y0 * width + x0] = color;
                    }

                    if (x0 == x1 && y0 == y1) break;

                    int e2 = 2 * err;
                    if (e2 > -dy) { err -= dy; x0 += sx; }
                    if (e2 < dx) { err += dx; y0 += sy; }
                }
            }

            void DrawCircleOnArray(int cx, int cy, int radius, Color color)
            {
                int x = 0;
                int y = radius;
                int d = 1 - radius;

                void AddPoint(int px, int py)
                {
                    if (px >= 0 && px < width && py >= 0 && py < height)
                        finalPixels[py * width + px] = color;
                }

                while (x <= y)
                {
                    AddPoint(cx + x, cy + y);
                    AddPoint(cx + y, cy + x);
                    AddPoint(cx - y, cy + x);
                    AddPoint(cx - x, cy + y);
                    AddPoint(cx - x, cy - y);
                    AddPoint(cx - y, cy - x);
                    AddPoint(cx + y, cy - x);
                    AddPoint(cx + x, cy - y);

                    if (d < 0)
                    {
                        d += 2 * x + 3;
                    }
                    else
                    {
                        d += 2 * (x - y) + 5;
                        y--;
                    }
                    x++;
                }
            }

            // --- Step A: Scale and Draw Aircraft ---
            int scaledWidth = aircraftMaxRadius * 2;
            int scaledHeight = aircraftMaxRadius * 2;

            if (scaledWidth <= 0) scaledWidth = 1;
            if (scaledHeight <= 0) scaledHeight = 1;

            // No need to create a separate texture for scaling, we can just map pixels directly
            Color[] srcPixels = processedAircraftTex.GetPixels();
            int srcW = processedAircraftTex.width;
            int srcH = processedAircraftTex.height;

            int startX = centerX - aircraftMaxRadius;
            int startY = centerY - aircraftMaxRadius;
            int endX = centerX + aircraftMaxRadius;
            int endY = centerY + aircraftMaxRadius;

            float scaleX = (float)srcW / scaledWidth;
            float scaleY = (float)srcH / scaledHeight;

            for (int y = startY; y < endY; y++)
            {
                for (int x = startX; x < endX; x++)
                {
                    if (x < 0 || x >= width || y < 0 || y >= height) continue;

                    float srcXFloat = (x - startX) * scaleX;
                    float srcYFloat = (y - startY) * scaleY;

                    int srcX = Mathf.Clamp(Mathf.FloorToInt(srcXFloat), 0, srcW - 1);
                    int srcY = Mathf.Clamp(Mathf.FloorToInt(srcYFloat), 0, srcH - 1);

                    Color planeColor = srcPixels[srcY * srcW + srcX];
                    float brightness = (planeColor.r + planeColor.g + planeColor.b) / 3f;

                    // Keep non-white background pixels
                    if (brightness < 0.95f)
                    {
                        finalPixels[y * width + x] = planeColor;
                    }
                }
            }

            // --- Step B: Draw Grid ---
            Color gridColor = new Color(1f, 1f, 1f, 1f); // Solid White

            DrawLineOnArray(centerX, 0, centerX, height - 1, gridColor);
            DrawLineOnArray(0, centerY, width - 1, centerY, gridColor);

            // --- Step B (Continued): Draw Concentric Circles and Label Them ---
            int numCircles = 5;
            float stepRcs = range / numCircles;

            for (int i = 1; i <= numCircles; i++)
            {
                float rcsVal = minRcs + (i * stepRcs);
                float normalizedRcs = (rcsVal - minRcs) / range;
                int radius = Mathf.RoundToInt(normalizedRcs * maxPlotRadius);

                DrawCircleOnArray(centerX, centerY, radius, gridColor);

                // Draw tick marks
                int tickX = centerX - radius;
                if (tickX >= 0) DrawLineOnArray(tickX, centerY - 3, tickX, centerY + 3, gridColor);

                int tickX2 = centerX + radius;
                if (tickX2 < width) DrawLineOnArray(tickX2, centerY - 3, tickX2, centerY + 3, gridColor);
            }

            // --- Step C: Draw the RCS Plot Data ---
            Color plotColor = new Color(1f, 0f, 0f, 1f);

            for (int i = 0; i < validData.Count; i++)
            {
                float azimuth = validData[i].azimuth;
                float rcs = validData[i].rcs;

                float rad = Mathf.Deg2Rad * azimuth;
                float normalizedRcs = (rcs - minRcs) / range;
                int r = Mathf.RoundToInt(normalizedRcs * maxPlotRadius);

                if (r > maxPlotRadius) r = maxPlotRadius;

                int x = Mathf.RoundToInt(centerX + r * Mathf.Sin(rad));
                int y = Mathf.RoundToInt(centerY + r * Mathf.Cos(rad));

                if (x >= 0 && x < width && y >= 0 && y < height)
                {
                    finalPixels[y * width + x] = plotColor;
                }

                int nextIndex = (i + 1) % validData.Count;
                float nextAzimuth = validData[nextIndex].azimuth;
                float nextRcs = validData[nextIndex].rcs;
                float nextRad = Mathf.Deg2Rad * nextAzimuth;
                float nextNormalizedRcs = (nextRcs - minRcs) / range;
                int nextR = Mathf.RoundToInt(nextNormalizedRcs * maxPlotRadius);
                if (nextR > maxPlotRadius) nextR = maxPlotRadius;

                int nextX = Mathf.RoundToInt(centerX + nextR * Mathf.Sin(nextRad));
                int nextY = Mathf.RoundToInt(centerY + nextR * Mathf.Cos(nextRad));

                DrawLineOnArray(nextX, nextY, x, y, plotColor);
            }

            finalTexture.SetPixels(finalPixels);
            finalTexture.Apply();

            DestroyImmediate(processedAircraftTex);

            return finalTexture;
        }
    } //EditorRCsWindow
}
