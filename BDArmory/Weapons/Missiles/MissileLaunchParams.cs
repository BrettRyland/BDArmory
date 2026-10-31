using UnityEngine;

using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.Utils;
using System;

namespace BDArmory.Weapons.Missiles
{
    public struct MissileLaunchParams
    {
        public float minLaunchRange;
        public float maxLaunchRange;

        private float rtr;

        /// <summary>
        /// Gets the maximum no-escape range.
        /// </summary>
        /// <value>The max no-escape range.</value>
        public float rangeTr
        {
            get
            {
                return rtr;
            }
        }

        public MissileLaunchParams(float min, float max)
        {
            minLaunchRange = min;
            maxLaunchRange = max;
            rtr = (max + min) / 2;
        }

        /// <summary>
        /// Gets the dynamic launch parameters.
        /// </summary>
        /// <returns>The dynamic launch parameters.</returns>
        /// <param name="launcherVelocity">Launcher velocity.</param>
        /// <param name="targetVelocity">Target velocity.</param>
        /// <param name="targetPosition">Target position.</param>
        /// <param name="maxAngleOffTarget">If non-negative, restrict the calculations to assuming the launcher velocity is at most this angle off-target. Avoids extreme extending ranges.</param>
        public static MissileLaunchParams GetDynamicLaunchParams(MissileBase missile, Vector3d targetVelocity, Vector3d targetPosition, float maxAngleOffTarget = -1, bool unguidedGuidedMissile = false)
        {
            if (missile == null || missile.part == null) return new MissileLaunchParams(0, 0); // Safety check in case the missile part is being destroyed at the same time.
            Vector3d launcherVelocity = missile.vessel.Velocity();
            Vector3d launcherPosition = missile.part.transform.position;
            Vector3d vectorToTarget = (targetPosition - launcherPosition).normalized;
            if (maxAngleOffTarget >= 0) { launcherVelocity = VectorUtils.RotateTowards(vectorToTarget, launcherVelocity, maxAngleOffTarget * BDAMath.Deg2Rad) * launcherVelocity.magnitude; }

            bool surfaceLaunch = missile.vessel.LandedOrSplashed;
            float minLaunchRange = Mathf.Max(missile.minStaticLaunchRange, missile.GetEngagementRangeMin());
            float maxLaunchRange = missile.GetEngagementRangeMax();
            if (unguidedGuidedMissile) maxLaunchRange /= 10;

            // For missiles in space, bypass DLZ calc and just return static ranges
            if (missile.vessel.InNearVacuum())
                return new MissileLaunchParams(Mathf.Clamp(minLaunchRange, 0, BDArmorySettings.MAX_ENGAGEMENT_RANGE), Mathf.Clamp(maxLaunchRange, 0, BDArmorySettings.MAX_ENGAGEMENT_RANGE));


            double bodyGravity = PhysicsGlobals.GravitationalAcceleration * missile.vessel.orbit.referenceBody.GeeASL; // Set gravity for calculations;
            float missileActiveTime = GetMissileActiveTime(missile, surfaceLaunch);
            float rangeAddMax = 0;
            float relSpeed;
            float missileMaxRangeTime = 8; //placeholder value for MMGs
            // Calculate relative speed
            Vector3d relV = targetVelocity - launcherVelocity;
            Vector3d relVProjected = Vector3d.Project(relV, vectorToTarget);
            relSpeed = (float)(-Math.Sign(Vector3d.Dot(relVProjected, vectorToTarget)) * relVProjected.magnitude); // Positive value when targets are closing on each other, negative when they are flying apart
            if (missile.GetComponent<BDModularGuidance>() == null)
            {
                // Basic time estimate for missile to drop and travel a safe distance from vessel assuming constant acceleration and firing vessel not accelerating
                MissileLauncher ml = missile.GetComponent<MissileLauncher>();
                Vector3d missileFwd = missile.GetForwardTransform();
                if (maxAngleOffTarget >= 0) { missileFwd = VectorUtils.RotateTowards(vectorToTarget, missileFwd, maxAngleOffTarget * BDAMath.Deg2Rad); }

                if ((Vector3d.Dot(vectorToTarget, missileFwd) < 0.965f) && (!surfaceLaunch && (missile.GetWeaponClass() != WeaponClasses.SLW) && ml.guidanceActive)) // Only evaluate missile turning ability if the target is outside ~15 deg cone, or isn't a torpedo and has guidance
                {
                    // Rough range estimate of max missile G in a turn after launch, the following code is quite janky but works decently well in practice
                    double maxEstimatedGForce = Math.Max(bodyGravity * ml.currMaxTorque, 15d); // Rough estimate of max G based on missile torque, use minimum of 15G to prevent some VLS parts from not working
                    if (ml.aero) // If missile has aerodynamics, modify G force by AoA limit
                    {
                        maxEstimatedGForce *= Math.Sin(ml.maxAoA * BDAMath.Deg2Rad);
                    }

                    // Rough estimate of turning radius and arc length to travel
                    double futureTime = BDAMath.Clamp((surfaceLaunch ? 0d : missile.dropTime), 0d, 2d);
                    Vector3d futureRelPosition = (targetPosition + targetVelocity * futureTime) - (launcherPosition + launcherVelocity * futureTime);
                    double missileTurnRadius = (ml.optimumAirspeed * ml.optimumAirspeed) / maxEstimatedGForce;
                    float targetAngle = VectorUtils.Angle(missileFwd, futureRelPosition);
                    float arcLength = Mathf.Deg2Rad * targetAngle * (float)missileTurnRadius;

                    // Add additional range term for the missile to manuever to target at missileActiveTime
                    minLaunchRange = Mathf.Max(arcLength, minLaunchRange);
                }
                missileMaxRangeTime = Mathf.Min(Vector3.Distance(targetPosition, launcherPosition), missile.maxStaticLaunchRange) / ml.optimumAirspeed;
            }
            //For missiles with static max launch range enabled, use static max range, but grab min range adjusted by predicted missile kinematics
            if (missile.UseStaticMaxLaunchRange || missile.vessel.InNearVacuum())
                return new MissileLaunchParams(Mathf.Clamp(minLaunchRange, 0, BDArmorySettings.MAX_ENGAGEMENT_RANGE), Mathf.Clamp(maxLaunchRange, 0, BDArmorySettings.MAX_ENGAGEMENT_RANGE));

            // Adjust ranges
            minLaunchRange = Mathf.Min(minLaunchRange + relSpeed * missileActiveTime, minLaunchRange);
            rangeAddMax += relSpeed * missileMaxRangeTime;

            // Add altitude term to max
            double diffAlt = missile.vessel.altitude - FlightGlobals.getAltitudeAtPos(targetPosition);
            rangeAddMax += (float)diffAlt;

            float min = Mathf.Clamp(minLaunchRange, 0, BDArmorySettings.MAX_ENGAGEMENT_RANGE);
            float max = Mathf.Clamp(maxLaunchRange + rangeAddMax, 0, BDArmorySettings.MAX_ENGAGEMENT_RANGE);
            return new MissileLaunchParams(min, max);
        }
        public static float GetMissileActiveTime(MissileBase missile, bool surfaceLaunch)
        {
            float missileActiveTime = surfaceLaunch ? 0f : missile.dropTime;
            if (missile.GetComponent<BDModularGuidance>() == null)
            {
                MissileLauncher ml = missile.GetComponent<MissileLauncher>();
                float maxMissileAccel = ml.thrust / missile.part.mass;
                float blastRadius = Mathf.Min(missile.GetBlastRadius(), 150f); // Allow missiles with absurd blast ranges to still be launched if desired
                missileActiveTime += BDAMath.Sqrt(2 * blastRadius / maxMissileAccel);
            }
            return Mathf.Clamp(missileActiveTime, 0f, missile.dropTime + 2f); // Clamp at (drop time + 2s)
        }
    }
}
