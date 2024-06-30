using UnityEngine;

using BDArmory.Control;
using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.Utils;

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
        public static MissileLaunchParams GetDynamicLaunchParams(MissileBase missile, Vector3 targetVelocity, Vector3 targetPosition, float maxAngleOffTarget = -1, bool unguidedGuidedMissile = false)
        {
            if (missile == null || missile.part == null) return new MissileLaunchParams(0, 0); // Safety check in case the missile part is being destroyed at the same time.
            Vector3 launcherVelocity = missile.vessel.Velocity();
            Vector3 launcherPosition = missile.part.transform.position;
            Vector3 vectorToTarget = targetPosition - launcherPosition;
            if (maxAngleOffTarget >= 0) { launcherVelocity = Vector3.RotateTowards(vectorToTarget, launcherVelocity, maxAngleOffTarget, 0); }

            bool surfaceLaunch = missile.vessel.LandedOrSplashed;
            float minLaunchRange = Mathf.Max(missile.minStaticLaunchRange, missile.GetEngagementRangeMin());
            float maxLaunchRange = missile.GetEngagementRangeMax();
            if (unguidedGuidedMissile) maxLaunchRange /= 10;
            
            // For missiles with static max launch range enabled, or when in space, bypass DLZ calc and just return static ranges
            if (missile.UseStaticMaxLaunchRange || missile.vessel.InNearVacuum())
                return new MissileLaunchParams(Mathf.Clamp(minLaunchRange, 0, BDArmorySettings.MAX_ENGAGEMENT_RANGE), Mathf.Clamp(maxLaunchRange, 0, BDArmorySettings.MAX_ENGAGEMENT_RANGE));


            float bodyGravity = (float)PhysicsGlobals.GravitationalAcceleration * (float)missile.vessel.orbit.referenceBody.GeeASL; // Set gravity for calculations;
            float missileActiveTime = 2f;
            float rangeAddMax = 0;
            float relSpeed;
            float missileMaxRangeTime = 8; //placeholder value for MMGs
            // Calculate relative speed
            Vector3 relV = targetVelocity - launcherVelocity;
            Vector3 relVProjected = Vector3.Project(relV, vectorToTarget);
            relSpeed = -Mathf.Sign(Vector3.Dot(relVProjected, vectorToTarget)) * relVProjected.magnitude; // Positive value when targets are closing on each other, negative when they are flying apart
            if (missile.GetComponent<BDModularGuidance>() == null)
            {
                // Basic time estimate for missile to drop and travel a safe distance from vessel assuming constant acceleration and firing vessel not accelerating
                MissileLauncher ml = missile.GetComponent<MissileLauncher>();
                float maxMissileAccel = ml.thrust / missile.part.mass;
                float blastRadius = Mathf.Min(missile.GetBlastRadius(), 150f); // Allow missiles with absurd blast ranges to still be launched if desired
                missileActiveTime = Mathf.Min((surfaceLaunch ? 0f : missile.dropTime) + BDAMath.Sqrt(2 * blastRadius / maxMissileAccel), 2f); // Clamp at 2s for now
                Vector3 missileFwd = missile.GetForwardTransform();
                if (maxAngleOffTarget >= 0) { missileFwd = Vector3.RotateTowards(vectorToTarget, missileFwd, maxAngleOffTarget, 0); }

                if ((Vector3.Dot(vectorToTarget, missileFwd) < 0.965f) || ((!surfaceLaunch) && (missile.GetWeaponClass() != WeaponClasses.SLW) && (ml.guidanceActive))) // Only evaluate missile turning ability if the target is outside ~15 deg cone, or isn't a torpedo and has guidance
                {
                    // Rough range estimate of max missile G in a turn after launch, the following code is quite janky but works decently well in practice
                    float maxEstimatedGForce = Mathf.Max(bodyGravity * ml.maxTorque, 15f); // Rough estimate of max G based on missile torque, use minimum of 15G to prevent some VLS parts from not working
                    if (ml.aero) // If missile has aerodynamics, modify G force by AoA limit
                    {
                        maxEstimatedGForce *= Mathf.Sin(ml.maxAoA * Mathf.Deg2Rad);
                    }

                    // Rough estimate of turning radius and arc length to travel
                    float futureTime = Mathf.Clamp((surfaceLaunch ? 0f : missile.dropTime), 0f, 2f);
                    Vector3 futureRelPosition = (targetPosition + targetVelocity * futureTime) - (launcherPosition + launcherVelocity * futureTime);
                    float missileTurnRadius = (ml.optimumAirspeed * ml.optimumAirspeed) / maxEstimatedGForce;
                    float targetAngle = Vector3.Angle(missileFwd, futureRelPosition);
                    float arcLength = Mathf.Deg2Rad * targetAngle * missileTurnRadius;

                    // Add additional range term for the missile to manuever to target at missileActiveTime
                    minLaunchRange = Mathf.Max(arcLength, minLaunchRange);
                }
                missileMaxRangeTime = Mathf.Min(Vector3.Distance(targetPosition, launcherPosition), missile.maxStaticLaunchRange) / ml.optimumAirspeed;
            }
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
    }
}
