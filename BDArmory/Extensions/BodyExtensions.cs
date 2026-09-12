using UnityEngine;
using System;
using BDArmory.Utils;
using System.Collections;
using System.Collections.Generic;

namespace BDArmory.Extensions
{
    public static class BodyExtensions
    {
        struct MagneticFieldParams
        {
            public float mu;
            public float BT;
            public bool affectedByParentBody;

            public MagneticFieldParams(float _strengthAtSurface, double _bodyRadius, float _strengthAtInfinity, bool _affectedByParentBody)
            {
                mu = (float)(_strengthAtSurface * (_bodyRadius * _bodyRadius * _bodyRadius));
                BT = _strengthAtInfinity;
                affectedByParentBody = _affectedByParentBody;
            }

            public const float earthStrengthAtSurface = 0.31f;
            public const float earthStrengthAtInfinity = 0.00015f;

            public MagneticFieldParams(double _bodyRadius)
            {
                mu = (float)(earthStrengthAtSurface * (_bodyRadius * _bodyRadius * _bodyRadius));
                BT = earthStrengthAtInfinity;
                affectedByParentBody = false;
            }
        }

        static Dictionary<CelestialBody, MagneticFieldParams> magneticFields;

        public static IEnumerator Load()
        {
            yield return new WaitUntil(() => (FlightGlobals.ready && FlightGlobals.Bodies != null));
            if (magneticFields != null) yield break; // Only load the armor defs once on startup.
            magneticFields = new Dictionary<CelestialBody, MagneticFieldParams>();
            UrlDir.UrlConfig[] nodes = GameDatabase.Instance.GetConfigs("MAGNETICPARAMS");
            ConfigNode node;

            List<CelestialBody> bodies = FlightGlobals.Bodies;

            for (int i = 0; i < nodes.Length; i++)
            {
                string name_ = "";
                try
                {
                    node = nodes[i].config;
                    name_ = (string)ParseField(node, "name", typeof(string));
                    CelestialBody currBody = null;
                    for (int j = 0; j < bodies.Count; j++)
                    {
                        if (bodies[j].name == name_)
                        {
                            currBody = bodies[j];
                            break;
                        }
                    }

                    if (currBody == null)
                    {
                        continue;
                    }

                    if (magneticFields.TryGetValue(currBody, out MagneticFieldParams temp)) // Avoid duplicates.
                    {
                        Debug.LogError($"[BDArmory.BodyExtensions]: Celestial body magnetic field definition {name_} from {nodes[i].parent.name} already exists, skipping.");
                        continue;
                    }
                    Debug.Log($"[BDArmory.BodyExtensions]: Parsing definition of {name_}'s magnetic field from {nodes[i].parent.name}");
                    magneticFields.Add(currBody,
                        new MagneticFieldParams(
                        (float)ParseField(node, "strengthAtSurface", typeof(float)),
                        currBody.Radius,
                        (float)ParseField(node, "strengthAtInfinity", typeof(float)),
                        (bool)ParseField(node, "affectedByParentBody", typeof(bool))
                        )
                    );
                }
                catch (Exception e)
                {
                    Debug.LogError($"[BDArmory.BodyExtensions]: Error loading body magnetic field config for body '{name_}' from '{nodes[i].parent.name}' | {e}");
                }
            }
        }

        private static object ParseField(ConfigNode node, string field, Type type)
        {
            try
            {
                if (!node.HasValue(field))
                    throw new ArgumentNullException(field, $"Field '{field}' is missing.");
                var value = node.GetValue(field);
                try
                {
                    if (type == typeof(string))
                    { return value; }
                    else if (type == typeof(bool))
                    { return bool.Parse(value); }
                    else if (type == typeof(int))
                    { return int.Parse(value); }
                    else if (type == typeof(float))
                    { return float.Parse(value); }
                    else
                    { throw new ArgumentException("Invalid type specified."); }
                }
                catch (Exception e)
                { throw new ArgumentException($"Field '{field}': '{value}' could not be parsed as '{type.ToString()}' | {e.ToString()}", field); }
            }
            catch (Exception e)
            {
                if (field == "name") throw; // Sanity check for field "name" to avoid potential stack overflow.

                string name = "unknown";
                try { name = (string)ParseField(node, "name", typeof(string)); } catch { }
                Debug.LogError($"[BDArmory.BodyExtensions]: Using default value for {field} of {name} | {e}");

                switch (name)
                {
                    case "strengthAtSurface":
                        {
                            return MagneticFieldParams.earthStrengthAtSurface;
                        }
                    case "strengthAtInfinity":
                        { 
                            return MagneticFieldParams.earthStrengthAtInfinity;
                        }
                    case "affectedByParentBody":
                        {
                            return false;
                        }
                    default:
                        {
                            throw;
                        }
                }
            }
        }

        public static Vector3 GetMagneticFieldStrength(this CelestialBody body, Vessel vessel)
        {
            if (!magneticFields.TryGetValue(body, out MagneticFieldParams magParams))
            {
                Debug.Log($"[BDArmory.CelestialBody.BodyExtensions] BD failed to find entry for {body.bodyName}'s magnetic field! Defaulting to Earth/Kerbin's");

                magParams = new MagneticFieldParams(body.Radius);
                magneticFields.Add(body, magParams);
            }

            Vector3d up;
            Vector3d north;
            Vector3d relVecLoc;
            double radAlt;

            if (body == vessel.mainBody)
            {
                up = vessel.up;
                radAlt = body.Radius + vessel.altitude;
            }
            else
            {
                (radAlt, up) = (vessel.CoMD - body.position).MagNorm();
            }

            north = (body.BodyFrame.Z.xzy).ProjectOnPlanePreNormalized(up).normalized;
            relVecLoc = body.BodyFrame.WorldToLocal(up.xzy);
            double cosCoLat = relVecLoc.z;
            double sinCoLat = Math.Sqrt(1.0 - cosCoLat * cosCoLat);

            double muDivR = radAlt;
            muDivR *= muDivR * muDivR;
            muDivR = -magParams.mu / muDivR;

            double radComp = 2.0 * cosCoLat * muDivR;
            double northComp = -sinCoLat * muDivR;

            Vector3d magStrength = new Vector3d(up.x * radComp + north.x * northComp,
                                                up.y * radComp + north.y * northComp,
                                                up.z * radComp + north.z * northComp);

            // We assume the tail isn't *super* significant for vessels on the mainBody
            // This is true as long as we're not in space...
            if (body == vessel.mainBody)
            {
                if (magParams.affectedByParentBody && body.referenceBody)
                { 
                    magStrength += body.referenceBody.GetMagneticFieldStrength(vessel);
                }
                return magStrength;
            }

            Vector3d sunDir = (body.referenceBody.position - body.position).normalized;

            Vector3d orbitNormal = body.orbit.GetOrbitNormal();
            // For some reason y and z are flipped in all orbit Vector3s per API...
            orbitNormal = orbitNormal.xzy;

            //double cosMagLon = Vector3d.Dot(sunDir, up.ProjectOnPlanePreNormalized(orbitNormal).normalized);

            magStrength -= sunDir * magParams.BT * Math.Sign(Vector3d.Dot(orbitNormal, body.BodyFrame.Z) * Vector3d.Dot(orbitNormal, up)); // Field strength at infinity, away from the sun, with +ve north of the planet and -ve south

            return magStrength;
        }

        public static Vector3 GetMagneticFieldStrength(this CelestialBody body, Vector3d position, bool isMainBody)
        {
            if (!magneticFields.TryGetValue(body, out MagneticFieldParams magParams))
            {
                Debug.Log($"[BDArmory.CelestialBody.BodyExtensions] BD failed to find entry for {body.bodyName}'s magnetic field! Defaulting to Earth/Kerbin's");

                magParams = new MagneticFieldParams(body.Radius);
                magneticFields.Add(body, magParams);
            }

            Vector3d up;
            Vector3d north;
            Vector3d relVecLoc;
            double radAlt;

            (radAlt, up) = (position - body.position).MagNorm();

            north = (body.BodyFrame.Z).ProjectOnPlanePreNormalized(up).normalized;
            relVecLoc = body.BodyFrame.WorldToLocal(up);
            double cosCoLat = relVecLoc.z;
            double sinCoLat = Math.Sqrt(1.0 - cosCoLat * cosCoLat);

            double muDivR = radAlt;
            muDivR *= muDivR * muDivR;
            muDivR = -magParams.mu / muDivR;

            double radComp = 2.0 * cosCoLat * muDivR;
            double northComp = sinCoLat * muDivR;

            Vector3d magStrength = new Vector3d(up.x * radComp + north.x * northComp,
                                            up.y * radComp + north.y * northComp,
                                            up.z * radComp + north.z * northComp);

            // We assume the tail isn't *super* significant for vessels on the mainBody
            // This is true as long as we're not in space...
            if (isMainBody)
            {
                if (magParams.affectedByParentBody && body.referenceBody)
                {
                    magStrength += body.referenceBody.GetMagneticFieldStrength(position, false);
                }
                return magStrength;
            }

            Vector3d sunDir = (body.referenceBody.position - body.position).normalized;

            Vector3d orbitNormal = body.orbit.GetOrbitNormal();
            // For some reason y and z are flipped in all orbit Vector3s per API...
            double temp = orbitNormal.z;
            orbitNormal.z = orbitNormal.y;
            orbitNormal.y = temp;

            //double cosMagLon = Vector3d.Dot(sunDir, up.ProjectOnPlanePreNormalized(orbitNormal).normalized);

            magStrength -= sunDir * magParams.BT * Math.Sign(Vector3d.Dot(orbitNormal, body.BodyFrame.Z) * Vector3d.Dot(orbitNormal, up)); // Field strength at infinity, away from the sun, with +ve north of the planet and -ve south

            return magStrength;
        }

        public static bool FindFlatSpotNear(this CelestialBody body, double startLatitude, double startLongitude, out double latitude, out double longitude, double maxDeviation)
        {
            var iLat = startLatitude;
            var iLng = startLongitude;
            var dLat = 0.0;
            var dLng = 0.0;
            var threshold = 0.000001;
            var maxIterations = 1000;
            var k = 0;

            while (k++ < maxIterations)
            {
                var norm = body.GetRelSurfaceNVector(iLat + dLat, iLng + dLng);
                var normA = body.GetRelSurfaceNVector(iLat + dLat - threshold, iLng + dLng);
                var normB = body.GetRelSurfaceNVector(iLat + dLat + threshold, iLng + dLng);
                var normC = body.GetRelSurfaceNVector(iLat + dLat, iLng + dLng - threshold);
                var normD = body.GetRelSurfaceNVector(iLat + dLat, iLng + dLng + threshold);

                var angle = VectorUtils.Angle(norm, Vector3.up);
                var angleA = VectorUtils.Angle(normA, Vector3.up);
                var angleB = VectorUtils.Angle(normB, Vector3.up);
                var angleC = VectorUtils.Angle(normC, Vector3.up);
                var angleD = VectorUtils.Angle(normD, Vector3.up);

                if (angleA < angle)
                {
                    dLat -= threshold;
                }
                else if (angleB < angle)
                {
                    dLat += threshold;
                }
                else if (angleC < angle)
                {
                    dLng -= threshold;
                }
                else if (angleD < angle)
                {
                    dLng += threshold;
                }
                else
                {
                    break;
                }
            }

            latitude = iLat + dLat;
            longitude = iLng + dLng;

            return k < maxIterations;
        }

        public static double MinSafeAltitude(this CelestialBody body)
        {
            double maxTerrainHeight = 200;
            if (body.pqsController)
            {
                PQS pqs = body.pqsController;
                maxTerrainHeight = pqs.radiusMax - pqs.radius;
            }
            return Math.Max(maxTerrainHeight, body.atmosphereDepth);
        }
    }
}
