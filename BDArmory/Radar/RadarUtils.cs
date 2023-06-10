using System;
using System.Collections.Generic;
using UnityEngine;

using BDArmory.Control;
using BDArmory.CounterMeasure;
using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.Shaders;
using BDArmory.Targeting;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.Weapons;
using BDArmory.Weapons.Missiles;

namespace BDArmory.Radar
{
    public static class RadarUtils
    {
        private static bool rcsSetupCompleted = false;
        private static int radarResolution = 128;

        private static RenderTexture rcsRenderingVariable;
        private static RenderTexture rcsRendering1;
        private static RenderTexture rcsRendering2;
        private static RenderTexture rcsRendering3;

        private static RenderTexture rcsRenderingFrontal;
        private static RenderTexture rcsRenderingLateral;
        private static RenderTexture rcsRenderingVentral;
        private static Camera radarCam;

        private static Texture2D drawTextureVariable;
        public static Texture2D GetTextureVariable { get { return drawTextureVariable; } }
        private static Texture2D drawTexture1;
        public static Texture2D GetTexture1 { get { return drawTexture1; } }
        private static Texture2D drawTexture2;
        public static Texture2D GetTexture2 { get { return drawTexture2; } }
        private static Texture2D drawTexture3;
        public static Texture2D GetTexture3 { get { return drawTexture3; } }

        // Legacy variables
        private static Texture2D drawTextureFrontal;
        public static Texture2D GetTextureFrontal { get { return drawTextureFrontal; } }
        private static Texture2D drawTextureLateral;
        public static Texture2D GetTextureLateral { get { return drawTextureLateral; } }
        private static Texture2D drawTextureVentral;
        public static Texture2D GetTextureVentral { get { return drawTextureVentral; } }

        // additional anti-exploit 45� offset renderings
        private static Texture2D drawTextureFrontal45;
        public static Texture2D GetTextureFrontal45 { get { return drawTextureFrontal45; } }
        private static Texture2D drawTextureLateral45;
        public static Texture2D GetTextureLateral45 { get { return drawTextureLateral45; } }
        private static Texture2D drawTextureVentral45;
        public static Texture2D GetTextureVentral45 { get { return drawTextureVentral45; } }

        internal static float rcsFrontal;             // internal so that editor analysis window has access to the details
        internal static float rcsLateral;             // dito
        internal static float rcsVentral;             // dito
        internal static float rcsFrontal45;             // dito
        internal static float rcsLateral45;             // dito
        internal static float rcsVentral45;             // dito
        // End legacy variables

        internal static float rcsTotal;               // dito

        internal const float RCS_NORMALIZATION_FACTOR = 3.04f;       //IMPORTANT FOR RCS CALCULATION! DO NOT CHANGE! (sphere with 1m^2 cross section should have 1m^2 RCS)
        internal const float RCS_MISSILES = 999f;                    //default rcs value for missiles if not configured in the part config
        internal const float RWR_PING_RANGE_FACTOR = 2.0f;
        internal const float RADAR_IGNORE_DISTANCE_SQR = 100f;
        internal const float ACTIVE_MISSILE_PING_PERISTS_TIME = 0.2f;
        internal const float MISSILE_DEFAULT_LOCKABLE_RCS = 5f;

        // RCS Aspects
        private static float[,] rcsAspects = new float[45, 2] {
            { 2.000f, -6.133f},
            { 6.000f, 6.133f},
            { 10.000f, -14.311f},
            { 14.000f, 13.289f},
            { 18.000f, 0.000f},
            { 22.000f, 19.422f},
            { 26.000f, -9.200f},
            { 30.000f, 9.200f},
            { 34.000f, -21.467f},
            { 38.000f, -4.089f},
            { 42.000f, 15.333f},
            { 46.000f, -16.356f},
            { 50.000f, 4.089f},
            { 54.000f, -11.244f},
            { 58.000f, 21.467f},
            { 62.000f, -2.044f},
            { 66.000f, 11.244f},
            { 70.000f, -19.422f},
            { 74.000f, 2.044f},
            { 78.000f, 17.378f},
            { 82.000f, -7.156f},
            { 86.000f, -22.489f},
            { 90.000f, 7.156f},
            { 94.000f, -13.289f},
            { 98.000f, 14.311f},
            { 102.000f, -1.022f},
            { 106.000f, 22.489f},
            { 110.000f, -10.222f},
            { 114.000f, 10.222f},
            { 118.000f, -18.400f},
            { 122.000f, 18.400f},
            { 126.000f, -5.111f},
            { 130.000f, 5.111f},
            { 134.000f, -15.333f},
            { 138.000f, 12.267f},
            { 142.000f, -8.178f},
            { 146.000f, 20.444f},
            { 150.000f, 1.022f},
            { 154.000f, -20.444f},
            { 158.000f, 8.178f},
            { 162.000f, -12.267f},
            { 166.000f, 16.356f},
            { 170.000f, -3.067f},
            { 174.000f, -17.378f},
            { 178.000f, 3.067f}
        };
        private static int numAspects = rcsAspects.GetLength(0); // Number of aspects
        public static float[,] worstRCSAspects = new float[3, 3]; // Worst three aspects
        static double[] rcsValues;
        static Color32[] pixels;

        /// <summary>
        /// Force radar signature update
        /// Optionally, pass in a list of the vessels to update, otherwise all vessels in BDATargetManager.LoadedVessels get updated.
        /// 
        /// This appears to cause a rather large amount of memory to be consumed (not actually a leak though).
        /// </summary>
        public static void ForceUpdateRadarCrossSections(List<Vessel> vessels = null)
        {
            foreach (var vessel in (vessels == null ? BDATargetManager.LoadedVessels : vessels))
            {
                if (vessel == null) continue;
                GetVesselRadarCrossSection(vessel, true);
            }
        }

        /// <summary>
        /// Get a vessel radar siganture, including all modifiers (ECM, stealth, ...)
        /// </summary>
        public static TargetInfo GetVesselRadarSignature(Vessel v)
        {
            //1. baseSig = GetVesselRadarCrossSection
            TargetInfo ti = GetVesselRadarCrossSection(v);
            //2. modifiedSig = GetVesselModifiedSignature(baseSig)    //ECM-jammers with rcs reduction effect; other rcs reductions (stealth)
            ti.radarRCSReducedSignature = ti.radarBaseSignature; //These are needed for Radar functions to work!
            ti.radarModifiedSignature = ti.radarBaseSignature;
            //ti.radarLockbreakFactor = 1;

            return ti;
        }

        /// <summary>
        /// Internal method: get a vessel base radar signature
        /// </summary>
        private static TargetInfo GetVesselRadarCrossSection(Vessel v, bool force = false)
        {
            //read vesseltargetinfo, or render against radar cameras
            TargetInfo ti = v.gameObject.GetComponent<TargetInfo>();

            if (ti == null)
            {
                ti = v.gameObject.AddComponent<TargetInfo>();
            }

            if (ti.isMissile)
            {
                // missile handling: get signature from missile config, unless it is radaractive, then use old legacy special handling.
                // LEGACY special handling missile: should always be detected, hence signature is set to maximum
                MissileBase missile = ti.MissileBaseModule;
                if (missile != null)
                {
                    if (missile.ActiveRadar)
                        ti.radarBaseSignature = RCS_MISSILES;
                    else
                        ti.radarBaseSignature = missile.missileRadarCrossSection;

                    ti.radarBaseSignatureNeedsUpdate = false;
                }
            }

            // Run intensive RCS rendering if 1. It has not been done yet, 2. If the competition just started (capture vessel changes such as gear-raise or robotics)
            if (force || ti.radarBaseSignature == -1 || ti.radarBaseSignatureNeedsUpdate)
            {
                // is it just some debris? then dont bother doing a real rcs rendering and just fake it with the parts mass
                if (VesselModuleRegistry.ignoredVesselTypes.Contains(v.vesselType) || !v.IsControllable)
                {
                    ti.radarBaseSignature = v.GetTotalMass();
                }
                else
                {
                    // perform radar rendering to obtain base cross section
                    ti.radarBaseSignature = RenderVesselRadarSnapshot(v, v.transform);
                }

                ti.radarBaseSignatureNeedsUpdate = false;
                ti.alreadyScheduledRCSUpdate = false;
                ti.radarMassAtUpdate = v.GetTotalMass();

                // Update ECM impact on RCS if base RCS is modified
                VesselECMJInfo jammer = v.gameObject.GetComponent<VesselECMJInfo>();
                if (jammer != null)
                    jammer.UpdateJammerStrength();
            }

            return ti;
        }

        /// <summary>
        /// Get vessel chaff factor
        /// </summary>
        public static float GetVesselChaffFactor(Vessel v)
        {
            float chaffFactor = 1.0f;

            // read vessel ecminfo for active lockbreaking jammers
            VesselChaffInfo vci = v.gameObject.GetComponent<VesselChaffInfo>();

            if (vci)
            {
                // lockbreaking strength relative to jammer's lockbreak strength in relation to vessel rcs signature:
                // lockbreak_factor = baseSig/modifiedSig x (1 � lopckBreakStrength/baseSig/100)
                chaffFactor = vci.GetChaffMultiplier();
            }

            return chaffFactor;
        }

        /// <summary>
        /// Get a vessel ecm jamming area (in m) where radar display garbling occurs
        /// </summary>
        public static float GetVesselECMJammingDistance(Vessel v)
        {
            float jammingDistance = 0f;

            if (v == null)
                return jammingDistance;

            var crossSection = GetVesselRadarCrossSection(v);
            if (crossSection != null)
                jammingDistance = crossSection.radarJammingDistance;
            return jammingDistance;
        }

        /// <summary>
        /// Internal method: do the actual radar snapshot rendering from 3 sides and store it in a vesseltargetinfo attached to the vessel
        ///
        /// Note: Transform t is passed separatedly (instead of using v.transform), as the method need to be called from the editor
        ///         and there we dont have a VESSEL, only a SHIPCONSTRUCT, so the EditorRcSWindow passes the transform separately.
        /// </summary>
        /// <param name="inEditorZoom">when true, we try to make the rendered vessel fill the rendertexture completely, for a better detailed view. This does skew the computed cross section, so it is only for a good visual in editor!</param>
        public static float RenderVesselRadarSnapshot(Vessel v, Transform t, bool inEditorZoom = false)
        {
            if (VesselModuleRegistry.ignoredVesselTypes.Contains(v.vesselType)) Debug.LogError($"[BDArmory.RadarUtils]: Rendering radar snapshot of {v.vesselName}, which should be being ignored!");
            const float radarDistance = 1000f;
            const float radarFOV = 2.0f;
            Vector3 presentationPosition = -t.forward * radarDistance;

            SetupResources();

            Quaternion priorRotation = Quaternion.Euler(0, 0, 0);

            //move vessel up for clear rendering shot (only if outside editor and thus vessel is a real vessel)
            // set rotation as well, otherwise the CalcVesselBounds results won't match those from the editor
            if (HighLogic.LoadedSceneIsFlight)
            {
                priorRotation = t.rotation;
                v.SetPosition(v.transform.position + presentationPosition);
                v.SetRotation(new Quaternion(-0.7f, 0f, 0f, -0.7f));

                t = v.transform;

                //move AB thrust transforms (fix for AirplanePlus .dds engine afterburner FX not using DXT5 standard and showing up in RCS render)
                using (var engines = VesselModuleRegistry.GetModules<ModuleEngines>(v).GetEnumerator())
                    while (engines.MoveNext())
                    {
                        if (engines.Current == null) continue;
                        using (var engineTransforms = engines.Current.thrustTransforms.GetEnumerator())
                            while (engineTransforms.MoveNext())
                            {
                                engineTransforms.Current.transform.position = engineTransforms.Current.transform.position + presentationPosition;
                            }
                    }
            }

            Bounds vesselbounds = CalcVesselBounds(v, t);

            if (BDArmorySettings.DEBUG_RADAR)
            {
                if (HighLogic.LoadedSceneIsFlight)
                    Debug.Log($"[BDArmory.RadarUtils]: Rendering radar snapshot of vessel {v.name}, type {v.vesselType}");
                else
                    Debug.Log("[BDArmory.RadarUtils]: Rendering radar snapshot of vessel");
                Debug.Log($"[BDArmory.RadarUtils]: - bounds: {vesselbounds}");
                Debug.Log($"[BDArmory.RadarUtils]: - rotation: {t.rotation}");
                //Debug.Log("[BDArmory.RadarUtils]: - size: " + vesselbounds.size + ", magnitude: " + vesselbounds.size.magnitude);
            }

            if (vesselbounds.size.sqrMagnitude == 0f)
            {
                // SAVE US THE RENDERING, result will be zero anyway...
                if (BDArmorySettings.DEBUG_RADAR)
                {
                    Debug.Log("[BDArmory.RadarUtils]: - rcs is zero.");
                }

                // revert presentation (only if outside editor and thus vessel is a real vessel)
                if (HighLogic.LoadedSceneIsFlight)
                    v.SetPosition(v.transform.position - presentationPosition);

                return 0f;
            }

            float rcsVariable = 0f;
            if (worstRCSAspects is null) worstRCSAspects = new float[3, 3];
            Array.Clear(worstRCSAspects, 0, 9);
            if (rcsValues is null) rcsValues = new double[numAspects];
            Array.Clear(rcsValues, 0, numAspects);
            rcsTotal = 0;
            Vector3 aspect;

            // Loop through all aspects
            for (int i = 0; i < numAspects; i++)
            {
                // Determine camera vector for aspect
                aspect = Vector3.RotateTowards(t.up, -t.up, rcsAspects[i, 0] / 180f * Mathf.PI, 0);
                aspect = Vector3.RotateTowards(aspect, Vector3.Cross(t.right, t.up), -rcsAspects[i, 1] / 180f * Mathf.PI, 0);

                // Render aspect
                RenderSinglePass(t, false, aspect, vesselbounds, radarDistance, radarFOV, rcsRenderingVariable, drawTextureVariable);

                // Count pixel colors to determine radar returns
                rcsVariable = 0;

                pixels = drawTextureVariable.GetPixels32(); // GetPixels causes a memory leak, so we need to go via GetPixels32!
                for (int pixelIndex = 0; pixelIndex < pixels.Length; ++pixelIndex)
                {
                    var pixel = pixels[pixelIndex];
                    var maxColorComponent = Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b));
                    rcsVariable += (float)maxColorComponent / 255f;
                }

                // normalize rcs value, so that a sphere with cross section of 1 m^2 gives a return of 1 m^2:
                rcsVariable /= RCS_NORMALIZATION_FACTOR;
                rcsValues[i] = (double)rcsVariable;

                // Remember worst three RCS aspects to display in editor
                if (inEditorZoom)
                {
                    if (rcsVariable > worstRCSAspects[0, 2])
                    {
                        worstRCSAspects[2, 0] = worstRCSAspects[1, 0];
                        worstRCSAspects[2, 1] = worstRCSAspects[1, 1];
                        worstRCSAspects[2, 2] = worstRCSAspects[1, 2];

                        worstRCSAspects[1, 0] = worstRCSAspects[0, 0];
                        worstRCSAspects[1, 1] = worstRCSAspects[0, 1];
                        worstRCSAspects[1, 2] = worstRCSAspects[0, 2];

                        worstRCSAspects[0, 0] = rcsAspects[i, 0];
                        worstRCSAspects[0, 1] = rcsAspects[i, 1];
                        worstRCSAspects[0, 2] = rcsVariable;
                    }
                    else if (rcsVariable > worstRCSAspects[1, 2])
                    {
                        worstRCSAspects[2, 0] = worstRCSAspects[1, 0];
                        worstRCSAspects[2, 1] = worstRCSAspects[1, 1];
                        worstRCSAspects[2, 2] = worstRCSAspects[1, 2];

                        worstRCSAspects[1, 0] = rcsAspects[i, 0];
                        worstRCSAspects[1, 1] = rcsAspects[i, 1];
                        worstRCSAspects[1, 2] = rcsVariable;
                    }
                    else if (rcsVariable > worstRCSAspects[2, 2])
                    {
                        worstRCSAspects[2, 0] = rcsAspects[i, 0];
                        worstRCSAspects[2, 1] = rcsAspects[i, 1];
                        worstRCSAspects[2, 2] = rcsVariable;
                    }
                }

                if (BDArmorySettings.DEBUG_RADAR)
                {
                    // Debug.Log($"[BDArmory.RadarUtils]: RCS Aspect Vector for (az/el) {rcsAspects[i, 0]}/{rcsAspects[i, 1]}  is: " + aspect.ToString());
                    Debug.Log($"[BDArmory.RadarUtils]: - Vessel rcs for (az/el) is: {rcsAspects[i, 0]}/{rcsAspects[i, 1]} = rcsVariable: {rcsVariable}");
                }
            }

            // Use third quartile for the total RCS (gives better results than average)
            rcsTotal = (float)Quartile(rcsValues, 3);

            // If we are in the editor, render the three highest RCS aspects
            if (inEditorZoom)
            {
                // Determine camera vectors for aspects
                Vector3 aspect1 = Vector3.RotateTowards(t.up, -t.up, worstRCSAspects[0, 0] / 180f * Mathf.PI, 0);
                aspect1 = Vector3.RotateTowards(aspect1, Vector3.Cross(t.right, t.up), -worstRCSAspects[0, 1] / 180f * Mathf.PI, 0);
                Vector3 aspect2 = Vector3.RotateTowards(t.up, -t.up, worstRCSAspects[1, 0] / 180f * Mathf.PI, 0);
                aspect2 = Vector3.RotateTowards(aspect2, Vector3.Cross(t.right, t.up), -worstRCSAspects[1, 1] / 180f * Mathf.PI, 0);
                Vector3 aspect3 = Vector3.RotateTowards(t.up, -t.up, worstRCSAspects[2, 0] / 180f * Mathf.PI, 0);
                aspect3 = Vector3.RotateTowards(aspect3, Vector3.Cross(t.right, t.up), -worstRCSAspects[2, 1] / 180f * Mathf.PI, 0);

                // Render three highest aspects
                RenderSinglePass(t, inEditorZoom, aspect1, vesselbounds, radarDistance, radarFOV, rcsRendering1, drawTexture1);
                RenderSinglePass(t, inEditorZoom, aspect2, vesselbounds, radarDistance, radarFOV, rcsRendering2, drawTexture2);
                RenderSinglePass(t, inEditorZoom, aspect3, vesselbounds, radarDistance, radarFOV, rcsRendering3, drawTexture3);
            }
            else
            {
                // revert presentation (only if outside editor and thus vessel is a real vessel)
                if (HighLogic.LoadedSceneIsFlight)
                {
                    //move AB thrust transforms (fix for AirplanePlus .dds engine afterburner FX not using DXT5 standard and showing up in RCS render)
                    using (var engines = VesselModuleRegistry.GetModules<ModuleEngines>(v).GetEnumerator())
                        while (engines.MoveNext())
                        {
                            if (engines.Current == null) continue;
                            using (var engineTransforms = engines.Current.thrustTransforms.GetEnumerator())
                                while (engineTransforms.MoveNext())
                                {
                                    engineTransforms.Current.transform.position = engineTransforms.Current.transform.position - presentationPosition;
                                }
                        }

                    v.SetRotation(priorRotation);
                    v.SetPosition(v.transform.position - presentationPosition);
                }
            }

            if (BDArmorySettings.DEBUG_RADAR)
            {
                Debug.Log($"[BDArmory.RadarUtils]: - Vessel all-aspect rcs is: rcsTotal: {rcsTotal}");
            }

            return rcsTotal;
        }

        // Used to calculate third quartile for RCS dataset
        internal static double Quartile(double[] array, int nth_quartile)
        {
            System.Array.Sort(array);
            double dblPercentage = 0;

            switch (nth_quartile)
            {
                case 0:
                    dblPercentage = 0; //Smallest value in the data set
                    break;
                case 1:
                    dblPercentage = 25; //First quartile (25th percentile)
                    break;
                case 2:
                    dblPercentage = 50; //Second quartile (50th percentile)
                    break;

                case 3:
                    dblPercentage = 75; //Third quartile (75th percentile)
                    break;

                case 4:
                    dblPercentage = 100; //Largest value in the data set
                    break;
                default:
                    dblPercentage = 0;
                    break;
            }


            if (dblPercentage >= 100.0d) return array[array.Length - 1];

            double position = (double)(array.Length + 1) * dblPercentage / 100.0;
            double leftNumber = 0.0d, rightNumber = 0.0d;

            double n = dblPercentage / 100.0d * (array.Length - 1) + 1.0d;

            if (position >= 1)
            {
                leftNumber = array[(int)System.Math.Floor(n) - 1];
                rightNumber = array[(int)System.Math.Floor(n)];
            }
            else
            {
                leftNumber = array[0]; // first data
                rightNumber = array[1]; // first data
            }

            if (leftNumber == rightNumber)
                return leftNumber;
            else
            {
                double part = n - System.Math.Floor(n);
                return leftNumber + part * (rightNumber - leftNumber);
            }
        }

        /// <summary>
        /// Internal method: do the actual radar snapshot rendering from 3 sides and store it in a vesseltargetinfo attached to the vessel
        ///
        /// Note: Transform t is passed separatedly (instead of using v.transform), as the method need to be called from the editor
        ///         and there we dont have a VESSEL, only a SHIPCONSTRUCT, so the EditorRcSWindow passes the transform separately.
        /// </summary>
        /// <param name="inEditorZoom">when true, we try to make the rendered vessel fill the rendertexture completely, for a better detailed view. This does skew the computed cross section, so it is only for a good visual in editor!</param>
        public static float RenderVesselRadarSnapshotLegacy(Vessel v, Transform t, bool inEditorZoom = false)
        {
            const float radarDistance = 1000f;
            const float radarFOV = 2.0f;
            Vector3 presentationPosition = -t.forward * radarDistance;

            SetupResources();

            //move vessel up for clear rendering shot (only if outside editor and thus vessel is a real vessel)
            if (HighLogic.LoadedSceneIsFlight)
                v.SetPosition(v.transform.position + presentationPosition);

            Bounds vesselbounds = CalcVesselBounds(v, t);
            if (BDArmorySettings.DEBUG_RADAR)
            {
                if (HighLogic.LoadedSceneIsFlight)
                    Debug.Log($"[BDArmory.RadarUtils]: Rendering radar snapshot of vessel {v.name}, type {v.vesselType}");
                else
                    Debug.Log("[BDArmory.RadarUtils]: Rendering radar snapshot of vessel");
                Debug.Log("[BDArmory.RadarUtils]: - bounds: " + vesselbounds.ToString());
                //Debug.Log("[BDArmory.RadarUtils]: - size: " + vesselbounds.size + ", magnitude: " + vesselbounds.size.magnitude);
            }

            if (vesselbounds.size.sqrMagnitude == 0f)
            {
                // SAVE US THE RENDERING, result will be zero anyway...
                if (BDArmorySettings.DEBUG_RADAR)
                {
                    Debug.Log("[BDArmory.RadarUtils]: - rcs is zero.");
                }

                // revert presentation (only if outside editor and thus vessel is a real vessel)
                if (HighLogic.LoadedSceneIsFlight)
                    v.SetPosition(v.transform.position - presentationPosition);

                return 0f;
            }

            // pass1: frontal
            RenderSinglePass(t, inEditorZoom, t.up, vesselbounds, radarDistance, radarFOV, rcsRenderingFrontal, drawTextureFrontal);
            // pass2: lateral
            RenderSinglePass(t, inEditorZoom, t.right, vesselbounds, radarDistance, radarFOV, rcsRenderingLateral, drawTextureLateral);
            // pass3: Ventral
            RenderSinglePass(t, inEditorZoom, t.forward, vesselbounds, radarDistance, radarFOV, rcsRenderingVentral, drawTextureVentral);

            //additional 45� offset renderings:
            RenderSinglePass(t, inEditorZoom, (t.up + t.right), vesselbounds, radarDistance, radarFOV, rcsRenderingFrontal, drawTextureFrontal45);
            RenderSinglePass(t, inEditorZoom, (t.right + t.forward), vesselbounds, radarDistance, radarFOV, rcsRenderingLateral, drawTextureLateral45);
            RenderSinglePass(t, inEditorZoom, (t.forward - t.up), vesselbounds, radarDistance, radarFOV, rcsRenderingVentral, drawTextureVentral45);

            // revert presentation (only if outside editor and thus vessel is a real vessel)
            if (HighLogic.LoadedSceneIsFlight)
                v.SetPosition(v.transform.position - presentationPosition);

            // Count pixel colors to determine radar returns (only for normal non-zoomed rendering!)
            if (!inEditorZoom)
            {
                rcsFrontal = 0;
                rcsLateral = 0;
                rcsVentral = 0;
                rcsFrontal45 = 0;
                rcsLateral45 = 0;
                rcsVentral45 = 0;

                for (int x = 0; x < radarResolution; x++)
                {
                    for (int y = 0; y < radarResolution; y++)
                    {
                        rcsFrontal += drawTextureFrontal.GetPixel(x, y).maxColorComponent;
                        rcsLateral += drawTextureLateral.GetPixel(x, y).maxColorComponent;
                        rcsVentral += drawTextureVentral.GetPixel(x, y).maxColorComponent;

                        rcsFrontal45 += drawTextureFrontal45.GetPixel(x, y).maxColorComponent;
                        rcsLateral45 += drawTextureLateral45.GetPixel(x, y).maxColorComponent;
                        rcsVentral45 += drawTextureVentral45.GetPixel(x, y).maxColorComponent;
                    }
                }

                // normalize rcs value, so that the structural 1x1 panel facing the radar exactly gives a return of 1 m^2:
                rcsFrontal /= RCS_NORMALIZATION_FACTOR;
                rcsLateral /= RCS_NORMALIZATION_FACTOR;
                rcsVentral /= RCS_NORMALIZATION_FACTOR;

                rcsFrontal45 /= RCS_NORMALIZATION_FACTOR;
                rcsLateral45 /= RCS_NORMALIZATION_FACTOR;
                rcsVentral45 /= RCS_NORMALIZATION_FACTOR;

                rcsTotal = (Mathf.Max(rcsFrontal, rcsFrontal45) + Mathf.Max(rcsLateral, rcsLateral45) + Mathf.Max(rcsVentral, rcsVentral45)) / 3f;
                if (BDArmorySettings.DEBUG_RADAR)
                {
                    Debug.Log($"[BDArmory.RadarUtils]: - Vessel rcs is (frontal/lateral/ventral), (frontal45/lateral45/ventral45): {rcsFrontal}/{rcsLateral}/{rcsVentral}, {rcsFrontal45}/{rcsLateral45}/{rcsVentral45} = rcsTotal: {rcsTotal}");
                }
            }

            return rcsTotal;
        }

        /// <summary>
        /// Internal helpder method
        /// </summary>
        private static void RenderSinglePass(Transform t, bool inEditorZoom, Vector3 cameraDirection, Bounds vesselbounds, float radarDistance, float radarFOV, RenderTexture rcsRendering, Texture2D rcsTexture)
        {
            // Render one snapshop pass:
            // setup camera FOV
            radarCam.allowMSAA = false; // Don't allow MSAA with RCS render as this significantly affects results!
            radarCam.transform.position = vesselbounds.center + cameraDirection * radarDistance;
            radarCam.transform.LookAt(vesselbounds.center, -t.forward);
            float distanceToShip = Vector3.Distance(radarCam.transform.position, vesselbounds.center);
            radarCam.nearClipPlane = distanceToShip - 200;
            radarCam.farClipPlane = distanceToShip + 200;
            if (inEditorZoom)
                radarCam.fieldOfView = Mathf.Atan(vesselbounds.size.magnitude / distanceToShip) * 180 / Mathf.PI;
            else
                radarCam.fieldOfView = radarFOV;
            // setup rendertexture            

            //hrm. 2 ideas for radar stealth coating/armor variant. 1st method - radarstealth is a per-vessel arorm type, not per-part, and the radarstealth aspect rating is a mult applied to 
            //radarDistance to effectively make the resulting ship render smaller, so sum a reduced RCS.
            //option 2, a per-part radar stealth coating/armor type, coould either a) have a foreach part in vessel call that looks for athe stealthcoat, and applies a grey/black shader to that specific part
            //then proceed with the rederwithshader. not sure if this will work, investigate. b) do a renderwithshader and make use of the replacementTag to not render stealthcoat parts, readtexture, then 
            //renderwithShader again, this time only stealthed parts with an offwhite shader color, then readPixels, and use the sum of the two renders pixelcount for rcs. Assumes the RCSShader is set with a tag

            radarCam.targetTexture = rcsRendering;
            RenderTexture.active = rcsRendering;
            Shader.SetGlobalVector("_LIGHTDIR", -cameraDirection);
            Shader.SetGlobalColor("_RCSCOLOR", Color.white);
            radarCam.RenderWithShader(BDAShaderLoader.RCSShader, string.Empty);
            rcsTexture.ReadPixels(new Rect(0, 0, radarResolution, radarResolution), 0, 0);
            rcsTexture.Apply();
        }

        /// <summary>
        /// Internal method: get a vessel's bounds
        /// Method implemention adapted from kronal vessel viewer
        /// </summary>
        private static Bounds CalcVesselBounds(Vessel v, Transform t)
        {
            Bounds result = new Bounds(t.position, Vector3.zero);

            List<Part>.Enumerator vp = v.Parts.GetEnumerator();
            while (vp.MoveNext())
            {
                if (vp.Current.collider && !vp.Current.Modules.Contains("LaunchClamp"))
                {
                    result.Encapsulate(vp.Current.collider.bounds);
                }
            }
            vp.Dispose();

            return result;
        }

        /// <summary>
        /// Internal method: get a vessel's size (based on it's bounds)
        /// Method implemention adapted from kronal vessel viewer
        /// </summary>
        private static Vector3 GetVesselSize(Vessel v, Transform t)
        {
            return CalcVesselBounds(v, t).size;
        }

        /// <summary>
        /// Initialization of required resources. Necessary once per scene.
        /// </summary>
        public static void SetupResources()
        {
            if (!rcsSetupCompleted)
            {
                //set up rendertargets and textures
                rcsRenderingVariable = new RenderTexture(radarResolution, radarResolution, 16);
                rcsRendering1 = new RenderTexture(radarResolution, radarResolution, 16);
                rcsRendering2 = new RenderTexture(radarResolution, radarResolution, 16);
                rcsRendering3 = new RenderTexture(radarResolution, radarResolution, 16);

                drawTextureVariable = new Texture2D(radarResolution, radarResolution, TextureFormat.RGB24, false);
                drawTexture1 = new Texture2D(radarResolution, radarResolution, TextureFormat.RGB24, false);
                drawTexture2 = new Texture2D(radarResolution, radarResolution, TextureFormat.RGB24, false);
                drawTexture3 = new Texture2D(radarResolution, radarResolution, TextureFormat.RGB24, false);

                rcsSetupCompleted = true;
            }

            if (radarCam == null)
            {
                //set up camera
                radarCam = (new GameObject("RadarCamera")).AddComponent<Camera>();
                radarCam.enabled = false;
                radarCam.clearFlags = CameraClearFlags.SolidColor;
                radarCam.backgroundColor = Color.black;
                radarCam.cullingMask = 1 << 0;   // only layer 0 active, see: http://wiki.kerbalspaceprogram.com/wiki/API:Layers
            }
        }

        /// <summary>
        /// LEGACY Initialization of required resources. Necessary once per scene.
        /// </summary>
        public static void SetupResourcesLegacy()
        {
            if (!rcsSetupCompleted)
            {
                //set up rendertargets and textures
                rcsRenderingFrontal = new RenderTexture(radarResolution, radarResolution, 16);
                rcsRenderingLateral = new RenderTexture(radarResolution, radarResolution, 16);
                rcsRenderingVentral = new RenderTexture(radarResolution, radarResolution, 16);
                drawTextureFrontal = new Texture2D(radarResolution, radarResolution, TextureFormat.RGB24, false);
                drawTextureLateral = new Texture2D(radarResolution, radarResolution, TextureFormat.RGB24, false);
                drawTextureVentral = new Texture2D(radarResolution, radarResolution, TextureFormat.RGB24, false);
                drawTextureFrontal45 = new Texture2D(radarResolution, radarResolution, TextureFormat.RGB24, false);
                drawTextureLateral45 = new Texture2D(radarResolution, radarResolution, TextureFormat.RGB24, false);
                drawTextureVentral45 = new Texture2D(radarResolution, radarResolution, TextureFormat.RGB24, false);

                rcsSetupCompleted = true;
            }

            if (radarCam == null)
            {
                //set up camera
                radarCam = (new GameObject("RadarCamera")).AddComponent<Camera>();
                radarCam.enabled = false;
                radarCam.clearFlags = CameraClearFlags.SolidColor;
                radarCam.backgroundColor = Color.black;
                radarCam.cullingMask = 1 << 0;   // only layer 0 active, see: http://wiki.kerbalspaceprogram.com/wiki/API:Layers
            }
        }

        /// <summary>
        /// Release of acquired resources. Necessary once at end of scene.
        /// </summary>
        public static void CleanupResources()
        {
            if (rcsSetupCompleted)
            {
                RenderTexture.Destroy(rcsRenderingVariable);
                RenderTexture.Destroy(rcsRendering1);
                RenderTexture.Destroy(rcsRendering2);
                RenderTexture.Destroy(rcsRendering3);
                Texture2D.Destroy(drawTextureVariable);
                Texture2D.Destroy(drawTexture1);
                Texture2D.Destroy(drawTexture2);
                Texture2D.Destroy(drawTexture3);
                GameObject.Destroy(radarCam);
                rcsSetupCompleted = false;
            }
        }

        /// <summary>
        /// LEGACY Release of acquired resources. Necessary once at end of scene.
        /// </summary>
        public static void CleanupResourcesLegacy()
        {
            if (rcsSetupCompleted)
            {
                RenderTexture.Destroy(rcsRenderingFrontal);
                RenderTexture.Destroy(rcsRenderingLateral);
                RenderTexture.Destroy(rcsRenderingVentral);
                Texture2D.Destroy(drawTextureFrontal);
                Texture2D.Destroy(drawTextureLateral);
                Texture2D.Destroy(drawTextureVentral);
                Texture2D.Destroy(drawTextureFrontal45);
                Texture2D.Destroy(drawTextureLateral45);
                Texture2D.Destroy(drawTextureVentral45);
                GameObject.Destroy(radarCam);
                rcsSetupCompleted = false;
            }
        }

        /// <summary>
        /// Determine for a vesselposition relative to the radar position how much effect the ground clutter factor will have.
        /// </summary>
        public static float GetRadarGroundClutterModifier(float clutterFactor, Transform referenceTransform, Vector3 position, Vector3 vesselposition, TargetInfo ti)
        {
            Vector3 upVector = referenceTransform.up;

            //ground clutter factor when looking down:
            Vector3 targetDirection = (vesselposition - position);
            float angleFromUp = Vector3.Angle(targetDirection, upVector);
            float lookDownAngle = angleFromUp - 90; // result range: -90 .. +90
            Mathf.Clamp(lookDownAngle, 0, 90);      // result range:   0 .. +90

            float groundClutterMutiplier = Mathf.Lerp(1, clutterFactor, (lookDownAngle / 90));
            //additional ground clutter factor when target is landed/splashed:
            if (ti != null && (ti.isLandedOrSurfaceSplashed || ti.isSplashed))
                groundClutterMutiplier *= clutterFactor;

            return groundClutterMutiplier;
        }

        /// <summary>
        /// Determine how much of an effect enemies that are jamming have on the target
        /// </summary>
        public static float GetStandoffJammingModifier(Vessel v, Competition.BDTeam team, Vector3 position, Vessel targetV, float signature)
        {
            if (!VesselModuleRegistry.GetModule<MissileFire>(targetV)) return 1f; // Don't evaluate SOJ effects for targets without weapons managers
            if (signature == 0) return 1f; // Don't evaluate SOJ effects for targets with 0 signature

            float standOffJammingMod = 0f;
            string debugSOJ = "Standoff Jammer Lockbreak Strengths: \n";

            using (var loadedvessels = BDATargetManager.LoadedVessels.GetEnumerator())
                while (loadedvessels.MoveNext())
                {

                    // ignore null, unloaded, self, teammates, the target and vessels without ECM
                    if (loadedvessels.Current == null || !loadedvessels.Current.loaded) continue;
                    if ((loadedvessels.Current == v) || (loadedvessels.Current == targetV)) continue;
                    if (loadedvessels.Current.vesselType == VesselType.Debris) continue;

                    MissileFire wm = VesselModuleRegistry.GetModule<MissileFire>(loadedvessels.Current);

                    if (!wm) continue;
                    if (team.IsFriendly(wm.Team)) continue;

                    VesselECMJInfo standOffJammer = loadedvessels.Current.gameObject.GetComponent<VesselECMJInfo>();

                    if (standOffJammer && (standOffJammer.lockBreakStrength > 0))
                    {
                        Vector3 relPositionJammer = loadedvessels.Current.CoM - position;
                        Vector3 relPositionTarget = targetV.CoM - position;

                        // Modify  total lockbreak strength of standoff jammer by angle off the vector to target
                        float angleModifier = Vector3.Dot(relPositionTarget.normalized, relPositionJammer.normalized);
                        float sojLBS = Mathf.Clamp01(angleModifier * angleModifier * angleModifier);

                        // Modify lockbreak strength by relative sqr distance
                        sojLBS *= Mathf.Clamp(1 - Mathf.Log10(relPositionJammer.sqrMagnitude / relPositionTarget.sqrMagnitude), 0f, 3f);

                        // Add up all stand up jammer lockbreaks
                        standOffJammingMod += sojLBS * standOffJammer.lockBreakStrength;

                        if (BDArmorySettings.DEBUG_RADAR) debugSOJ += sojLBS * standOffJammer.lockBreakStrength + ", " + loadedvessels.Current.GetDisplayName() + "\n";
                    }
                }

            float modifiedSignature = Mathf.Max(signature - standOffJammingMod / 100f, 0f);

            if ((BDArmorySettings.DEBUG_RADAR) && (modifiedSignature != signature)) Debug.Log("[BDArmory.RadarUtils]: Standoff Jamming: " + targetV.GetDisplayName() + " signature relative to " + v.GetDisplayName() + " modified from " + signature + " to " + modifiedSignature + "\n" + debugSOJ);

            return modifiedSignature / signature;
        }

        /// <summary>
        /// Special scanning method that needs to be set manually on the radar: perform fixed boresight scan with locked fov.
        /// Called from ModuleRadar, which will then attempt to immediately lock onto the detected targets.
        /// Uses detectionCurve for rcs evaluation.
        /// </summary>
        //was: public static void UpdateRadarLock(Ray ray, float fov, float minSignature, ref TargetSignatureData[] dataArray, float dataPersistTime, bool pingRWR, RadarWarningReceiver.RWRThreatTypes rwrType, bool radarSnapshot)
        public static bool RadarUpdateScanBoresight(Ray ray, float fov, ref TargetSignatureData[] dataArray, float dataPersistTime, ModuleRadar radar)
        {
            int dataIndex = 0;
            bool hasLocked = false;

            // guard clauses
            if (!radar)
                return false;

            using (var loadedvessels = BDATargetManager.LoadedVessels.GetEnumerator())
                while (loadedvessels.MoveNext())
                {
                    // ignore null and unloaded
                    if (loadedvessels.Current == null || !loadedvessels.Current.loaded) continue;

                    // ignore self, ignore behind ray
                    Vector3 vectorToTarget = (loadedvessels.Current.transform.position - ray.origin);
                    if (((vectorToTarget).sqrMagnitude < RADAR_IGNORE_DISTANCE_SQR) ||
                         (Vector3.Dot(vectorToTarget, ray.direction) < 0))
                        continue;

                    if (Vector3.Angle(loadedvessels.Current.CoM - ray.origin, ray.direction) < fov / 2f)
                    {
                        // ignore when blocked by terrain
                        if (TerrainCheck(ray.origin, loadedvessels.Current.transform.position))
                            continue;

                        // get vessel's radar signature
                        TargetInfo ti = GetVesselRadarSignature(loadedvessels.Current);
                        float signature = ti.radarModifiedSignature;
                        signature *= GetRadarGroundClutterModifier(radar.radarGroundClutterFactor, radar.referenceTransform, ray.origin, loadedvessels.Current.CoM, ti);
                        signature *= GetStandoffJammingModifier(radar.vessel, radar.weaponManager.Team, ray.origin, loadedvessels.Current, signature);
                        // no ecm lockbreak factor here
                        // no chaff factor here

                        // evaluate range
                        float distance = (loadedvessels.Current.CoM - ray.origin).magnitude / 1000f;                                      //TODO: Performance! better if we could switch to sqrMagnitude...
                        if (RadarCanDetect(radar, signature, distance))
                        {
                            // detected by radar
                            // fill attempted locks array for locking later:
                            while (dataIndex < dataArray.Length - 1)
                            {
                                if (!dataArray[dataIndex].exists || (dataArray[dataIndex].exists && (Time.time - dataArray[dataIndex].timeAcquired) > dataPersistTime))
                                {
                                    break;
                                }
                                dataIndex++;
                            }

                            if (dataIndex < dataArray.Length)
                            {
                                dataArray[dataIndex] = new TargetSignatureData(loadedvessels.Current, signature);
                                dataIndex++;
                                hasLocked = true;
                            }
                        }

                        //  our radar ping can be received at a higher range than we can detect, according to RWR range ping factor:
                        if (distance < radar.radarMaxDistanceDetect * RWR_PING_RANGE_FACTOR)
                            RadarWarningReceiver.PingRWR(loadedvessels.Current, ray.origin, radar.rwrType, radar.signalPersistTimeForRwr);
                    }
                }

            return hasLocked;
        }

        /// <summary>
        /// Special scanning method for missiles with active radar homing.
        /// Called from MissileBase / MissileLauncher, which will then attempt to immediately lock onto the detected targets.
        /// Uses the missiles locktrackCurve for rcs evaluation.
        /// </summary>
        //was: UpdateRadarLock(ray, maxOffBoresight, activeRadarMinThresh, ref scannedTargets, 0.4f, true, RadarWarningReceiver.RWRThreatTypes.MissileLock, true);
        public static bool RadarUpdateMissileLock(Ray ray, float fov, ref TargetSignatureData[] dataArray, float dataPersistTime, MissileBase missile,bool hasIFF = true)
        {
            int dataIndex = 0;
            bool hasLocked = false;

            // guard clauses
            if (!missile)
                return false;

            using (var loadedvessels = BDATargetManager.LoadedVessels.GetEnumerator())
                while (loadedvessels.MoveNext())
                {
                    // ignore null, unloaded and ignored types
                    if (loadedvessels.Current == null || loadedvessels.Current.packed || !loadedvessels.Current.loaded) continue;

                    // IFF code check to prevent friendly lock-on (neutral vessel without a weaponmanager WILL be lockable!)
                    if (hasIFF)
                    {
                        MissileFire wm = VesselModuleRegistry.GetModule<MissileFire>(loadedvessels.Current);
                        if (wm != null)
                        {
                            if (missile.Team.IsFriendly(wm.Team))
                                continue;
                        }
                    }
                    // ignore self, ignore behind ray
                    Vector3 vectorToTarget = (loadedvessels.Current.transform.position - ray.origin);
                    if (((vectorToTarget).sqrMagnitude < RADAR_IGNORE_DISTANCE_SQR) ||
                         (Vector3.Dot(vectorToTarget, ray.direction) < 0))
                        continue;

                    if (Vector3.Angle(loadedvessels.Current.CoM - ray.origin, ray.direction) < fov / 2f)
                    {
                        // ignore when blocked by terrain
                        if (TerrainCheck(ray.origin, loadedvessels.Current.transform.position))
                            continue;

                        // get vessel's radar signature
                        TargetInfo ti = GetVesselRadarSignature(loadedvessels.Current);
                        float signature = 10f;
                        if (ti != null)
                        {
                            signature = ti.radarModifiedSignature;
                            // no ground clutter modifier for missiles
                            signature *= ti.radarLockbreakFactor;    //multiply lockbreak factor from active ecm
                        }                                                                 //do not multiply chaff factor here
                        signature *= GetStandoffJammingModifier(missile.vessel, missile.Team, ray.origin, loadedvessels.Current, signature);

                        // evaluate range
                        float distance = (loadedvessels.Current.CoM - ray.origin).magnitude;
                        //TODO: Performance! better if we could switch to sqrMagnitude...

                        if (distance < missile.activeRadarRange)
                        {
                            //evaluate if we can detect such a signature at that range
                            float minDetectSig = missile.activeRadarLockTrackCurve.Evaluate(distance / 1000f);

                            if (signature > minDetectSig)
                            {
                                // detected by radar
                                // fill attempted locks array for locking later:
                                while (dataIndex < dataArray.Length - 1)
                                {
                                    if (!dataArray[dataIndex].exists || (dataArray[dataIndex].exists && (Time.time - dataArray[dataIndex].timeAcquired) > dataPersistTime))
                                    {
                                        break;
                                    }
                                    dataIndex++;
                                }

                                if (dataIndex < dataArray.Length)
                                {
                                    dataArray[dataIndex] = new TargetSignatureData(loadedvessels.Current, signature);
                                    dataIndex++;
                                    hasLocked = true;
                                }
                            }
                        }

                        //  our radar ping can be received at a higher range than we can detect, according to RWR range ping factor:
                        if (distance < missile.activeRadarRange * RWR_PING_RANGE_FACTOR)
                        {
                            if (missile.GetWeaponClass() == WeaponClasses.SLW)
                                RadarWarningReceiver.PingRWR(loadedvessels.Current, ray.origin, RadarWarningReceiver.RWRThreatTypes.TorpedoLock, ACTIVE_MISSILE_PING_PERISTS_TIME);
                            else
                                RadarWarningReceiver.PingRWR(loadedvessels.Current, ray.origin, RadarWarningReceiver.RWRThreatTypes.MissileLock, ACTIVE_MISSILE_PING_PERISTS_TIME);
                        }
                    }
                }

            return hasLocked;
        }

        /// <summary>
        /// Main scanning and locking method called from ModuleRadar.
        /// scanning both for omnidirectional and boresight scans.
        /// Uses detectionCurve OR locktrackCurve for rcs evaluation, depending on wether modeTryLock is true or false.
        /// </summary>
        /// <param name="modeTryLock">true: track/lock target; false: scan only</param>
        /// <param name="dataArray">relevant only for modeTryLock=true</param>
        /// <param name="dataPersistTime">optional, relevant only for modeTryLock=true</param>
        /// <returns></returns>
        public static bool RadarUpdateScanLock(MissileFire myWpnManager, float directionAngle, Transform referenceTransform, float fov, Vector3 position, ModuleRadar radar, bool modeTryLock, ref TargetSignatureData[] dataArray, float dataPersistTime = 0f)
        {
            Vector3 forwardVector = referenceTransform.forward;
            Vector3 upVector = referenceTransform.up;
            Vector3 lookDirection = Quaternion.AngleAxis(directionAngle, upVector) * forwardVector;
            int dataIndex = 0;
            bool hasLocked = false;

            // guard clauses
            if (!myWpnManager || !myWpnManager.vessel || !radar)
                return false;

            using (var loadedvessels = BDATargetManager.LoadedVessels.GetEnumerator())
                while (loadedvessels.MoveNext())
                {
                    // ignore null, unloaded and self
                    if (loadedvessels.Current == null || loadedvessels.Current.packed || !loadedvessels.Current.loaded) continue;
                    if (loadedvessels.Current == myWpnManager.vessel) continue;

                    // ignore too close ones
                    if ((loadedvessels.Current.transform.position - position).sqrMagnitude < RADAR_IGNORE_DISTANCE_SQR)
                        continue;

                    Vector3 vesselDirection = (loadedvessels.Current.CoM - position).ProjectOnPlanePreNormalized(upVector);
                    if (Vector3.Angle(vesselDirection, lookDirection) < fov / 2f)
                    {
                        // ignore when blocked by terrain
                        if (TerrainCheck(referenceTransform.position, loadedvessels.Current.transform.position))
                            continue;

                        // get vessel's radar signature
                        TargetInfo ti = GetVesselRadarSignature(loadedvessels.Current);
                        float signature = ti.radarModifiedSignature;
                            //do not multiply chaff factor here
                            signature *= GetRadarGroundClutterModifier(radar.radarGroundClutterFactor, referenceTransform, position, loadedvessels.Current.CoM, ti);

                        // evaluate range
                        float distance = (loadedvessels.Current.CoM - position).magnitude / 1000f;                                      //TODO: Performance! better if we could switch to sqrMagnitude...

                        BDATargetManager.ClearRadarReport(loadedvessels.Current, myWpnManager);
                        if (modeTryLock)    // LOCK/TRACK TARGET:
                        {
                            //evaluate if we can lock/track such a signature at that range
                            if (distance > radar.radarMinDistanceLockTrack && distance < radar.radarMaxDistanceLockTrack)
                            {
                                //evaluate if we can lock/track such a signature at that range
                                float minLockSig = radar.radarLockTrackCurve.Evaluate(distance);

                                signature *= ti.radarLockbreakFactor;    //multiply lockbreak factor from active ecm
                                                                         //do not multiply chaff factor here
                                signature *= GetStandoffJammingModifier(radar.vessel, radar.weaponManager.Team, position, loadedvessels.Current, signature);

                                if (signature >= minLockSig && RadarCanDetect(radar, signature, distance)) // Must be able to detect and lock to lock targets
                                {
                                    // detected by radar
                                    if (myWpnManager != null)
                                    {
                                        BDATargetManager.ReportVessel(loadedvessels.Current, myWpnManager, true);
                                    }

                                    // fill attempted locks array for locking later:
                                    while (dataIndex < dataArray.Length - 1)
                                    {
                                        if (!dataArray[dataIndex].exists || (dataArray[dataIndex].exists && (Time.time - dataArray[dataIndex].timeAcquired) > dataPersistTime))
                                        {
                                            break;
                                        }
                                        dataIndex++;
                                    }

                                    if (dataIndex < dataArray.Length)
                                    {
                                        dataArray[dataIndex] = new TargetSignatureData(loadedvessels.Current, signature);
                                        dataIndex++;
                                        hasLocked = true;
                                    }
                                }
                            }

                            //  our radar ping can be received at a higher range than we can lock/track, according to RWR range ping factor:
                            if (distance < radar.radarMaxDistanceLockTrack * RWR_PING_RANGE_FACTOR)
                                RadarWarningReceiver.PingRWR(loadedvessels.Current, position, radar.rwrType, radar.signalPersistTimeForRwr);
                        }
                        else   // SCAN/DETECT TARGETS:
                        {
                            //evaluate if we can detect such a signature at that range
                            if (RadarCanDetect(radar, signature, distance))
                            {
                                // detected by radar
                                if (myWpnManager != null)
                                {
                                    BDATargetManager.ReportVessel(loadedvessels.Current, myWpnManager, true);
                                }

                                // report scanned targets only
                                radar.ReceiveContactData(new TargetSignatureData(loadedvessels.Current, signature), false);
                            }

                            //  our radar ping can be received at a higher range than we can detect, according to RWR range ping factor:
                            if (distance < radar.radarMaxDistanceDetect * RWR_PING_RANGE_FACTOR)
                                RadarWarningReceiver.PingRWR(loadedvessels.Current, position, radar.rwrType, radar.signalPersistTimeForRwr);
                        }
                    }
                }

            return hasLocked;
        }

        /// <summary>
        /// Update a lock on a tracked target.
        /// Uses locktrackCurve for rcs evaluation.
        /// </summary>
        //was: public static void UpdateRadarLock(Ray ray, Vector3 predictedPos, float fov, float minSignature, ModuleRadar radar, bool pingRWR, bool radarSnapshot, float dataPersistTime, bool locked, int lockIndex, Vessel lockedVessel)
        public static bool RadarUpdateLockTrack(Ray ray, Vector3 predictedPos, float fov, ModuleRadar radar, float dataPersistTime, bool locked, int lockIndex, Vessel lockedVessel)
        {
            float closestSqrDist = 1000f;

            // guard clauses
            if (!radar)
                return false;

            // first: re-acquire lock if temporarily lost
            if (!lockedVessel)
            {
                using (var loadedvessels = BDATargetManager.LoadedVessels.GetEnumerator())
                    while (loadedvessels.MoveNext())
                    {
                        // ignore null, unloaded
                        if (loadedvessels.Current == null || !loadedvessels.Current.loaded) continue;

                        // ignore self, ignore behind ray
                        Vector3 vectorToTarget = (loadedvessels.Current.transform.position - ray.origin);
                        if (((vectorToTarget).sqrMagnitude < RADAR_IGNORE_DISTANCE_SQR) ||
                             (Vector3.Dot(vectorToTarget, ray.direction) < 0))
                            continue;

                        if (Vector3.Angle(loadedvessels.Current.CoM - ray.origin, ray.direction) < fov / 2)
                        {
                            float sqrDist = Vector3.SqrMagnitude(loadedvessels.Current.CoM - predictedPos);
                            if (sqrDist < closestSqrDist)
                            {
                                // best candidate so far, take it
                                closestSqrDist = sqrDist;
                                lockedVessel = loadedvessels.Current;
                            }
                        }
                    }
            }

            // second: track that lock
            if (lockedVessel)
            {
                // blocked by terrain?
                if (TerrainCheck(ray.origin, lockedVessel.transform.position))
                {
                    return false;
                }

                // get vessel's radar signature
                TargetInfo ti = GetVesselRadarSignature(lockedVessel);
                float signature = ti.radarModifiedSignature;
                signature *= GetRadarGroundClutterModifier(radar.radarGroundClutterFactor, radar.referenceTransform, ray.origin, lockedVessel.CoM, ti);
                signature *= ti.radarLockbreakFactor;    //multiply lockbreak factor from active ecm
                if (radar.weaponManager is not null) signature *= GetStandoffJammingModifier(radar.vessel, radar.weaponManager.Team, ray.origin, lockedVessel, signature);
                //do not multiply chaff factor here

                // evaluate range
                float distance = (lockedVessel.CoM - ray.origin).magnitude / 1000f;                                      //TODO: Performance! better if we could switch to sqrMagnitude...
                if (distance > radar.radarMinDistanceLockTrack && distance < radar.radarMaxDistanceLockTrack)
                {
                    //evaluate if we can detect such a signature at that range
                    float minTrackSig = radar.radarLockTrackCurve.Evaluate(distance);

                    if ((signature >= minTrackSig) && (RadarCanDetect(radar, signature, distance)))
                    {
                        // can be tracked
                        radar.ReceiveContactData(new TargetSignatureData(lockedVessel, signature), locked);
                    }
                    else
                    {
                        // cannot track, so unlock it
                        return false;
                    }
                }

                //  our radar ping can be received at a higher range than we can detect, according to RWR range ping factor:
                if (distance < radar.radarMaxDistanceLockTrack * RWR_PING_RANGE_FACTOR)
                    RadarWarningReceiver.PingRWR(lockedVessel, ray.origin, radar.rwrType, ACTIVE_MISSILE_PING_PERISTS_TIME);

                return true;
            }
            else
            {
                // nothing tracked/locked at this index
                return false;
            }
        }
        /// <summary>
        /// Main scanning and locking method called from ModuleIRST.
        /// scanning both for omnidirectional and boresight scans.
        /// </summary>
        public static bool IRSTUpdateScan(MissileFire myWpnManager, float directionAngle, Transform referenceTransform, float fov, Vector3 position, ModuleIRST irst)
        {
            Vector3 forwardVector = referenceTransform.forward;
            Vector3 upVector = referenceTransform.up;
            Vector3 lookDirection = Quaternion.AngleAxis(directionAngle, upVector) * forwardVector;
            TargetSignatureData finalData = TargetSignatureData.noTarget;
            Tuple<float, Part> IRSig; //heat value
            // guard clauses
            if (!myWpnManager || !myWpnManager.vessel || !irst)
                return false;

            using (var loadedvessels = BDATargetManager.LoadedVessels.GetEnumerator())
                while (loadedvessels.MoveNext())
                {
                    // ignore null, unloaded and self
                    if (loadedvessels.Current == null || !loadedvessels.Current.loaded) continue;
                    if (loadedvessels.Current == myWpnManager.vessel) continue;
                    if (loadedvessels.Current.vesselType == VesselType.Debris) continue;

                    // ignore too close ones
                    if ((loadedvessels.Current.transform.position - position).sqrMagnitude < RADAR_IGNORE_DISTANCE_SQR)
                        continue;

                    Vector3 vesselDirection = (loadedvessels.Current.CoM - position).ProjectOnPlanePreNormalized(upVector);
                    float angle = Vector3.Angle(vesselDirection, lookDirection);
                    if (angle < fov / 2f)
                    {
                        // ignore when blocked by terrain
                        if (TerrainCheck(referenceTransform.position, loadedvessels.Current.transform.position))
                            continue;

                        // get vessel's heat signature
                        TargetInfo tInfo = loadedvessels.Current.gameObject.GetComponent<TargetInfo>();
                        if (tInfo == null)
                        {
                            tInfo = loadedvessels.Current.gameObject.AddComponent<TargetInfo>();
                        }

                        IRSig = BDATargetManager.GetVesselHeatSignature(loadedvessels.Current, irst.referenceTransform.position, 1f, irst.TempSensitivityCurve);
                        float signature = IRSig.Item1 * (irst.boresightScan ? Mathf.Clamp01(15 / angle) : 1);
                        //signature *= (1400 * 1400) / Mathf.Clamp((loadedvessels.Current.CoM - referenceTransform.position).sqrMagnitude, 90000, 36000000); //300 to 6000m - clamping sig past 6km; Commenting out as it makes tuning detection curves much easier

                        signature *= Mathf.Clamp(Vector3.Angle(loadedvessels.Current.transform.position - referenceTransform.position, -VectorUtils.GetUpDirection(referenceTransform.position)) / 90, 0.5f, 1.5f);
                        //ground will mask thermal sig                        
                        signature *= (GetRadarGroundClutterModifier(irst.GroundClutterFactor, irst.referenceTransform, position, loadedvessels.Current.CoM, tInfo) * (tInfo.isSplashed ? 12 : 1));
                        //cold ocean on the other hand...

                        // evaluate range
                        float distance = (loadedvessels.Current.CoM - position).magnitude / 1000f;                                      //TODO: Performance! better if we could switch to sqrMagnitude...

                        BDATargetManager.ClearRadarReport(loadedvessels.Current, myWpnManager);

                        //evaluate if we can detect such a signature at that range
                        float attenuationFactor = ((float)FlightGlobals.getAtmDensity(FlightGlobals.getStaticPressure(irst.referenceTransform.position), FlightGlobals.getExternalTemperature(irst.referenceTransform.position))) +
                            ((float)FlightGlobals.getAtmDensity(FlightGlobals.getStaticPressure(loadedvessels.Current.CoM), FlightGlobals.getExternalTemperature(loadedvessels.Current.CoM) / 2));

                        if (distance > irst.irstMinDistanceDetect && distance < (irst.irstMaxDistanceDetect * irst.atmAttenuationCurve.Evaluate(attenuationFactor)))
                        {
                            //evaluate if we can detect or lock such a signature at that range
                            float minDetectSig = irst.DetectionCurve.Evaluate(distance / attenuationFactor);

                            if (signature >= minDetectSig)
                            {
                                // detected by irst
                                if (myWpnManager != null)
                                {
                                    BDATargetManager.ReportVessel(loadedvessels.Current, myWpnManager, true);
                                }
                                irst.ReceiveContactData(new TargetSignatureData(loadedvessels.Current, signature), signature);
                                if (BDArmorySettings.DEBUG_RADAR) Debug.Log("[IRSTdebugging] sent data to IRST for " + loadedvessels.Current.GetName() + "'s thermalSig");
                            }
                        }
                    }
                }
            return false;
        }

        /// <summary>
        /// Returns whether the radar can detect the target, including jamming effects
        /// </summary>
        public static bool RadarCanDetect(ModuleRadar radar, float signature, float distance)
        {
            bool detected = false;
            // float distance already in km

            //evaluate if we can detect such a signature at that range
            if ((distance > radar.radarMinDistanceDetect) && (distance < radar.radarMaxDistanceDetect))
            {
                //evaluate if we can detect or lock such a signature at that range
                float minDetectSig = radar.radarDetectionCurve.Evaluate(distance);
                //do not consider lockbreak factor from active ecm here!
                //do not consider chaff here

                if (signature >= minDetectSig)
                {
                    detected = true;
                }
            }

            return detected;
        }

        /// <summary>
        /// Scans for targets in direction with field of view.
        /// (Visual Target acquisition)
        /// </summary>
        public static ViewScanResults GuardScanInDirection(MissileFire myWpnManager, Transform referenceTransform, float fov, float maxDistance)
        {
            fov *= 1.1f;
            var results = new ViewScanResults
            {
                foundMissile = false,
                foundHeatMissile = false,
                foundRadarMissile = false,
                foundAntiRadiationMissile = false,
                foundAGM = false,
                firingAtMe = false,
                missDistance = float.MaxValue,
                missDeviation = float.MaxValue,
                threatVessel = null,
                threatWeaponManager = null,
                incomingMissiles = new List<IncomingMissile>()
            };

            if (!myWpnManager || !referenceTransform)
            {
                return results;
            }

            Vector3 position = referenceTransform.position;
            Vector3 forwardVector = referenceTransform.forward;
            Vector3 upVector = referenceTransform.up;
            Vector3 lookDirection = -forwardVector;
            var pilotAI = VesselModuleRegistry.GetBDModulePilotAI(myWpnManager.vessel, true);
            var ignoreMyTargetTargetingMe = pilotAI != null && pilotAI.evasionIgnoreMyTargetTargetingMe;

            using (var loadedvessels = BDATargetManager.LoadedVessels.GetEnumerator())
                while (loadedvessels.MoveNext())
                {
                    if (loadedvessels.Current == null || !loadedvessels.Current.loaded || VesselModuleRegistry.ignoredVesselTypes.Contains(loadedvessels.Current.vesselType)) continue;
                    if (loadedvessels.Current == myWpnManager.vessel) continue; //ignore self

                    Vector3 vesselProjectedDirection = (loadedvessels.Current.transform.position - position).ProjectOnPlanePreNormalized(upVector);
                    Vector3 vesselDirection = loadedvessels.Current.transform.position - position;

                    float vesselDistance = (loadedvessels.Current.transform.position - position).sqrMagnitude;
                    //BDATargetManager.ClearRadarReport(loadedvessels.Current, myWpnManager); //reset radar contact status
                    if (vesselDistance < maxDistance * maxDistance && Vector3.Angle(vesselProjectedDirection, lookDirection) < fov / 2f) // && Vector3.Angle(loadedvessels.Current.transform.position - position, -myWpnManager.transform.forward) < myWpnManager.guardAngle / 2f) //WM facing direction? that s going to cause issues for any that aren't mounted pointing forward if guardAngle < 360; check combatSeat forward vector
                        {
                        if (TerrainCheck(referenceTransform.position, loadedvessels.Current.transform.position))
                        {
                            continue; //blocked by terrain
                        }

                        BDATargetManager.ReportVessel(loadedvessels.Current, myWpnManager);

                        TargetInfo tInfo;
                        if ((tInfo = loadedvessels.Current.gameObject.GetComponent<TargetInfo>()))
                        {
                            if (tInfo.isMissile)
                            {
                                MissileBase missileBase = tInfo.MissileBaseModule;
                                if (missileBase != null)
                                {
                                    if (missileBase.SourceVessel == myWpnManager.vessel) continue; // ignore missiles we've fired
                                    if (BDArmorySettings.VARIABLE_MISSILE_VISIBILITY)
                                    {
                                        //thrusting missiles at full range, cruising missiles at 3/4ths range, coasting missiles at 1/3rd range?
                                        //or have be hard cutoffs, e.g. 5km/4km/2.5km, etc?
                                        float sightDistance = maxDistance * (missileBase.MissileState == MissileBase.MissileStates.Boost ? 1 : (missileBase.MissileState == MissileBase.MissileStates.Cruise ? 0.75f : 0.33f));
                                        if (vesselDistance > sightDistance * sightDistance) continue; //missile outside of modified visibility range, disregard
                                    }
                                    if (MissileIsThreat(missileBase, myWpnManager))
                                    {
                                        results.incomingMissiles.Add(new IncomingMissile
                                        {
                                            guidanceType = missileBase.TargetingMode,
                                            distance = Vector3.Distance(missileBase.part.transform.position, myWpnManager.part.transform.position),
                                            time = AIUtils.TimeToCPA(missileBase.vessel, myWpnManager.vessel, 16f),
                                            position = missileBase.transform.position,
                                            vessel = missileBase.vessel,
                                            weaponManager = missileBase.SourceVessel != null ? VesselModuleRegistry.GetModule<MissileFire>(missileBase.SourceVessel) : null,
                                        });
                                        switch (missileBase.TargetingMode)
                                        {
                                            case MissileBase.TargetingModes.Heat:
                                                results.foundHeatMissile = true;
                                                break;
                                            case MissileBase.TargetingModes.Radar:
                                                results.foundRadarMissile = true;
                                                break;
                                            case MissileBase.TargetingModes.Laser:
                                                results.foundAGM = true;
                                                break;
                                            case MissileBase.TargetingModes.AntiRad: //How does one differentiate between a passive IR sensor and a passive AR sensor?
                                                results.foundAntiRadiationMissile = true; //admittedly, combining the two would result in launching flares at ARMs and turning off radar when having incoming heaters...
                                                break;
                                        }
                                    }
                                }
                                else
                                {
                                    Debug.LogWarning("[BDArmory.RadarUtils]: Supposed missile (" + loadedvessels.Current.vesselName + ") has no MissileBase!");
                                    tInfo.isMissile = false; // The target vessel has lost it's missile base component and should no longer count as a missile.
                                }
                            }
                            else
                            {
                                using (var weapon = VesselModuleRegistry.GetModules<ModuleWeapon>(loadedvessels.Current).GetEnumerator())
                                    while (weapon.MoveNext())
                                    {
                                        if (weapon.Current == null || weapon.Current.weaponManager == null) continue;
                                        if (ignoreMyTargetTargetingMe && myWpnManager.currentTarget != null && weapon.Current.weaponManager.vessel == myWpnManager.currentTarget.Vessel) continue;
                                        // If we're being targeted, calculate a miss distance
                                        if (weapon.Current.weaponManager.currentTarget != null && weapon.Current.weaponManager.currentTarget.Vessel == myWpnManager.vessel)
                                        {
                                            var missDistance = MissDistance(weapon.Current, myWpnManager.vessel);
                                            if (missDistance < results.missDistance)
                                            {
                                                results.firingAtMe = true;
                                                results.threatPosition = weapon.Current.fireTransforms[0].position; // Position of weapon that's attacking.
                                                results.threatVessel = weapon.Current.vessel;
                                                results.threatWeaponManager = weapon.Current.weaponManager;
                                                results.missDistance = missDistance;
                                                results.missDeviation = (weapon.Current.fireTransforms[0].position - myWpnManager.vessel.transform.position).magnitude * weapon.Current.maxDeviation / 2f * Mathf.Deg2Rad; // y = x*tan(θ), expansion of tan(θ) is θ + O(θ^3).
                                            }
                                        }
                                    }
                            }
                        }
                    }
                }
            // Sort incoming missiles by time
            if (results.incomingMissiles.Count > 0)
            {
                results.foundMissile = true;
                results.incomingMissiles.Sort(delegate (IncomingMissile m1, IncomingMissile m2) { return m1.time.CompareTo(m2.time); });

                // If the missile is further away than 16s (max time calculated), then sort by distance
                if (results.incomingMissiles[0].time >= 16f)
                {
                    results.foundMissile = true;
                    results.incomingMissiles.Sort(delegate (IncomingMissile m1, IncomingMissile m2) { return m1.distance.CompareTo(m2.distance); });
                }
            }

            return results;
        }

        public static bool MissileIsThreat(MissileBase missile, MissileFire mf, bool threatToMeOnly = true)
        {
            if (missile == null || missile.part == null) return false;
            Vector3 vectorFromMissile = mf.vessel.CoM - missile.part.transform.position;
            if ((vectorFromMissile.sqrMagnitude > (mf.guardRange * mf.guardRange)) && (missile.TargetingMode != MissileBase.TargetingModes.Radar)) return false;
            if (threatToMeOnly)
            {
                Vector3 relV = missile.vessel.Velocity() - mf.vessel.Velocity();
                bool approaching = Vector3.Dot(relV, vectorFromMissile) > 0;
                bool withinRadarFOV = (missile.TargetingMode == MissileBase.TargetingModes.Radar) ?
                    (Vector3.Angle(missile.GetForwardTransform(), vectorFromMissile) <= Mathf.Clamp(missile.lockedSensorFOV, 40f, 90f) / 2f) : false;
                var missileBlastRadiusSqr = 3f * missile.GetBlastRadius();
                missileBlastRadiusSqr *= missileBlastRadiusSqr;

                return (missile.HasFired && missile.MissileState > MissileBase.MissileStates.Drop && approaching &&
                            (
                                (missile.TargetPosition - (mf.vessel.CoM + (mf.vessel.Velocity() * Time.fixedDeltaTime))).sqrMagnitude < missileBlastRadiusSqr || // Target position is within blast radius of missile.
                                mf.vessel.PredictClosestApproachSqrSeparation(missile.vessel, mf.cmThreshold) < missileBlastRadiusSqr || // Closest approach is within blast radius of missile. 
                                withinRadarFOV // We are within radar FOV of missile boresight.
                            ));
            }
            else
            {
                using (var friendly = FlightGlobals.Vessels.GetEnumerator())
                    while (friendly.MoveNext())
                    {
                        if (friendly.Current == null)
                            continue;
                        if (VesselModuleRegistry.ignoredVesselTypes.Contains(friendly.Current.vesselType)) continue;
                        var wms = VesselModuleRegistry.GetModule<MissileFire>(friendly.Current);
                        if (wms == null || wms.Team != mf.Team)
                            continue;

                        Vector3 relV = missile.vessel.Velocity() - wms.vessel.Velocity();
                        bool approaching = Vector3.Dot(relV, vectorFromMissile) > 0;
                        bool withinRadarFOV = (missile.TargetingMode == MissileBase.TargetingModes.Radar) ?
                            (Vector3.Angle(missile.GetForwardTransform(), vectorFromMissile) <= Mathf.Clamp(missile.lockedSensorFOV, 40f, 90f) / 2f) : false;
                        var missileBlastRadiusSqr = 3f * missile.GetBlastRadius();
                        missileBlastRadiusSqr *= missileBlastRadiusSqr;

                        return (missile.HasFired && missile.TimeIndex > 1f && approaching &&
                                    (
                                        (missile.TargetPosition - (wms.vessel.CoM + (wms.vessel.Velocity() * Time.fixedDeltaTime))).sqrMagnitude < missileBlastRadiusSqr || // Target position is within blast radius of missile.
                                        wms.vessel.PredictClosestApproachSqrSeparation(missile.vessel, wms.cmThreshold) < missileBlastRadiusSqr || // Closest approach is within blast radius of missile. 
                                        withinRadarFOV // We are within radar FOV of missile boresight.
                                    ));
                    }
            }
            return false;
        }

        public static float MissDistance(ModuleWeapon threatWeapon, Vessel self) // Returns how far away bullets from enemy are from craft in meters
        {
            Transform fireTransform = threatWeapon.fireTransforms[0];
            // If we're out of range, then it's not a threat.
            if (threatWeapon.maxEffectiveDistance * threatWeapon.maxEffectiveDistance < (fireTransform.position - self.transform.position).sqrMagnitude) return float.MaxValue;
            // If we have a firing solution, use that, otherwise use relative vessel positions
            Vector3 aimDirection = fireTransform.forward;
            float targetCosAngle = threatWeapon.FiringSolutionVector != null ? Vector3.Dot(aimDirection, (Vector3)threatWeapon.FiringSolutionVector) : Vector3.Dot(aimDirection, (self.vesselTransform.position - fireTransform.position).normalized);

            // Find vertical component of aiming angle
            float angleThreat = targetCosAngle < 0 ? float.MaxValue : BDAMath.Sqrt(Mathf.Max(0f, 1f - targetCosAngle * targetCosAngle)); // Treat angles beyond 90 degrees as not a threat

            // Calculate distance between incoming threat position and its aimpoint (or self position)
            float distanceThreat = !threatWeapon.finalAimTarget.IsZero() ? Vector3.Magnitude(threatWeapon.finalAimTarget - fireTransform.position) : Vector3.Magnitude(self.vesselTransform.position - fireTransform.position);

            return angleThreat * distanceThreat; // Calculate aiming arc length (how far away the bullets will travel)

        }

        /// <summary>
        /// Helper method: check if line intersects terrain
        /// </summary>
        public static bool TerrainCheck(Vector3 start, Vector3 end)
        {
            if (!BDArmorySettings.IGNORE_TERRAIN_CHECK)
            {
                return Physics.Linecast(start, end, (int)LayerMasks.Scenery);
            }

            return false;
        }

        /// <summary>
        /// Helper method: map a position onto the radar display
        /// </summary>
        public static Vector2 WorldToRadar(Vector3 worldPosition, Transform referenceTransform, Rect radarRect, float maxDistance)
        {
            float scale = maxDistance / (radarRect.height / 2);
            Vector3 localPosition = referenceTransform.InverseTransformPoint(worldPosition);
            localPosition.y = 0;
            if (BDArmorySettings.LOGARITHMIC_RADAR_DISPLAY)
            {
                scale = Mathf.Log(localPosition.magnitude + 1) / Mathf.Log(maxDistance + 1);
                localPosition = localPosition.normalized * scale * scale; // Log^2 gives a nicer curve than log.
                return new Vector2(radarRect.width * (1 + localPosition.x) / 2, radarRect.height * (1 - localPosition.z) / 2);
            }
            else
            {
                return new Vector2((radarRect.width / 2) + (localPosition.x / scale), ((radarRect.height / 2) - (localPosition.z / scale)));
            }
        }

        /// <summary>
        /// Helper method: map a position onto the radar display (for non-omni radars)
        /// </summary>
        public static Vector2 WorldToRadarRadial(Vector3 worldPosition, Transform referenceTransform, Rect radarRect, float maxDistance, float maxAngle)
        {
            if (referenceTransform == null) return new Vector2();

            float scale = maxDistance / (radarRect.height);
            Vector3 localPosition = referenceTransform.InverseTransformPoint(worldPosition);
            localPosition.y = 0;
            float angle = Vector3.Angle(localPosition, Vector3.forward);
            if (localPosition.x < 0) angle = -angle;
            float xPos = (radarRect.width / 2) + ((angle / maxAngle) * radarRect.width / 2);
            float yPos = radarRect.height;

            if (BDArmorySettings.LOGARITHMIC_RADAR_DISPLAY)
            {
                scale = Mathf.Log(localPosition.magnitude + 1) / Mathf.Log(maxDistance + 1);
                yPos -= radarRect.height * scale * scale;
            }
            else
            {
                yPos -= localPosition.magnitude / scale;
            }
            Vector2 radarPos = new Vector2(xPos, yPos);
            return radarPos;
        }
    }
}