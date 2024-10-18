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
        public float notchMod;
        public TargetInfo targetInfo;
        public BDTeam Team;
        public Vector2 pingPosition;
        public VesselECMJInfo vesselJammer;
        public ModuleRadar lockedByRadar;
        public Vessel vessel;
        public Part IRSource;
        bool orbital;
        Orbit orbit;

        public bool Equals(TargetSignatureData other)
        {
            return
                exists == other.exists &&
                geoPos == other.geoPos &&
                timeAcquired == other.timeAcquired;
        }

        public TargetSignatureData(Vessel v, float _signalStrength, Part heatpart = null, float _notchMod = 0f)
        {
            orbital = v.InOrbit();
            orbit = v.orbit;

            timeAcquired = Time.time;
            vessel = v;
            velocity = v.Velocity();
            IRSource = heatpart;
            geoPos = VectorUtils.WorldPositionToGeoCoords(IRSource != null ? IRSource.transform.position : v.CoM, v.mainBody);
            acceleration = v.acceleration_immediate;
            exists = true;
            notchMod = _notchMod;

            signalStrength = _signalStrength;

            targetInfo = v.gameObject.GetComponent<TargetInfo>();

            // vessel never been picked up on radar before: create new targetinfo record
            if (targetInfo == null)
            {
                targetInfo = v.gameObject.AddComponent<TargetInfo>();
            }

            Team = null;

            if (targetInfo)  // Always true, as we just set it?
            {
                Team = targetInfo.Team;
            }
            else
            {
                var mf = VesselModuleRegistry.GetMissileFire(v, true);
                if (mf != null) Team = mf.Team;
            }

            vesselJammer = v.gameObject.GetComponent<VesselECMJInfo>();

            pingPosition = Vector2.zero;
            lockedByRadar = null;
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
            pingPosition = Vector2.zero;
            orbital = false;
            orbit = null;
            lockedByRadar = null;
            vessel = null;
            IRSource = null;
            notchMod = 0f;
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
            pingPosition = Vector2.zero;
            orbital = false;
            orbit = null;
            lockedByRadar = null;
            vessel = null;
            IRSource = null;
            notchMod = 0f;
        }

        public TargetSignatureData(Vector3 _velocity, Vector3 _position, Vector3 _acceleration, bool _exists, float _signalStrength)
        {
            velocity = _velocity;
            geoPos = VectorUtils.WorldPositionToGeoCoords(_position, FlightGlobals.currentMainBody);
            acceleration = _acceleration;
            exists = _exists;
            timeAcquired = Time.time;
            signalStrength = _signalStrength;
            targetInfo = null;
            vesselJammer = null;
            Team = null;
            pingPosition = Vector2.zero;
            orbital = false;
            orbit = null;
            lockedByRadar = null;
            vessel = null;
            IRSource = null;
            notchMod = 0f;
        }

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

        public Vector3 predictedPositionWithChaffFactor(float chaffEffectivity = 1f)
        {
            // get chaff factor of vessel and calculate decoy distortion caused by chaff echos
            float decoyFactor = 0f;
            Vector3 posDistortion = Vector3.zero;

            if (vessel != null)
            {
                // chaff check
                decoyFactor = (1f - RadarUtils.GetVesselChaffFactor(vessel)) * (1f + notchMod);
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
                    posDistortion *= Mathf.Max(BDArmorySettings.CHAFF_FACTOR, 0f) * chaffEffectivity;
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
                return new TargetSignatureData(Vector3.zero, Vector3.zero, Vector3.zero, false, (float)RadarWarningReceiver.RWRThreatTypes.None);
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
