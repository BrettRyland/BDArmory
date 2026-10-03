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
    public struct TargetSignatureData : IEquatable<TargetSignatureData>
    {
        public Vector3 velocity;
        public Vector3 geoPos;
        public Vector3 acceleration;
        public bool exists;
        public float timeAcquired;
        public float signalStrength;
        //public RadarWarningReceiver.RWRThreatTypes signalType;
        public float notchVMod;
        public float notchRMod;
        public TargetInfo targetInfo;
        public BDTeam Team;
        //public Vector2 pingPosition;
        public VesselECMJInfo vesselJammer;
        public ModuleRadar lockedByRadar;
        public Vessel vessel;
        public Part IRSource;
        public bool isDecoy = false;
        public float range;
        //SEE TODO in CheckJamming
        //public Vector3 jammedGeoPos;

        // Appears to be unused?
        //bool orbital;
        //Orbit orbit;

        public bool Equals(TargetSignatureData other)
        {
            return
                exists == other.exists &&
                geoPos == other.geoPos &&
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

            geoPos = VectorUtils.WorldPositionToGeoCoords(IRSource != null ? IRSource.transform.position : (_glintMod > 0 ? v.CoM + _glintMod * targetInfo.GetRadarGlint() : v.CoM), v.mainBody);

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
            geoPos = VectorUtils.WorldPositionToGeoCoords(flare.transform.position, FlightGlobals.currentMainBody);
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
            geoPos = VectorUtils.WorldPositionToGeoCoords(decoy.transform.position, FlightGlobals.currentMainBody);
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
            geoPos = VectorUtils.WorldPositionToGeoCoords(_position, FlightGlobals.currentMainBody);
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
                return VectorUtils.GetWorldSurfacePostion(geoPos, FlightGlobals.currentMainBody);
            }
            set
            {
                geoPos = VectorUtils.WorldPositionToGeoCoords(value, FlightGlobals.currentMainBody);
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

        public Vector3 predictedPositionWithChaffFactor(float chaffEffectivity = 1.0f, float chaffNotchVEffectivity = 1.0f, float chaffNotchREffectivity = 1.0f)
        {
            // get chaff factor of vessel and calculate decoy distortion caused by chaff echos
            float decoyFactor = 0f;
            Vector3 posDistortion = Vector3.zero;

            if (vessel != null)
            {
                // chaff check
                decoyFactor = (1f - RadarUtils.GetVesselChaffFactor(vessel));
                Vector3 velOrAccel = (!vessel.InVacuum()) ? vessel.Velocity() : vessel.acceleration_immediate;

                if (decoyFactor > 0f)
                {
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
                }
            }

            return position + (velocity * age) + posDistortion;
        }

        public float altitude
        {
            get
            {
                return geoPos.z;
            }
        }

        public float age
        {
            get
            {
                return (Time.time - timeAcquired);
            }
        }

        public static TargetSignatureData noTarget
        {
            get
            {
                return new TargetSignatureData(Vector3.zero, Vector3.zero, Vector3.zero, false);
            }
        }

        public static void ResetTSDArray(ref TargetSignatureData[] tsdArray)
        {
            for (int i = 0; i < tsdArray.Length; i++)
            {
                tsdArray[i] = noTarget;
            }
        }
    }
}
