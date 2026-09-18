using UnityEngine;
using System.Runtime.CompilerServices;

using BDArmory.Utils;

namespace BDArmory.Extensions
{
    public static class VectorExtensions
    {
        /// <summary>
        /// Project a vector onto a plane defined by the plane normal (pre-normalized) and return the dot product.
        /// 
        /// This implementation assumes that the plane normal is already normalized,
        /// skipping such checks and normalization that Vector3.ProjectOnPlane does,
        /// which gives a speed-up by a factor of approximately 1.7.
        /// </summary>
        /// <param name="vector">The vector to project.</param>
        /// <param name="planeNormal">The plane normal (pre-normalized).</param>
        /// <returns>The dot product and the projected vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (float, Vector3) DotProjectOnPlanePreNormalized(this in Vector3 vector, in Vector3 planeNormal)
        {
            float dot = Vector3.Dot(vector, planeNormal);
            return (dot, new Vector3(
                vector.x - planeNormal.x * dot,
                vector.y - planeNormal.y * dot,
                vector.z - planeNormal.z * dot));
        }

        /// <summary>
        /// Project a vector onto a plane defined by the plane normal (pre-normalized).
        /// 
        /// This implementation assumes that the plane normal is already normalized,
        /// skipping such checks and normalization that Vector3.ProjectOnPlane does,
        /// which gives a speed-up by a factor of approximately 1.7.
        /// </summary>
        /// <param name="vector">The vector to project.</param>
        /// <param name="planeNormal">The plane normal (pre-normalized).</param>
        /// <returns>The projected vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ProjectOnPlanePreNormalized(this in Vector3 vector, in Vector3 planeNormal)
        {
            var dot = Vector3.Dot(vector, planeNormal);
            return new Vector3(
                vector.x - planeNormal.x * dot,
                vector.y - planeNormal.y * dot,
                vector.z - planeNormal.z * dot);
        }

        /// <summary>
        /// Overload for Vector3d, returns Vector3.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ProjectOnPlanePreNormalized(this in Vector3d vector, in Vector3 planeNormal)
        {
            var dot = Vector3.Dot(vector, planeNormal);
            return new Vector3(
                (float)vector.x - planeNormal.x * dot,
                (float)vector.y - planeNormal.y * dot,
                (float)vector.z - planeNormal.z * dot);
        }

        /// <summary>
        /// Overload for Vector3d, returns Vector3.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ProjectOnPlanePreNormalized(this in Vector3d vector, in Vector3d planeNormal)
        {
            var dot = Vector3d.Dot(vector, planeNormal);
            return new Vector3(
                (float)(vector.x - planeNormal.x * dot),
                (float)(vector.y - planeNormal.y * dot),
                (float)(vector.z - planeNormal.z * dot));
        }

        /// <summary>
        /// Project a vector onto a plane defined by the plane normal (not-necessarily normalized).
        /// 
        /// This implementation is the same as the Unity reference implementation,
        /// but with an extra optimisation to reduce the number of division operations to 1.
        /// </summary>
        /// <param name="vector">The vector to project.</param>
        /// <param name="planeNormal">The plane normal.</param>
        /// <returns>The projected vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ProjectOnPlane(this in Vector3 vector, in Vector3 planeNormal)
        {
            var sqrMag = Vector3.Dot(planeNormal, planeNormal);
            if (sqrMag < Mathf.Epsilon) return vector;
            var dotNorm = Vector3.Dot(vector, planeNormal) / sqrMag;
            return new Vector3(
                vector.x - planeNormal.x * dotNorm,
                vector.y - planeNormal.y * dotNorm,
                vector.z - planeNormal.z * dotNorm);
        }

        /// <summary>
        /// Overload for Vector3d, returns Vector3.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ProjectOnPlane(this in Vector3d vector, in Vector3 planeNormal)
        {
            var sqrMag = Vector3.Dot(planeNormal, planeNormal);
            if (sqrMag < Mathf.Epsilon) return vector;
            var dotNorm = Vector3.Dot(vector, planeNormal) / sqrMag;
            return new Vector3(
                (float)vector.x - planeNormal.x * dotNorm,
                (float)vector.y - planeNormal.y * dotNorm,
                (float)vector.z - planeNormal.z * dotNorm);
        }

        /// <summary>
        /// A (2x) faster Vector3.Dot(v1.normalized, v2.normalized) by only performing a single sqrt and division.
        /// The vectors do not need normalising beforehand.
        /// </summary>
        /// <param name="v1">A vector</param>
        /// <param name="v2">Another vector</param>
        /// <returns>The dot product between the normalised vectors.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DotNormalized(this in Vector3 v1, in Vector3 v2)
        {
            var normalisationFactor = BDAMath.Sqrt(v1.sqrMagnitude * v2.sqrMagnitude);
            return normalisationFactor > 0 ? Vector3.Dot(v1, v2) / normalisationFactor : 0;
        }

        /// <summary>
        /// Efficient replacement for Vector3.Distance(from, to) < distance.
        /// Intel and AMD appear to support sqrt in hardware, but M1 Macs don't.
        /// This is the most efficient version from benchmarks and gives the cleanest code.
        /// </summary>
        /// <param name="v"></param>
        /// <param name="to"></param>
        /// <param name="distance"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CloserToThan(this in Vector3 v, in Vector3 to, float distance)
        {
            return (v - to).sqrMagnitude < distance * distance;
        }
        /// <summary>
        /// Efficient replacement for Vector3.Distance(from, to) > distance.
        /// </summary>
        /// <param name="v"></param>
        /// <param name="from"></param>
        /// <param name="distance"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool FurtherFromThan(this in Vector3 v, in Vector3 from, float distance)
        {
            return (v - from).sqrMagnitude > distance * distance;
        }

        /// <summary>
        /// Decompose a vector into a magnitude and normalized direction.
        /// This performs the same operations as v.normalized but also returns the magnitude, which is often needed.
        /// </summary>
        /// <param name="v">The vector to decompose.</param>
        /// <returns>The magnitude and normalized unit vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (float, Vector3) MagNorm(this in Vector3 v)
        {
            float mag = v.magnitude;
            if (mag > Vector3.kEpsilon)
                return (mag, v / mag);
            else
                return (mag, Vector3.zero);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (double, Vector3d) MagNorm(this in Vector3d v)
        {
            double mag = v.magnitude;
            if (mag > Vector3.kEpsilon)
                return (mag, v / mag);
            else
                return (mag, Vector3.zero);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (float, Vector2) MagNorm(this in Vector2 v)
        {
            float mag = v.magnitude;
            if (mag > Vector2.kEpsilon)
                return (mag, v / mag);
            else
                return (mag, Vector2.zero);
        }

        /// <summary>
        /// Check if any of the vector elements are NaN.
        /// </summary>
        /// <param name="v">A Vector3.</param>
        /// <returns>True if any of the elements are NaN.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNaN(this in Vector3 v)
        { return float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z); }
        /// <summary>
        /// Check if any of the quaternion elements are NaN.
        /// </summary>
        /// <param name="q">A Quaternion</param>
        /// <returns>True if any of the elements are NaN.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNaN(this in Quaternion q) // Techinically not a Vector3 extension, but it fits here.
        { return float.IsNaN(q.w) || float.IsNaN(q.x) || float.IsNaN(q.y) || float.IsNaN(q.z); }

        /// <summary>
        /// Check if any of the vector elements are Inf.
        /// </summary>
        /// <param name="v">A Vector3.</param>
        /// <returns>True if any of the elements are Inf.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInf(this in Vector3 v)
        { return float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z); }
        /// <summary>
        /// Check if any of the quaternion elements are Inf.
        /// </summary>
        /// <param name="q">A Quaternion</param>
        /// <returns>True if any of the elements are Inf.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInf(this in Quaternion q) // Techinically not a Vector3 extension, but it fits here.
        { return float.IsInfinity(q.w) || float.IsInfinity(q.x) || float.IsInfinity(q.y) || float.IsInfinity(q.z); }

        // Combined methods for convenience.
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsInfOrNaN(this in Vector3 v) => v.IsNaN() || v.IsInf();
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsInfOrNaN(this in Quaternion q) => q.IsNaN() || q.IsInf();
    }
}