using System;
using UnityEngine;

using BDArmory.Competition;
using BDArmory.CounterMeasure;
using BDArmory.Extensions;
using BDArmory.Radar;
using BDArmory.Settings;
using BDArmory.Utils;

namespace BDArmory.Targeting
{
    public readonly struct TargetSignatureData : IEquatable<TargetSignatureData>
    {
        public readonly Vector3 velocity;
        //public readonly Vector3 geoPos;
        public readonly float altitude;
        public readonly Vector3d localPos;
        public readonly Vector3 acceleration;
        public readonly bool exists;
        public readonly float timeAcquired;
        public readonly float signalStrength;
        //public RadarWarningReceiver.RWRThreatTypes signalType;
        public readonly float notchVMod;
        public readonly float notchRMod;
        public readonly TargetInfo targetInfo;
        public readonly BDTeam Team;
        //public Vector2 pingPosition;
        public readonly VesselECMJInfo vesselJammer;
        public readonly ModuleRadar lockedByRadar;
        public readonly Vessel vessel;
        public readonly Part IRSource;
        public readonly bool isDecoy = false;
        public readonly float range;
        //SEE TODO in CheckJamming
        //public Vector3 jammedGeoPos;

        // Appears to be unused?
        //bool orbital;
        //Orbit orbit;

        public bool Equals(TargetSignatureData other)
        {
            return
                exists == other.exists &&
                localPos == other.localPos &&
                timeAcquired == other.timeAcquired;
        }

        public string Name()
        {
            return (vessel ? vessel.vesselName : (isDecoy ? "Decoy" : "Null"));
        }

        public Guid ID()
        {
            return (vessel ? vessel.id : Guid.Empty);
        }

        public TargetSignatureData(Vessel v, float _signalStrength, Part heatpart = null, float _notchVMod = 0f, float _notchRMod = 0f, float _range = -1f, float _glintMod = -1f, ModuleRadar _lockedByRadar = null)
        {
            //orbital = v.InOrbit();
            //orbit = v.orbit;

            timeAcquired = Time.time;
            vessel = v;
            velocity = v.Velocity();
            IRSource = heatpart;
            acceleration = v.acceleration_immediate;
            exists = true;
            notchVMod = _notchVMod;
            notchRMod = _notchRMod;
            range = _range;

            signalStrength = _signalStrength;

            targetInfo = v.gameObject.GetComponent<TargetInfo>();

            // vessel never been picked up on radar before: create new targetinfo record
            if (targetInfo == null)
            {
                targetInfo = v.gameObject.AddComponent<TargetInfo>();
            }

            localPos = VectorUtils.WorldPositionToLocalCoords(IRSource != null ? IRSource.transform.position : (_glintMod > 0 ? v.CoM + _glintMod * targetInfo.GetRadarGlint() : v.CoM), v.mainBody);
            //geoPos = VectorUtils.LocalPositionToGeoCoords(in localPos, v.mainBody);
            altitude = (float)v.altitude; // NOTE: this is NOT accurate relative to `localPos`, however, given the usage of TSD.altitude, this is fine, and it's faster
            // however if at any point TSD.altitude *has* to be an accurate measure of `localPos.magnitude - body.Radius`, this should be converted to that.

            Team = null;

            if (targetInfo)  // Always true, as we just set it?
            {
                Team = targetInfo.Team;
            }
            else
            {
                var mf = v.ActiveController().WM;
                if (mf != null) Team = mf.Team;
            }

            vesselJammer = v.gameObject.GetComponent<VesselECMJInfo>();

            //pingPosition = Vector2.zero;
            lockedByRadar = _lockedByRadar;
        }

        public TargetSignatureData(CMFlare flare, float _signalStrength)
        {
            velocity = flare.velocity;
            localPos = VectorUtils.WorldPositionToLocalCoords(flare.transform.position, FlightGlobals.currentMainBody);
            //geoPos = VectorUtils.LocalPositionToGeoCoords(in localPos, FlightGlobals.currentMainBody);
            altitude = (float)(localPos.magnitude - FlightGlobals.currentMainBody.Radius);
            exists = true;
            acceleration = Vector3.zero;
            timeAcquired = Time.time;
            signalStrength = _signalStrength;
            targetInfo = null;
            vesselJammer = null;
            Team = null;
            //pingPosition = Vector2.zero;
            //orbital = false;
            //orbit = null;
            lockedByRadar = null;
            vessel = null;
            IRSource = null;
            isDecoy = true;
        }

        public TargetSignatureData(CMDecoy decoy, float _signalStrength)
        {
            velocity = decoy.velocity;
            localPos = VectorUtils.WorldPositionToLocalCoords(decoy.transform.position, FlightGlobals.currentMainBody);
            //geoPos = VectorUtils.LocalPositionToGeoCoords(in localPos, FlightGlobals.currentMainBody);
            altitude = (float)(localPos.magnitude - FlightGlobals.currentMainBody.Radius);
            exists = true;
            acceleration = Vector3.zero;
            timeAcquired = Time.time;
            signalStrength = _signalStrength;
            targetInfo = null;
            vesselJammer = null;
            Team = null;
            //pingPosition = Vector2.zero;
            //orbital = false;
            //orbit = null;
            lockedByRadar = null;
            vessel = null;
            IRSource = null;
            isDecoy = true;
        }

        public TargetSignatureData(Vector3 _velocity, Vector3 _position, Vector3 _acceleration, bool _exists)
        {
            velocity = _velocity;
            localPos = VectorUtils.WorldPositionToLocalCoords(_position, FlightGlobals.currentMainBody);
            //geoPos = VectorUtils.LocalPositionToGeoCoords(in localPos, FlightGlobals.currentMainBody);
            altitude = (float)(localPos.magnitude - FlightGlobals.currentMainBody.Radius);
            acceleration = _acceleration;
            exists = _exists;
            timeAcquired = Time.time;
            signalStrength = -1f;
            //signalType = _signalType;
            targetInfo = null;
            vesselJammer = null;
            Team = null;
            //pingPosition = Vector2.zero;
            //orbital = false;
            //orbit = null;
            lockedByRadar = null;
            vessel = null;
            IRSource = null;
        }

        public TargetSignatureData()
        {
            velocity = Vector3.zero;
            localPos = Vector3d.zero;
            //geoPos = Vector3.zero;
            altitude = 0.0f;
            acceleration = Vector3.zero;
            exists = false;
            timeAcquired = 0.0f;
            signalStrength = -1f;
            targetInfo = null;
            vesselJammer = null;
            Team = null;
            lockedByRadar = null;
            vessel = null;
            IRSource = null;
        }

        /*public TargetSignatureData(Vector3 _position, Vector2 _pingPosition, bool _exists, RadarWarningReceiver.RWRThreatTypes _signalType, Vessel _vessel)
        {
            velocity = Vector3.zero;
            geoPos = VectorUtils.WorldPositionToGeoCoords(_position, FlightGlobals.currentMainBody);
            acceleration = Vector3.zero;
            exists = _exists;
            timeAcquired = Time.time;
            signalType = _signalType;
            targetInfo = null;
            vesselJammer = null;
            Team = null;
            pingPosition = _pingPosition;
            orbital = false;
            orbit = null;
            lockedByRadar = null;
            vessel = _vessel;
            IRSource = null;
            notchMod = 0f;
        }*/

        public Vector3 position
        {
            get
            {
                return VectorUtils.LocalPosToWorldSurfacePosition(localPos, FlightGlobals.currentMainBody);
            }
        }

        /// <summary>
        /// This is an expensive get (Asin + Atan and a .magnitude) so be conservative when using it...
        /// Generally should not be used, and localPos / body-relative local position should be used instead,
        /// as it is more performant when it comes to converting back and forth.
        /// </summary>
        public Vector3 geoPos
        {
            get
            {
                return VectorUtils.LocalPositionToGeoCoords(localPos, FlightGlobals.currentMainBody);
            }
        }

        public Vector3 predictedPosition
        {
            get
            {
                return position + (velocity * age);
            }
        }

        // TODO: Finish this stuff, we'll have to decide on how jamming affects positional
        // accuracy in the display. For now, I'm throwing this code in VesselRadarData
        // as it was before, but ideally we should be pre-calculating this, and only
        // when the radar performs a sweep (so this would ideally be called in
        // RadarUtils.RadarUpdateScanLock, in the modeTryLock = false branch).
        /*public void CheckJamming(ModuleRadar radar)
        {
            float jamDistance = RadarUtils.GetVesselECMJammingDistance(vessel);
            Vector3 radarPosition = radar.currPosition;
            Vector3 vectorToTarget = vessel.CoM - radar.currPosition;
            float sqrDist = vectorToTarget.sqrMagnitude;
            if (vesselJammer && jamDistance * jamDistance > sqrDist)
            {
                Vector3 dirToTarget = vectorToTarget / BDAMath.Sqrt(sqrDist);

                Vector3 jammedPosition = radarPosition + (dirToTarget * UnityEngine.Random.Range(100, rIncrements[rangeIndex]));
                float bearingVariation = Mathf.Clamp(1024e6f /    // 32000 * 32000
                        sqrDist, 0,  80);
                jammedPosition = radarPosition + (Quaternion.AngleAxis(Random.Range(-bearingVariation, bearingVariation), currUp) * (jammedPosition - transform.position));
            }
        }*/

        public Vector3 ChaffDistortion(float chaffEffectivity = 1.0f, in float chaffNotchVEffectivity = 1.0f, in float chaffNotchREffectivity = 1.0f)
        {
            if (vessel == null)
            {
                return Vector3.zero;
            }

            // get chaff factor of vessel and calculate decoy distortion caused by chaff echos
            float decoyFactor = 0f;
            Vector3 posDistortion;

            // chaff check
            decoyFactor = (1f - RadarUtils.GetVesselChaffFactor(vessel));

            if (decoyFactor <= 0f)
            {
                return Vector3.zero;
            }

            Vector3 velOrAccel = (!vessel.InVacuum()) ? vessel.Velocity() : vessel.acceleration_immediate;

            // With ecm on better chaff effectiveness due to jammer strength
            VesselECMJInfo vesseljammer = vessel.gameObject.GetComponent<VesselECMJInfo>();

            // Jamming biases position distortion further to rear, depending on ratio of jamming strength and radarModifiedSignature
            float jammingFactor = vesseljammer is null ? 0 : decoyFactor * Mathf.Clamp01(vesseljammer.jammerStrength / 100f / Mathf.Max(targetInfo.radarModifiedSignature, 0.1f));

            // Random radius of distortion, 16-256m
            float distortionFactor = decoyFactor * UnityEngine.Random.Range(16f, 256f);

            // Convert Float jammingFactor position bias and signatureFactor scaling to Vector3 position
            Vector3 signatureDistortion = distortionFactor * (UnityEngine.Random.insideUnitSphere - jammingFactor * velOrAccel.normalized);

            // Higher speed -> missile decoyed further "behind" where the chaff drops (also means that chaff is least effective for head-on engagements)
            posDistortion = signatureDistortion - Mathf.Clamp(decoyFactor * decoyFactor, 0f, 0.5f) * velOrAccel;

            // Apply effects from global settings and individual missile chaffEffectivity
            //modern radar can filter out chaff easily due to doppler comparisons (chaff stationary, plane not)
            //doppler comparison can be countered via jammer + chaff to illuminate the chaff with an adjusted wavelength to simulate necessary doppler shifting for a faster moving object

            //So - CE of 1: missile/radar has no doppler correction, fully fooled by chaff
            // - CE of 0: missile/radar has doppler correction, not fooled at all by chaff
            // - jammer on, doppler correction countered, CE:0 countered and radar gets some spoofing from chaff
            chaffEffectivity = jammingFactor > 0 ? Mathf.Clamp01(chaffEffectivity + jammingFactor) : chaffEffectivity;
            posDistortion *= Mathf.Max(BDArmorySettings.CHAFF_FACTOR, 0f) * (chaffEffectivity + (1f + jammingFactor) * (chaffNotchVEffectivity * notchVMod + chaffNotchREffectivity * notchRMod));

            return posDistortion;
        }

        public Vector3 predictedPositionWithChaffFactor(in float chaffEffectivity = 1.0f, in float chaffNotchVEffectivity = 1.0f, in float chaffNotchREffectivity = 1.0f)
        {
            return position + (velocity * age) + ChaffDistortion(chaffEffectivity, chaffNotchVEffectivity, chaffNotchREffectivity);
        }

        /*public float altitude
        {
            get
            {
                return geoPos.z;
            }
        }*/

        public float age
        {
            get
            {
                return (Time.time - timeAcquired);
            }
        }

        private static readonly TargetSignatureData noTargetData = new TargetSignatureData();

        public static TargetSignatureData noTarget => noTargetData;

        public static void ResetTSDArray(ref TargetSignatureData[] tsdArray)
        {
            for (int i = 0; i < tsdArray.Length; i++)
            {
                tsdArray[i] = noTarget;
            }
        }
    }
}
