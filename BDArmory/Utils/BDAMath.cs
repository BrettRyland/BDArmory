using System;
using UnityEngine;
using System.Runtime.CompilerServices;

namespace BDArmory.Utils
{
    public static class BDAMath
    {
        public static float RangedProbability(float[] probs)
        {
            float total = 0;
            foreach (float elem in probs)
            {
                total += elem;
            }

            float randomPoint = UnityEngine.Random.value * total;

            for (int i = 0; i < probs.Length; i++)
            {
                if (randomPoint < probs[i])
                {
                    return i;
                }
                else
                {
                    randomPoint -= probs[i];
                }
            }
            return probs.Length - 1;
        }

        public static bool Between(this float num, float lower, float upper, bool inclusive = true)
        {
            return inclusive
                ? lower <= num && num <= upper
                : lower < num && num < upper;
        }

        public static Vector3 ProjectOnPlane(in Vector3 point, in Vector3 planePoint, Vector3 planeNormal)
        {
            planeNormal = planeNormal.normalized;

            Plane plane = new Plane(planeNormal, planePoint);
            float distance = plane.GetDistanceToPoint(point);

            return point - (distance * planeNormal);
        }

        [Obsolete("Use -VectorUtils.GetAngleOnPlane(fromDirection, toDirection, referenceRight) instead.")]
        public static float SignedAngle(in Vector3 fromDirection, in Vector3 toDirection, in Vector3 referenceRight)
        {
            float angle = VectorUtils.Angle(fromDirection, toDirection);
            float sign = Mathf.Sign(Vector3.Dot(toDirection, referenceRight));
            float finalAngle = sign * angle;
            return finalAngle;
        }

        public static float RoundToUnit(float value, float unit = 1f)
        {
            var rounded = Mathf.Round(value / unit) * unit;
            return (unit % 1 != 0) ? rounded : Mathf.Round(rounded); // Fix near-integer loss of precision.
        }

        // This is a fun workaround for M1-chip Macs (Apple Silicon). Specific issue the workaround is for is here: 
        // https://issuetracker.unity3d.com/issues/m1-incorrect-calculation-of-values-using-multiplication-with-mathf-dot-sqrt-when-an-unused-variable-is-declared
        public static float Sqrt(float value) => (UI.BDArmorySetup.AppleSilicon) ? SqrtARM(value) : (float)System.Math.Sqrt((double)value);

        private static float SqrtARM(float value)
        {
            float sqrt = (float)System.Math.Sqrt((double)value);
            float sqrt1 = 1f * sqrt;
            return sqrt1;
        }

        /// <summary>
        /// Solve quadratic of a•t²+v•t=d for t, where acceleration (a) and distance (d) are assumed to be non-negative and v is the speed in the direction of the target.
        /// </summary>
        /// <param name="distance"></param>
        /// <param name="acceleration"></param>
        /// <param name="vel"></param>
        /// <returns></returns>
        public static float SolveTime(float distance, float acceleration, float vel = 0)
        {
            if (acceleration == 0f)
            {
                if (vel == 0)
                    return float.MaxValue;
                else
                    return Mathf.Abs(distance) / vel;
            }
            else
            {
                float a = 0.5f * acceleration;
                float b = vel;
                float c = -Mathf.Abs(distance);

                float x = (-b + BDAMath.Sqrt(b * b - 4 * a * c)) / (2 * a);

                return x;
            }
        }

        /// <summary>
        /// A double version of Mathf.Clamp.
        /// </summary>
        public static double Clamp(double value, double min, double max)
        {
            return Math.Min(Math.Max(value, min), max);  // This is about 50% faster than using conditionals. Oddly, the same doesn't apply for float, which is considerably slower.
            // return value < min ? min : value > max ? max : value;
        }

        /// <summary>
        /// Equivalent to Mathf.Approximately, but includes a third value used for computing the tolerance.
        /// This is useful for comparing changing values a and b when they're small relative to an original value c.
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <param name="c"></param>
        /// <returns></returns>
        public static bool Approximately(float a, float b, float c = 0f)
        {
            return Mathf.Abs(b - a) < Mathf.Max(1E-06f * Mathf.Max(Mathf.Abs(a), Mathf.Abs(b), Mathf.Abs(c)), Mathf.Epsilon * 8f);
        }
    }
}
