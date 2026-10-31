using BDArmory.Control;
using BDArmory.Extensions;
using BDArmory.Radar;
using BDArmory.Settings;
using BDArmory.Targeting;
using BDArmory.UI;
using BDArmory.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace BDArmory.Modules
{
    public class ModuleMAD : BDAPartModule
    {
        [KSPField]
        public string sensorName;

        [KSPField]
        public float range = 1000f;
        double rangeSqr;

        [KSPField]
        public double ownSignalAttenuation = 0.8d;

        [KSPField]
        public double noiseFloor = 0.000025d;

        [KSPField]
        public double ownSignalNoiseFactor = 0.0001d;

        [KSPField]
        public double minimumSensitivity = 0.0000001d;

        [KSPField(isPersistant = true)]
        public bool sensorEnabled = true;

        static readonly Texture2D MADTexture = GameDatabase.Instance.GetTexture(BDArmorySetup.textureDir + "MADSensorTexture", false);

        static GUIStyle scaleStyleLeft;
        static GUIStyle scaleStyleRight;

        public MissileFire WeaponManager
        {
            get
            {
                if (field && field.vessel == vessel) return field;

                field = ActiveController.GetActiveController(vessel).WM;
                return field;
            }
        }

        static bool WindowRectMADInitialized = false;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (scaleStyleLeft == null)
            {
                scaleStyleLeft = new GUIStyle
                {
                    normal = { textColor = new Color(0, 1, 0, 0.75f) },
                    alignment = TextAnchor.UpperLeft
                };
                scaleStyleRight = new GUIStyle
                {
                    normal = { textColor = new Color(0, 1, 0, 0.75f) },
                    alignment = TextAnchor.UpperRight
                };
            }
            part.force_activate();

            rangeSqr = range * range;
            if (minimumSensitivity < minSens)
            {
                Debug.LogWarning($"[BDArmory.ModuleMAD] Your minimum sensitivity of {minimumSensitivity} is lower than {minSens}, this a fairly extreme value and is likely unnecessary, consider using {minSens} or higher.");
            }
            pastData = new Vector2d[Mathf.CeilToInt(1.1f * currHistWindow / Time.fixedDeltaTime)];

            if (!WindowRectMADInitialized)
            {
                BDArmorySetup.WindowRectMAD = new Rect(BDArmorySetup.WindowRectMAD.x, BDArmorySetup.WindowRectMAD.y, MADDisplayRect.height + BorderSize, MADDisplayRect.height + BorderSize + HeaderSize);
                WindowRectMADInitialized = true;
            }

            using (var mf = VesselModuleRegistry.GetMissileFires(vessel).GetEnumerator())
                while (mf.MoveNext())
                {
                    if (mf.Current == null) continue;
                    mf.Current.MADSensor = this; // Set the rwr on all weapon managers to this.
                }
        }

        Vector2d[] pastData;
        int currDataIndex = 0;
        float currHistWindow = 30f;
        int currHistWindowIndex = 4;
        static readonly float[] histWindowOptions = [ 5f, 10f, 15f, 20f, 30f, 60f ];
        const float gap = 2;
        const float buttonHeight = 18;

        public void UpdateDataArray()
        {
            int newDataLength = Mathf.CeilToInt(1.1f * currHistWindow / Time.fixedDeltaTime);
            int currDataLength = pastData.Length;
            if (currDataLength > newDataLength) return; // If data array is already long enough, return

            // Otherwise, resize data array
            Vector2d[] newData = new Vector2d[newDataLength];
            int lengthToEnd = currDataLength - currDataIndex;
            // Copy from currDataIndex to the end of the array
            Array.Copy(pastData, currDataIndex, newData, 0, lengthToEnd);
            // And then from the start of the array to currDataIndex
            if (currDataIndex != 0)
            {
                Array.Copy(pastData, 0, newData, lengthToEnd - 1, currDataIndex);
            }

            /*int start = currDataIndex - 1;
            if (start < 0)
            {
                start = currDataLength - 1;
            }
            int currCounter = 0;
            float currTime = Time.time;
            for (int i = currDataIndex; i != currDataIndex; ++i)
            {
                if (i >= pastData.Length)
                {
                    i = 0;
                }

                if ((pastData[i].x + currHistWindow) < currTime)
                {
                    continue;
                }

                newData[currCounter++] = pastData[i];
            }*/

            pastData = newData;

            Debug.Log($"[BDArmory.ModuleMAD] Completed array resizing. currDataLength: {currDataLength}, newDataLength: {newDataLength}, currDataIndex: {currDataIndex}, lengthToEnd: {lengthToEnd}");

            // This copies the whole of currDataLength into the new array, in proper order, and thus
            // means our current index is currDataLength - 1
            //currDataIndex = currCounter + 1;
            currDataIndex = currDataLength - 1;
        }

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();

            if (!(HighLogic.LoadedSceneIsFlight && FlightGlobals.ready)) return;
            if (!sensorEnabled) return;

            Vector2d currData = new Vector2d(Time.time, GetMagneticSignal());
            double tempRange = Math.Abs(currData.y);
            if (tempRange > currDisplayRange)
            {
                currDisplayRange = tempRange;
                RoundDisplayRange();
            }
            pastData[currDataIndex] = currData;
            currDataIndex++;
            if (currDataIndex >= pastData.Length)
            {
                currDataIndex = 0;
            }
        }

        void OnGUI()
        {
            if (!HighLogic.LoadedSceneIsFlight || !FlightGlobals.ready || !BDArmorySetup.GAME_UI_ENABLED ||
                !vessel.isActiveVessel || !displayMAD) return;

            if (resizingWindow && Event.current.type == EventType.MouseUp) { resizingWindow = false; }

            if (BDArmorySettings.UI_SCALE_ACTUAL != 1) GUIUtility.ScaleAroundPivot(BDArmorySettings.UI_SCALE_ACTUAL * Vector2.one, BDArmorySetup.WindowRectMAD.position);
            BDArmorySetup.WindowRectMAD = GUI.Window(73652, BDArmorySetup.WindowRectMAD, WindowMAD, "MAD Sensor Data", GUI.skin.window);
            GUIUtils.UseMouseEventInRect(BDArmorySetup.WindowRectMAD);
        }

        internal const float MADDisplaySize = 256;
        internal const float BorderSize = 10;
        internal const float HeaderSize = 15;
        internal static Rect MADDisplayRect = new Rect(0, 0, MADDisplaySize * BDArmorySettings.MAD_WINDOW_SCALE, MADDisplaySize * BDArmorySettings.MAD_WINDOW_SCALE);
        public Rect MADresizeRect = new Rect(
            BDArmorySetup.WindowRectMAD.width - (16 * BDArmorySettings.MAD_WINDOW_SCALE),
            BDArmorySetup.WindowRectMAD.height - (16 * BDArmorySettings.MAD_WINDOW_SCALE),
            (16 * BDArmorySettings.MAD_WINDOW_SCALE),
            (16 * BDArmorySettings.MAD_WINDOW_SCALE));
        public bool displayMAD = false;
        internal static bool resizingWindow = false;
        double currDisplayRange = 0.5d;

        public void WindowMAD(int windowID)
        {
            if (GUI.Button(new Rect(BDArmorySetup.WindowRectMAD.width - 18, 2, 16, 16), " X", BDArmorySetup.CloseButtonStyleDark))
            {
                displayMAD = false;
                BDArmorySetup.SaveConfig();
            }

            GUI.BeginGroup(new Rect(BorderSize / 2, HeaderSize + (BorderSize / 2), MADDisplayRect.width, MADDisplayRect.height));
            GUI.DrawTexture(MADDisplayRect, MADTexture, ScaleMode.StretchToFill, false);

            float currTime = Time.time;

            float timeScale = (MADDisplayRect.height - 2f * BorderSize) / currHistWindow; //(MADDisplayRect.height - HeaderSize - BorderSize) / currHistWindow; //(MADDisplayRect.height - BorderSize) / currHistWindow;
            double gaussScale = (double)(MADDisplayRect.width - BorderSize) / (2d * currDisplayRange);

            float gaussOffset = 0.5f * MADDisplayRect.width; //0.5f * (MADDisplayRect.xMax + MADDisplayRect.xMin) + BorderSize;
            float timeOffset = 0.0f; //MADDisplayRect.yMin + HeaderSize; //MADDisplayRect.yMin; //MADDisplayRect.yMax - currHistWindow * timeScale;

            string halfRange = (0.5f * currDisplayRange).ToString("G2");
            string fullRange = (currDisplayRange).ToString("G2");
            GUI.Label(new Rect(gaussOffset - 0.25f * (MADDisplayRect.width - BorderSize), MADDisplayRect.height - HeaderSize, 200, 24),
                        $"-{halfRange}", scaleStyleLeft);
            GUI.Label(new Rect(gaussOffset - 0.5f * (MADDisplayRect.width - BorderSize), MADDisplayRect.height - HeaderSize, 200, 24),
                        $"-{fullRange}", scaleStyleLeft);

            GUI.Label(new Rect(gaussOffset + 0.25f * (MADDisplayRect.width - BorderSize) - 200, MADDisplayRect.height - HeaderSize, 200, 24),
                        halfRange, scaleStyleRight);
            GUI.Label(new Rect(gaussOffset + 0.5f * (MADDisplayRect.width - BorderSize) - 200, MADDisplayRect.height - HeaderSize, 200, 24),
                        fullRange, scaleStyleRight);

            int currIndex = currDataIndex - 1;
            if (currIndex < 0)
            {
                currIndex = pastData.Length - 1;
            }

            currDisplayRange = pastData[currIndex].y;
            Vector2 prevPoint = new Vector2(gaussOffset + (float)(pastData[currIndex].y * gaussScale), timeOffset + (currTime - (float)pastData[currIndex].x) * timeScale);
            if (BDArmorySettings.DEBUG_RADAR && BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.ModuleMAD] Drawing first point at index {currIndex}, point: {prevPoint}, signal: {pastData[currIndex].y}, time: {pastData[currIndex].x}, currTime: {currTime}, currHistWindow: {currHistWindow}, timeScale: {timeScale}");

            for (int i = --currIndex; i != currDataIndex; --i)
            {
                if (i < 0)
                {
                    i = pastData.Length - 1;
                }

                float tempTime = (float)pastData[i].x;
                if ((tempTime + currHistWindow) < currTime)
                {
                    if (BDArmorySettings.DEBUG_RADAR && BDArmorySettings.DEBUG_OTHER)
                    {
                        Debug.Log($"[BDArmory.ModuleMAD] Breaking at point at index {i}, time: {tempTime}, currTime: {currTime}");

                        i++;
                        if (i >= pastData.Length)
                        {
                            i = 0;
                        }
                        tempTime = (float)pastData[i].x;
                        Debug.Log($"[BDArmory.ModuleMAD] Drawing last point at index {i} point: {prevPoint}, signal: {pastData[i].y}, time: {tempTime}, currTime: {currTime}");
                    }
                    break;
                }

                double tempRange = Math.Abs(pastData[i].y);
                if (tempRange > currDisplayRange)
                {
                    currDisplayRange = tempRange;
                }

                Vector2 currPoint = new Vector2(gaussOffset + (float)(pastData[i].y * gaussScale), timeOffset + (currTime - tempTime) * timeScale);

                GUIUtils.DrawLine(prevPoint, currPoint, Color.green, 2, true);
                prevPoint = currPoint;
            }

            RoundDisplayRange();

            if (BDArmorySettings.DEBUG_RADAR && BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.ModuleMAD] currDisplayRange: {currDisplayRange}, gaussScale: {gaussScale}");

            // Draw the final point
            //Drawing.DrawLine(prevPoint, new Vector2(gaussOffset, MADDisplayRect.yMax - BorderSize), Color.blue, 2, true);

            GUI.EndGroup();

            DrawHistPeriodButtons();

            // Resizing code block.
            MADresizeRect = new Rect(BDArmorySetup.WindowRectMAD.width - 18, BDArmorySetup.WindowRectMAD.height - 18, 16, 16);
            GUI.DrawTexture(MADresizeRect, GUIUtils.resizeTexture, ScaleMode.StretchToFill, true);
            if (Event.current.type == EventType.MouseDown && MADresizeRect.Contains(Event.current.mousePosition))
            {
                resizingWindow = true;
            }
            else GUIUtils.DragWindow();

            if (Event.current.type == EventType.Repaint && resizingWindow)
            {
                if (Mouse.delta.x != 0 || Mouse.delta.y != 0)
                {
                    float diff = (Mathf.Abs(Mouse.delta.x) > Mathf.Abs(Mouse.delta.y) ? Mouse.delta.x : Mouse.delta.y) / BDArmorySettings.UI_SCALE_ACTUAL;
                    BDArmorySettings.MAD_WINDOW_SCALE = Mathf.Clamp(BDArmorySettings.MAD_WINDOW_SCALE + diff / MADDisplaySize, BDArmorySettings.MAD_WINDOW_SCALE_MIN, BDArmorySettings.MAD_WINDOW_SCALE_MAX);
                    BDArmorySetup.ResizeMADWindow(BDArmorySettings.MAD_WINDOW_SCALE);
                }
            }
            // End Resizing code.

            GUIUtils.RepositionWindow(ref BDArmorySetup.WindowRectMAD);
        }

        private void DrawHistPeriodButtons()
        {
            float zoomGap = 9f * gap;
            if (currHistWindow >= 10f)
                zoomGap += 5f * gap;

            float windowSize = MADDisplaySize * BDArmorySettings.MAD_WINDOW_SCALE;
            float zoomStartX = windowSize * 0.94f - (buttonHeight * 3) - zoomGap;
            float zoomStartY = (windowSize * 0.06f);
            Rect zoomOutRect = new Rect(zoomStartX, zoomStartY, buttonHeight, buttonHeight);
            Rect zoomInfoRect = new Rect(zoomStartX + buttonHeight + gap, zoomStartY, buttonHeight + zoomGap, buttonHeight);
            Rect zoomInRect = new Rect(zoomStartX + buttonHeight * 2f + (zoomGap + 2f * gap), zoomStartY, buttonHeight, buttonHeight);

            GUI.enabled = currHistWindowIndex > 0;
            if (GUI.Button(zoomOutRect, "-", GUI.skin.button))
            {
                currHistWindow = histWindowOptions[--currHistWindowIndex];
            }

            GUIStyle zoomBox = GUI.skin.box;
            zoomBox.alignment = TextAnchor.UpperCenter;
            zoomBox.padding.top = 0;
            GUI.enabled = true;
            GUI.Label(zoomInfoRect, $"{currHistWindow:F1} s", zoomBox);

            GUI.enabled = currHistWindowIndex < histWindowOptions.Length - 1;
            if (GUI.Button(zoomInRect, "+", GUI.skin.button))
            {
                currHistWindow = histWindowOptions[++currHistWindowIndex];
                UpdateDataArray();
            }
            GUI.enabled = true;
        }

        public void RoundDisplayRange()
        {
            currDisplayRange = RoundSigFigs(Math.Max(currDisplayRange, minimumSensitivity));
        }

        // Since we're using doubles we actually support far more than this range, but I want to keep it fast and I doubt we'd go above 1E+3 or dip below 1E-12
        readonly static double[] mults = [1E-3d, 1E-2d, 1E-1d, 1d, 1E+1d, 1E+2d, 1E+3d, 1E+4d, 1E+5d, 1E+6d, 1E+7d, 1E+8d, 1E+9d, 1E+10d, 1E+11d, 1E+12d];
        readonly static double[] divis = [1E+3d, 1E+2d, 1E+1d, 1d, 1E-1d, 1E-2d, 1E-3d, 1E-4d, 1E-5d, 1E-6d, 1E-7d, 1E-8d, 1E-9d, 1E-10d, 1E-11d, 1E-12d];
        const double minSens = 1E-12d; // Should always match the last value in divis
        const double minSensMult = 1E+12d; // Should always match the last value in mults
        const double maxSens = 1E+3d; // Should always match the first value in divis

        public double RoundSigFigs(double input)
        {
            // Extreme case where we're below minSens / the last value in divis
            if (input < minSens)
            {
                // If we're lower than machine epsilon, return early
                if (input < 1E-45)
                {
                    return minSens;
                }
                // Initialize divisor to 1
                double divisor = 1d;
                // While input is < minSens
                while(input < minSens)
                {
                    // Multiply it by 10
                    input *= 10d;
                    // And multiply the divisor by 0.1
                    divisor *= 0.1d;
                }
                // Once we reach minSens, we multiply input by minSensMult, allowing us to round properly, and then multiply by minSens and divisor to get back to the value we want
                return Math.Ceiling(input * minSensMult) * minSens * divisor;
            }
            // Basically like taking Log10 and flooring, but slightly faster since it's all just comparisons
            int index = 0;
            for (int i = 0; i < divis.Length; i++)
            {
                // This is gonna be faster than bisection since we've got a very short array of fixed length
                if (divis[i] < input)
                {
                    index = i;
                    break;
                }
            }

            double temp = Math.Ceiling(input * mults[index]);
            return temp * divis[index];
        }

        public override string GetInfo()
        {
            StringBuilder output = new StringBuilder();
            output.Append(Environment.NewLine);
            output.AppendLine($" Own signal attenuation: {ownSignalAttenuation}."); //localize this at some point, future me
            return output.ToString();
        }

        public double GetMagneticDipoleOfVessel(Vessel v, Vector3d relVec, double Rsqr)
        {
            TargetInfo ti = RadarUtils.GetVesselRadarCrossSection(v);

            Vector3d magSig = ti.GetMagneticSignature();
            double Rcube = Math.Sqrt(Rsqr);
            Rcube *= Rsqr; // Not sure if the mono sqrt bug affects this too..

            //return (-magSig / Rcube + (3f / (Rcube * Rsqr) * Vector3.Dot(magSig, relVec)) * relVec);
            Vector3d tempVec = (-magSig / Rcube + (3d / (Rcube * Rsqr) * Vector3d.Dot(magSig, relVec)) * relVec);
            double temp = Vector3d.Dot(tempVec, Bplanet) * BplanetInvMag;
            
            if (BDArmorySettings.DEBUG_RADAR) Debug.Log($"[BDArmory.ModuleMAD]: Vessel: {v} has signature: {temp} from: {tempVec}, mag: {tempVec.magnitude}, Dot: {Vector3d.Dot(tempVec, Bplanet)}, BplanetInvMag: {BplanetInvMag}, Ti.baseSig: {ti.magneticBaseSignature}");
            return temp;
        }

        Vector3d Bplanet = Vector3d.zero;
        double BplanetInvMag = 0d;

        public double GetMagneticSignal()
        {
            //Vector3 Bvessels = Vector3.zero;
            List<Vessel> loadedVessels = BDATargetManager.LoadedVessels;
            Vector3d sensorPos = part.transform.position;

            Bplanet = vessel.mainBody.GetMagneticFieldStrength(vessel);
            BplanetInvMag = 1d / Bplanet.magnitude;

            Vector3d vecToCoM = vessel.CoMD - sensorPos;

            //Bvessels += (1f - ownSignalAttenuation) * GetMagneticDipoleOfVessel(v, relVec, Rsqr);
            double ownNoise = GetMagneticDipoleOfVessel(vessel, vecToCoM, vecToCoM.sqrMagnitude);
            double anomaly = BDAMath.Clamp01(1f - ownSignalAttenuation) * ownNoise;
            ownNoise *= ownSignalNoiseFactor;

            double totalNoise = Math.Max(ownNoise, noiseFloor + 0.1d * ownNoise);

            // Noise Range + Anomaly
            // If own anomaly is large (large steady error) it's hard to read smaller signatures,
            // ditto if signal noise is large
            double minSignalForDetection = 0.25d * (totalNoise + Math.Abs(anomaly));

            for (int i = 0; i < loadedVessels.Count; i++)
            {
                Vessel v = loadedVessels[i];
                if (v == null || v.gameObject == null || v.packed || !v.loaded || !v.isActiveAndEnabled) continue;
                if (v == vessel) continue;

                Vector3d relVec = v.CoMD - sensorPos;
                double Rsqr = relVec.sqrMagnitude;

                if (Rsqr > rangeSqr) continue;

                //Bvessels += GetMagneticDipoleOfVessel(v, relVec, Rsqr);
                double currSignal = GetMagneticDipoleOfVessel(v, relVec, Rsqr);
                anomaly += currSignal;
                if (Math.Abs(currSignal) > minSignalForDetection)
                {
                    BDATargetManager.ReportVessel(v, WeaponManager);
                }
            }

            //return Vector3.Dot(Bvessels, Bplanet) / Bplanet.magnitude;

            anomaly += totalNoise * (double)(2f * UnityEngine.Random.value - 1f);

            if (BDArmorySettings.DEBUG_RADAR) Debug.Log($"[BDArmory.ModuleMAD]: Curr Signal Strength: {anomaly}, currDataIndex: {currDataIndex}, noise amplitude: {totalNoise}");

            return (anomaly < 0d) ? BDAMath.Clamp(anomaly, -maxSens, -minimumSensitivity) : BDAMath.Clamp(anomaly, minimumSensitivity, maxSens);
        }
    }
}
