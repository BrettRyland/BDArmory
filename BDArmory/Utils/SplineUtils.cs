using UnityEngine;

namespace BDArmory.Utils
{
	public static class SplineUtils
	{
		// TODO Add checks for when time-deltas are really small that might give NaNs/Infs and handle them gracefully
		#region Vector3
		/// <summary>
		/// Evaluate the function p(x) = h00(t)*p0 + h10(t)*(x1-x0)*m0 + h01(t)*p1 + h11(t)*(x1-x0)*m1, where
		/// 		h00, h10, h01, h11 are the Hermite polynomial basis functions,
		/// 		p0, p1, m0, m1 are the points and their slopes,
		/// 		t is the normalised distance [0,1] between the points (x0 and x1) to perform the interpolation.
		/// 		https://en.wikipedia.org/wiki/Cubic_Hermite_spline#Interpolation_on_an_arbitrary_interval
		/// <param name="point1">The start point</param>
		/// <param name="slope1">The slope at the start point</param>
		/// <param name="point2">The end point</param>
		/// <param name="slope2">The slope at the end point</param>
		/// <param name="t">The point in the interval (between tStart and tStop) to evaluate the function</param>
		/// <param name="tStart">The start of the interval</param>
		/// <param name="tStop">The end of the interval</param>
		/// <returns></returns>
		public static Vector3 EvaluateSpline(in Vector3 point1, in Vector3 slope1, in Vector3 point2, in Vector3 slope2, float t, float tStart, float tStop)
		{
			var dt = tStop - tStart;
			t = Mathf.Clamp01((t - tStart) / dt); // Rescale the t paramter and enforce that it is in the correct range.
			var t2 = t * t;
			var t3 = t2 * t;
			var h00 = 2 * t3 - 3 * t2 + 1;
			var h10 = t3 - 2 * t2 + t;
			var h01 = -2 * t3 + 3 * t2;
			var h11 = t3 - t2;
			return h00 * point1 + h10 * dt * slope1 + h01 * point2 + h11 * dt * slope2;
		}

		/// <summary>
		/// Slope estimation using 3-point finite difference.
		/// https://en.wikipedia.org/wiki/Cubic_Hermite_spline#Finite_difference
		/// </summary>
		/// <param name="point0"></param>
		/// <param name="point1"></param>
		/// <param name="point2"></param>
		/// <param name="dt01"></param>
		/// <param name="dt12"></param>
		/// <returns></returns>
		public static Vector3 EstimateSlope(in Vector3 point0, in Vector3 point1, in Vector3 point2, float dt01 = 0, float dt12 = 0)
		{
			// If the time deltas between the points aren't specified, treat the time parameter as distance.
			if (dt01 == 0) dt01 = (point1 - point0).magnitude;
			if (dt12 == 0) dt12 = (point2 - point1).magnitude;
			return 0.5f * ((point2 - point1) / dt12 + (point1 - point0) / dt01);
		}

		/// <summary>
		/// Slope estimation using 2-point finite difference.
		/// </summary>
		/// <param name="point0"></param>
		/// <param name="point1"></param>
		/// <param name="dt"></param>
		/// <returns></returns>
		public static Vector3 EstimateSlope(in Vector3 point0, in Vector3 point1, float dt = 0)
		{
			// If the time delta between the points isn't specified, treat the time parameter as distance.
			if (dt == 0) dt = (point1 - point0).magnitude;
			return (point1 - point0) / dt;
		}
		#endregion
	}
}