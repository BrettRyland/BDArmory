using System;
using System.Collections.Generic;
using UnityEngine;

using BDArmory.Control;
using BDArmory.CounterMeasure;
using BDArmory.Extensions;
using BDArmory.ModIntegration;
using BDArmory.Settings;
using BDArmory.Shaders;
using BDArmory.Targeting;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.Weapons;
using BDArmory.Weapons.Missiles;
using BDArmory.Damage;
using System.Net;

namespace BDArmory.Radar
{
    public static class RadarUtils
    {
        private static bool rcsSetupCompleted = false;
        private static int radarResolution = 128;

        private static bool hangarHiddenExternally = false;

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

        // additional anti-exploit 45° offset renderings
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
        internal const float ACTIVE_MISSILE_PING_PERSIST_TIME = 0.4f;
        internal const float LAUNCH_PING_PERSIST_TIME = 2f;         // Should be long enough to cover missile launching and turning to target
        internal const float MISSILE_DEFAULT_LOCKABLE_RCS = 5f;
        internal const float MISSILE_DEFAULT_GATE_RCS = 0.05f;

        // RCS Aspects
        private static float[,] rcsAspectsConstant = new float[45, 2] {
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

        private static Vector3[] rcsAspectsConstantDir;
        private static bool rcsAspectsConstantDirSet = false;

        private static float[,] rcsAspectsRealTime = new float[107, 2] {
            { 0f, 0f},
            { 90f, 0f},
            { 0f, 90f},
            { 11.25f, 0f},
            { 22.5f, 0f},
            { 33.75f, 0f},
            { 45f, 0f},
            { 56.25f, 0f},
            { 67.5f, 0f},
            { 78.75f, 0f},
            { 101.25f, 0f},
            { 112.5f, 0f},
            { 123.75f, 0f},
            { 135f, 0f},
            { 146.25f, 0f},
            { 157.5f, 0f},
            { 168.75f, 0f},
            { 5.625f, 8.42105f},
            { 5.625f, -8.42105f},
            { 16.875f, 8.42105f},
            { 16.875f, -8.42105f},
            { 28.125f, 8.42105f},
            { 28.125f, -8.42105f},
            { 39.375f, 8.42105f},
            { 39.375f, -8.42105f},
            { 50.625f, 8.42105f},
            { 50.625f, -8.42105f},
            { 61.875f, 8.42105f},
            { 61.875f, -8.42105f},
            { 73.125f, 8.42105f},
            { 73.125f, -8.42105f},
            { 84.375f, 8.42105f},
            { 84.375f, -8.42105f},
            { 95.625f, 8.42105f},
            { 95.625f, -8.42105f},
            { 106.875f, 8.42105f},
            { 106.875f, -8.42105f},
            { 118.125f, 8.42105f},
            { 118.125f, -8.42105f},
            { 129.375f, 8.42105f},
            { 129.375f, -8.42105f},
            { 140.625f, 8.42105f},
            { 140.625f, -8.42105f},
            { 151.875f, 8.42105f},
            { 151.875f, -8.42105f},
            { 163.125f, 8.42105f},
            { 163.125f, -8.42105f},
            { 174.375f, 8.42105f},
            { 174.375f, -8.42105f},
            { 0f, 18.94737f},
            { 0f, -18.94737f},
            { 22.5f, 18.94737f},
            { 22.5f, -18.94737f},
            { 45f, 18.94737f},
            { 45f, -18.94737f},
            { 67.5f, 18.94737f},
            { 67.5f, -18.94737f},
            { 90f, 18.94737f},
            { 90f, -18.94737f},
            { 112.5f, 18.94737f},
            { 112.5f, -18.94737f},
            { 135f, 18.94737f},
            { 135f, -18.94737f},
            { 157.5f, 18.94737f},
            { 157.5f, -18.94737f},
            { 180f, 18.94737f},
            { 180f, -18.94737f},
            { 11.25f, 31.57895f},
            { 11.25f, -31.57895f},
            { 33.75f, 31.57895f},
            { 33.75f, -31.57895f},
            { 56.25f, 31.57895f},
            { 56.25f, -31.57895f},
            { 78.75f, 31.57895f},
            { 78.75f, -31.57895f},
            { 101.25f, 31.57895f},
            { 101.25f, -31.57895f},
            { 123.75f, 31.57895f},
            { 123.75f, -31.57895f},
            { 146.25f, 31.57895f},
            { 146.25f, -31.57895f},
            { 168.75f, 31.57895f},
            { 168.75f, -31.57895f},
            { 0f, 47.36842f},
            { 0f, -47.36842f},
            { 45f, 47.36842f},
            { 45f, -47.36842f},
            { 90f, 47.36842f},
            { 90f, -47.36842f},
            { 135f, 47.36842f},
            { 135f, -47.36842f},
            { 180f, 47.36842f},
            { 180f, -47.36842f},
            { 22.5f, 66.31579f},
            { 22.5f, -66.31579f},
            { 67.5f, 66.31579f},
            { 67.5f, -66.31579f},
            { 112.5f, 66.31579f},
            { 112.5f, -66.31579f},
            { 157.5f, 66.31579f},
            { 157.5f, -66.31579f},
            { 0f, -90f},
            { 90f, 90f},
            { 90f, -90f},
            { 180f, 0f},
            { 180f, 90f},
            { 180f, -90f},
        };

        private static Vector3[] rcsAspectsRealTimeDir;
        private static bool rcsAspectsRealTimeDirSet = false;

        public static float minRCSHeatmap = float.MaxValue;
        public static float maxRCSHeatmap = 0f;

        private static int numAspectsForOverallRTEval = 83; // Use the first N rows of rcsAspectsRealTime for evaluating overall craft RCS
        public static float[,] editorRCSAspects = new float[3, 3]; // Worst three aspects
        public static Vector3[] editorRCSAspectsDir = new Vector3[3];
        public static float[,] RCSMatrix = null; // RCS Matrix for aspected
        static Shader RCSshader;
        static double[] rcsValues = [];
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
        public static TargetInfo GetVesselRadarSignature(Vessel v, bool updateJammers = true)
        {
            //1. baseSig = GetVesselRadarCrossSection
            TargetInfo ti = GetVesselRadarCrossSection(v, updateJammers: updateJammers);
            //2. modifiedSig = GetVesselModifiedSignature(baseSig)    //ECM-jammers with rcs reduction effect; other rcs reductions (stealth)
            ti.radarRCSReducedSignature = ti.radarBaseSignature; //These are needed for Radar functions to work!
            ti.radarModifiedSignature = ti.radarBaseSignature;
            //ti.radarLockbreakFactor = 1;

            return ti;
        }

        public static float GetVesselRadarSignatureAtAspect(TargetInfo ti, Vector3 radarPosition, float distance)
        {
            if (ti.radarSignatureMatrix is null)
                return ti.radarBaseSignature;

            try
            {
                Vector3 directionOfRadar = radarPosition - ti.Vessel.ReferenceTransform.position;
                /*Vector3 azComponent = Vector3.ProjectOnPlane(directionOfRadar, ti.Vessel.ReferenceTransform.forward);
                Vector3 elComponent = Vector3.ProjectOnPlane(directionOfRadar, ti.Vessel.ReferenceTransform.right);

                float azAngle = Mathf.Abs(Vector3.SignedAngle(ti.Vessel.ReferenceTransform.up, azComponent, ti.Vessel.ReferenceTransform.forward));
                float elAngle = Vector3.SignedAngle(ti.Vessel.ReferenceTransform.up, elComponent, -ti.Vessel.ReferenceTransform.right);*/

                // NOTE! vessel.ReferenceTransform.up actually faces forwards! And vessel.ReferenceTransform.forward actually faces down! Who would've thunk?
                // This is why we flip these in the function. It's also why `elAngle` has to be multiplied by a negative 1!
                //VectorUtils.GetAzimuthElevation(directionOfRadar, ti.Vessel.ReferenceTransform.up, ti.Vessel.ReferenceTransform.forward, out float azAngle, out float elAngle);
                float azAngle = VectorUtils.GetAngleOnPlane(directionOfRadar, ti.Vessel.ReferenceTransform.up, ti.Vessel.ReferenceTransform.right);
                float elAngle = VectorUtils.GetElevation(directionOfRadar, ti.Vessel.ReferenceTransform.forward, distance);

                // Note that we would've also had to negate azAngle (due to the flipped z axis) but since we assume craft are left/right symmetric
                // we just use an Abs here.
                azAngle = Mathf.Abs(azAngle);
                elAngle *= -1f;

                float signatureAtAspect = RCSMatrixEval(ti.radarSignatureMatrix, ti.radarBaseSignature, azAngle, elAngle);

                // Incorporate any signature modification
                signatureAtAspect *= ti.radarModifiedSignature / ti.radarBaseSignature;

                if (BDArmorySettings.DEBUG_RADAR)
                    Debug.Log($"[BDArmory.RadarUtils]: {ti.Vessel.vesselName} signature of {signatureAtAspect.ToString("0.00")} m² at az/el {azAngle.ToString("0.0")}/{elAngle.ToString("0.0")} deg.");

                return signatureAtAspect;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BDArmory.RadarUtils]: Failed to evaluate aspected RCS of {ti.Vessel.vesselName}, using radarModifiedSignature {ti.radarModifiedSignature} instead: {e.Message}");
                return ti.radarModifiedSignature;
            }
        }

        public static float RCSMatrixEval(float[,] rcsMatrix, float overallRCS, float azAngle, float elAngle)
        {
            float rcs;

            if (elAngle > 90f)
                elAngle = 180f - elAngle;
            else if (elAngle < -90f)
                elAngle = -180f - elAngle;

            // Find the three closest evaluated az/el RCS pairs and convert to barycentric coordinates for triangular interpolation
            // Using triangular interpolation so we don't need a perfect grid of az/el RCS pairs, which would drive up the time for RCS snapshots
            // The below code could probably be re-written in a cleaner way
            // --------------------------------------------------------------------------------
            float x1 = 0f;
            float y1 = 0f;
            float d1 = float.MaxValue;
            float w1 = 0f;
            float x2 = 0f;
            float y2 = 0f;
            float d2 = float.MaxValue;
            float w2 = 0f;
            float x3 = 0f;
            float y3 = 0f;
            float d3 = float.MaxValue;
            float w3 = 0f;
            float rcs1 = 0f;
            float rcs2 = 0f;
            float rcs3 = 0f;

            for (int i = 0; i < rcsMatrix.GetLength(0); i++)
            {
                float sqrDist = (rcsMatrix[i, 0] - azAngle) * (rcsMatrix[i, 0] - azAngle) + (rcsMatrix[i, 1] - elAngle) * (rcsMatrix[i, 1] - elAngle);

                if (sqrDist < d3)
                {
                    if (sqrDist < d2)
                    {
                        if (sqrDist < d1)
                        {
                            d3 = d2;
                            x3 = x2;
                            y3 = y2;
                            rcs3 = rcs2;

                            d2 = d1;
                            x2 = x1;
                            y2 = y1;
                            rcs2 = rcs1;

                            d1 = sqrDist;
                            x1 = rcsMatrix[i, 0];
                            y1 = rcsMatrix[i, 1];
                            rcs1 = rcsMatrix[i, 2];
                        }
                        else
                        {
                            d3 = d2;
                            x3 = x2;
                            y3 = y2;
                            rcs3 = rcs2;

                            d2 = sqrDist;
                            x2 = rcsMatrix[i, 0];
                            y2 = rcsMatrix[i, 1];
                            rcs2 = rcsMatrix[i, 2];
                        }
                    }
                    else
                    {
                        d3 = sqrDist;
                        x3 = rcsMatrix[i, 0];
                        y3 = rcsMatrix[i, 1];
                        rcs3 = rcsMatrix[i, 2];
                    }
                }
            }
            // --------------------------------------------------------------------------------

            // Compute interpolation weights using barycentric coordinates
            w1 = ((y2 - y3) * (azAngle - x3) + (x3 - x2) * (elAngle - y3)) / ((y2 - y3) * (x1 - x3) + (x3 - x2) * (y1 - y3));
            w2 = ((y3 - y1) * (azAngle - x3) + (x1 - x3) * (elAngle - y3)) / ((y2 - y3) * (x1 - x3) + (x3 - x2) * (y1 - y3));
            w3 = 1 - w1 - w2;

            if ((w1 > 0) && (w2 > 0) && (w3 > 0)) // If point is inside triangle, weights will all be positive, if not inside triangle use nearest neighbor
                rcs = w1 * rcs1 + w2 * rcs2 + w3 * rcs3;
            else
                rcs = rcs1;

            // Compute weighted average for aspect
            rcs = (1 - BDArmorySettings.ASPECTED_RCS_OVERALL_RCS_WEIGHT) * rcs + BDArmorySettings.ASPECTED_RCS_OVERALL_RCS_WEIGHT * overallRCS;

            return rcs;
        }

        /// <summary>
        /// Internal method: get a vessel base radar signature
        /// </summary>
        private static TargetInfo GetVesselRadarCrossSection(Vessel v, bool force = false, bool updateJammers = true)
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
                    if (!missile.updateRadarCS)
                    {
                        return ti;
                    }

                    if (missile.ActiveRadar || missile.radarLOALSearching)
                    {
                        ti.radarBaseSignature = RCS_MISSILES;
                    }
                    else
                    {
                        ti.radarBaseSignature = missile.missileRadarCrossSection;
                    }

                    ti.radarBaseSignatureNeedsUpdate = false;
                    ti.radarSignatureMatrixNeedsUpdate = false;
                    ti.UpdateBounds();
                    missile.updateRadarCS = false;

                    if (updateJammers)
                    {
                        // Update ECM impact on RCS if base RCS is modified
                        VesselECMJInfo jammer = v.gameObject.GetComponent<VesselECMJInfo>();
                        if (jammer != null)
                        {
                            jammer.UpdateJammerStrength(ti);
                        }
                    }
                    else
                    {
                        // NOTE: This might be called on startup depending on who initializes first, if VesselECMJInfo calls
                        // UpdateJammerStrength before tInfo gets constructed, then this will get triggered.
                        Debug.Log($"[BDArmory.RadarUtils] DETECTED INFINITE LOOP! Missile: {missile.shortName} on vessel: {(v ? v.vesselName : "null")} caused infinite loop for some reason!");
                    }

                    return ti;
                }
                else
                {
                    Debug.LogWarning("[BDArmory.RadarUtils]: Supposed missile (" + v.vesselName + ") has no MissileBase!");
                    ti.isMissile = false;
                }
            }

            // Run intensive RCS rendering if 1. It has not been done yet, 2. If the competition just started (capture vessel changes such as gear-raise or robotics)
            if (force || ti.radarBaseSignature == -1 || ti.radarBaseSignatureNeedsUpdate || (BDArmorySettings.ASPECTED_RCS && ti.radarSignatureMatrixNeedsUpdate))
            {
                if (BDArmorySettings.DEBUG_RADAR) Debug.Log($"[BDArmory.RadarUtils] Performing RCS Rendering! Vessel: {(v ? v.vesselName : "null")}, with mass: {ti.radarMassAtUpdate}, force: {force}, ti.radarBaseSignature: {ti.radarBaseSignature}, radarBaseSignatureNeedsUpdate: {ti.radarBaseSignatureNeedsUpdate} and radarSignatureMatrixNeedsUpdate: {ti.radarSignatureMatrixNeedsUpdate}.");

                // is it just some debris? then dont bother doing a real rcs rendering and just fake it with the parts mass
                if (VesselModuleRegistry.IgnoredVesselTypes.Contains(v.vesselType) || !v.IsControllable)
                {
                    ti.radarBaseSignature = v.GetTotalMass();
                }
                else
                {
                    // perform radar rendering to obtain base cross section
                    try
                    {
                        ti = RenderVesselRadarSnapshot(v, v.transform, ti);
                    }
                    catch (Exception e) // Unity physics sometimes breaks (MMGs sometimes cause this).
                    {
                        Debug.LogWarning($"[BDArmory.RadarUtils]: Failed to get a radar snapshot of {v.GetName()}, using mass instead: {e.Message}");
                        ti.radarBaseSignature = v.GetTotalMass();
                    }
                }

                ti.radarSignatureMatrixNeedsUpdate = BDArmorySettings.ASPECTED_RCS ? false : ti.radarSignatureMatrixNeedsUpdate;
                ti.radarBaseSignatureNeedsUpdate = false;
                ti.alreadyScheduledRCSUpdate = false;
                ti.radarMassAtUpdate = v.GetTotalMass();

                if (updateJammers)
                {
                    // Update ECM impact on RCS if base RCS is modified
                    VesselECMJInfo jammer = v.gameObject.GetComponent<VesselECMJInfo>();
                    if (jammer != null)
                    {
                        jammer.UpdateJammerStrength(ti);
                    }
                }
                else
                {
                    // NOTE: This might be called on startup depending on who initializes first, if VesselECMJInfo calls
                    // UpdateJammerStrength before tInfo gets constructed, then this will get triggered.
                    Debug.Log($"[BDArmory.RadarUtils] DETECTED INFINITE LOOP! Vessel: {(v ? v.vesselName : "null")}, with mass: {ti.radarMassAtUpdate}, ti.radarBaseSignature: {ti.radarBaseSignature}, radarBaseSignatureNeedsUpdate: {ti.radarBaseSignatureNeedsUpdate} and radarSignatureMatrixNeedsUpdate: {ti.radarSignatureMatrixNeedsUpdate} caused infinite loop for some reason!");
                }
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
                // lockbreak_factor = baseSig/modifiedSig x (1 - lockBreakStrength/baseSig/100)
                chaffFactor = vci.GetChaffMultiplier();
            }

            return chaffFactor;
        }
        /// <summary>
        /// Get the degree vessel's sonar return degraded by bubble screens between sonar and target, similar to lockBreakFactor
        /// </summary>
        public static float GetVesselBubbleFactor(Vector3 sensorPos, Vessel v)
        {
            float Factor = CMBubble.RaycastBubblescreen(new Ray(sensorPos, v.CoM - sensorPos));

            return Factor;
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
        public static TargetInfo RenderVesselRadarSnapshot(Vessel v, Transform t, TargetInfo ti = null, bool inEditorZoom = false)
        {
            if (VesselModuleRegistry.IgnoredVesselTypes.Contains(v.vesselType)) Debug.LogError($"[BDArmory.RadarUtils]: Rendering radar snapshot of {v.vesselName}, which should be being ignored!");
            bool aspected = BDArmorySettings.ASPECTED_RCS;
            float[,] rcsAspects = (aspected) ? rcsAspectsRealTime : rcsAspectsConstant;
            int numAspects = rcsAspects.GetLength(0); // Number of aspects
            Vector3[] rcsDirArray = aspected ? rcsAspectsRealTimeDir : rcsAspectsConstantDir;
            if (!(aspected ? rcsAspectsRealTimeDirSet : rcsAspectsConstantDirSet))
            {
                rcsDirArray = new Vector3[numAspects];
                for (int i = 0; i < rcsDirArray.Length; i++)
                {
                    float cosEl = Mathf.Cos(rcsAspects[i, 1] / 180f * Mathf.PI);
                    float sinEl = Mathf.Sin(rcsAspects[i, 1] / 180f * Mathf.PI); // Mathf.Sign(rcsAspects[i, 1]) * Mathf.Sqrt(1f - cosEl * cosEl); // Technically faster, but might as well be more accurate since we only calc this once
                    float cosAz = Mathf.Cos(rcsAspects[i, 0] / 180f * Mathf.PI);
                    float sinAz = Mathf.Sin(rcsAspects[i, 0] / 180f * Mathf.PI); // Mathf.Sqrt(1f - cosAz * cosAz);
                    // Pre-calculate aspect directions in local coords
                    rcsDirArray[i] = new Vector3(-cosEl * sinAz,
                                                  cosEl * cosAz,
                                                 -sinEl);
                }

                if (aspected)
                {
                    rcsAspectsRealTimeDir = rcsDirArray;
                    rcsAspectsRealTimeDirSet = true;
                }
                else
                {
                    rcsAspectsConstantDir = rcsDirArray;
                    rcsAspectsConstantDirSet = true;
                }
            }
            float[,] rcsMatrix = new float[numAspects, 3];
            const float radarDistance = 1000f;
            const float radarFOV = 2.0f;
            Vector3 presentationPosition = -t.forward * radarDistance;
            rcsTotal = 0;

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

                if (ti is not null)
                {
                    ti.radarBaseSignature = 0f;
                    ti.radarSignatureMatrix = null;
                }

                return ti;
            }

            // Disable rendering of conformal decals, which messes with the parent part's RCS render.
            if (ConformalDecals.hasConformalDecals)
                SetConformalDecalRendering(v, false);

            // If in editor, turn off rendering hangar
            if (!HighLogic.LoadedSceneIsFlight)
                SetHangarRender(false);

            float rcsVariable = 0f;
            //if (editorRCSAspects is null) editorRCSAspects = new float[3, 3];
            Array.Clear(editorRCSAspects, 0, 9);
            //if (editorRCSAspectsDir is null) editorRCSAspectsDir = new Vector3[3];
            Array.Clear(editorRCSAspectsDir, 0, 3);
            //if (rcsValues is null)
            //    rcsValues = new double[numAspects];
            //else
            if (rcsValues.Length != numAspects)
                Array.Resize(ref rcsValues, numAspects);
            Array.Clear(rcsValues, 0, numAspects);
            Vector3 aspect;
            // Loop through all aspects
            for (int i = 0; i < numAspects; i++)
            {
                // Determine camera vector for aspect
                aspect = t.TransformDirection(rcsDirArray[i]);
                //aspect = Vector3.RotateTowards(t.up, -t.up, rcsAspects[i, 0] / 180f * Mathf.PI, 0);
                //aspect = Vector3.RotateTowards(aspect, Vector3.Cross(t.right, t.up), -rcsAspects[i, 1] / 180f * Mathf.PI, 0);

                // Render aspect
                RenderSinglePass(v, t, false, aspect, vesselbounds, radarDistance, radarFOV, rcsRenderingVariable, drawTextureVariable);

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
                rcsValues[i] = rcsVariable;
                minRCSHeatmap = Mathf.Min(minRCSHeatmap, (float)rcsValues[i]);

                // Add values to RCS Matrix for real-time evaluation
                rcsMatrix[i, 0] = rcsAspects[i, 0];
                rcsMatrix[i, 1] = rcsAspects[i, 1];
                rcsMatrix[i, 2] = rcsVariable;

                // Remember worst three RCS aspects to display in editor
                if (inEditorZoom)
                {
                    if (!BDArmorySettings.ASPECTED_RCS) // Use worst aspects by default
                    {
                        if (rcsVariable > editorRCSAspects[0, 2])
                        {
                            editorRCSAspects[2, 0] = editorRCSAspects[1, 0];
                            editorRCSAspects[2, 1] = editorRCSAspects[1, 1];
                            editorRCSAspects[2, 2] = editorRCSAspects[1, 2];
                            editorRCSAspectsDir[2] = editorRCSAspectsDir[1];

                            editorRCSAspects[1, 0] = editorRCSAspects[0, 0];
                            editorRCSAspects[1, 1] = editorRCSAspects[0, 1];
                            editorRCSAspects[1, 2] = editorRCSAspects[0, 2];
                            editorRCSAspectsDir[1] = editorRCSAspectsDir[0];

                            editorRCSAspects[0, 0] = rcsAspects[i, 0];
                            editorRCSAspects[0, 1] = rcsAspects[i, 1];
                            editorRCSAspects[0, 2] = rcsVariable;
                            editorRCSAspectsDir[0] = aspect;
                        }
                        else if (rcsVariable > editorRCSAspects[1, 2])
                        {
                            editorRCSAspects[2, 0] = editorRCSAspects[1, 0];
                            editorRCSAspects[2, 1] = editorRCSAspects[1, 1];
                            editorRCSAspects[2, 2] = editorRCSAspects[1, 2];
                            editorRCSAspectsDir[2] = editorRCSAspectsDir[1];

                            editorRCSAspects[1, 0] = rcsAspects[i, 0];
                            editorRCSAspects[1, 1] = rcsAspects[i, 1];
                            editorRCSAspects[1, 2] = rcsVariable;
                            editorRCSAspectsDir[1] = aspect;
                        }
                        else if (rcsVariable > editorRCSAspects[2, 2])
                        {
                            editorRCSAspects[2, 0] = rcsAspects[i, 0];
                            editorRCSAspects[2, 1] = rcsAspects[i, 1];
                            editorRCSAspects[2, 2] = rcsVariable;
                            editorRCSAspectsDir[2] = aspect;
                        }
                    }
                    else if (BDArmorySettings.ASPECTED_RCS && i <= 2) // For aspected RCS use first three evaluated aspects
                    {
                        editorRCSAspects[i, 0] = rcsAspects[i, 0];
                        editorRCSAspects[i, 1] = rcsAspects[i, 1];
                        editorRCSAspects[i, 2] = rcsVariable;
                        editorRCSAspectsDir[i] = aspect;
                    }
                }

                if (BDArmorySettings.DEBUG_RADAR)
                {
                    Debug.Log($"[BDArmory.RadarUtils]: - Vessel rcs for (az/el) is: {rcsAspects[i, 0]}/{rcsAspects[i, 1]} = rcsVariable: {rcsVariable}");
                }
            }

            // Re-size array for overall RCS calc when aspected RCS is enabled
            if (BDArmorySettings.ASPECTED_RCS)
                Array.Resize(ref rcsValues, numAspectsForOverallRTEval);

            // Use third quartile for the total RCS (gives better results than average)
            rcsTotal = (float)Percentile(rcsValues, 75d);

            // If we are in the editor, render the three highest RCS aspects
            if (inEditorZoom)
            {
                // Determine camera vectors for aspects
                /*Vector3 aspect1 = Vector3.RotateTowards(t.up, -t.up, editorRCSAspects[0, 0] / 180f * Mathf.PI, 0);
                aspect1 = Vector3.RotateTowards(aspect1, Vector3.Cross(t.right, t.up), -editorRCSAspects[0, 1] / 180f * Mathf.PI, 0);
                Vector3 aspect2 = Vector3.RotateTowards(t.up, -t.up, editorRCSAspects[1, 0] / 180f * Mathf.PI, 0);
                aspect2 = Vector3.RotateTowards(aspect2, Vector3.Cross(t.right, t.up), -editorRCSAspects[1, 1] / 180f * Mathf.PI, 0);
                Vector3 aspect3 = Vector3.RotateTowards(t.up, -t.up, editorRCSAspects[2, 0] / 180f * Mathf.PI, 0);
                aspect3 = Vector3.RotateTowards(aspect3, Vector3.Cross(t.right, t.up), -editorRCSAspects[2, 1] / 180f * Mathf.PI, 0);*/

                // Render three highest aspects
                RenderSinglePass(v, t, inEditorZoom, editorRCSAspectsDir[0], vesselbounds, radarDistance, radarFOV, rcsRendering1, drawTexture1);
                RenderSinglePass(v, t, inEditorZoom, editorRCSAspectsDir[1], vesselbounds, radarDistance, radarFOV, rcsRendering2, drawTexture2);
                RenderSinglePass(v, t, inEditorZoom, editorRCSAspectsDir[2], vesselbounds, radarDistance, radarFOV, rcsRendering3, drawTexture3);

                // Store RCS Matrix
                RCSMatrix = rcsMatrix;
            }
            else
            {
                RCSMatrix = null;
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
            //if (!BDArmorySettings.DEBUG_RADAR)
            //{
            using (List<Part>.Enumerator parts = (HighLogic.LoadedSceneIsEditor ? EditorLogic.fetch.ship.Parts.GetEnumerator() : v.parts.GetEnumerator()))
                while (parts.MoveNext())
                {
                    HitpointTracker a = parts.Current.GetComponent<HitpointTracker>();
                    FlagDecal flag = parts.Current.GetComponent<FlagDecal>();
                    if (parts.Current.GetComponent<KerbalEVA>()) continue;
                    if (flag != null)
                    {
                        if (!flag.flagDisplayed)
                        {
                            flag.ToggleFlag();
                        }
                    }
                    var r = parts.Current.GetComponentsInChildren<Renderer>();
                    for (int i = 0; i < r.Length; i++)
                    {
                        try
                        {
                            if (r[i].GetComponentInParent<Part>() != parts.Current) continue; // Don't recurse to child parts.
                            int key = r[i].material.GetInstanceID();
                            if (!a.defaultShader.ContainsKey(key))
                            {
                                if (BDArmorySettings.DEBUG_RADAR) Debug.Log($"[BDArmory.RadarUtils]: {r[i].material.name} ({key}) not found in defaultShader for part {parts.Current.partInfo.name} on {(HighLogic.LoadedSceneIsFlight ? v.vesselName : EditorLogic.fetch.ship.shipName)}"); // Enable this to see what materials aren't getting RCS shaders applied to them.
                                continue;
                            }
                            if (r[i].material.shader != a.defaultShader[key])
                            {
                                if (a.defaultShader[key] != null)
                                {
                                    r[i].material.shader = a.defaultShader[key];
                                }
                                if (a.defaultColor.ContainsKey(key))
                                {
                                    if (a.defaultColor[key] != null)
                                    {
                                        if (parts.Current.name.Contains("B9.Aero.Wing.Procedural"))
                                            r[i].material.SetColor("_MainTex", a.defaultColor[key]);
                                        else
                                            r[i].material.SetColor("_Color", a.defaultColor[key]);
                                    }
                                    else
                                    {
                                        if (parts.Current.name.Contains("B9.Aero.Wing.Procedural"))
                                            r[i].material.SetColor("_MainTex", Color.white);
                                        else
                                            r[i].material.SetColor("_Color", Color.white);
                                    }
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.Log($"[RadarUtils]: material on {parts.Current.name} could not find default shader/color: {e.Message}\n{e.StackTrace}");
                        }
                    }
                }
            //}
            // Re-enable rendering of conformal decals.
            if (ConformalDecals.hasConformalDecals)
                SetConformalDecalRendering(v, true);

            // If in editor, turn back on rendering of hangar
            if (!HighLogic.LoadedSceneIsFlight)
                SetHangarRender(true);

            if (BDArmorySettings.DEBUG_RADAR)
            {
                Debug.Log($"[BDArmory.RadarUtils]: - Vessel all-aspect rcs is: rcsTotal: {rcsTotal}");
            }

            if (ti is not null)
            {
                ti.radarBaseSignature = rcsTotal;
                ti.radarSignatureMatrix = rcsMatrix;
            }

            return ti;
        }

        public static void SetConformalDecalRendering(Vessel v, bool renderEnabled)
        {
            if (!ConformalDecals.hasConformalDecals) return;
            if (HighLogic.LoadedSceneIsFlight && v == null) return; // Invalid vessel to render.
            using List<Part>.Enumerator parts = HighLogic.LoadedSceneIsEditor ? EditorLogic.fetch.ship.Parts.GetEnumerator() : v.Parts.GetEnumerator();
            while (parts.MoveNext())
            {
                foreach (var module in parts.Current.Modules)
                {
                    if ((module.moduleName == "ModuleConformalDecal") || (module.moduleName == "ModuleConformalFlag") || (module.moduleName == "ModuleConformalText"))
                    {
                        if (BDArmorySettings.DEBUG_RADAR) Debug.Log($"[BDArmory.RadarUtils]: Found {module.moduleName} for {parts.Current.name}.");
                        foreach (var r in parts.Current.GetComponentsInChildren<Renderer>())
                        {
                            if (r.GetComponentInParent<Part>() != parts.Current) continue; // Don't recurse to child parts.
                            r.enabled = renderEnabled;
                            if (BDArmorySettings.DEBUG_RADAR) Debug.Log($"[BDArmory.RadarUtils]: Set rendering for {r.name} on {parts.Current.name} to {renderEnabled}.");
                        }
                        var cdComponent = ConformalDecals.Instance.GetMCDComponent(parts.Current);
                        if (cdComponent != null) ConformalDecals.Instance.SetMCDIsAttached(cdComponent, renderEnabled);
                    }
                }
            }
        }

        // Code to hide/show SPH/VAB during RCS render to prevent the hangar itself from affecting RCS calculation, code modified from HangarExtender
        private static void SetHangarRender(bool renderEnabled)
        {
            if (!renderEnabled)
                hangarHiddenExternally = false;
            else if (renderEnabled && hangarHiddenExternally)
                return;

            string[] rcsNames = { "vabscenery", "sphscenery", "vablvl1", "vablvl2", "vablvl3", "vabmodern", "sphlvl1", "sphlvl2", "sphlvl3", "sphmodern", "vabcrew", "sphcrew" };
            List<Transform> rootNodes = new List<Transform>();

            foreach (Transform t in UnityEngine.Object.FindObjectsOfType<Transform>())
            {
                Transform newTransform = t.root;
                while (newTransform.parent != null)
                {
                    newTransform = newTransform.parent;
                }
                if (!rootNodes.Contains(newTransform))
                {
                    rootNodes.Add(newTransform);
                }
            }

            // Check for hidden hangars if we are setting rendering to false
            if (!renderEnabled)
            {
                foreach (Transform t in rootNodes)
                {
                    foreach (string s in rcsNames)
                    {
                        if (string.Equals(t.name.ToLower(), s))
                        {
                            List<SkinnedMeshRenderer> skinRenderers = new List<SkinnedMeshRenderer>();
                            t.transform.GetComponentsInChildren<SkinnedMeshRenderer>(skinRenderers);
                            foreach (SkinnedMeshRenderer r in skinRenderers)
                            {
                                if (!renderEnabled) // If turning rendering off, check if it is already off
                                    hangarHiddenExternally = hangarHiddenExternally || !r.enabled;

                                if (hangarHiddenExternally) return;
                            }
                            List<MeshRenderer> renderers = new List<MeshRenderer>();
                            t.transform.GetComponentsInChildren<MeshRenderer>(renderers);
                            foreach (MeshRenderer r in renderers)
                            {
                                if (!renderEnabled) // If turning rendering off, check if it is already off
                                    hangarHiddenExternally = hangarHiddenExternally || !r.enabled;

                                if (hangarHiddenExternally) return;
                            }
                        }
                    }
                }
            }

            // Set rendering
            foreach (Transform t in rootNodes)
            {
                foreach (string s in rcsNames)
                {
                    if (string.Equals(t.name.ToLower(), s))
                    {
                        List<SkinnedMeshRenderer> skinRenderers = new List<SkinnedMeshRenderer>();
                        t.transform.GetComponentsInChildren<SkinnedMeshRenderer>(skinRenderers);
                        foreach (SkinnedMeshRenderer r in skinRenderers)
                        {
                            if (!renderEnabled) // If turning rendering off, check if it is already off
                                hangarHiddenExternally = hangarHiddenExternally || !r.enabled;

                            if (hangarHiddenExternally)
                                return;
                            else
                                r.enabled = renderEnabled;
                        }
                        List<MeshRenderer> renderers = new List<MeshRenderer>();
                        t.transform.GetComponentsInChildren<MeshRenderer>(renderers);
                        foreach (MeshRenderer r in renderers)
                        {
                            if (!renderEnabled)
                                hangarHiddenExternally = hangarHiddenExternally || !r.enabled;

                            if (hangarHiddenExternally)
                                return;
                            else
                                r.enabled = renderEnabled;
                        }
                    }
                }
            }
        }

        // Used to calculate percentiles for RCS dataset
        internal static double Percentile(double[] array, double dblPercentage = 0)
        {
            System.Array.Sort(array);

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

        // Currently unused
        public static void RCSHeatMap(float[,] rcsMatrix, Texture2D rcsMap)
        {
            float az;
            float el;
            float rcs;
            Color rcsColor;

            //Detect edges on slice and write to output
            for (int x = 0; x < radarResolution; x++)
            {
                az = (float)x / (radarResolution - 1) * 180f;
                for (int y = 0; y < radarResolution; y++)
                {
                    el = (float)y / (radarResolution - 1) * 180f - 90f;
                    rcs = RCSMatrixEval(rcsMatrix, rcsTotal, az, el);
                    rcs = Mathf.Clamp(rcs, minRCSHeatmap, maxRCSHeatmap);
                    if (rcs <= rcsTotal)
                        rcsColor = Color.HSVToRGB((0.5f * (rcsTotal - rcs) / (rcsTotal - minRCSHeatmap) + 0.5f) / 3f, 1, 1);
                    else
                        rcsColor = Color.HSVToRGB((0.5f - (0.5f / (maxRCSHeatmap - rcsTotal) * (rcs - rcsTotal))) / 3, 1, 1);
                    rcsMap.SetPixel(x, y, rcsColor);
                }
            }
            rcsMap.Apply();
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
            RenderSinglePass(v, t, inEditorZoom, t.up, vesselbounds, radarDistance, radarFOV, rcsRenderingFrontal, drawTextureFrontal);
            // pass2: lateral
            RenderSinglePass(v, t, inEditorZoom, t.right, vesselbounds, radarDistance, radarFOV, rcsRenderingLateral, drawTextureLateral);
            // pass3: Ventral
            RenderSinglePass(v, t, inEditorZoom, t.forward, vesselbounds, radarDistance, radarFOV, rcsRenderingVentral, drawTextureVentral);

            //additional 45° offset renderings:
            RenderSinglePass(v, t, inEditorZoom, (t.up + t.right), vesselbounds, radarDistance, radarFOV, rcsRenderingFrontal, drawTextureFrontal45);
            RenderSinglePass(v, t, inEditorZoom, (t.right + t.forward), vesselbounds, radarDistance, radarFOV, rcsRenderingLateral, drawTextureLateral45);
            RenderSinglePass(v, t, inEditorZoom, (t.forward - t.up), vesselbounds, radarDistance, radarFOV, rcsRenderingVentral, drawTextureVentral45);

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
        /// Internal helper method
        /// </summary>
        private static void RenderSinglePass(Vessel v, Transform t, bool inEditorZoom, Vector3 cameraDirection, Bounds vesselbounds, float radarDistance, float radarFOV, RenderTexture rcsRendering, Texture2D rcsTexture)
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

            /////////////////
            Color StealthAdjust;
            RCSshader = BDAShaderLoader.RCSShader;
            using (List<Part>.Enumerator parts = (HighLogic.LoadedSceneIsEditor ? EditorLogic.fetch.ship.Parts.GetEnumerator() : v.parts.GetEnumerator()))
                while (parts.MoveNext())
                {
                    HitpointTracker a = parts.Current.GetComponent<HitpointTracker>();
                    FlagDecal flag = parts.Current.GetComponent<FlagDecal>();
                    if (flag != null)
                    {
                        if (flag.flagDisplayed)
                        {
                            flag.ToggleFlag();
                        }
                    }
                    if (parts.Current.GetComponent<KerbalEVA>()) continue; //ignore kerbals
                    var r = parts.Current.GetComponentsInChildren<Renderer>();
                    try
                    {
                        if (!a.RegisterProcWingShader && parts.Current.name.Contains("B9.Aero.Wing.Procedural"))
                        {
                            for (int s = 0; s < r.Length; s++)
                            {
                                if (r[s].GetComponentInParent<Part>() != parts.Current) continue; // Don't recurse to child parts.
                                int key = r[s].material.GetInstanceID();
                                a.defaultShader.Add(key, r[s].material.shader);
                                if (r[s].material.HasProperty("_Color"))
                                {
                                    a.defaultColor.Add(key, r[s].material.color);
                                }
                            }
                            a.RegisterProcWingShader = true;
                        }
                        for (int i = 0; i < r.Length; i++)
                        {
                            if (!a.defaultShader.ContainsKey(r[i].material.GetInstanceID())) continue; // Don't modify shaders that we don't have defaults for as we can't then replace them.
                            if (r[i].GetComponentInParent<Part>() != parts.Current) continue; // Don't recurse to child parts.
                            if (r[i].material.shader.name.Contains("Alpha")) continue;
                            if (r[i].material.shader.name.Contains("Waterfall")) continue;
                            if (r[i].material.shader.name.Contains("KSP/Particles")) continue;
                            r[i].material.shader = RCSshader;
                            r[i].material.SetVector("_LIGHTDIR", -cameraDirection);
                            r[i].material.SetColor("_RCSCOLOR", Color.white);
                            if (a != null)
                            {
                                StealthAdjust.r = a.radarReflectivity;
                                StealthAdjust.g = a.radarReflectivity;
                                StealthAdjust.b = a.radarReflectivity;
                                StealthAdjust.a = 1;
                                r[i].material.SetColor("_RCSCOLOR", StealthAdjust);
                            }
                        }
                    }
                    catch
                    {
                        Debug.Log("[RadarUtils]: material on " + parts.Current.name + "could not find set RCS shader/color");
                    }
                }
            /////////////////

            radarCam.targetTexture = rcsRendering;
            RenderTexture.active = rcsRendering;
            //Shader.SetGlobalVector("_LIGHTDIR", -cameraDirection);
            //Shader.SetGlobalColor("_RCSCOLOR", Color.white);
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
                rcsRenderingVariable = new RenderTexture(radarResolution, radarResolution, (int)RenderTextureFormat.R8);
                rcsRendering1 = new RenderTexture(radarResolution, radarResolution, (int)RenderTextureFormat.R8);
                rcsRendering2 = new RenderTexture(radarResolution, radarResolution, (int)RenderTextureFormat.R8);
                rcsRendering3 = new RenderTexture(radarResolution, radarResolution, (int)RenderTextureFormat.R8);

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
                rcsRenderingFrontal = new RenderTexture(radarResolution, radarResolution, (int)RenderTextureFormat.R8);
                rcsRenderingLateral = new RenderTexture(radarResolution, radarResolution, (int)RenderTextureFormat.R8);
                rcsRenderingVentral = new RenderTexture(radarResolution, radarResolution, (int)RenderTextureFormat.R8);
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
        /// Determine for a targetDirection relative to the radar position how much effect the ground clutter factor will have.
        /// </summary>
        public static float GetRadarGroundClutterModifier(float clutterFactor, Vector3 position, Vector3 targetDirection, TargetInfo ti)
        {
            //Vector3 upVector = referenceTransform.up;
            Vector3 upVector = VectorUtils.GetUpDirection(position);

            //ground clutter factor when looking down:
            //Vector3 targetDirection = (vesselposition - position);
            float angleFromUp = VectorUtils.AnglePreNormalized(upVector, targetDirection);
            float lookDownAngle = angleFromUp - 90; // result range: -90 .. +90
            lookDownAngle = Mathf.Clamp(lookDownAngle, 0, 90);      // result range:   0 .. +90

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
            if (targetV.ActiveController().WM == null) return 1f; // Don't evaluate SOJ effects for targets without weapons managers
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

                    MissileFire wm = loadedvessels.Current.ActiveController().WM;

                    if (!wm) continue;
                    if (team.IsFriendly(wm.Team)) continue;

                    VesselECMJInfo standOffJammer = loadedvessels.Current.gameObject.GetComponent<VesselECMJInfo>();

                    if (standOffJammer && (standOffJammer.lockBreakStrength > 0))
                    {
                        Vector3 relPositionJammer = loadedvessels.Current.CoM - position;
                        Vector3 relPositionTarget = targetV.CoM - position;

                        // Modify  total lockbreak strength of standoff jammer by angle off the vector to target
                        float angleModifier = relPositionTarget.DotNormalized(relPositionJammer);
                        float sojLBS = Mathf.Clamp01(angleModifier * angleModifier * angleModifier);

                        // Modify lockbreak strength by relative sqr distance
                        sojLBS *= Mathf.Clamp(1 - Mathf.Log10(relPositionJammer.sqrMagnitude / relPositionTarget.sqrMagnitude), 0f, 3f);

                        // Add up all stand up jammer lockbreaks
                        standOffJammingMod += sojLBS * standOffJammer.lockBreakStrength;

                        if (BDArmorySettings.DEBUG_RADAR) debugSOJ += sojLBS * standOffJammer.lockBreakStrength + ", " + loadedvessels.Current.GetName() + "\n";
                    }
                }

            float modifiedSignature = Mathf.Max(signature - standOffJammingMod / 100f, 0f);

            if ((BDArmorySettings.DEBUG_RADAR) && (modifiedSignature != signature)) Debug.Log("[BDArmory.RadarUtils]: Standoff Jamming: " + targetV.GetName() + " signature relative to " + v.GetName() + " modified from " + signature + " to " + modifiedSignature + "\n" + debugSOJ);

            return modifiedSignature / signature;
        }

        private static float CalculateRadarNotchingModifier(Vector3 position, Vector3 vesselposition, Vector3 vesselsrfvel,
            FloatCurve radarRangeGate, FloatCurve radarVelocityGate,
            float radarMaxVelocityGate, float radarMaxRangeGate, float radarMinVelocityGate, float radarMinRangeGate,
            FloatCurve radarGlintCurve, float radarGlintMult,
            float terrainRange, float targetRange, float targetAlt, out float notchVMod, out float notchRMod, out float glintMod)
        {
            terrainRange -= targetRange;

            // If we allow radars to see through water via BDArmorySettings.CHECK_WATER_TERRAIN, then we'll get < terrain ranges
            if (terrainRange > 0)
            {
                // In cases where terrainRange > targetAlt, we use a weighted average to bring down the terrainRange
                if (terrainRange > targetAlt)
                {
                    terrainRange = BDAMath.Sqrt(0.25f * terrainRange * terrainRange + 0.75f * targetAlt * targetAlt);
                }
            }
            else
            {
                // When using BDArmorySettings.CHECK_WATER_TERRAIN, we just use the full notch mult if the target is being seen through water
                terrainRange = 0;
            }

            notchVMod = 0f;
            notchRMod = 0f;
            if (radarGlintMult < 0 || radarGlintCurve.minTime == float.MaxValue || terrainRange > radarGlintCurve.maxTime)
            {
                glintMod = radarGlintMult;
            }
            else
            {
                glintMod = radarGlintMult * radarGlintCurve.Evaluate(terrainRange);
            }

            if (radarRangeGate.minTime == float.MaxValue || radarVelocityGate.minTime == float.MaxValue)
                return 1f;

            Vector3 targetDirection = (vesselposition - position) / targetRange;

            float inLineSpeed = Mathf.Abs(Vector3.Dot(vesselsrfvel, targetDirection));

            //if (BDArmorySettings.DEBUG_RADAR) Debug.Log($"[BDArmory.RadarUtils]: Current inLineSpeed: {inLineSpeed}.");

            if (radarMaxVelocityGate < inLineSpeed)
                return 1f;

            inLineSpeed = Mathf.Max(inLineSpeed, radarMinVelocityGate);

            notchVMod = (1f - Mathf.Clamp01(radarVelocityGate.Evaluate(inLineSpeed))) * BDArmorySettings.RADAR_NOTCHING_FACTOR;

            if (radarMaxRangeGate < terrainRange)
            {
                if (BDArmorySettings.DEBUG_RADAR) Debug.Log($"[BDArmory.RadarUtils]: Current notch multiplier: 1. Current notchVMod: {notchVMod}, notchRMod: 0.");
                return 1f;
            }

            terrainRange = Mathf.Max(terrainRange, radarMinRangeGate);

            float multiplier = notchVMod * Mathf.Clamp01(radarRangeGate.Evaluate(terrainRange));
            notchRMod = multiplier;

            multiplier = 1f - multiplier;

            if (BDArmorySettings.DEBUG_RADAR) Debug.Log($"[BDArmory.RadarUtils]: Current notch multiplier: {multiplier}. Current notchVMod: {notchVMod}, notchRMod: {notchRMod}.");

            return multiplier;
        }

        public static float GetRadarNotchingSCR(float signature, float fov, float targetRange, float terrainRange, float terrainAngle)
        {
            float groundAngleFac = 1f / Mathf.Cos(terrainAngle * Mathf.Deg2Rad);
            float equivArea = terrainRange / (targetRange * 1000f); // Get equivalent radius of target RCS, targetRange is in km
            equivArea *= equivArea * signature * groundAngleFac; // Similar cones, equivalent area of the target projected onto the ground

            float groundArea = terrainRange * fov * Mathf.Deg2Rad; // Approximation of radius given the current FoV
            groundArea = groundArea * groundArea * groundAngleFac * Mathf.PI;

            float SCR = equivArea / (groundArea * BDArmorySettings.RADAR_NOTCHING_SCR_FACTOR);  // Default to -20 dBm^2

            if (BDArmorySettings.DEBUG_RADAR) Debug.Log($"[BDArmory.RadarUtils]: Current notch SCR: {SCR}. Current Terrain Range: {terrainRange}");

            return SCR;
        }

        private static bool RadarTerrainNotchingCheck(bool isNotSonar, Vector3 position,
            FloatCurve radarRangeGate, FloatCurve radarVelocityGate,
            float radarMaxVelocityGate, float radarMaxRangeGate, float radarMinVelocityGate, float radarMinRangeGate,
            FloatCurve radarGlintCurve, float radarGlintMult,
            Vessel radarVessel, Vessel targetVessel, Vector3 targetPosition, float distance, out float terrainR, out float terrainAngle,
            out float notchMultiplier, out float notchVMod, out float notchRMod, out float glintMod, bool isMissile = false)
        {
            // NOTE: Distance here HAS to be given in km for radars and m for missiles, why? because radar FloatCurves are in km and missile FloatCurves are in m
            notchVMod = 0f;
            notchRMod = 0f;
            glintMod = radarGlintMult;
            notchMultiplier = 1f;
            terrainR = 0f;
            terrainAngle = 90f;

            if (isNotSonar)
            {
                bool surfaceTarget = (targetVessel.Landed || targetVessel.Splashed);
                // If radar, then check against water
                if (BDArmorySettings.RADAR_NOTCHING && !surfaceTarget && radarMinRangeGate != float.MaxValue && radarMinVelocityGate != float.MaxValue)
                {
                    // Because radar curves are in km, we have to convert distance to km if it's not a missile
                    if (TerrainCheck(position, targetPosition, FlightGlobals.currentMainBody, distance + radarMaxRangeGate, out terrainR, out terrainAngle, true))
                        return false;
                    notchMultiplier = CalculateRadarNotchingModifier(position, targetVessel.CoM, targetVessel.srf_velocity,
                        radarRangeGate, radarVelocityGate,
                        radarMaxVelocityGate, radarMaxRangeGate, radarMinVelocityGate, radarMinRangeGate,
                        radarGlintCurve, radarGlintMult,
                        terrainR, distance, (float)targetVessel.radarAltitude, out notchVMod, out notchRMod, out glintMod);
                }
                else
                {
                    if (targetVessel.Splashed)
                    {
                        if (TerrainCheck(position, targetPosition + targetVessel.upAxis * ((targetVessel.altitude < 0.0 && targetVessel.altitude > -20.0) ? (-targetVessel.altitude + 2.0) : 0.0), FlightGlobals.currentMainBody, !isMissile && BDArmorySettings.RADAR_ALLOW_SURFACE_WARFARE && surfaceTarget && (radarVessel.Landed || radarVessel.Splashed)))
                            return false;
                    }
                    else
                    {
                        if (TerrainCheck(position, targetPosition, FlightGlobals.currentMainBody, !isMissile && BDArmorySettings.RADAR_ALLOW_SURFACE_WARFARE && surfaceTarget && (radarVessel.Landed || radarVessel.Splashed)))
                            return false;
                    }
                    if (radarGlintMult > 0 && radarGlintCurve.minTime != float.MaxValue && targetVessel.altitude < radarGlintCurve.maxTime)
                    {
                        glintMod *= radarGlintCurve.Evaluate((float)targetVessel.altitude);
                    }
                }
            }
            else
            {
                if (TerrainCheck(position, targetPosition))
                    return false;
            }

            return true;
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

            // fov is cone width, so we use half of it
            fov *= 0.5f;

            // guard clauses
            if (!radar)
                return false;

            Vessel radarVessel = radar.vessel;

            using (var loadedvessels = BDATargetManager.LoadedVessels.GetEnumerator())
                while (loadedvessels.MoveNext())
                {
                    // ignore null and unloaded
                    if (loadedvessels.Current == null || !loadedvessels.Current.loaded || !loadedvessels.Current.isActiveAndEnabled) continue;
                    if (loadedvessels.Current.IsUnderwater() && radar.sonarMode == ModuleRadar.SonarModes.None) //don't detect underwater targets with radar
                        continue;
                    if (!loadedvessels.Current.Splashed && radar.sonarMode != ModuleRadar.SonarModes.None) //don't detect flying targets with sonar
                        continue;
                    // ignore self, ignore behind ray
                    Vector3 vectorToTarget = (loadedvessels.Current.CoM - ray.origin);
                    //float distance = vectorToTarget.sqrMagnitude;
                    (float distance, Vector3 directionToTarget) = vectorToTarget.MagNorm();
                    float angle = VectorUtils.AnglePreNormalized(ray.direction, directionToTarget);
                    if ((distance * distance < RADAR_IGNORE_DISTANCE_SQR) ||
                         (angle > 90f))
                        continue;

                    if (angle < fov)
                    {
                        float terrainR; // = float.MaxValue;
                        float terrainAngle; // = 90f;
                        float notchMultiplier; // = 1f;
                        float notchVMod; // = 0f;
                        float notchRMod; // = 0f;
                        float glintMod; // = 1f;

                        // evaluate range

                        if (!RadarTerrainNotchingCheck(radar.sonarMode == ModuleRadar.SonarModes.None, ray.origin,
                            radar.radarRangeGate, radar.radarVelocityGate,
                            radar.radarMaxVelocityGate, radar.radarMaxRangeGate, radar.radarMinVelocityGate, radar.radarMinRangeGate,
                            radar.radarGlintCurve, radar.radarGlintMult,
                            radar.vessel, loadedvessels.Current, loadedvessels.Current.CoM, distance,
                            out terrainR, out terrainAngle, out notchMultiplier, out notchVMod, out notchRMod, out glintMod))
                            continue;

                        // get vessel's radar signature
                        TargetInfo ti = GetVesselRadarSignature(loadedvessels.Current);
                        float signature = 0;
                        // The only scenario in which this should occur is if the vessel is about to be destroyed,
                        // in which case the previous TargetInfo was destroyed, a new one was created, however
                        // as the vessel is de-activated, the new TargetInfo does not have a vessel as Awake() is
                        // not even called. I would've thought !vessel.loaded would've caught this but perhaps not.
                        if (ti.Vessel == null)
                            continue;
                        if (radar.sonarMode != ModuleRadar.SonarModes.passive)
                        {
                            signature = (BDArmorySettings.ASPECTED_RCS) ? GetVesselRadarSignatureAtAspect(ti, ray.origin, distance) : ti.radarModifiedSignature;
                            signature *= GetRadarGroundClutterModifier(radar.radarGroundClutterFactor, ray.origin, directionToTarget, ti);
                            signature *= GetStandoffJammingModifier(radar.vessel, radar.WeaponManager.Team, ray.origin, loadedvessels.Current, signature);
                            if (radar.sonarMode == ModuleRadar.SonarModes.Active && radar.vessel.Splashed && loadedvessels.Current.Splashed) signature *= GetVesselBubbleFactor(ray.origin, loadedvessels.Current);
                            if (radar.radarCanNotch)
                            {
                                signature *= notchMultiplier;
                            }
                        }
                        else
                        {
                            float selfNoise = BDATargetManager.GetVesselAcousticSignature(radar.vessel, ray.origin).Item1 / 3;
                            signature = BDATargetManager.GetVesselAcousticSignature(loadedvessels.Current, ray.origin).Item1 - selfNoise;
                        }
                        // no ecm lockbreak factor here
                        // no chaff factor here

                        // Must convert from m to km due to all radar FloatCurves being specified in km
                        if (RadarCanDetect(radar, signature, distance * 0.001f))
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
                                dataArray[dataIndex] = new TargetSignatureData(loadedvessels.Current, signature, _range: distance, _notchVMod: notchVMod, _notchRMod: notchRMod, _glintMod: glintMod);
                                dataArray[dataIndex].lockedByRadar = radar;
                                dataIndex++;
                                hasLocked = true;
                            }
                        }

                        //  our radar ping can be received at a higher range than we can detect, according to RWR range ping factor:
                        if (radar.sonarMode != ModuleRadar.SonarModes.passive)
                        {
                            if (distance < radar.radarMaxDistanceDetect * RWR_PING_RANGE_FACTOR)
                                RadarWarningReceiver.PingRWR(loadedvessels.Current, ray.origin, radar.rwrType, radar.signalPersistTimeForRwr, radarVessel);
                        }
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
        public static int RadarUpdateMissileLock(Ray ray, float fov, ref TargetSignatureData[] dataArray, float dataPersistTime, MissileBase missile, bool pingRWR)
        {
            int dataIndex = 0;
            //bool hasLocked = false;

            // guard clauses
            if (!missile)
            {
                dataArray[0] = TargetSignatureData.noTarget;
                return dataIndex;
            }
            if (!missile.vessel.Splashed && missile.GetWeaponClass() == WeaponClasses.SLW || missile.vessel.IsUnderwater() && missile.GetWeaponClass() != WeaponClasses.SLW) //that torpedo sonar is going to be really effective up in the air...
            {
                dataArray[0] = TargetSignatureData.noTarget;
                return dataIndex;
            }
            if (BDArmorySettings.DEBUG_RADAR)
            {
                Debug.Log($"[BDArmory.RadarUtils{{RadarUpdateMissileLock}}] Missile: {missile.shortName} with UUID: {missile.vessel.id} beginning scan with FoV: {fov}. pingRWR? {pingRWR}");
            }

            // fov gives cone width, so halve it
            fov *= 0.5f;

            using (var loadedvessels = BDATargetManager.LoadedVessels.GetEnumerator())
                while (loadedvessels.MoveNext())
                {
                    // ignore null, unloaded and ignored types
                    if (loadedvessels.Current == null || loadedvessels.Current.packed || !loadedvessels.Current.loaded || !loadedvessels.Current.isActiveAndEnabled || loadedvessels.Current == missile.vessel) continue;
                    if (!loadedvessels.Current.Splashed && missile.GetWeaponClass() == WeaponClasses.SLW) continue; //don't detect non-water targets if a torpedo
                    if (loadedvessels.Current.IsUnderwater() && missile.GetWeaponClass() != WeaponClasses.SLW) continue; //don't detect underwater targets with radar

                    // IFF code check to prevent friendly lock-on (neutral vessel without a weaponmanager WILL be lockable!)
                    MissileFire wm = loadedvessels.Current.ActiveController().WM;
                    if (wm != null)
                    {
                        if (missile.hasIFF && missile.Team.IsFriendly(wm.Team)) continue;
                    }

                    // ignore self, ignore behind ray
                    Vector3 vectorToTarget = (loadedvessels.Current.CoM - ray.origin);
                    (float distance, Vector3 directionToTarget) = vectorToTarget.MagNorm();
                    float angle = VectorUtils.AnglePreNormalized(ray.direction, directionToTarget);
                    //if (((vectorToTarget).sqrMagnitude < RADAR_IGNORE_DISTANCE_SQR) ||
                    //     (Vector3.Dot(vectorToTarget, ray.direction) < 0))

                    if (BDArmorySettings.DEBUG_RADAR)
                    {
                        Debug.Log($"[BDArmory.RadarUtils{{RadarUpdateMissileLock}}] Processing Target: {loadedvessels.Current.name} with UUID: {loadedvessels.Current.id} at distance: {distance}m and angle: {angle}/{fov}");
                    }

                    // No targets behind the seeker's view! Note maybe this should change,
                    // as unlike radars, this is called with `maxOffBoresight` in some cases
                    // rather than `lockedSensorFoV`, and should be treated as an overall
                    // scan rather than just a sensor-look scan.
                    if (angle > 90f) continue;

                    if (angle > fov) continue;

                    // evaluate range
                    // range already evaluated above, NOTE: no conversion from m to km is needed here due to
                    // all missile radar FloatCurves being in m, not km. Unfortunately as this convention
                    // began in legacy code, not much we can do about it!

                    float terrainR; // = float.MaxValue;
                    float terrainAngle; // = 90f;
                    float notchMultiplier; // = 1f;
                    float notchVMod; // = 0f;
                    float notchRMod; // = 0f;
                    float glintMod; // = 1f;

                    if (!RadarTerrainNotchingCheck(missile.GetWeaponClass() != WeaponClasses.SLW, ray.origin,
                        missile.activeRadarRangeGate, missile.activeRadarVelocityGate,
                        missile.activeRadarVelocityFilter, missile.activeRadarRangeFilter, missile.activeRadarVelocityGate.minTime, missile.activeRadarRangeGate.minTime,
                        missile.activeRadarGlintCurve, missile.activeRadarGlintMult,
                        missile.vessel, loadedvessels.Current, loadedvessels.Current.CoM, distance,
                        out terrainR, out terrainAngle, out notchMultiplier, out notchVMod, out notchRMod, out glintMod, true))
                    {
                        if (BDArmorySettings.DEBUG_RADAR)
                        {
                            Debug.Log($"[BDArmory.RadarUtils{{RadarUpdateMissileLock}}] Target rejected due to terrain!");
                        }
                        continue;
                    }

                    // get vessel's radar signature
                    TargetInfo ti = GetVesselRadarSignature(loadedvessels.Current);
                    float signature = 10f;
                    // See comment in RadarUpdateScanBoresight for more info about this.
                    if (ti.Vessel == null)
                    {
                        if (BDArmorySettings.DEBUG_RADAR)
                        {
                            Debug.Log($"[BDArmory.RadarUtils{{RadarUpdateMissileLock}}] Target rejected due to missile vessel in TargetInfo!");
                        }
                        continue; 
                    }

                    if (ti != null)
                    {
                        signature = (BDArmorySettings.ASPECTED_RCS) ? GetVesselRadarSignatureAtAspect(ti, ray.origin, distance) : ti.radarModifiedSignature;
                        // no ground clutter modifier for missiles
                        signature *= ti.radarLockbreakFactor;    //multiply lockbreak factor from active ecm
                    }                                                                 //do not multiply chaff factor here
                    signature *= GetStandoffJammingModifier(missile.vessel, missile.Team, ray.origin, loadedvessels.Current, signature);
                    if (missile.GetWeaponClass() == WeaponClasses.SLW) signature *= GetVesselBubbleFactor(missile.transform.position, loadedvessels.Current);

                    float baseSignature = signature;
                    // Does notching affect the notch mult?
                    if (missile.activeRadarCanNotch)
                    {
                        signature *= notchMultiplier;
                    }

                    // check SCR if we're checking notching, are not a torpedo, the target isn't splashed and the radar is active
                    // technically the notchMultiplier < 1f condition should account for the rest
                    // Note, since SCR behavior is only for locked radars ActiveRadar has to be true, hence why it's evaluated
                    // before notchMultiplier, otherwise we don't account for SCR
                    bool SCRcheck = BDArmorySettings.RADAR_NOTCHING && missile.activeRadarCanNotch && (notchMultiplier < 1f) && missile.GetWeaponClass() != WeaponClasses.SLW && !loadedvessels.Current.Splashed;

                    if (distance < missile.activeRadarRange)
                    {
                        //evaluate if we can detect such a signature at that range
                        float minDetectSig = missile.activeRadarLockTrackCurve.Evaluate(distance);

                        if (signature > minDetectSig || (SCRcheck && baseSignature > minDetectSig && GetRadarNotchingSCR(baseSignature, fov, distance * 0.001f, terrainR, terrainAngle) > missile.activeRadarMinTrackSCR))
                        {
                            // Only fill up array up to dataArray.Length, need the rest of the processing for RWR pings though
                            if (dataIndex < dataArray.Length)
                            {
                                // detected by radar
                                dataArray[dataIndex] = new TargetSignatureData(loadedvessels.Current, signature, _notchVMod: notchVMod, _notchRMod: notchRMod, _range: distance, _glintMod: glintMod);
                                dataIndex++;
                            }
                        }
                        else
                        {
                            if (BDArmorySettings.DEBUG_RADAR)
                            {
                                Debug.Log($"[BDArmory.RadarUtils{{RadarUpdateMissileLock}}] Target rejected due to failing sig check! signature: {signature}/{minDetectSig}, SCRcheck: {SCRcheck}{(SCRcheck ? $", baseSignature: {baseSignature}/{minDetectSig}, SCR: {GetRadarNotchingSCR(baseSignature, fov, distance * 0.001f, terrainR, terrainAngle)}/{missile.activeRadarMinTrackSCR}" : "")}");
                            }
                        }
                    }
                    else
                    {
                        if (BDArmorySettings.DEBUG_RADAR)
                        {
                            Debug.Log($"[BDArmory.RadarUtils{{RadarUpdateMissileLock}}] Target rejected, out of range!");
                        }
                    }

                    //  our radar ping can be received at a higher range than we can detect, according to RWR range ping factor:
                    if (pingRWR && distance < missile.activeRadarRange * RWR_PING_RANGE_FACTOR)
                    {
                        if (missile.GetWeaponClass() == WeaponClasses.SLW && missile.vessel.altitude < 0)
                                RadarWarningReceiver.PingRWR(loadedvessels.Current, ray.origin, RadarWarningReceiver.RWRThreatTypes.TorpedoLock, ACTIVE_MISSILE_PING_PERSIST_TIME, missile.vessel);
                        else if (missile.GetWeaponClass() != WeaponClasses.SLW && missile.vessel.altitude > 0) //if for whatever reason someone is trying to fire AMRAAMS underwater...
                            RadarWarningReceiver.PingRWR(loadedvessels.Current, ray.origin, RadarWarningReceiver.RWRThreatTypes.MissileLock, ACTIVE_MISSILE_PING_PERSIST_TIME, missile.vessel);
                    }
                }

            if (dataIndex < dataArray.Length)
            {
                dataArray[dataIndex] = TargetSignatureData.noTarget;
            }

            return dataIndex;
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
        public static bool RadarUpdateScanLock(MissileFire myWpnManager, float directionAngle, float elevationAngle, float azFov, float elFov, ModuleRadar radar, bool modeTryLock, ref TargetSignatureData[] dataArray, float dataPersistTime = 0f)
        {
            Vector3 position = radar.currPosition;
            Vector3 forwardVector = radar.currForward;
            Vector3 upVector = radar.currUp;
            Vector3 rightVector = radar.currRight;
            //Vector3 lookDirection = Quaternion.AngleAxis(directionAngle, upVector) * forwardVector;
            int dataIndex = 0;
            bool hasLocked = false;
            float selfNoise = 0;

            // fov is cone width, so we halve it
            azFov *= 0.5f;
            elFov *= 0.5f;

            //for (int i = 0; i < lockArray.Length; i++)
            //{
            //    lockArray[i] = false;
            //}

            // guard clauses
            if (!myWpnManager || !myWpnManager.vessel || !radar)
                return false;
            if (radar.sonarMode == ModuleRadar.SonarModes.passive)
            {
                selfNoise = BDATargetManager.GetVesselAcousticSignature(radar.vessel, position).Item1 / 3;
            }
            Vessel radarVessel = radar.vessel;
            float radarAlt = FlightGlobals.getAltitudeAtPos(position);
            if (radar.sonarMode != ModuleRadar.SonarModes.None && radarAlt > 0 ||
                radar.sonarMode == ModuleRadar.SonarModes.None && radarAlt < -10)
            {
                if (BDArmorySettings.DEBUG_RADAR)
                {
                    Debug.Log($"[BDArmory.RadarUtils{{RadarUpdateScanLock}}] Vessel: {radarVessel.vesselName} with UUID: {radarVessel.id}, {(radar.sonarMode == ModuleRadar.SonarModes.None ? "Radar" : "Sonar")}: {radar.name}, not in operating medium, aborting!");
                }
                return false; //sonar in air/radar underwater
            }

            if (BDArmorySettings.DEBUG_RADAR)
            {
                Debug.Log($"[BDArmory.RadarUtils{{RadarUpdateScanLock}}] Vessel: {radarVessel.vesselName} with UUID: {radarVessel.id}, {(radar.sonarMode == ModuleRadar.SonarModes.None ? "Radar" : "Sonar")}: {radar.name}, scanning az/el: {directionAngle}/{elevationAngle} with az/el FoV: {azFov}/{elFov}.");
            }

            using (var loadedvessels = BDATargetManager.LoadedVessels.GetEnumerator())
                while (loadedvessels.MoveNext())
                {
                    // ignore null, unloaded and self
                    if (loadedvessels.Current == null || loadedvessels.Current.packed || !loadedvessels.Current.loaded || !loadedvessels.Current.isActiveAndEnabled) continue;
                    if (loadedvessels.Current == myWpnManager.vessel) continue;

                    Vector3 vectorToTarget = loadedvessels.Current.CoM - position;
                    float distance = vectorToTarget.sqrMagnitude;

                    // ignore too close ones
                    if (distance < RADAR_IGNORE_DISTANCE_SQR)
                        continue;
                    if (loadedvessels.Current.IsUnderwater() && radar.sonarMode == ModuleRadar.SonarModes.None) //don't detect underwater targets with radar
                        continue;
                    if (!loadedvessels.Current.Splashed && radar.sonarMode != ModuleRadar.SonarModes.None) //don't detect sonar targets when out of water
                        continue;

                    // evaluate range
                    //TODO: Performance! better if we could switch to sqrMagnitude...
                    distance = BDAMath.Sqrt(distance);

                    // Get azimuth and elevation relative to the target
                    //VectorUtils.GetAzimuthElevation(vectorToTarget, forwardVector, upVector, out float targetAz, out float targetEl);
                    float targetAz = VectorUtils.GetAngleOnPlane(vectorToTarget, forwardVector, rightVector);
                    float targetEl = VectorUtils.GetElevation(vectorToTarget, upVector, distance);

                    // Correct for omnidirectional radars
                    if (directionAngle > 180f)
                        directionAngle -= 360f;

                    // Since azimuth can go all the way around, if we get a
                    // reflex angle, get the conjugate.
                    float azDiff = Mathf.Abs(targetAz - directionAngle);
                    if (azDiff > 180f)
                        azDiff = 360f - azDiff;

                    if (BDArmorySettings.DEBUG_RADAR) Debug.Log($"[BDArmory.RadarUtils{{RadarUpdateScanLock}}] Processing Target: {loadedvessels.Current.name} with UUID: {loadedvessels.Current.id} at distance: {distance}m; targetAz: {targetAz}, diff: {azDiff}/{azFov}, targetEL: {targetEl}, diff: {Mathf.Abs(targetEl - elevationAngle)}/{elFov}");

                    if (azDiff < azFov && Mathf.Abs(targetEl - elevationAngle) < elFov)
                    {
                        float terrainR; // = float.MaxValue;
                        float terrainAngle; // = 90f;
                        float notchMultiplier; //= 1f;
                        float notchVMod; // = 0f;
                        float notchRMod; // = 0f;
                        float glintMod; // = 1f;

                        Vector3 directionToTarget = vectorToTarget / distance;

                        if (!RadarTerrainNotchingCheck(radar.sonarMode == ModuleRadar.SonarModes.None, position,
                            radar.radarRangeGate, radar.radarVelocityGate,
                            radar.radarMaxVelocityGate, radar.radarMaxRangeGate, radar.radarMinVelocityGate, radar.radarMinRangeGate,
                            radar.radarGlintCurve, radar.radarGlintMult,
                            radar.vessel, loadedvessels.Current, loadedvessels.Current.CoM, distance,
                            out terrainR, out terrainAngle, out notchMultiplier, out notchVMod, out notchRMod, out glintMod))
                            continue;

                        // get vessel's radar signature
                        TargetInfo ti = GetVesselRadarSignature(loadedvessels.Current);
                        float signature = 1;
                        // See comment in RadarUpdateScanBoresight for more info about this
                        if (ti.Vessel == null)
                            continue;
                        if (radar.sonarMode != ModuleRadar.SonarModes.passive)    //radar or active soanr
                        {
                            signature = BDArmorySettings.ASPECTED_RCS ? GetVesselRadarSignatureAtAspect(ti, position, distance) : ti.radarModifiedSignature;
                            signature *= GetRadarGroundClutterModifier(radar.radarGroundClutterFactor, position, directionToTarget, ti);
                            if (radar.sonarMode == ModuleRadar.SonarModes.Active && radar.vessel.Splashed && loadedvessels.Current.Splashed) signature *= GetVesselBubbleFactor(position, loadedvessels.Current);

                            if (radar.radarCanNotch)
                                signature *= notchMultiplier;
                        }
                        else //passive sonar
                            signature = BDATargetManager.GetVesselAcousticSignature(loadedvessels.Current, position).Item1 - selfNoise;
                        //do not multiply chaff factor here

                        distance *= 0.001f; // Need to convert from m to km because of radar FloatCurves...

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
                                signature *= GetStandoffJammingModifier(radar.vessel, radar.WeaponManager.Team, position, loadedvessels.Current, signature);

                                if (signature >= minLockSig && RadarCanDetect(radar, signature, distance)) // Must be able to detect and lock to lock targets
                                {
                                    if (BDArmorySettings.DEBUG_RADAR) Debug.Log($"[BDArmory.RadarUtils] Target: {loadedvessels.Current.name} passed lock checks!");

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

                                    if (!(dataIndex < dataArray.Length))
                                    {
                                        Array.Resize(ref dataArray, BDATargetManager.LoadedVessels.Count);
                                    }

                                    dataArray[dataIndex] = new TargetSignatureData(loadedvessels.Current, signature, _notchVMod: notchVMod, _notchRMod: notchRMod, _range: 1000f * distance, _glintMod: glintMod);
                                    dataArray[dataIndex].lockedByRadar = radar;
                                    dataIndex++;
                                    hasLocked = true;
                                }
                            }
                            if (radar.sonarMode != ModuleRadar.SonarModes.passive)
                            {
                                //  our radar ping can be received at a higher range than we can lock/track, according to RWR range ping factor:
                                if (distance < radar.radarMaxDistanceLockTrack * RWR_PING_RANGE_FACTOR)
                                    RadarWarningReceiver.PingRWR(loadedvessels.Current, position, radar.rwrType, radar.signalPersistTimeForRwr, radarVessel);
                            }
                        }
                        else   // SCAN/DETECT TARGETS:
                        {
                            //evaluate if we can detect such a signature at that range
                            if (RadarCanDetect(radar, signature, distance))
                            {
                                if (BDArmorySettings.DEBUG_RADAR) Debug.Log($"[BDArmory.RadarUtils] Target: {loadedvessels.Current.name} passed detection checks!");

                                // detected by radar
                                if (myWpnManager != null)
                                {
                                    BDATargetManager.ReportVessel(loadedvessels.Current, myWpnManager, true);
                                }

                                // report scanned targets only
                                radar.ReceiveContactData(new TargetSignatureData(loadedvessels.Current, signature, _range: 1000f * distance, _notchVMod: notchVMod, _notchRMod: notchRMod, _glintMod: glintMod), false);
                            }
                            if (radar.sonarMode != ModuleRadar.SonarModes.passive)
                            {
                                //  our radar ping can be received at a higher range than we can detect, according to RWR range ping factor:
                                if (distance < radar.radarMaxDistanceDetect * RWR_PING_RANGE_FACTOR)
                                    RadarWarningReceiver.PingRWR(loadedvessels.Current, position, radar.rwrType, radar.signalPersistTimeForRwr, radarVessel);
                            }
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

            // fov is cone width, so we use half of it
            fov *= 0.5f;

            Vector3 directionToTarget = Vector3.zero;
            float distance = -1f;

            if (BDArmorySettings.DEBUG_RADAR)
            {
                Debug.Log($"[BDArmory.RadarUtils{{RadarUpdateLockTrack}}] {(radar.sonarMode == ModuleRadar.SonarModes.None ? "Radar" : "Sonar")}: {radar.name} with UUID: {radar.vessel.id}, checking target: {(lockedVessel ? lockedVessel.vesselName : "null")} with UUID: {(lockedVessel ? lockedVessel.id : "null")}.");
            }

            // first: re-acquire lock if temporarily lost
            if (!lockedVessel)
            {
                using (var loadedvessels = BDATargetManager.LoadedVessels.GetEnumerator())
                    while (loadedvessels.MoveNext())
                    {
                        // ignore null, unloaded
                        if (loadedvessels.Current == null || !loadedvessels.Current.loaded || !loadedvessels.Current.isActiveAndEnabled) continue;

                        // Seems like we only use it once so I've left it as is, but I've written this down as a reminder
                        // do consider replacing all .transform.position calls with .CoM
                        //targetPosition = loadedvessels.Current.transform.position;

                        // ignore self, ignore behind ray
                        Vector3 vectorToTarget = (loadedvessels.Current.CoM - ray.origin);
                        (float tempDistance, Vector3 tempDirectionToTarget) = vectorToTarget.MagNorm();
                        float angle = VectorUtils.AnglePreNormalized(ray.direction, tempDirectionToTarget);
                        if ((tempDistance * tempDistance < RADAR_IGNORE_DISTANCE_SQR) ||
                             (angle > 90f))
                            continue;

                        // See RadarUpdateScanBoresight for discussion of the efficiency
                        // of performing VectorUtils.Angle() here, repeating the above Dot
                        // product.
                        if (angle < fov)
                        {
                            float sqrDist = Vector3.SqrMagnitude(loadedvessels.Current.CoM - predictedPos);
                            if (sqrDist < closestSqrDist)
                            {
                                // best candidate so far, take it
                                closestSqrDist = sqrDist;
                                lockedVessel = loadedvessels.Current;
                                distance = tempDistance;
                                directionToTarget = tempDirectionToTarget;
                            }
                        }
                    }

                if (BDArmorySettings.DEBUG_RADAR)
                {
                    Debug.Log($"[BDArmory.RadarUtils{{RadarUpdateLockTrack}}] Selected Target: {(lockedVessel ? lockedVessel.vesselName : "null")} with UUID: {(lockedVessel ? lockedVessel.id : "null")} at distance: {distance}m for re-acquisition.");
                }
            }
            else
            {
                (distance, directionToTarget) = (lockedVessel.CoM - ray.origin).MagNorm();
                if (BDArmorySettings.DEBUG_RADAR)
                {
                    Debug.Log($"[BDArmory.RadarUtils{{RadarUpdateLockTrack}}] Target at distance: {distance}m.");
                }
            }

            // second: track that lock
            if (lockedVessel)
            {
                // Check within FoV
                if (!radar.CheckFOVDir(directionToTarget))
                {
                    //if (BDArmorySettings.DEBUG_RADAR)
                    //    Debug.Log($"[BDArmory.RadarUtils{{RadarUpdateLockTrack}}] Failed FoV Check!");
                    return false;
                }

                // evaluate range
                //TODO: Performance! better if we could switch to sqrMagnitude...

                float terrainR; // = float.MaxValue;
                float terrainAngle; // = 90f;
                float notchMultiplier; // = 1f;
                float notchVMod; // = 0f;
                float notchRMod; // = 0f;
                float glintMod; // = 1f;

                if (!RadarTerrainNotchingCheck(radar.sonarMode == ModuleRadar.SonarModes.None, ray.origin,
                            radar.radarRangeGate, radar.radarVelocityGate,
                            radar.radarMaxVelocityGate, radar.radarMaxRangeGate, radar.radarMinVelocityGate, radar.radarMinRangeGate,
                            radar.radarGlintCurve, radar.radarGlintMult,
                            radar.vessel, lockedVessel, lockedVessel.CoM, distance,
                            out terrainR, out terrainAngle, out notchMultiplier, out notchVMod, out notchRMod, out glintMod))
                {
                    //if (BDArmorySettings.DEBUG_RADAR)
                    //    Debug.Log($"[BDArmory.RadarUtils{{RadarUpdateLockTrack}}] Failed terrain check!");
                    return false;
                }

                // get vessel's radar signature
                TargetInfo ti = GetVesselRadarSignature(lockedVessel);
                // See comment in RadarUpdateScanBoresight for more about this
                if (ti.Vessel == null)
                {
                    //if (BDArmorySettings.DEBUG_RADAR)
                    //    Debug.Log($"[BDArmory.RadarUtils{{RadarUpdateLockTrack}}] TargetInfo.Vessel == null!");
                    return false;
                }
                float signature = (BDArmorySettings.ASPECTED_RCS) ? GetVesselRadarSignatureAtAspect(ti, ray.origin, distance) : ti.radarModifiedSignature;
                signature *= GetRadarGroundClutterModifier(radar.radarGroundClutterFactor, ray.origin, directionToTarget, ti);
                signature *= ti.radarLockbreakFactor;    //multiply lockbreak factor from active ecm
                if (radar.WeaponManager is not null) signature *= GetStandoffJammingModifier(radar.vessel, radar.WeaponManager.Team, ray.origin, lockedVessel, signature);
                if (radar.sonarMode == ModuleRadar.SonarModes.Active && radar.vessel.Splashed && lockedVessel.Splashed) signature *= GetVesselBubbleFactor(radar.transform.position, lockedVessel);
                //do not multiply chaff factor here

                float baseSignature = signature; // Kept for notching

                if (radar.radarCanNotch)
                    signature *= notchMultiplier;

                distance *= 0.001f; // Convert from m to km due to radar FloatCurves...

                if (distance > radar.radarMinDistanceLockTrack && distance < radar.radarMaxDistanceLockTrack)
                {
                    //evaluate if we can detect such a signature at that range
                    float minTrackSig = radar.radarLockTrackCurve.Evaluate(distance);

                    if ((signature >= minTrackSig) && (RadarCanDetect(radar, signature, distance)))
                    {
                        // can be tracked
                        radar.ReceiveContactData(new TargetSignatureData(lockedVessel, signature, _notchVMod: notchVMod, _notchRMod: notchRMod, _range: 1000f * distance, _glintMod: glintMod), locked);
                    }
                    else
                    {
                        // cannot track, so unlock it, unless above SCR, note we only check SCR if we're checking for notching
                        // and the notchMultiplier < 1f, the rest of the conditions are a failsafe
                        if (BDArmorySettings.RADAR_NOTCHING && radar.radarCanNotch && (notchMultiplier < 1f) && radar.sonarMode == ModuleRadar.SonarModes.None && !(BDArmorySettings.RADAR_ALLOW_SURFACE_WARFARE && (lockedVessel.Landed || lockedVessel.Splashed) && (radar.vessel.Landed || radar.vessel.Splashed)) && radar.radarMinRangeGate != float.MaxValue && radar.radarMinVelocityGate != float.MaxValue)
                        {
                            if (baseSignature < minTrackSig || !((signature >= minTrackSig) || RadarCanDetect(radar, baseSignature, distance)) || (GetRadarNotchingSCR(baseSignature, fov, distance, terrainR, terrainAngle) < radar.radarMinTrackSCR))
                            {
                                //if (BDArmorySettings.DEBUG_RADAR)
                                //    Debug.Log($"[BDArmory.RadarUtils{{RadarUpdateLockTrack}}] Failed to track! signature/baseSignature/minTrackSig: {signature}/{baseSignature}/{minTrackSig}, canDetect: {RadarCanDetect(radar, baseSignature, distance)}, SCR / minTrackSCR: {GetRadarNotchingSCR(baseSignature, fov, distance, terrainR, terrainAngle)}/{radar.radarMinTrackSCR}");
                                return false;
                            }

                            radar.ReceiveContactData(new TargetSignatureData(lockedVessel, signature, _notchVMod: notchVMod, _notchRMod: notchRMod, _range: 1000f * distance, _glintMod: glintMod), locked);
                        }
                        else
                        {
                            //if (BDArmorySettings.DEBUG_RADAR)
                            //    Debug.Log($"[BDArmory.RadarUtils{{RadarUpdateLockTrack}}] Failed to track! signature/minTrackSig: {signature}/{baseSignature}/{minTrackSig}, canDetect: {RadarCanDetect(radar, baseSignature, distance)}");
                            return false;
                        }
                    }
                }

                //  our radar ping can be received at a higher range than we can detect, according to RWR range ping factor:
                if (distance < radar.radarMaxDistanceLockTrack * RWR_PING_RANGE_FACTOR)
                    RadarWarningReceiver.PingRWR(lockedVessel, ray.origin, radar.rwrType, ACTIVE_MISSILE_PING_PERSIST_TIME, radar.vessel);

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

            // fov is cone width, so we use half of it
            fov *= 0.5f;

            using (var loadedvessels = BDATargetManager.LoadedVessels.GetEnumerator())
                while (loadedvessels.MoveNext())
                {
                    // ignore null, unloaded and self
                    if (loadedvessels.Current == null || !loadedvessels.Current.loaded) continue;
                    if (loadedvessels.Current == myWpnManager.vessel) continue;
                    if (loadedvessels.Current.vesselType == VesselType.Debris) continue;

                    // ignore too close ones
                    Vector3 vectorToTarget = loadedvessels.Current.CoM - position;
                    float distance = vectorToTarget.sqrMagnitude;
                    if (distance < RADAR_IGNORE_DISTANCE_SQR)
                        continue;

                    Vector3 vesselDirection = vectorToTarget.ProjectOnPlanePreNormalized(upVector);
                    float angle = VectorUtils.Angle(vesselDirection, lookDirection);
                    if (angle < fov)
                    {
                        // ignore when blocked by terrain
                        /*
                        if (TerrainCheck(referenceTransform.position, loadedvessels.Current.CoM))
                            continue;
                        */
                        if (loadedvessels.Current.Splashed)
                        {
                            if (loadedvessels.Current.IsUnderwater()) continue; // No underwater detection!
                            if (TerrainCheck(position, loadedvessels.Current.CoM + loadedvessels.Current.upAxis * (loadedvessels.Current.altitude < 0f ? -loadedvessels.Current.altitude + 2f : 0f), FlightGlobals.currentMainBody))
                                continue;
                        }
                        else
                        {
                            if (TerrainCheck(position, loadedvessels.Current.CoM, FlightGlobals.currentMainBody))
                                continue;
                        }

                        // get vessel's heat signature
                        TargetInfo tInfo = loadedvessels.Current.gameObject.GetComponent<TargetInfo>();
                        if (tInfo == null)
                        {
                            tInfo = loadedvessels.Current.gameObject.AddComponent<TargetInfo>();
                        }

                        IRSig = BDATargetManager.GetVesselHeatSignature(loadedvessels.Current, position, 1f, irst.TempSensitivityCurve);
                        float signature = IRSig.Item1 * (irst.boresightScan ? Mathf.Clamp01(15 / angle) : 1);
                        //signature *= (1400 * 1400) / Mathf.Clamp((loadedvessels.Current.CoM - referenceTransform.position).sqrMagnitude, 90000, 36000000); //300 to 6000m - clamping sig past 6km; Commenting out as it makes tuning detection curves much easier

                        // evaluate range
                        distance = BDAMath.Sqrt(distance);

                        signature *= Mathf.Clamp(VectorUtils.Angle(vectorToTarget, -irst.vessel.upAxis) / 90, 0.5f, 1.5f);
                        //ground will mask thermal sig
                        signature *= (GetRadarGroundClutterModifier(irst.GroundClutterFactor, position, vectorToTarget / distance, tInfo) * (tInfo.isSplashed ? 12 : 1));
                        //cold ocean on the other hand...

                        distance *= 0.001f;                                      //TODO: Performance! better if we could switch to sqrMagnitude...

                        BDATargetManager.ClearRadarReport(loadedvessels.Current, myWpnManager);

                        //evaluate if we can detect such a signature at that range
                        float attenuationFactor = ((float)FlightGlobals.getAtmDensity(FlightGlobals.getStaticPressure(position), FlightGlobals.getExternalTemperature(position))) +
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
            if (radar.vessel.altitude < -10 && radar.sonarMode == ModuleRadar.SonarModes.None) return detected; // Normal Radar Should not detect stuff underwater
            if (!radar.vessel.Splashed && radar.sonarMode != ModuleRadar.SonarModes.None) return detected; // Sonar should only work when in the water

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
        public static ViewScanResults GuardScanInDirection(MissileFire myWpnManager, Transform referenceTransform, float fov, float maxViewDistance, RadarWarningReceiver RWR = null)
        {
            fov *= 1.1f;
            var results = new ViewScanResults
            {
                foundMissile = false,
                foundHeatMissile = false,
                foundRadarMissile = false,
                foundAntiRadiationMissile = false,
                foundGPSMissile = false,
                foundAGM = false,
                foundPassiveMissile = false,
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

            // fov is cone width, so use the half angle
            fov *= 0.5f;

            Vector3 position = referenceTransform.position;
            Vector3 forwardVector = referenceTransform.forward;
            Vector3 upVector = referenceTransform.up;
            Vector3 lookDirection = -forwardVector;

            var AI = myWpnManager.vessel.ActiveController().AI;
            var ignoreMyTargetTargetingMe = AI != null && AI.pilotEnabled && AI.aiType switch
            {
                AIType.PilotAI => (AI as BDModulePilotAI).evasionIgnoreMyTargetTargetingMe,
                AIType.OrbitalAI => (AI as BDModuleOrbitalAI).evasionIgnoreMyTargetTargetingMe,
                _ => false
            };
            float maxMWSDistance;
            float MWSSqrDist = float.PositiveInfinity;
            bool MWSAddTargets;
            if (RWR && RWR.enabled && RWR.omniDetection)
            {
                maxMWSDistance = RWR.RWRMWSRange;
                RWR.UpdateReferenceTransform();
                if (Time.time >= RWR.TimeOfNextMWSUpdate)
                {
                    MWSAddTargets = true;
                    RWR.ResetMWSSlots();
                    RWR.TimeOfNextMWSUpdate = Time.time + RWR.RWRMWSUpdateRate;
                }
                else
                    MWSAddTargets = false;
            }
            else
            {
                maxMWSDistance = 0;
                MWSAddTargets = false;
            }

            //float maxScanDistance = Mathf.Max(maxMWSDistance, maxViewDistance);
            myWpnManager.staleTarget.Clear();            
            myWpnManager.detectedTargetTimeout.Clear(); //clean out dicts of any vessels that are no longer loaded/alive; loaded/living vessels get readded below
            using (var loadedvessels = BDATargetManager.LoadedVessels.GetEnumerator())
                while (loadedvessels.MoveNext())
                {
                    if (loadedvessels.Current == null || !loadedvessels.Current.loaded || VesselModuleRegistry.IgnoredVesselTypes.Contains(loadedvessels.Current.vesselType)) continue;
                    if (loadedvessels.Current == myWpnManager.vessel) continue; //ignore self
                    //var tgtMF = loadedvessels.Current.ActiveController().WM;
                    //if (tgtMF && tgtMF.vesselRadarData) maxRWRDistance = tgtMF.vesselRadarData.MaxRadarRange() * 2;
                    //maxScanDistance = Mathf.Max(maxViewDistance, maxRWRDistance);

                    TargetInfo tInfo = loadedvessels.Current.gameObject.GetComponent<TargetInfo>();
                    if (!tInfo)
                    {
                        BDATargetManager.ReportVessel(loadedvessels.Current, myWpnManager, false, true); //initial adding of TargetInfo to this vessel
                        // If tInfo still doesn't exist..
                        if (!(tInfo = loadedvessels.Current.gameObject.GetComponent<TargetInfo>()))
                            continue;
                    }

                    Vector3 vesselDirection = loadedvessels.Current.CoM - position;
                    Vector3 vesselProjectedDirection = (vesselDirection).ProjectOnPlanePreNormalized(upVector);
                    float vesselDistanceSqr = (vesselDirection).sqrMagnitude;

                    if (tInfo.isMissile)
                    {
                        // This is performed in CanSeeTarget for regular vessels
                        if (TerrainCheck(position, loadedvessels.Current.CoM, loadedvessels.Current.mainBody))
                        {
                            continue; //blocked by terrain
                        }

                        MissileBase missileBase = tInfo.MissileBaseModule;
                        if (missileBase == null)
                        {
                            Debug.LogWarning($"[BDArmory.RadarUtils]: Supposed missile ({loadedvessels.Current.vesselName}) has no MissileBase!");
                            tInfo.isMissile = false; // The target vessel has lost it's missile base component and should no longer count as a missile. This can happen for modular missiles that are getting destroyed.
                            continue;
                        }

                        if (missileBase.SourceVessel == myWpnManager.vessel) continue; // ignore missiles we've fired
                        float sightDistance = 0;

                        // Have we detected the missile with MWS?
                        bool MWSDetected = false;

                        if (maxMWSDistance > 0)
                        {
                            if ((MWSDetected = RWR.PerformMWSCheck(missileBase, out float currMWSSqrDist, MWSAddTargets)) && currMWSSqrDist < MWSSqrDist)
                            {
                                MWSSqrDist = currMWSSqrDist;
                            }
                        }

                        if (!MWSDetected)
                        {
                            bool detected = false;

                            //thrusting missiles at full range, cruising missiles at 3/4ths range, coasting missiles at 1/3rd range?
                            //or have be hard cutoffs, e.g. 5km/4km/2.5km, etc?
                            sightDistance = BDArmorySettings.VARIABLE_MISSILE_VISIBILITY ? maxViewDistance * (missileBase.MissileState == MissileBase.MissileStates.Boost ? 1 : (missileBase.MissileState == MissileBase.MissileStates.Cruise ? 0.75f : 0.33f)) : maxViewDistance;

                            // Check if there's a point to checking visual detection
                            if (vesselDistanceSqr < sightDistance * sightDistance)
                            {
                                float angle = VectorUtils.Angle(vesselProjectedDirection, lookDirection);  // && VectorUtils.Angle(vesselProjectedDirection, lookDirection) < fov / 2f) // && VectorUtils.Angle(loadedvessels.Current.transform.position - position, -myWpnManager.transform.forward) < myWpnManager.guardAngle / 2f) //WM facing direction? that s going to cause issues for any that aren't mounted pointing forward if guardAngle < 360; check combatSeat forward vector
                                if (angle < fov)
                                    detected = vesselDistanceSqr < sightDistance * sightDistance;
                            }

                            // If not visually detected
                            if (!detected)
                            {
                                //missile outside of modified visibility range, check RWR
                                if (RWR == null || !RWR.enabled)
                                {
                                    continue;
                                }

                                // No MWS/visual detection, check RWR
                                if (!(missileBase.TargetingMode == MissileBase.TargetingModes.Radar || missileBase.TargetingModeTerminal == MissileBase.TargetingModes.Radar) || // Must be radar missile
                                    ((missileBase.ActiveRadar || missileBase.radarLOALSearching) && // if active radar
                                    ((missileBase.activeRadarRange * missileBase.activeRadarRange * 4f < vesselDistanceSqr) || !RWR.IsRadarMissileDetected(loadedvessels.Current))) || // Active radar must be within range and detected
                                    (!missileBase.ActiveRadar && !missileBase.vrd)) // Or if SARH, must have an active SARH track, note strictly speaking there's more nuance to be had here with regards to detection, but we want AI to know that they've been launched at
                                    continue;
                                //if (VectorUtils.Angle(missileBase.GetForwardTransform(), -vesselDirection) > missileBase.maxOffBoresight) continue; // TODO: Profile this at some point to see if it's faster than checking RWR...
                            }
                        }

                        if (MissileIsThreat(missileBase, myWpnManager))
                        {
                            results.incomingMissiles.Add(new IncomingMissile
                            {
                                guidanceType = missileBase.TargetingMode,
                                distance = Vector3.Distance(missileBase.vessel.CoM, myWpnManager.vessel.CoM),
                                time = AIUtils.TimeToCPA(missileBase.vessel, myWpnManager.vessel, myWpnManager.evadeThreshold * 1.2f),
                                position = missileBase.vessel.CoM,
                                vessel = missileBase.vessel,
                                weaponManager = missileBase.SourceVessel == null ? null : missileBase.SourceVessel.ActiveController().WM,
                            });
                            switch (missileBase.TargetingMode)
                            {
                                // We hardcode identification within 0.33 * maxViewDistance, if not
                                // detected by MWS or indentified, passive missiles are unidentified

                                // This does leave radar missiles in a weird spot where a RWR-less
                                // vessel can tell it's a radar missile from > ID range...
                                case MissileBase.TargetingModes.Heat:
                                    if (MWSDetected || vesselDistanceSqr < 0.1089f * maxViewDistance * maxViewDistance)
                                        results.foundHeatMissile = true;
                                    else
                                        results.foundPassiveMissile = true;
                                    break;
                                case MissileBase.TargetingModes.Radar:
                                    results.foundRadarMissile = true;
                                    break;
                                case MissileBase.TargetingModes.Laser:
                                    results.foundAGM = true;
                                    break;
                                case MissileBase.TargetingModes.AntiRad: //How does one differentiate between a passive IR sensor and a passive AR sensor?
                                    if (MWSDetected || vesselDistanceSqr < 0.1089f * maxViewDistance * maxViewDistance)
                                        results.foundAntiRadiationMissile = true; //admittedly, combining the two would result in launching flares at ARMs and turning off radar when having incoming heaters...
                                    else
                                        results.foundPassiveMissile = true;
                                    break;
                                case MissileBase.TargetingModes.Gps:
                                case MissileBase.TargetingModes.Inertial:
                                    if (MWSDetected || vesselDistanceSqr < 0.1089f * maxViewDistance * maxViewDistance)
                                        results.foundGPSMissile = true;
                                    else
                                        results.foundPassiveMissile = true;
                                    break;
                            }
                            if (missileBase.GetWeaponClass() == WeaponClasses.SLW) results.foundTorpedo = true;
                        }
                        BDATargetManager.ReportVessel(loadedvessels.Current, myWpnManager); //report all missiles in RWR range so default RWR Missile Approach Warning behavior can correctly detect missile
                    }
                    else if (myWpnManager.guardMode) // Only check being under fire when in guard mode (for non-guardmode CMs) and when within view range/FOV.
                    {
                        bool canSeeTarget = (vesselDistanceSqr < maxViewDistance * maxViewDistance &&
                            VectorUtils.Angle(vesselProjectedDirection, lookDirection) < fov &&
                            myWpnManager.CanSeeTarget(tInfo, false, false) == MissileFire.TargetVisibility.Visible); //should target being (deep?) underwater affect visrange?

                        MissileFire tgtMF = loadedvessels.Current.ActiveController().WM;

                        if (!canSeeTarget && RWR && RWR.rwrEnabled)
                        {
                            float maxRadarRWRRange = 0f;
                            if (tgtMF && tgtMF.vesselRadarData) maxRadarRWRRange = tgtMF.vesselRadarData.MaxRadarRange() * 2;

                            // Is there any possibility that we're within RWR range...
                            if (vesselDistanceSqr < maxRadarRWRRange * maxRadarRWRRange)
                            {
                                canSeeTarget = RWR.IsVesselDetected(loadedvessels.Current);
                            }
                        }

                        if (canSeeTarget)
                        {
                            BDATargetManager.ReportVessel(loadedvessels.Current, myWpnManager); //we have visual on the target, report it.
                            using var weapon = VesselModuleRegistry.GetModules<ModuleWeapon>(loadedvessels.Current).GetEnumerator();
                            while (weapon.MoveNext())
                            {
                                var threatWeaponManager = weapon.Current.WeaponManager;
                                if (weapon.Current == null || threatWeaponManager == null) continue;
                                if (ignoreMyTargetTargetingMe && myWpnManager.currentTarget != null && threatWeaponManager.vessel == myWpnManager.currentTarget.Vessel) continue;
                                // If we're being targeted, calculate a miss distance
                                if (threatWeaponManager.currentTarget != null && threatWeaponManager.currentTarget.Vessel == myWpnManager.vessel)
                                {
                                    var missDistance = MissDistance(weapon.Current, myWpnManager.vessel);
                                    if (missDistance < results.missDistance)
                                    {
                                        results.firingAtMe = true;
                                        results.threatPosition = weapon.Current.fireTransforms[0].position; // Position of weapon that's attacking.
                                        results.threatVessel = weapon.Current.vessel;
                                        results.threatWeaponManager = threatWeaponManager;
                                        results.missDistance = missDistance;
                                        results.missDeviation = (weapon.Current.fireTransforms[0].position - myWpnManager.vessel.CoM).magnitude * weapon.Current.maxDeviation / 2f * Mathf.Deg2Rad; // y = x*tan(θ), expansion of tan(θ) is θ + O(θ^3).
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

                if (MWSAddTargets && !float.IsPositiveInfinity(MWSSqrDist))
                {
                    RWR.PlayWarningSound(RadarWarningReceiver.RWRThreatTypes.MWS, MWSSqrDist);
                }
            }

            return results;
        }

        public static bool MissileIsThreat(MissileBase missile, MissileFire mf, bool threatToMeOnly = true, bool ignoreInterceptors = false)
        {
            if (missile == null || missile.part == null) return false;
            if (ignoreInterceptors && missile.targetVessel && missile.targetVessel.isMissile) return false; // Ignore interceptor missiles
            Vector3 vectorFromMissile = mf.vessel.CoM - missile.vessel.CoM;
            //if ((vectorFromMissile.sqrMagnitude > (mf.rwr && mf.rwr.omniDetection ? mf.rwr.rwrDisplayRange * mf.rwr.rwrDisplayRange : mf.guardRange * mf.guardRange)) && (missile.TargetingMode != MissileBase.TargetingModes.Radar)) return false;
            bool maneuverCapability = missile.vessel.InVacuum() ? true : missile.vessel.srfSpeed > missile.GetKinematicSpeed();  // Missiles with no ability to hit target are not a threat
            if (threatToMeOnly)
            {
                Vector3 relV = missile.vessel.Velocity() - mf.vessel.Velocity();
                bool approaching = Vector3.Dot(relV, vectorFromMissile) > 0;
                bool teammate = false; // Missile isn't coming from teammate
                MissileFire missileController;
                if (missile.SourceVessel != null && (missileController = missile.SourceVessel.ActiveController().WM) != null)
                    teammate = (missileController.team == mf.team) && (missile.targetVessel != null ? (missile.targetVessel.Vessel != mf.vessel) : true); // Missile is fired from teammate and not locked onto us
                var missileBlastRadiusSqr = teammate ? mf.vessel.GetRadius() : 3f * Mathf.Max(missile.GetBlastRadius(), mf.vessel.GetRadius()); // Blast radius or self radius, whichever is larger (use self radius if missile is from teammate)
                missileBlastRadiusSqr *= missileBlastRadiusSqr;

                return (missile.HasFired && missile.MissileState > MissileBase.MissileStates.Drop && approaching && maneuverCapability &&
                            (
                                (missile.TargetPosition - (mf.vessel.CoM + (mf.vessel.Velocity() * Time.fixedDeltaTime))).sqrMagnitude < missileBlastRadiusSqr || // Target position is within blast radius of missile.
                                mf.vessel.PredictClosestApproachSqrSeparation(missile.vessel, Mathf.Max(mf.cmThreshold, mf.evadeThreshold)) < missileBlastRadiusSqr || // Closest approach is within blast radius of missile. 
                                ((missile.TargetingMode == MissileBase.TargetingModes.Radar && !teammate) && (VectorUtils.Angle(missile.TargetPosition - missile.vessel.CoM, vectorFromMissile) <= (missile.radarLOALSearching ? missile.maxOffBoresight : missile.lockedSensorFOV * 0.5f))) // We are within radar FOV of missile boresight.
                            ));
            }
            else
            {
                Vector3 forwardTransform = (missile.TargetingMode == MissileBase.TargetingModes.Radar) ? missile.GetForwardTransform() : Vector3.zero;
                using (var friendly = FlightGlobals.Vessels.GetEnumerator())
                    while (friendly.MoveNext())
                    {
                        if (friendly.Current == null)
                            continue;
                        if (VesselModuleRegistry.IgnoredVesselTypes.Contains(friendly.Current.vesselType)) continue;
                        var wm = friendly.Current.ActiveController().WM;
                        if (wm == null || wm.Team != mf.Team)
                            continue;

                        Vector3 relV = missile.vessel.Velocity() - wm.vessel.Velocity();
                        vectorFromMissile = wm.vessel.CoM - missile.vessel.CoM;
                        bool approaching = Vector3.Dot(relV, vectorFromMissile) > 0;
                        var missileBlastRadiusSqr = 3f * missile.GetBlastRadius();
                        missileBlastRadiusSqr *= missileBlastRadiusSqr;

                        return (missile.HasFired && missile.TimeIndex > 1f && approaching && maneuverCapability &&
                                    (
                                        (missile.TargetPosition - (wm.vessel.CoM + (wm.vessel.Velocity() * Time.fixedDeltaTime))).sqrMagnitude < missileBlastRadiusSqr || // Target position is within blast radius of missile.
                                        wm.vessel.PredictClosestApproachSqrSeparation(missile.vessel, Mathf.Max(wm.evadeThreshold, wm.cmThreshold)) < missileBlastRadiusSqr || // Closest approach is within blast radius of missile. 
                                        ((missile.TargetingMode == MissileBase.TargetingModes.Radar) && (VectorUtils.Angle(missile.TargetPosition - missile.vessel.CoM, vectorFromMissile) <= (missile.radarLOALSearching ? missile.maxOffBoresight : missile.lockedSensorFOV * 0.5f))) // We are within radar FOV of missile boresight.
                                    ));
                    }
            }
            return false;
        }

        public static float MissDistance(ModuleWeapon threatWeapon, Vessel self) // Returns how far away bullets from enemy are from craft in meters
        {
            Transform fireTransform = threatWeapon.fireTransforms[0];
            // If we're out of range, then it's not a threat.
            if (threatWeapon.maxEffectiveDistance * threatWeapon.maxEffectiveDistance < (fireTransform.position - self.CoM).sqrMagnitude) return float.MaxValue;
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
            //if (!BDArmorySettings.IGNORE_TERRAIN_CHECK) //Thisversion of TerrainCheck is only used by weapon LOS check, and should never be disabled.
            //{
            return Physics.Linecast(start, end, (int)LayerMasks.Scenery);
            //}
            //return false;
        }

        /// <summary>
        /// Helper method: check if line intersects terrain OR water
        /// </summary>
        public static bool TerrainCheck(Vector3 start, Vector3 end, CelestialBody body, bool forceIgnoreWater = false)
        {
            if (!BDArmorySettings.CHECK_WATER_TERRAIN || forceIgnoreWater)
                return Physics.Linecast(start, end, (int)LayerMasks.Scenery);

            if (!Physics.Linecast(start, end, (int)LayerMasks.Scenery))
            {
                float dummyR, dummyA;
                bool result = checkWater(start, end, body, -1f, out dummyR, out dummyA);
                return result;
            }

            return true;
        }

        /// <summary>
        /// Helper method: check if line intersects terrain and gives range and angle of intersection. Note this check goes to up to sqrRange, though the boolean behavior is still restricted to between start and end
        /// </summary>
        public static bool TerrainCheck(Vector3 start, Vector3 end, CelestialBody body, float range, out float R, out float angle, bool forceWaterCheck = false)
        {
            angle = 0f;
            if (!BDArmorySettings.IGNORE_TERRAIN_CHECK)
            {
                Vector3 offset = end - start;
                //if (BDArmorySettings.DEBUG_RADAR) Debug.Log($"[BDArmory.RadarUtils.TerrainCheck]: Current calcSqrDist: {sqrDist * 0.000001f} km^2.");

                RaycastHit hitInfo;

                if (Physics.Raycast(start, end - start, out hitInfo, range, (int)LayerMasks.Scenery, QueryTriggerInteraction.UseGlobal) && (((hitInfo.point - body.position).sqrMagnitude > body.Radius * body.Radius) || !body.ocean))
                {
                    // If we hit terrain and we're above sea level
                    R = hitInfo.distance;
                    angle = 90f - VectorUtils.Angle(hitInfo.normal, start - end);

                    //if (BDArmorySettings.DEBUG_RADAR) Debug.Log($"[BDArmory.RadarUtils.TerrainCheck]: Hit terrain at sqrDist {R * R * 0.000001f} km^2. Terrain blocking?: {(R * R) < offset.sqrMagnitude}");

                    return (R * R) < offset.sqrMagnitude;
                }
                else
                {
                    if (!(BDArmorySettings.CHECK_WATER_TERRAIN || forceWaterCheck))
                    {
                        R = float.MaxValue;
                        return false;
                    }

                    if (checkWater(start, end, body, range, out R, out angle, true))
                    {
                        R = BDAMath.Sqrt(R);

                        //if (BDArmorySettings.DEBUG_RADAR) Debug.Log($"[BDArmory.RadarUtils.TerrainCheck]: Hit water at sqrDist {R * R * 0.000001f} km^2. Water blocking?: {(R * R) < offset.sqrMagnitude}");

                        // Used for notching
                        // If we're checking for water AND the water is closer than the target
                        return (BDArmorySettings.CHECK_WATER_TERRAIN && (R * R) < offset.sqrMagnitude);
                    }
                    return false;
                }
            }
            R = float.MaxValue;
            return false;
        }

        public static bool checkWater(Vector3 start, Vector3 end, CelestialBody body, float range, out float sqrRange, out float angle, bool calcAngle = false)
        {
            angle = 0f;
            if (body.ocean || !body.hasSolidSurface)
            {
                double R = body.Radius;
                double x, y, z;
                double xB, yB, zB;
                double a, b, c, det;

                x = end.x - start.x;
                y = end.y - start.y;
                z = end.z - start.z;
                xB = body.position.x;
                yB = body.position.y;
                zB = body.position.z;

                a = x * x + y * y + z * z;
                b = 2.0 * (x * (start.x - xB) + y * (start.y - yB) + z * (start.z - zB));
                c = xB * xB + yB * yB + zB * zB + start.x * start.x + start.y * start.y + start.z * start.z - 2.0 * (xB * start.x + yB * start.y + zB * start.z) - R * R;
                det = b * b - 4.0 * a * c;
                if (a < 0.001 || det < 0 || b > 0)
                {
                    sqrRange = float.MaxValue;
                    return false;
                }

                double u;

                if (det < 0.0001f)
                {
                    // Quadratic Eq assuming det = 0: u =  - b / (2 * a)
                    u = (-0.5 * b / a);
                }
                else
                {
                    // Quadratic Eq: u = (-b - sqrt(det)) / (2 * a)
                    // Note we only check the closer intersection!
                    u = 0.5 * (-b - Math.Sqrt(det)) / a;
                }

                sqrRange = (float)(a * u * u);

                // If the point of intersection is further than the range we're checking then just ignore this
                if (range < 0)
                {
                    if (u > 1.0)
                        return false;
                }
                else if (sqrRange > range * range)
                    return false;

                if (calcAngle)
                {
                    Vector3d intcptVec;
                    intcptVec.x = (u * x + start.x - xB);
                    intcptVec.y = (u * y + start.y - yB);
                    intcptVec.z = (u * z + start.z - zB);

                    angle = VectorUtils.Angle(new Vector3d(-x, -y, -z), intcptVec);
                    angle = 90f - angle;
                }

                return true;
            }

            sqrRange = float.MaxValue;
            return false;
        }

        // Previously log scale depended on window size, this has now been made
        // independent of window size. Based on the default window size of
        // 256 pixels for RWRs (256 looks to provide better curve than 360)
        // 1f / Mathf.Log(128f + 1f)
        const float logRangeDenominator = 0.20576925955053367050163980444128f;
        // 1f / Mathf.Log(256f + 1f)
        const float logRangeRadialDenominator = 0.18021017998330121274552856323254f;

        /// <summary>
        /// Helper method: map a position onto the radar display
        /// </summary>
        public static Vector2 WorldToRadar(Vector3 worldPosition, Transform referenceTransform, Rect radarRect, float maxDistance)
        {
            Vector3 localPosition = referenceTransform.InverseTransformPoint(worldPosition);
            localPosition.y = 0;
            if (BDArmorySettings.LOGARITHMIC_RADAR_DISPLAY)
            {
                (float dist, Vector3 dir) = localPosition.MagNorm();
                float scale = Mathf.Log(dist * 128f / maxDistance + 1f) * logRangeDenominator;
                localPosition = dir * scale;
                return new Vector2((radarRect.width * 0.5f) * (1f + localPosition.x), (radarRect.height * 0.5f) * (1f - localPosition.z));
            }
            else
            {
                float scale = (radarRect.height * 0.5f) / maxDistance;
                return new Vector2((radarRect.width * 0.5f) + (localPosition.x * scale), ((radarRect.height * 0.5f) - (localPosition.z * scale)));
            }
        }

        /// <summary>
        /// Helper method: map a position onto the radar display (for non-omni radars)
        /// </summary>
        public static Vector2 WorldToRadarRadial(Vector3 worldPosition, Transform referenceTransform, Rect radarRect, float maxDistance, float maxAngle, bool noLog = false)
        {
            if (referenceTransform == null) return new Vector2();

            Vector3 localPosition = referenceTransform.InverseTransformPoint(worldPosition);
            localPosition.y = 0;
            float angle = VectorUtils.GetAngleOnPlane(localPosition, Vector3.forward, Vector3.right);
            //if (localPosition.x < 0) angle = -angle;
            float xPos = (radarRect.width * 0.5f) + ((angle / maxAngle) * radarRect.width * 0.5f);
            float yPos = radarRect.height;

            if (BDArmorySettings.LOGARITHMIC_RADAR_DISPLAY && !noLog)
            {
                float scale = Mathf.Log(localPosition.magnitude * 128f / maxDistance + 1f) * logRangeDenominator;
                yPos -= radarRect.height * scale * scale; // Log^2 scales better here for some reason.
            }
            else
            {
                float scale = radarRect.height / maxDistance;
                yPos -= localPosition.magnitude * scale;
            }
            Vector2 radarPos = new Vector2(xPos, yPos);
            return radarPos;
        }

        /// <summary>
        /// Returns string for use in RCS analysis window, if RCS is non-zero and 0.01 m^2 or lower, it will return result in dBsm instead of m^2
        /// </summary>
        public static string RCSString(float rcs)
        {
            if (rcs >= 0.01f || rcs == 0f)
                return rcs.ToString("0.00") + " m²";
            else
                return (10f * Mathf.Log10(rcs)).ToString("0.0") + " dBsm";
        }
    }
}
