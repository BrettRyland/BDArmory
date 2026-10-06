using UnityEngine;
using BDArmory.Utils;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace BDArmory.Extensions
{
    public static class BodyExtensions
    {
        readonly struct MagneticFieldParams
        {
            readonly public double mu;
            readonly public double BT;
            readonly public bool affectedByParentBody;

            public MagneticFieldParams(float _strengthAtSurface, double _bodyRadius, float _strengthAtInfinity, bool _affectedByParentBody)
            {
                mu = -_strengthAtSurface;
                BT = _strengthAtInfinity;
                affectedByParentBody = _affectedByParentBody;
            }

            public const double earthStrengthAtSurface = 0.31d;
            public const double earthStrengthAtInfinity = 0.00015d;

            public MagneticFieldParams(double _bodyRadius)
            {
                mu = -earthStrengthAtSurface;
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
                        (float)ParseField(node, "strengthAtSurface", typeof(double)),
                        currBody.Radius,
                        (float)ParseField(node, "strengthAtInfinity", typeof(double)),
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
                    else if (type == typeof(double))
                    { return double.Parse(value); }
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

        static Vector3d CalcMagStrength(this CelestialBody body, in Vector3d up, in double radDist, in MagneticFieldParams magParams)
        {
            Vector3d north = (body.BodyFrame.Z.xzy).ProjectOnPlanePreNormalized(up).normalized;
            Vector3d relVecLoc = body.WorldToLocal(up.xzy);
            double cosCoLat = relVecLoc.z;
            double sinCoLat = Math.Sqrt(1.0 - cosCoLat * cosCoLat);

            // mu * (radius / radDist)³
            double muDivR = body.Radius / radDist;
            muDivR *= (muDivR * muDivR) * magParams.mu;

            double radComp = 2.0 * cosCoLat * muDivR;
            double northComp = -sinCoLat * muDivR;

            Vector3d magStrength;
            magStrength.x = up.x * radComp + north.x * northComp;
            magStrength.y = up.y * radComp + north.y * northComp;
            magStrength.z = up.z * radComp + north.z * northComp;

            return magStrength;
        }

        static Vector3d CalcTailMagStrength(this CelestialBody body, in Vector3d up, in MagneticFieldParams magParams)
        {
            Vector3d dirFromSun = (body.position - body.referenceBody.position).normalized;

            Vector3d orbitNormal = body.orbit.GetOrbitNormal();

            //double cosMagLon = Vector3d.Dot(sunDir, up.ProjectOnPlanePreNormalized(orbitNormal).normalized);

            // +ve away from the sun
            return dirFromSun * magParams.BT * Math.Sign(Vector3d.Dot(orbitNormal, body.BodyFrame.Z) * Vector3d.Dot(orbitNormal, up)); // Field strength at infinity, away from the sun, with +ve north of the planet and -ve south
        }

        public static Vector3d GetMagneticFieldStrength(this CelestialBody body, Vessel vessel)
        {
            if (!magneticFields.TryGetValue(body, out MagneticFieldParams magParams))
            {
                Debug.Log($"[BDArmory.CelestialBody.BodyExtensions] BD failed to find entry for {body.bodyName}'s magnetic field! Defaulting to Earth/Kerbin's");

                magParams = new MagneticFieldParams(body.Radius);
                magneticFields.Add(body, magParams);
            }

            Vector3d up;
            double radDist;

            if (body == vessel.mainBody)
            {
                up = vessel.up;
                radDist = body.Radius + vessel.altitude;
            }
            else
            {
                (radDist, up) = (vessel.CoMD - body.position).MagNorm();
            }

            Vector3d magStrength = body.CalcMagStrength(up, radDist, magParams);

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

            magStrength += CalcTailMagStrength(body, up, magParams); // Field strength at infinity, away from the sun, with +ve north of the planet and -ve south

            return magStrength;
        }

        public static Vector3d GetMagneticFieldStrength(this CelestialBody body, Vector3d position, bool isMainBody)
        {
            if (!magneticFields.TryGetValue(body, out MagneticFieldParams magParams))
            {
                Debug.Log($"[BDArmory.CelestialBody.BodyExtensions] BD failed to find entry for {body.bodyName}'s magnetic field! Defaulting to Earth/Kerbin's");

                magParams = new MagneticFieldParams(body.Radius);
                magneticFields.Add(body, magParams);
            }

            (double radDist, Vector3d up) = (position - body.position).MagNorm();

            Vector3d magStrength = body.CalcMagStrength(up, radDist, magParams);

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

            magStrength += CalcTailMagStrength(body, up, magParams); // Field strength at infinity, away from the sun, with +ve north of the planet and -ve south

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

        /// <summary>
        /// Note, requires r to be xzy'd!
        /// </summary>
        /// <param name="body"></param>
        /// <param name="r"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3d WorldToLocal(this CelestialBody body, in Vector3d r)
        {
            Vector3d result;
            result.x = Vector3d.Dot(r, body.BodyFrame.X);
            result.y = Vector3d.Dot(r, body.BodyFrame.Y);
            result.z = Vector3d.Dot(r, body.BodyFrame.Z);
            return result;
        }

        /// <summary>
        /// IMPORTANT NOTE: Returns in xzy format! Vector3d must by xzy'd for use as actual world coords!
        /// In most cases you should use LocalToWorldXYZ!
        /// </summary>
        /// <param name="body"></param>
        /// <param name="r"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3d LocalToWorld(this CelestialBody body, in Vector3d r)
        {
            // Doc's optimized LocalToWorld
            Vector3d result;
            result.x = r.x * body.BodyFrame.X.x + r.y * body.BodyFrame.Y.x + r.z * body.BodyFrame.Z.x;
            result.y = r.x * body.BodyFrame.X.y + r.y * body.BodyFrame.Y.y + r.z * body.BodyFrame.Z.y;
            result.z = r.x * body.BodyFrame.X.z + r.y * body.BodyFrame.Y.z + r.z * body.BodyFrame.Z.z;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3d LocalToWorldXYZ(this CelestialBody body, in Vector3d r)
        {
            // Doc's optimized LocalToWorld, but with y and z flipped...
            Vector3d result;
            result.x = r.x * body.BodyFrame.X.x + r.y * body.BodyFrame.Y.x + r.z * body.BodyFrame.Z.x;
            result.z = r.x * body.BodyFrame.X.y + r.y * body.BodyFrame.Y.y + r.z * body.BodyFrame.Z.y;
            result.y = r.x * body.BodyFrame.X.z + r.y * body.BodyFrame.Y.z + r.z * body.BodyFrame.Z.z;
            return result;
        }

        private static readonly double Deg2Rad = Math.PI / 180.0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3d GeoPosToLocal(this CelestialBody body, in Vector3d geoPos)
        {
            double lat = geoPos.x * Deg2Rad;
            double lon = geoPos.y * Deg2Rad;
            double latCos = Math.Cos(lat);

            double radialPos = geoPos.z + body.Radius;

            Vector3d result;
            // Lat-Lon -> Radial Vector
            result.x = radialPos * (latCos * Math.Cos(lon));
            result.y = radialPos * (latCos * Math.Sin(lon));
            result.z = radialPos * Math.Sin(lat);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3d GeoPosToLocal(this CelestialBody body, double lat, double lon, in double alt)
        {
            lat *= Deg2Rad;
            lon *= Deg2Rad;
            double latCos = Math.Cos(lat);
            double radialPos = alt + body.Radius;

            // Would be nice to use the sqrt(1 - cos * cos) trick here by converting latitude to
            // co-latitude, but given that we need the full 360 deg for lon and the possibility of
            // bizzarro lat values < -90 or > 90, we should just avoid that...

            Vector3d result;
            // Lat-Lon -> Radial Vector
            result.x = radialPos * (latCos * Math.Cos(lon));
            result.y = radialPos * (latCos * Math.Sin(lon));
            result.z = radialPos * Math.Sin(lat);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3d BDGetWorldSurfacePosition(this CelestialBody body, in double lat, in double lon, in double alt)
        {
            Vector3d result = body.LocalToWorldXYZ(body.GeoPosToLocal(lat, lon, alt));
            Vector3d bodyPos = body.position;
            result.x = result.x + bodyPos.x;
            result.y = result.y + bodyPos.y;
            result.z = result.z + bodyPos.z;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3d BDGetWorldSurfacePosition(this CelestialBody body, in Vector3d geoPosition)
        {
            Vector3d result = body.LocalToWorldXYZ(body.GeoPosToLocal(geoPosition));
            Vector3d bodyPos = body.position;
            result.x = result.x + bodyPos.x;
            result.y = result.y + bodyPos.y;
            result.z = result.z + bodyPos.z;
            return result;
        }
    }
}
