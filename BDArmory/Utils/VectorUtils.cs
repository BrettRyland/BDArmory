using System;
using UnityEngine;

using BDArmory.Extensions;
using System.Runtime.CompilerServices;

namespace BDArmory.Utils
{
    public static class VectorUtils
    {
        private static System.Random RandomGen = new System.Random();

        private static readonly double RadToDeg = 180.0 / Math.PI;

        /// <summary>
        /// A slightly more efficient `Vector3.Sign` function, still requires a sqrt so it is best replaced with
        /// `VectorUtils.GetAngleOnPlane`, however that requires orthogonality from `fromDirection`. This function
        /// may be used even if `referenceRight` is not orthogonal to `fromDirection`. This function also does not
        /// require the magnitudes of any of its inputs to be specified in some way.
        /// </summary>
        /// <param name="referenceRight">Right compared to fromDirection, make sure it's not orthogonal to toDirection, or you'll get unstable signs</param>
        public static float SignedAngle(in Vector3 fromDirection, in Vector3 toDirection, in Vector3 referenceRight)
        {
            float angle = Angle(fromDirection, toDirection);
            float sign = Mathf.Sign(Vector3.Dot(toDirection, referenceRight));
            float finalAngle = sign * angle;
            return finalAngle;
        }

        /// <summary>
        /// Same as SignedAngle, just using double precision for the cosine calculation.
        /// For very small angles the floating point precision starts to matter, as the cosine is close to 1, not to 0.
        /// </summary>
        public static float SignedAngleDP(in Vector3 fromDirection, in Vector3 toDirection, in Vector3 referenceRight)
        {
            float angle = (float)Vector3d.Angle(fromDirection, toDirection);
            float sign = Mathf.Sign(Vector3.Dot(toDirection, referenceRight));
            float finalAngle = sign * angle;
            return finalAngle;
        }

        /// <summary>
        /// Convert an angle to be between -180 and 180.
        /// </summary>
        public static float ToAngle(this float angle)
        {
            angle = (angle + 180) % 360;
            return angle > 0 ? angle - 180 : angle + 180;
        }

        //from howlingmoonsoftware.com
        //calculates how long it will take for a target to be where it will be when a bullet fired now can reach it.
        //delta = initial relative position, vr = relative velocity, muzzleV = bullet velocity.
        public static float CalculateLeadTime(in Vector3 delta, in Vector3 vr, float muzzleV)
        {
            // Quadratic equation coefficients a*t^2 + b*t + c = 0
            float a = Vector3.Dot(vr, vr) - muzzleV * muzzleV;
            float b = 2f * Vector3.Dot(vr, delta);
            float c = Vector3.Dot(delta, delta);

            float det = b * b - 4f * a * c;

            // If the determinant is negative, then there is no solution
            if (det > 0f)
            {
                return 2f * c / (BDAMath.Sqrt(det) - b);
            }
            else
            {
                return -1f;
            }
        }

        /// <summary>
        /// Returns a value between -1 and 1 via Perlin noise.
        /// </summary>
        /// <returns>Returns a value between -1 and 1 via Perlin noise.</returns>
        /// <param name="x">The x coordinate.</param>
        /// <param name="y">The y coordinate.</param>
        public static float FullRangePerlinNoise(float x, float y)
        {
            float perlin = Mathf.PerlinNoise(x, y);

            perlin -= 0.5f;
            perlin *= 2;

            return perlin;
        }

        public static Vector3 RandomDirectionDeviation(in Vector3 direction, float maxAngle)
        {
            return Vector3.RotateTowards(direction, UnityEngine.Random.rotation * direction, UnityEngine.Random.Range(0, maxAngle * Mathf.Deg2Rad), 0).normalized;
        }

        public static Vector3 WeightedDirectionDeviation(in Vector3 direction, float maxAngle)
        {
            float random = UnityEngine.Random.Range(0f, 1f);
            float maxRotate = maxAngle * (random * random);
            maxRotate = Mathf.Clamp(maxRotate, 0, maxAngle) * Mathf.Deg2Rad;
            return Vector3.RotateTowards(direction, UnityEngine.Random.onUnitSphere.ProjectOnPlane(direction), maxRotate, 0).normalized;
        }

        /// <summary>
        /// Returns the original vector rotated in a random direction using the give standard deviation.
        /// </summary>
        /// <param name="direction">mean direction</param>
        /// <param name="standardDeviation">standard deviation in degrees</param>
        /// <returns>Randomly adjusted Vector3</returns>
        /// <remarks>
        /// Technically, this is calculated using the chi-squared distribution in polar coordinates,
        /// which, incidentally, makes the math easier too.
        /// However a chi-squared (k=2) distance from center distribution produces a vector distributed normally
        /// on any chosen axis orthogonal to the original vector, which is exactly what we want.
        /// </remarks>
        public static Vector3 GaussianDirectionDeviation(in Vector3 direction, float standardDeviation)
        {
            return Quaternion.AngleAxis(UnityEngine.Random.Range(-180f, 180f), direction)
                * Quaternion.AngleAxis(Rayleigh() * standardDeviation,
                                       new Vector3(-1 / direction.x, -1 / direction.y, 2 / direction.z))  // orthogonal vector
                * direction;
        }

        /// <returns>Random float distributed with an approximated standard normal distribution</returns>
        /// <see>https://en.wikipedia.org/wiki/Box%E2%80%93Muller_transform</see>
        /// <remarks>Note a standard normal variable is technically unbounded</remarks>
        public static float Gaussian()
        {
            // Technically this will raise an exception if the first random produces a zero (which should never happen now that it's log(1-rnd))
            try
            {
                return BDAMath.Sqrt(-2 * Mathf.Log(1f - UnityEngine.Random.value)) * Mathf.Cos(Mathf.PI * UnityEngine.Random.value);
            }
            catch (Exception e)
            { // I have no idea what exception Mathf.Log raises when it gets a zero
                Debug.LogWarning("[BDArmory.VectorUtils]: Exception thrown in Gaussian: " + e.Message + "\n" + e.StackTrace);
                return 0;
            }
        }

        static FloatCurve GaussianQuantileApp = new([
            new(0f, 0.0f, 1.253314f, 1.253314f),
            new(0.5762892f, 0.8f, 1.7259735f, 1.7259735f),
            new(0.8663856f, 1.5f, 3.8604795f, 3.8604795f),
            new(1f, 4.5f, 60f, 0.0f),
        ]);

        public static float GaussianQuant()
        {
            // Technically this will raise an exception if the first random produces a zero (which should never happen now that it's log(1-rnd))
            try
            {
                return GaussianQuantileApp.Evaluate(UnityEngine.Random.value);
            }
            catch (Exception e)
            { // I have no idea what exception Mathf.Log raises when it gets a zero
                Debug.LogWarning("[BDArmory.VectorUtils]: Exception thrown in Gaussian: " + e.Message + "\n" + e.StackTrace);
                return 0;
            }
        }

        /// <summary>
        /// Generate a Vector3 with elements from an approximately normal distribution (mean: 0, std.dev: 1).
        /// </summary>
        /// <returns></returns>
        public static Vector3 GaussianVector3()
        {
            return new Vector3(Gaussian(), Gaussian(), Gaussian());
        }

        public static Vector3d GaussianVector3d(in Vector3d mean, in Vector3d stdDev)
        {
            return new Vector3d(
                mean.x + stdDev.x * Math.Sqrt(-2 * Math.Log(1 - RandomGen.NextDouble())) * Math.Cos(Math.PI * RandomGen.NextDouble()),
                mean.y + stdDev.y * Math.Sqrt(-2 * Math.Log(1 - RandomGen.NextDouble())) * Math.Cos(Math.PI * RandomGen.NextDouble()),
                mean.z + stdDev.z * Math.Sqrt(-2 * Math.Log(1 - RandomGen.NextDouble())) * Math.Cos(Math.PI * RandomGen.NextDouble())
            );
        }

        /// <returns>
        /// Random float distributed with the chi-squared distribution with two degrees of freedom
        /// aka the Rayleigh distribution.
        /// Multiply by deviation for best results.
        /// </returns>
        /// <see>https://en.wikipedia.org/wiki/Rayleigh_distribution</see>
        /// <remarks>Note a chi-square distributed variable is technically unbounded</remarks>
        public static float Rayleigh()
        {
            // Technically this will raise an exception if the random produces a zero, which should almost never happen
            try
            {
                return BDAMath.Sqrt(-2 * Mathf.Log(UnityEngine.Random.value));
            }
            catch (Exception e)
            { // I have no idea what exception Mathf.Log raises when it gets a zero
                Debug.LogWarning("[BDArmory.VectorUtils]: Exception thrown in Rayleigh: " + e.Message + "\n" + e.StackTrace);
                return 0;
            }
        }

        /// <summary>
        /// Converts world position to Lat,Long,Alt form.
        /// </summary>
        /// <returns>The position in geo coords.</returns>
        /// <param name="worldPosition">World position.</param>
        /// <param name="body">Body.</param>
        public static Vector3d WorldPositionToGeoCoords(in Vector3d worldPosition, CelestialBody body)
        {
            if (!body)
            {
                return Vector3d.zero;
            }

            //double lat = body.GetLatitude(worldPosition);
            //double longi = body.GetLongitude(worldPosition);
            //double alt = body.GetAltitude(worldPosition);
            body.GetLatLonAlt(worldPosition, out double lat, out double longi, out double alt);
            return new Vector3d(lat, longi, alt);
        }

        /// <summary>
        /// Calculates the coordinates of a point a certain distance away in a specified direction.
        /// </summary>
        /// <param name="start">Starting point coordinates, in Lat,Long,Alt form</param>
        /// <param name="body">The body on which the movement is happening</param>
        /// <param name="bearing">Bearing to move in, in degrees, where 0 is north and 90 is east</param>
        /// <param name="distance">Distance to move, in meters</param>
        /// <returns>Ending point coordinates, in Lat,Long,Alt form</returns>
        public static Vector3 GeoCoordinateOffset(in Vector3 start, CelestialBody body, float bearing, float distance)
        {
            //https://stackoverflow.com/questions/2637023/how-to-calculate-the-latlng-of-a-point-a-certain-distance-away-from-another
            float lat1 = start.x * Mathf.Deg2Rad;
            float lon1 = start.y * Mathf.Deg2Rad;
            bearing *= Mathf.Deg2Rad;
            distance /= ((float)body.Radius + start.z);

            float lat2 = Mathf.Asin(Mathf.Sin(lat1) * Mathf.Cos(distance) + Mathf.Cos(lat1) * Mathf.Sin(distance) * Mathf.Cos(bearing));
            float lon2 = lon1 + Mathf.Atan2(Mathf.Sin(bearing) * Mathf.Sin(distance) * Mathf.Cos(lat1), Mathf.Cos(distance) - Mathf.Sin(lat1) * Mathf.Sin(lat2));

            return new Vector3(lat2 * Mathf.Rad2Deg, lon2 * Mathf.Rad2Deg, start.z);
        }

        /// <summary>
        /// Calculate the bearing going from one point to another
        /// </summary>
        /// <param name="start">Starting point coordinates, in Lat,Long,Alt form</param>
        /// <param name="destination">Destination point coordinates, in Lat,Long,Alt form</param>
        /// <returns>Bearing when looking at destination from start, in degrees, where 0 is north and 90 is east</returns>
        public static float GeoForwardAzimuth(in Vector3 start, in Vector3 destination)
        {
            //http://www.movable-type.co.uk/scripts/latlong.html
            float lat1 = start.x * Mathf.Deg2Rad;
            float lon1 = start.y * Mathf.Deg2Rad;
            float lat2 = destination.x * Mathf.Deg2Rad;
            float lon2 = destination.y * Mathf.Deg2Rad;
            return Mathf.Atan2(Mathf.Sin(lon2 - lon1) * Mathf.Cos(lat2), Mathf.Cos(lat1) * Mathf.Sin(lat2) - Mathf.Sin(lat1) * Mathf.Cos(lat2) * Mathf.Cos(lon2 - lon1)) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Calculate the distance from one point to another on a globe
        /// </summary>
        /// <param name="start">Starting point coordinates, in Lat,Long,Alt form</param>
        /// <param name="destination">Destination point coordinates, in Lat,Long,Alt form</param>
        /// <param name="body">The body on which the distance is calculated</param>
        /// <returns>distance between the two points</returns>
        public static float GeoDistance(in Vector3 start, in Vector3 destination, CelestialBody body)
        {
            //http://www.movable-type.co.uk/scripts/latlong.html
            float lat1 = start.x * Mathf.Deg2Rad;
            float lat2 = destination.x * Mathf.Deg2Rad;
            float dlat = lat2 - lat1;
            float dlon = (destination.y - start.y) * Mathf.Deg2Rad;
            float a = Mathf.Sin(dlat / 2) * Mathf.Sin(dlat / 2) + Mathf.Cos(lat1) * Mathf.Cos(lat2) * Mathf.Sin(dlon / 2) * Mathf.Sin(dlon / 2);
            float distance = 2 * Mathf.Atan2(BDAMath.Sqrt(a), BDAMath.Sqrt(1 - a)) * (float)body.Radius;
            return BDAMath.Sqrt(distance * distance + (destination.z - start.z) * (destination.z - start.z));
        }

        public static Vector3 RotatePointAround(in Vector3 pointToRotate, in Vector3 pivotPoint, in Vector3 axis, float angle)
        {
            Vector3 line = pointToRotate - pivotPoint;
            line = Quaternion.AngleAxis(angle, axis) * line;
            return pivotPoint + line;
        }

        public static Vector3 GetNorthVector(in Vector3 position, CelestialBody body)
        {
            /*var latlon = body.GetLatitudeAndLongitude(position);
            var surfacePoint = body.GetWorldSurfacePosition(latlon.x, latlon.y, 0);
            var up = (body.GetWorldSurfacePosition(latlon.x, latlon.y, 1000) - surfacePoint).normalized;
            var north = -Math.Sign(latlon.x) * (body.GetWorldSurfacePosition(latlon.x - Math.Sign(latlon.x), latlon.y, 0) - surfacePoint).ProjectOnPlanePreNormalized(up).normalized;
            */
            body.GetLatLonAlt(position, out double lat, out double lon, out double alt);
            Vector3d up = (body.position - position) / (body.Radius + alt);
            /*var latlon = body.GetLatitudeAndLongitude(position);
            var up = GetUpDirection(position, out double alt);*/
            Vector3 north = (-Math.Sign(lat) * body.GetWorldSurfacePosition(lat - Math.Sign(lat), lon, alt) - position).ProjectOnPlanePreNormalized(up).normalized;
            return north;
        }

        /// <summary>
        /// Efficiently calculate up, north and right at a given worldspace position on a body.
        /// </summary>
        /// <param name="body"></param>
        /// <param name="position"></param>
        /// <param name="up"></param>
        /// <param name="north"></param>
        /// <param name="right"></param>
        public static void GetWorldCoordinateFrame(CelestialBody body, in Vector3 position, out Vector3 up, out Vector3 north, out Vector3 right)
        {
            /*var latlon = body.GetLatitudeAndLongitude(position);
            var surfacePoint = body.GetWorldSurfacePosition(latlon.x, latlon.y, 0);
            up = (body.GetWorldSurfacePosition(latlon.x, latlon.y, 1000) - surfacePoint).normalized;
            north = -Math.Sign(latlon.x) * (body.GetWorldSurfacePosition(latlon.x - Math.Sign(latlon.x), latlon.y, 0) - surfacePoint).ProjectOnPlanePreNormalized(up).normalized;*/
            /*var latlon = body.GetLatitudeAndLongitude(position);
            up = GetUpDirection(position, out double alt);*/
            body.GetLatLonAlt(position, out double lat, out double lon, out double alt);
            up = (position - body.position) / (body.Radius + alt);
            north = (-Math.Sign(lat) * body.GetWorldSurfacePosition(lat - Math.Sign(lat), lon, alt) - position).ProjectOnPlanePreNormalized(up).normalized;
            right = Vector3.Cross(up, north);
        }

        /// <summary>
        /// Efficiently calculate up, north and right at a given worldspace position on a body.
        /// </summary>
        /// <param name="body"></param>
        /// <param name="position"></param>
        /// <param name="up"></param>
        /// <param name="north"></param>
        /// <param name="right"></param>
        public static void GetWorldCoordinateFrame(Vessel vessel, out Vector3 up, out Vector3 north, out Vector3 right)
        {
            var lat = vessel.latitude;
            var lon = vessel.longitude;
            up = vessel.upAxis;
            north = (-Math.Sign(lat) * vessel.mainBody.GetWorldSurfacePosition(lat - Math.Sign(lat), lon, vessel.altitude) - vessel.CoMD).ProjectOnPlanePreNormalized(up).normalized;
            right = Vector3.Cross(up, north);
        }

        public static Vector3 GetWorldSurfacePostion(in Vector3d geoPosition, CelestialBody body)
        {
            if (!body)
            {
                return Vector3.zero;
            }
            return body.GetWorldSurfacePosition(geoPosition.x, geoPosition.y, geoPosition.z);
        }

        /// <summary>
        /// Get the up direction at a position.
        /// Note: If the position is a vessel's position, then this is the same as vessel.up, which is precomputed. Use that instead!
        /// </summary>
        /// <param name="position"></param>
        /// <returns>The normalized up direction at the position.</returns>
        public static Vector3 GetUpDirection(in Vector3 position)
        {
            if (FlightGlobals.currentMainBody == null) return Vector3.up;
            return (position - FlightGlobals.currentMainBody.position).normalized;
        }

        /// <summary>
        /// Get the up direction and altitude at a position.
        /// Note: If the position is a vessel's position, then this is the same as vessel.up and vessel.altitude, which are precomputed. Use those instead!
        /// </summary>
        /// <param name="position"></param>
        /// <param name="altitude"></param>
        /// <returns>The normalized up direction at the position.</returns>
        public static Vector3 GetUpDirection(in Vector3 position, out double altitude)
        {
            if (FlightGlobals.currentMainBody == null)
            {
                altitude = 0;
                return Vector3.up;
            }
            (altitude, Vector3 upDir) = (position - FlightGlobals.currentMainBody.position).MagNorm();
            altitude -= FlightGlobals.currentMainBody.Radius;

            return upDir;
        }

        public static bool SphereRayIntersect(in Ray ray, in Vector3 sphereCenter, double sphereRadius, out double distance)
        {
            Vector3 o = ray.origin;
            Vector3 l = ray.direction;
            Vector3d c = sphereCenter;
            double r = sphereRadius;

            double d;

            var dotLOC = Vector3.Dot(l, o - c);
            d = -(Vector3.Dot(l, o - c) + Math.Sqrt(dotLOC * dotLOC - (o - c).sqrMagnitude + (r * r)));

            if (double.IsNaN(d))
            {
                distance = 0;
                return false;
            }
            else
            {
                distance = d;
                return true;
            }
        }

        public static bool CheckClearOfSphere(in Ray ray, in Vector3 sphereCenter, float sphereRadius)
        {
            // Return true if no sphere intersections, false if sphere intersections
            // Better handling of conditions when ray origin is inside sphere or direction is away from sphere than SphereRayIntersect

            if ((ray.origin - sphereCenter).sqrMagnitude < (sphereRadius * sphereRadius))
                return false;

            bool intersect = SphereRayIntersect(ray, sphereCenter, (double)sphereRadius, out double distance);

            if (!intersect)
                return true;
            else
            {
                if (distance > 0) // Valid intersection
                    return false;
                else // -ray intersects, but +ray does not
                    return true;
            }
        }

        /// <summary>
        /// A more accurate Angle that maintains precision down to an angle of 1e-5
        /// instead of the 1e-2 that Vector3.Angle gives (as compared to (float)Vector3d.Angle).
        /// Additionally, it's around 30% faster than Vector3.Angle.
        /// 
        /// Note: When called with Vector3 inputs, the inputs are automatically cast to Vector3d
        /// with insignificant overhead, so explicit overloads aren't necessary.
        /// </summary>
        /// <param name="from"></param>
        /// <param name="to"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Angle(in Vector3d from, in Vector3d to)
        {
            double num = from.sqrMagnitude * to.sqrMagnitude;
            if (num < 1e-30)
            {
                return 0f;
            }

            double num2 = BDAMath.Clamp(Vector3d.Dot(from, to) / Math.Sqrt(num), -1.0, 1.0);
            return (float)(Math.Acos(num2) * RadToDeg);
        }

        /// <summary>
        /// Get angle between two pre-normalized vectors.
        /// 
        /// This implementation assumes that the input vectors are already normalized,
        /// skipping such checks and normalization that Vector3.Angle does.
        /// IMPORTANT NOTE: Unlike Vector3.Angle(), this returns 90° if one or both
        /// vectors are zero vectors! Vector3.Angle() returns 0° instead.
        /// If this behavior is undesireable, the "AnglePreNormalized" function which takes
        /// in the two original vectors and their magnitudes should be used instead.
        /// </summary>
        /// <param name="from">First vector.</param>
        /// <param name="to">Second vector.</param>
        /// <returns>The angle between the two vectors.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float AnglePreNormalized(in Vector3d from, in Vector3d to)
        {
            double num2 = BDAMath.Clamp(Vector3d.Dot(from, to), -1d, 1d);
            return (float)(Math.Acos(num2) * RadToDeg);
        }

        /// <summary>
        /// Get angle between two vectors, with known magnitudes.
        /// 
        /// This implementation assumes that the magnitude of the input vectors is known,
        /// skipping some checks and normalization that Vector3.Angle does. It is not
        /// truly more efficient, however it is slightly more efficient when both
        /// magnitudes are already known.
        /// </summary>
        /// <param name="from">First vector.</param>
        /// <param name="to">Second vector.</param>
        /// <param name="fromMag">First vector magnitude.</param>
        /// <param name="toMag">Second vector magnitude.</param>
        /// <returns>The angle between the two vectors.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float AnglePreNormalized(in Vector3d from, in Vector3d to, float fromMag, float toMag)
        {
            // Using (double) here should help with precision when the magnitudes get very small or very big
            double num = (double)fromMag * (double)toMag;
            if (num < 1E-15f)
                return 0f;

            double num2 = BDAMath.Clamp(Vector3d.Dot(from, to) / num, -1d, 1d);
            return (float)(Math.Acos(num2) * RadToDeg);
        }

        // No accuracy or efficiency gain (aside from the case where maxRadiansDelta > angle between the vectors
        /*[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 RotateTowards(Vector3 current, Vector3 target, float maxRadiansDelta, bool constantMag = false)
        {
            float currSqrMag = 1f;
            if (constantMag)
            {
                currSqrMag = current.sqrMagnitude;
            }

            float currAngle = Angle(current, target);

            if (currAngle < (maxRadiansDelta *= Mathf.Rad2Deg)) 
            {
                if (constantMag)
                {
                    target *= BDAMath.Sqrt(currSqrMag / target.sqrMagnitude);
                }
                return target;
            }

            current = Vector3.Slerp(current, target, maxRadiansDelta / currAngle);

            if (constantMag)
            {
                current *= BDAMath.Sqrt(currSqrMag / current.sqrMagnitude);
            }

            return current;
        }*/

        /// <summary>
        /// Get AoA and Sideslip of a vector, relative to axes defined by forward and up.
        /// Note that forward and up are expected to be unit vectors, however dir does not have
        /// to be a unit vector!
        /// 
        /// </summary>
        /// <param name="dir">Direction vector.</param>
        /// <param name="forward">Aircraft aligned forward vector.</param>
        /// <param name="up">Aircraft aligned up/lift vector.</param>
        /// <param name="AoA">AoA output.</param>
        /// <param name="sideslip">Sideslip output.</param>
        /// <returns>The AoA and Sideslip angle, in degrees, of "dir" relative to the axes defined by forward and up.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GetAoASideslip(in Vector3d dir, in Vector3d forward, in Vector3d up, out float AoA, out float sideslip)
        {
            // Get the left vector to fully define the coordinate system
            Vector3d left = Vector3d.Cross(up, forward);

            // Get the projections
            double x = Vector3d.Dot(dir, forward);
            double y = Vector3d.Dot(dir, left);
            double z = Vector3d.Dot(dir, up);

            // Return the AoA/sideslip
            AoA = (float)(-RadToDeg * Math.Atan2(z, x));
            sideslip = (float)(-RadToDeg * Math.Atan2(y, x));
        }

        /// <summary>
        /// Get angle of a vector, projected on a plane defined by a forward and a left vector.
        /// Note that forward and left must have equal magnitudes but do not have to be unit
        /// vectors (though unit vectors are most likely the most convenient for this purpose).
        /// dir does not have to be a unit vector.
        /// 
        /// </summary>
        /// <param name="dir">Direction vector.</param>
        /// <param name="forward">Forward vector.</param>
        /// <param name="left">Left vector.</param>
        /// <returns>The angle of "dir" relative to "forward", in degrees, projected onto a plane defined by "forward" and "left".</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetAngleOnPlane(in Vector3d dir, in Vector3d forward, in Vector3d left)
        {
            // Get the projections
            double x = Vector3d.Dot(dir, forward);
            double y = Vector3d.Dot(dir, left);

            // Check for if the desired vector is straight up/down
            if (Math.Abs(x) < 2E-5 && Math.Abs(y) < 2E-5)
                return 0f;

            // Return the azimuth/elevation
            return (float) (RadToDeg * Math.Atan2(y, x));
        }

        /// <summary>
        /// Get elevation angle of a vector, relative to an up vector.
        /// Note that this basically an alternate form of AnglePreNormalized.
        /// 
        /// </summary>
        /// <param name="dir">Direction vector.</param>
        /// <param name="up">Up vector.</param>
        /// <param name="dist">Magnitude of the direction vector.</param>
        /// <param name="upMag">Magnitude of the up vector, defaults to 1.</param>
        /// <returns>The angle of "dir" relative to "up", in degrees, as an elevation angle, with range -90° to 90°.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetElevation(in Vector3 dir, in Vector3 up, float dist, float upMag = 1.0f)
        {
            return 90f - AnglePreNormalized(up, dir, upMag, dist);
        }

        /// <summary>
        /// Get elevation angle of a vector, relative to an up vector, with both vectors being normalized
        /// 
        /// </summary>
        /// <param name="dir">Direction vector.</param>
        /// <param name="up">Up vector.</param>
        /// <returns>The angle of "dir" relative to "up", in degrees, as an elevation angle, with range -90° to 90°.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetElevationPreNorm(in Vector3d dir, in Vector3d up)
        {
            return 90f - AnglePreNormalized(up, dir);
        }

        /// <summary>
        /// Get elevation angle of a vector, relative to an up vector.
        /// Note that this basically an alternate form of Vector3.Angle,
        /// somewhat optimized for the case where the up vector is a
        /// unit vector (skipping a mere "sqrMagnitude" call). If the
        /// magnitude of the direction vector is known, the overload
        /// with this magnitude is preferred:
        /// GetElevation(dir, up, dist, upMag)
        /// 
        /// </summary>
        /// <param name="dir">Direction vector.</param>
        /// <param name="up">Up vector.</param>
        /// <returns>The angle of "dir" relative to "up", in degrees, as an elevation angle, with range -90° to 90°.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetElevation(in Vector3d dir, in Vector3d up)
        {
            double dirMag = Vector3d.Magnitude(dir);
            if (dirMag < 1E-15)
            {
                return 0f;
            }

            double num2 = BDAMath.Clamp(Vector3d.Dot(up, dir) / dirMag, -1d, 1d);
            return 90f - (float)(Math.Acos(num2) * RadToDeg);
        }

        /// <summary>
        /// Get normalized difference between two vectors, useful for direction vectors.
        /// </summary>
        /// <param name="v1">First vector.</param>
        /// <param name="v2">Second vector.</param>
        /// <returns>(v1 - v2).normalized.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 NormalizedDiff(in Vector3 v1, in Vector3 v2)
        {
            float x = v1.x - v2.x, y = v1.y - v2.y, z = v1.z - v2.z;
            float normalizationFactor = 1f / BDAMath.Sqrt(x * x + y * y + z * z);
            return new Vector3(x * normalizationFactor, y * normalizationFactor, z * normalizationFactor);
        }

        /// <summary>
        /// Get vector sum, scaled by a scale vector.
        /// </summary>
        /// <param name="v1">First vector.</param>
        /// <param name="v2">Second vector.</param>
        /// <param name="scale">Scale vector.</param>
        /// <returns>v1.Scale(scale) + v2.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ScaledSum(in Vector3 v1, in Vector3 v2, in Vector3 scale)
        {
            return new Vector3(scale.x * v1.x + v2.x, scale.y * v1.y + v2.y, scale.z * v1.z + v2.z);
        }

        /// <summary>
        /// Get normalized difference between two vectors with given distance, useful for direction vectors.
        /// </summary>
        /// <param name="v1">First vector.</param>
        /// <param name="v2">Second vector.</param>
        /// <param name="dist">Distance.</param>
        /// <returns>(v1 - v2).normalized.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 NormalizedDiff(in Vector3 v1, in Vector3 v2, float dist)
        {
            float x = v1.x - v2.x, y = v1.y - v2.y, z = v1.z - v2.z;
            float normalizationFactor = 1f / dist;
            return new Vector3(x * normalizationFactor, y * normalizationFactor, z * normalizationFactor);
        }

        /// <summary>
        /// Get square distance between two vectors, in cases where the vector difference isn't needed.
        /// </summary>
        /// <param name="v1">First vector.</param>
        /// <param name="v2">Second vector.</param>
        /// <returns>(v1 - v2).sqrMagnitude.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SqrDist(in Vector3 v1, in Vector3 v2)
        {
            float x = v1.x - v2.x, y = v1.y - v2.y, z = v1.z - v2.z;
            return x * x + y * y + z * z;
        }

        /// <summary>
        /// Rotates a Vector2 in 2D about (0,0).
        /// </summary>
        /// <param name="v">Vector.</param>
        /// <param name="theta">Angle.</param>
        /// <returns>v rotated by theta degrees (anti-clockwise positive).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 Rotate2DVec2(in Vector2 v, float theta)
        {
            float x = v.x, y = v.y;
            float cos = Mathf.Cos(theta * Mathf.Deg2Rad);
            float sin = BDAMath.Sqrt(1 - cos * cos);
            return new Vector2(x * cos - y * sin, x * sin + y * cos);
        }

        /// <summary>
        /// Rotates a Vector2 in 2D about a given point.
        /// </summary>
        /// <param name="v">Vector to rotate.</param>
        /// <param name="p">Point to rotate about.</param>
        /// <param name="theta">Angle.</param>
        /// <returns>v rotated by theta degrees (anti-clockwise positive) about p.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 Rotate2DVec2(in Vector2 v, in Vector2 p, float theta)
        {
            float x = v.x - p.x, y = v.y - p.y;
            float cos = Mathf.Cos(theta);
            float sin = BDAMath.Sqrt(1 - cos * cos);
            return new Vector2(x * cos - y * sin + p.x, x * sin + y * cos + p.y);
        }

        /// <summary>
        /// Compute the 1-norm of a Vector3.
        /// </summary>
        /// <returns>The 1-norm.</returns>
        public static float OneNorm(this in Vector3 v)
        {
            return Mathf.Abs(v.x) + Mathf.Abs(v.y) + Mathf.Abs(v.z);
        }

        /// <summary>
        /// Round the Vector3 to the given unit.
        /// </summary>
        /// <param name="unit">The unit to round to.</param>
        /// <returns>The modified Vector3.</returns>
        public static Vector3 Round(this ref Vector3 v, float unit)
        {
            if (unit == 0) return v;
            v.x = Mathf.Round(v.x / unit) * unit;
            v.y = Mathf.Round(v.y / unit) * unit;
            v.z = Mathf.Round(v.z / unit) * unit;
            return v;
        }

        /// <summary>
        /// Non-modifying version of Vector3.Round.
        /// </summary>
        /// <param name="unit">The unit to round to.</param>
        /// <returns>A new Vector3 rounded to the unit.</returns>
        public static Vector3 Rounded(this Vector3 v, float unit) => v.Round(unit);

        /// <summary>
        /// Solves for the cone-plane intersect problem, returning the solution closest to "desiredDir".
        /// </summary>
        /// <param name="desiredDir">Desired direction.</param>
        /// <param name="planeNormal">Normalized normal vector of the plane.</param>
        /// <param name="coneDir">Normalized cone direction vector.</param>
        /// <param name="coneHalfAngle">Cone half-angle in radians.</param>
        /// <returns>Solution of the cone-plane intercept problem.</returns>
        public static Vector3 ConePlaneIntercept(in Vector3 desiredDir, in Vector3 planeNormal, in Vector3 coneDir, float coneHalfAngle, bool forceCone = false)
        {
            // This function solves the system of equations in the form:
            // coneDir dot V = cos(theta)
            // planeNormal dot V = 0
            // ||V|| = 1

            // It essentially solves for the intersection of a plane and a cone, both centered at the origin,
            // where coneDir is the cone's direction vector, and planeNormal is the normal vector of the plane.
            // Because the solutions are lines, we close the system by enforcing unit-vector results.

            float cosCone = Mathf.Cos(coneHalfAngle);

            float x1 = coneDir.x;
            float y1 = coneDir.y;
            float z1 = coneDir.z;

            float x2 = planeNormal.x;
            float y2 = planeNormal.y;
            float z2 = planeNormal.z;

            float dot = Vector3.Dot(coneDir, planeNormal);

            // Because the equation solved requires division by the appropriate component of the cross product, we
            // must check if it isn't 0.
            float crossx = y1 * z2 - z1 * y2;
            if (Mathf.Abs(crossx) > Mathf.Epsilon)
            {
                // If it is, we can solve for the two x-solutions
                float newCosCone = calcConePlane(x1, x2, y2, z2, crossx, dot, cosCone, forceCone, out float xsol1, out float xsol2);
                if (forceCone && newCosCone < 1.5f)
                {
                    Vector3 projectedConeDir = new Vector3(
                        coneDir.x - planeNormal.x * dot,
                        coneDir.y - planeNormal.y * dot,
                        coneDir.z - planeNormal.z * dot);
                    return Vector3.RotateTowards(coneDir, projectedConeDir, coneHalfAngle, 99999f);
                }
                // Giving us the two vector solutions
                calcConePlaneYZ(x1, x2, y1, y2, z1, z2, xsol1, xsol2, crossx, newCosCone, out Vector3 sol1, out Vector3 sol2);
                // We pick the vector most aligned with breakDirection
                if (Vector3.Dot(desiredDir, sol1) > Vector3.Dot(desiredDir, sol2))
                {
                    return sol1;
                }
                else
                {
                    return sol2;
                }
            }

            // If the solving for the x-component isn't possible due to crossx being 0, we try y
            float crossy = z1 * x2 - x1 * z2;
            if (Mathf.Abs(crossy) > Mathf.Epsilon)
            {
                float newCosCone = calcConePlane(y1, y2, x2, z2, crossy, dot, cosCone, forceCone, out float ysol1, out float ysol2);
                if (forceCone && newCosCone < 1.5f)
                {
                    Vector3 projectedConeDir = new Vector3(
                        coneDir.x - planeNormal.x * dot,
                        coneDir.y - planeNormal.y * dot,
                        coneDir.z - planeNormal.z * dot);
                    return Vector3.RotateTowards(coneDir, projectedConeDir, coneHalfAngle, 99999f);
                }
                calcConePlaneXZ(x1, x2, y1, y2, z1, z2, ysol1, ysol2, crossy, newCosCone, out Vector3 sol1, out Vector3 sol2);
                if (Vector3.Dot(desiredDir, sol1) > Vector3.Dot(desiredDir, sol2))
                {
                    return sol1;
                }
                else
                {
                    return sol2;
                }
            }

            // And if y is also zero we try z
            float crossz = x1 * y2 - y1 * x2;
            if (Mathf.Abs(crossz) > Mathf.Epsilon)
            {
                float newCosCone = calcConePlane(z1, z2, x2, y2, crossz, dot, cosCone, forceCone, out float zsol1, out float zsol2);
                if (forceCone && newCosCone < 1.5f)
                {
                    Vector3 projectedConeDir = new Vector3(
                        coneDir.x - planeNormal.x * dot,
                        coneDir.y - planeNormal.y * dot,
                        coneDir.z - planeNormal.z * dot);
                    return Vector3.RotateTowards(coneDir, projectedConeDir, coneHalfAngle, 99999f);
                }
                calcConePlaneXY(x1, x2, y1, y2, z1, z2, zsol1, zsol2, crossz, newCosCone, out Vector3 sol1, out Vector3 sol2);
                if (Vector3.Dot(desiredDir, sol1) > Vector3.Dot(desiredDir, sol2))
                {
                    return sol1;
                }
                else
                {
                    return sol2;
                }
            }

            // If the down and radar vectors are in-line, then we have a planar solution, and we just return breakDirection
            return desiredDir;
        }

        /// <summary>
        /// This solves for the component corresponding to the d1/d2 values (I.E. x1/x2 solves for x) of the cone-plane intersection
        /// problem. a2 and b2 are the two other components of the normal vector of the plane (I.E. y2, z2 for x1/x2). Due to these
        /// being added, the order is irrelevant.
        /// Note that if a solution isn't found, then cosCone is adjusted until there is a single solution.
        /// </summary>
        /// <param name="d1">Component of the cone's vector corresponding to the component to be solved for.</param>
        /// <param name="d2">Component of the plane's normal vector corresponding to the component to be solved for.</param>
        /// <param name="a2">Other component of the plane's normal vector (I.E. y/z for x).</param>
        /// <param name="b2">Other component of the plane's normal vector (I.E. y/z for x).</param>
        /// <param name="crossd">Cross product component in the direction to solve for (cone cross plane).</param>
        /// <param name="dot">Dot product of the two vectors.</param>
        /// <param name="cosCone">Cosine of the cone's half-angle.</param>
        /// <param name="r1">Solution 1 of the problem.</param>
        /// <param name="r2">Solution 2 of the problem.</param>
        /// <returns></returns>
        private static float calcConePlane(float d1, float d2, float a2, float b2, float crossd, float dot, float cosCone, bool forceCone, out float r1, out float r2)
        {
            float denominator = (1.0f - dot * dot);
            float numerator = cosCone * (d1 - d2 * dot);
            float determinant = numerator * numerator - denominator * (cosCone * cosCone * (a2 * a2 + b2 * b2) - crossd * crossd);

            if (determinant < 0.0f)
            {
                if (determinant < -5e-5f)
                {
                    if (forceCone)
                    {
                        // Set cosCone to an impossible value and return
                        cosCone = -10f;
                        r1 = 0f;
                        r2 = 0f;
                        return cosCone;
                    }

                    // Otherwise solve for cosCone required for determinant to be zero
                    cosCone = BDAMath.Sqrt(numerator * numerator / (denominator * (a2 * a2 + b2 * b2 - crossd * crossd)));
                    determinant = 0.0f;
                }
                else
                {
                    determinant = 0.0f;
                }
            }
            else
                determinant = BDAMath.Sqrt(determinant);

            r1 = (numerator - determinant) / denominator;
            r2 = (numerator + determinant) / denominator;
            return cosCone;
        }

        // Solves for the two other components of the solution given the X-component
        private static void calcConePlaneYZ(float x1, float x2, float y1, float y2, float z1, float z2, float xsol1, float xsol2, float crossx, float cosCone, out Vector3 sol1, out Vector3 sol2)
        {
            crossx = 1.0f / crossx;
            float ysol1 = (z1 * x2 * xsol1 + z2 * (cosCone - x1 * xsol1)) * crossx;
            float zsol1 = -(y1 * x2 * xsol1 + y2 * (cosCone - x1 * xsol1)) * crossx;
            float ysol2 = (z1 * x2 * xsol2 + z2 * (cosCone - x1 * xsol2)) * crossx;
            float zsol2 = -(y1 * x2 * xsol2 + y2 * (cosCone - x1 * xsol2)) * crossx;

            sol1 = new Vector3(xsol1, ysol1, zsol1);
            sol2 = new Vector3(xsol2, ysol2, zsol2);
        }

        // Solves for the two other components of the solution given the Y-component
        private static void calcConePlaneXZ(float x1, float x2, float y1, float y2, float z1, float z2, float ysol1, float ysol2, float crossy, float cosCone, out Vector3 sol1, out Vector3 sol2)
        {
            crossy = 1.0f / crossy;
            float xsol1 = -(z1 * y2 * ysol1 + z2 * (cosCone - y1 * ysol1)) * crossy;
            float zsol1 = (x1 * y2 * ysol1 + x2 * (cosCone - y1 * ysol1)) * crossy;
            float xsol2 = -(z1 * y2 * ysol2 + z2 * (cosCone - y1 * ysol2)) * crossy;
            float zsol2 = (x1 * y2 * ysol2 + x2 * (cosCone - y1 * ysol2)) * crossy;

            sol1 = new Vector3(xsol1, ysol1, zsol1);
            sol2 = new Vector3(xsol2, ysol2, zsol2);
        }

        // Solves for the two other components of the solution given the Z-component
        private static void calcConePlaneXY(float x1, float x2, float y1, float y2, float z1, float z2, float zsol1, float zsol2, float crossz, float cosCone, out Vector3 sol1, out Vector3 sol2)
        {
            crossz = 1.0f / crossz;
            float xsol1 = (y1 * z2 * zsol1 + y2 * (cosCone - z1 * zsol1)) * crossz;
            float ysol1 = -(x1 * z2 * zsol1 + x2 * (cosCone - z1 * zsol1)) * crossz;
            float xsol2 = (y1 * z2 * zsol2 + y2 * (cosCone - z1 * zsol2)) * crossz;
            float ysol2 = -(x1 * z2 * zsol2 + x2 * (cosCone - z1 * zsol2)) * crossz;

            sol1 = new Vector3(xsol1, ysol1, zsol1);
            sol2 = new Vector3(xsol2, ysol2, zsol2);
        }
    }
}
