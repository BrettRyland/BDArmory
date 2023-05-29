using System;
using UnityEngine;

using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.Utils;
using BDArmory.Weapons.Missiles;

namespace BDArmory.Guidances
{
    public class MissileGuidance
    {
        public static Vector3 GetAirToGroundTarget(Vector3 targetPosition, Vector3 targetVelocity, Vessel missileVessel, float descentRatio, float minSpeed = 200)
        {
            // Incorporate lead for target velocity
            Vector3 currVel = Mathf.Max((float)missileVessel.srfSpeed, minSpeed) * missileVessel.Velocity().normalized;
            float targetDistance = Vector3.Distance(targetPosition, missileVessel.transform.position);
            float leadTime = Mathf.Clamp((float)(1 / ((targetVelocity - currVel).magnitude / targetDistance)), 0f, 8f);
            targetPosition += targetVelocity * leadTime;

            Vector3 upDirection = VectorUtils.GetUpDirection(missileVessel.CoM);
            //-FlightGlobals.getGeeForceAtPosition(targetPosition).normalized;
            Vector3 surfacePos = missileVessel.transform.position +
                                 Vector3.Project(targetPosition - missileVessel.transform.position, upDirection);
            //((float)missileVessel.altitude*upDirection);
            Vector3 targetSurfacePos;

            targetSurfacePos = targetPosition;

            float distanceToTarget = Vector3.Distance(surfacePos, targetSurfacePos);

            if (missileVessel.srfSpeed < 75 && missileVessel.verticalSpeed < 10)
            //gain altitude if launching from stationary
            {
                return missileVessel.transform.position + (5 * missileVessel.transform.forward) + (1 * upDirection);
            }

            float altitudeClamp = Mathf.Clamp(
                (distanceToTarget - ((float)missileVessel.srfSpeed * descentRatio)) * 0.22f, 0,
                (float)missileVessel.altitude);

            //Debug.Log("[BDArmory.MissileGuidance]: AGM altitudeClamp =" + altitudeClamp);
            Vector3 finalTarget = targetPosition + (altitudeClamp * upDirection.normalized);

            //Debug.Log("[BDArmory.MissileGuidance]: Using agm trajectory. " + Time.time);

            return finalTarget;
        }

        public static bool GetBallisticGuidanceTarget(Vector3 targetPosition, Vessel missileVessel, bool direct,
            out Vector3 finalTarget)
        {
            Vector3 up = VectorUtils.GetUpDirection(missileVessel.transform.position);
            Vector3 forward = (targetPosition - missileVessel.transform.position).ProjectOnPlanePreNormalized(up);
            float speed = (float)missileVessel.srfSpeed;
            float sqrSpeed = speed * speed;
            float sqrSpeedSqr = sqrSpeed * sqrSpeed;
            float g = (float)FlightGlobals.getGeeForceAtPosition(missileVessel.transform.position).magnitude;
            float height = FlightGlobals.getAltitudeAtPos(targetPosition) -
                           FlightGlobals.getAltitudeAtPos(missileVessel.transform.position);
            float sqrRange = forward.sqrMagnitude;
            float range = BDAMath.Sqrt(sqrRange);

            float plusOrMinus = direct ? -1 : 1;

            float top = sqrSpeed + (plusOrMinus * BDAMath.Sqrt(sqrSpeedSqr - (g * ((g * sqrRange + (2 * height * sqrSpeed))))));
            float bottom = g * range;
            float theta = Mathf.Atan(top / bottom);

            if (!float.IsNaN(theta))
            {
                Vector3 finalVector = Quaternion.AngleAxis(theta * Mathf.Rad2Deg, Vector3.Cross(forward, up)) * forward;
                finalTarget = missileVessel.transform.position + (100 * finalVector);
                return true;
            }
            else
            {
                finalTarget = Vector3.zero;
                return false;
            }
        }

        public static bool GetBallisticGuidanceTarget(Vector3 targetPosition, Vector3 missilePosition,
            float missileSpeed, bool direct, out Vector3 finalTarget)
        {
            Vector3 up = VectorUtils.GetUpDirection(missilePosition);
            Vector3 forward = (targetPosition - missilePosition).ProjectOnPlanePreNormalized(up);
            float speed = missileSpeed;
            float sqrSpeed = speed * speed;
            float sqrSpeedSqr = sqrSpeed * sqrSpeed;
            float g = (float)FlightGlobals.getGeeForceAtPosition(missilePosition).magnitude;
            float height = FlightGlobals.getAltitudeAtPos(targetPosition) -
                           FlightGlobals.getAltitudeAtPos(missilePosition);
            float sqrRange = forward.sqrMagnitude;
            float range = BDAMath.Sqrt(sqrRange);

            float plusOrMinus = direct ? -1 : 1;

            float top = sqrSpeed + (plusOrMinus * BDAMath.Sqrt(sqrSpeedSqr - (g * ((g * sqrRange + (2 * height * sqrSpeed))))));
            float bottom = g * range;
            float theta = Mathf.Atan(top / bottom);

            if (!float.IsNaN(theta))
            {
                Vector3 finalVector = Quaternion.AngleAxis(theta * Mathf.Rad2Deg, Vector3.Cross(forward, up)) * forward;
                finalTarget = missilePosition + (100 * finalVector);
                return true;
            }
            else
            {
                finalTarget = Vector3.zero;
                return false;
            }
        }

        public static Vector3 GetBeamRideTarget(Ray beam, Vector3 currentPosition, Vector3 currentVelocity,
            float correctionFactor, float correctionDamping, Ray previousBeam)
        {
            float onBeamDistance = Vector3.Project(currentPosition - beam.origin, beam.direction).magnitude;
            //Vector3 onBeamPos = beam.origin+Vector3.Project(currentPosition-beam.origin, beam.direction);//beam.GetPoint(Vector3.Distance(Vector3.Project(currentPosition-beam.origin, beam.direction), Vector3.zero));
            Vector3 onBeamPos = beam.GetPoint(onBeamDistance);
            Vector3 previousBeamPos = previousBeam.GetPoint(onBeamDistance);
            Vector3 beamVel = (onBeamPos - previousBeamPos) / Time.fixedDeltaTime;
            Vector3 target = onBeamPos + (500f * beam.direction);
            Vector3 offset = onBeamPos - currentPosition;
            offset += beamVel * 0.5f;
            target += correctionFactor * offset;

            Vector3 velDamp = correctionDamping * (currentVelocity - beamVel).ProjectOnPlanePreNormalized(beam.direction);
            target -= velDamp;

            return target;
        }

        public static Vector3 GetAirToAirTarget(Vector3 targetPosition, Vector3 targetVelocity,
            Vector3 targetAcceleration, Vessel missileVessel, out float timeToImpact, float minSpeed = 200)
        {
            float leadTime = 0;
            float targetDistance = Vector3.Distance(targetPosition, missileVessel.transform.position);

            Vector3 currVel = Mathf.Max((float)missileVessel.srfSpeed, minSpeed) * missileVessel.Velocity().normalized;

            leadTime = (float)(1 / ((targetVelocity - currVel).magnitude / targetDistance));
            timeToImpact = leadTime;
            leadTime = Mathf.Clamp(leadTime, 0f, 8f);

            return targetPosition + (targetVelocity * leadTime);
        }

        /*public static Vector3 GetAirToAirLoftTarget(Vector3 targetPosition, Vector3 targetVelocity,
            Vector3 targetAcceleration, Vessel missileVessel, float targetAlt, float maxAltitude,
            float rangeFactor, float altComp, float velComp, float loftAngle, float termAngle,
            float termDist, ref int loftState, out float timeToImpact, out float targetDistance,
            float minSpeed = 200)*/
        public static Vector3 GetAirToAirLoftTarget(Vector3 targetPosition, Vector3 targetVelocity,
            Vector3 targetAcceleration, Vessel missileVessel, float targetAlt, float maxAltitude,
            float rangeFactor, float vertVelComp, float velComp, float loftAngle, float termAngle,
            float termDist, ref int loftState, out float timeToImpact, out float targetDistance,
            bool LoftUseAPN, float N, float minSpeed = 200)
        {

            Vector3 velDirection = missileVessel.srf_vel_direction; //missileVessel.Velocity().normalized;

            targetDistance = Vector3.Distance(targetPosition, missileVessel.transform.position);

            float currSpeed = Mathf.Max((float)missileVessel.srfSpeed, minSpeed);
            Vector3 currVel = currSpeed * velDirection;

            //Vector3 Rdir = (targetPosition - missileVessel.transform.position).normalized;
            //float rDot = Vector3.Dot(targetVelocity - currVel, Rdir);

            float leadTime = (float)(1 / ((targetVelocity - currVel).magnitude / targetDistance));
            //float leadTime = (targetDistance / rDot);

            timeToImpact = leadTime;
            leadTime = Mathf.Clamp(leadTime, 0f, 16f);

            // If loft is not terminal
            if ((targetDistance > termDist) && (loftState < 3))
            {
                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileGuidance]: Lofting");

                // Get up direction
                Vector3 upDirection = VectorUtils.GetUpDirection(missileVessel.CoM);

                // Use the gun aim-assist logic to determine ballistic angle (assuming no drag)
                Vector3 missileRelativePosition, missileRelativeVelocity, missileAcceleration, missileRelativeAcceleration, targetPredictedPosition, missileDropOffset, lastVelDirection, ballisticTarget, targetHorVel, targetCompVel;

                var firePosition = missileVessel.transform.position; //+ (currSpeed * velDirection) * Time.fixedDeltaTime; // Bullets are initially placed up to 1 frame ahead (iTime). Not offsetting by part vel gives the correct initial placement.
                missileRelativePosition = targetPosition - firePosition;
                float timeToCPA = timeToImpact; // Rough initial estimate.
                targetPredictedPosition = AIUtils.PredictPosition(targetPosition, targetVelocity, targetAcceleration, timeToCPA);

                // Velocity Compensation Logic
                float compMult = Mathf.Clamp(0.5f * (targetDistance - termDist) / termDist, 0f, 1f);
                Vector3 velDirectionHor = (velDirection.ProjectOnPlanePreNormalized(upDirection)).normalized; //(velDirection - upDirection * Vector3.Dot(velDirection, upDirection)).normalized;
                targetHorVel = targetVelocity.ProjectOnPlanePreNormalized(upDirection); //targetVelocity - upDirection * Vector3.Dot(targetVelocity, upDirection); // Get target horizontal velocity (relative to missile frame)
                float targetAlVelMag = Vector3.Dot(targetHorVel, velDirectionHor); // Get magnitude of velocity aligned with the missile velocity vector (in the horizontal axis)
                targetAlVelMag *= Mathf.Sign(velComp) * compMult;
                targetAlVelMag = Mathf.Max(targetAlVelMag, 0f); //0.5f * (targetAlVelMag + Mathf.Abs(targetAlVelMag)); // Set -ve velocity (I.E. towards the missile) to 0 if velComp is +ve, otherwise for -ve

                float targetVertVelMag = Mathf.Max(0f, Mathf.Sign(vertVelComp) * compMult * Vector3.Dot(targetVelocity, upDirection));

                //targetCompVel = targetVelocity + velComp * targetHorVel.magnitude* targetHorVel.normalized; // Old velComp logic
                //targetCompVel = targetVelocity + velComp * targetAlVelMag * velDirectionHor; // New velComp logic
                targetCompVel = targetVelocity + velComp * targetAlVelMag * velDirectionHor + vertVelComp * targetVertVelMag * upDirection; // New velComp logic

                var count = 0;
                do
                {
                    lastVelDirection = velDirection;
                    currVel = currSpeed * velDirection;
                    //firePosition = missileVessel.transform.position + (currSpeed * velDirection) * Time.fixedDeltaTime; // Bullets are initially placed up to 1 frame ahead (iTime).
                    missileAcceleration = FlightGlobals.getGeeForceAtPosition((firePosition + targetPredictedPosition) / 2f); // Drag is ignored.
                    //bulletRelativePosition = targetPosition - firePosition + compMult * altComp * upDirection; // Compensate for altitude
                    missileRelativePosition = targetPosition - firePosition; // Compensate for altitude
                    missileRelativeVelocity = targetVelocity - currVel;
                    missileRelativeAcceleration = targetAcceleration - missileAcceleration;
                    timeToCPA = AIUtils.TimeToCPA(missileRelativePosition, missileRelativeVelocity, missileRelativeAcceleration, timeToImpact * 3f);
                    targetPredictedPosition = AIUtils.PredictPosition(targetPosition, targetCompVel, targetAcceleration, timeToCPA);
                    missileDropOffset = -0.5f * missileAcceleration * timeToCPA * timeToCPA;
                    ballisticTarget = targetPredictedPosition + missileDropOffset;
                    velDirection = (ballisticTarget - missileVessel.transform.position).normalized;
                } while (++count < 10 && Vector3.Angle(lastVelDirection, velDirection) > 1f); // 1° margin of error is sufficient to prevent premature firing (usually)


                // Determine horizontal and up components of velocity, calculate the elevation angle
                float velUp = Vector3.Dot(velDirection, upDirection);
                float velForwards = (velDirection - upDirection * velUp).magnitude;
                float angle = Mathf.Atan2(velUp, velForwards);

                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: Loft Angle: [{(angle * Mathf.Rad2Deg):G3}]");

                // Use simple lead compensation to minimize over-compensation
                // Get planar direction to target
                Vector3 planarDirectionToTarget =
                    ((AIUtils.PredictPosition(targetPosition, targetVelocity, targetAcceleration, leadTime + TimeWarp.fixedDeltaTime) - missileVessel.transform.position).ProjectOnPlanePreNormalized(upDirection)).normalized;

                // Check if termination angle agrees with termAngle
                if ((angle > -termAngle * Mathf.Deg2Rad) && (loftState < 2))
                {
                    /*// If not yet at termination, simple lead compensation
                    targetPosition += targetVelocity * leadTime + 0.5f * leadTime * leadTime * targetAcceleration;

                    // Get planar direction to target
                    Vector3 planarDirectionToTarget = //(velDirection - upDirection * Vector3.Dot(velDirection, upDirection)).normalized;
                        ((targetPosition - missileVessel.transform.position).ProjectOnPlanePreNormalized(upDirection)).normalized;*/

                    // Altitude clamp based on rangeFactor and maxAlt, cannot be lower than target
                    float altitudeClamp = Mathf.Clamp(targetAlt + rangeFactor * Vector3.Dot(targetPosition - missileVessel.transform.position, planarDirectionToTarget), targetAlt, Mathf.Max(maxAltitude, targetAlt));

                    // Old loft climb logic, wanted to limit turn. Didn't work well but leaving it in if I decide to fix it
                    /*if (missileVessel.altitude < (altitudeClamp - 0.5f))
                    //gain altitude if launching from stationary
                    {*/
                    //currSpeed = (float)missileVessel.Velocity().magnitude;

                    // 5g turn, v^2/r = a, v^2/(dh*(tan(45°/2)sin(45°))) > 5g, v^2/(tan(45°/2)sin(45°)) > 5g * dh, I.E. start turning when you need to pull a 5g turn,
                    // before that the required gs is lower, inversely proportional
                    /*if (loftState == 1 || (currSpeed * currSpeed * 0.2928932188134524755991556378951509607151640623115259634116f) >= (5f * (float)PhysicsGlobals.GravitationalAcceleration) * (altitudeClamp - missileVessel.altitude))
                    {*/
                    /*
                    loftState = 1;

                    // Calculate upwards and forwards velocity components
                    velUp = Vector3.Dot(missileVessel.Velocity(), upDirection);
                    velForwards = (float)(missileVessel.Velocity() - upDirection * velUp).magnitude;

                    // Derivation of relationship between dh and turn radius
                    // tan(theta/2) = dh/L, sin(theta) = L/r
                    // tan(theta/2) = sin(theta)/(1+cos(theta))
                    float turnR = (float)(altitudeClamp - missileVessel.altitude) * (currSpeed * currSpeed + currSpeed * velForwards) / (velUp * velUp);

                    float accel = Mathf.Clamp(currSpeed * currSpeed / turnR, 0, 5f * (float)PhysicsGlobals.GravitationalAcceleration);
                    */

                    // Limit climb angle by turnFactor, turnFactor goes negative when above target alt
                    float turnFactor = (float)(altitudeClamp - missileVessel.altitude) / (4f * (float)missileVessel.srfSpeed);
                    turnFactor = Mathf.Clamp(turnFactor, -1f, 1f);
                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: AAM Loft altitudeClamp: [{altitudeClamp:G6}] COS: [{Mathf.Cos(loftAngle * turnFactor * Mathf.Deg2Rad):G3}], SIN: [{Mathf.Sin(loftAngle * turnFactor * Mathf.Deg2Rad):G3}], turnFactor: [{turnFactor:G3}].");
                    return missileVessel.transform.position + (float)missileVessel.srfSpeed * ((Mathf.Cos(loftAngle * turnFactor * Mathf.Deg2Rad) * planarDirectionToTarget) + (Mathf.Sin(loftAngle * turnFactor * Mathf.Deg2Rad) * upDirection));

                    /*
                    Vector3 newVel = (velForwards * planarDirectionToTarget + velUp * upDirection);
                    //Vector3 accVec = Vector3.Cross(newVel, Vector3.Cross(upDirection, planarDirectionToTarget));
                    Vector3 accVec = accel*(Vector3.Dot(newVel, planarDirectionToTarget) * upDirection - Vector3.Dot(newVel, upDirection) * planarDirectionToTarget).normalized;

                    return missileVessel.transform.position + 1.5f * Time.fixedDeltaTime * newVel + 2.25f * Time.fixedDeltaTime * Time.fixedDeltaTime * accVec;
                    */
                    /*}
                    return missileVessel.transform.position + 0.5f * (float)missileVessel.srfSpeed * ((Mathf.Cos(loftAngle * Mathf.Deg2Rad) * planarDirectionToTarget) + (Mathf.Sin(loftAngle * Mathf.Deg2Rad) * upDirection));
                    */
                    //}

                    //Vector3 finalTarget = missileVessel.transform.position + 0.5f * (float)missileVessel.srfSpeed * planarDirectionToTarget + ((altitudeClamp - (float)missileVessel.altitude) * upDirection.normalized);

                    //return finalTarget;
                }
                else
                {
                    loftState = 2;

                    // Tried to do some kind of pro-nav method. Didn't work well, leaving it just in case I want to fix it.
                    /*
                    Vector3 newVel = (float)missileVessel.srfSpeed * velDirection;
                    Vector3 accVec = (newVel - missileVessel.Velocity());
                    Vector3 unitVel = missileVessel.Velocity().normalized;
                    accVec = accVec - unitVel * Vector3.Dot(unitVel, accVec);

                    float accelTime = Mathf.Clamp(timeToImpact, 0f, 4f);

                    accVec = accVec / accelTime;

                    float accel = accVec.magnitude;

                    if (accel > 20f * (float)PhysicsGlobals.GravitationalAcceleration)
                    {
                        accel = 20f * (float)PhysicsGlobals.GravitationalAcceleration / accel;
                    }
                    else
                    {
                        accel = 1f;
                    }

                    Debug.Log("[BDArmory.MissileGuidance]: Loft: Diving, accel = " + accel);
                    return missileVessel.transform.position + 1.5f * Time.fixedDeltaTime * missileVessel.Velocity() + 2.25f * Time.fixedDeltaTime * Time.fixedDeltaTime * accVec * accel;
                    */

                    if (velUp > 0f)
                    {
                        /*return missileVessel.transform.position + (float)missileVessel.srfSpeed * new Vector3(velDirection.x - upDirection.x * velUp,
                            velDirection.y - upDirection.y * velUp,
                            velDirection.z - upDirection.z * velUp) + Mathf.Max(targetAlt - (float)missileVessel.altitude, 0f) * upDirection;*/
                        return missileVessel.transform.position + (float)missileVessel.srfSpeed * planarDirectionToTarget + Mathf.Max(targetAlt - (float)missileVessel.altitude, 0f) * upDirection;
                    }

                    //return missileVessel.transform.position + (float)missileVessel.srfSpeed *  velDirection;
                    return missileVessel.transform.position + (float)missileVessel.srfSpeed * new Vector3(velUp * upDirection.x + velForwards * planarDirectionToTarget.x,
                        velUp * upDirection.y + velForwards * planarDirectionToTarget.y,
                        velUp * upDirection.z + velForwards * planarDirectionToTarget.z);
                }
            }
            else
            {
                // If terminal just go straight for target + lead
                loftState = 3;
                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileGuidance]: Terminal");

                if (LoftUseAPN && (targetDistance < termDist))
                {
                    return GetAPNTarget(targetPosition, targetVelocity, targetAcceleration, missileVessel, N, out timeToImpact);
                }
                else
                {
                    return AIUtils.PredictPosition(targetPosition, targetVelocity, targetAcceleration, leadTime + TimeWarp.fixedDeltaTime); //targetPosition + targetVelocity * leadTime + 0.5f * leadTime * leadTime * targetAcceleration;
                    //return targetPosition + targetVelocity * leadTime;
                }
            }
        }

        public static Vector3 GetAirToAirTargetModular(Vector3 targetPosition, Vector3 targetVelocity, Vector3 targetAcceleration, Vessel missileVessel, out float timeToImpact)
        {
            float targetDistance = Vector3.Distance(targetPosition, missileVessel.CoM);

            //Basic lead time calculation
            Vector3 currVel = ((float)missileVessel.srfSpeed * missileVessel.Velocity().normalized);
            timeToImpact = (float)(1 / ((targetVelocity - currVel).magnitude / targetDistance));

            // Calculate time to CPA to determine target position
            float timeToCPA = missileVessel.TimeToCPA(targetPosition, targetVelocity, targetAcceleration, 16f);
            timeToImpact = (timeToCPA < 16f) ? timeToCPA : timeToImpact;
            // Ease in velocity from 16s to 8s, ease in acceleration from 8s to 2s using the logistic function to give smooth adjustments to target point.
            float easeAccel = Mathf.Clamp01(1.1f / (1f + Mathf.Exp((timeToCPA - 5f))) - 0.05f);
            float easeVel = Mathf.Clamp01(2f - timeToCPA / 8f);
            return AIUtils.PredictPosition(targetPosition, targetVelocity * easeVel, targetAcceleration * easeAccel, timeToCPA + TimeWarp.fixedDeltaTime); // Compensate for the off-by-one frame issue.
        }

        public static Vector3 GetPNTarget(Vector3 targetPosition, Vector3 targetVelocity, Vessel missileVessel, float N, out float timeToGo)
        {
            Vector3 missileVel = (float)missileVessel.srfSpeed * missileVessel.Velocity().normalized;
            Vector3 relVelocity = targetVelocity - missileVel;
            Vector3 relRange = targetPosition - missileVessel.CoM;
            Vector3 RotVector = Vector3.Cross(relRange, relVelocity) / Vector3.Dot(relRange, relRange);
            Vector3 RefVector = missileVel.normalized;
            Vector3 normalAccel = -N * relVelocity.magnitude * Vector3.Cross(RefVector, RotVector);
            timeToGo = missileVessel.TimeToCPA(targetPosition, targetVelocity, Vector3.zero, 120f);
            return missileVessel.CoM + missileVel * timeToGo + normalAccel * timeToGo * timeToGo;
        }

        public static Vector3 GetAPNTarget(Vector3 targetPosition, Vector3 targetVelocity, Vector3 targetAcceleration, Vessel missileVessel, float N, out float timeToGo)
        {
            Vector3 missileVel = (float)missileVessel.srfSpeed * missileVessel.Velocity().normalized;
            Vector3 relVelocity = targetVelocity - missileVel;
            Vector3 relRange = targetPosition - missileVessel.CoM;
            Vector3 RotVector = Vector3.Cross(relRange, relVelocity) / Vector3.Dot(relRange, relRange);
            Vector3 RefVector = missileVel.normalized;
            Vector3 normalAccel = -N * relVelocity.magnitude * Vector3.Cross(RefVector, RotVector);
            // float tgo = relRange.magnitude / relVelocity.magnitude;
            Vector3 accelBias = Vector3.Cross(relRange.normalized, targetAcceleration);
            accelBias = Vector3.Cross(RefVector, accelBias);
            normalAccel -= 0.5f * N * accelBias;
            timeToGo = missileVessel.TimeToCPA(targetPosition, targetVelocity, targetAcceleration, 120f);
            return missileVessel.CoM + missileVel * timeToGo + normalAccel * timeToGo * timeToGo;
        }
        public static float GetLOSRate(Vector3 targetPosition, Vector3 targetVelocity, Vessel missileVessel)
        {
            Vector3 missileVel = (float)missileVessel.srfSpeed * missileVessel.Velocity().normalized;
            Vector3 relVelocity = targetVelocity - missileVel;
            Vector3 relRange = targetPosition - missileVessel.CoM;
            Vector3 RotVector = Vector3.Cross(relRange, relVelocity) / Vector3.Dot(relRange, relRange);
            Vector3 LOSRate = Mathf.Rad2Deg * RotVector;
            return LOSRate.magnitude;
        }

        /// <summary>
        /// Calculate a very accurate time to impact, use the out timeToimpact property if the method returned true. DEPRECATED, use TimeToCPA.
        /// </summary>
        /// <param name="targetVelocity"></param>
        /// <param name="missileVessel"></param>
        /// <param name="effectiveMissileAcceleration"></param>
        /// <param name="effectiveTargetAcceleration"></param>
        /// <param name="targetDistance"></param>
        /// <param name="timeToImpact"></param>
        /// <returns> true if it was possible to reach the target, false otherwise</returns>
        private static bool CalculateAccurateTimeToImpact(float targetDistance, Vector3 targetVelocity, Vessel missileVessel,
            Vector3d effectiveMissileAcceleration, Vector3 effectiveTargetAcceleration, out float timeToImpact)
        {
            int iterations = 0;
            Vector3d relativeAcceleration = effectiveMissileAcceleration - effectiveTargetAcceleration;
            Vector3d relativeVelocity = (float)missileVessel.srfSpeed * missileVessel.Velocity().normalized -
                                   targetVelocity;
            Vector3 missileFinalPosition = missileVessel.CoM;
            float previousDistanceSqr = 0f;
            float currentDistanceSqr;
            do
            {
                missileFinalPosition += relativeVelocity * Time.fixedDeltaTime;
                relativeVelocity += relativeAcceleration;
                currentDistanceSqr = (missileFinalPosition - missileVessel.CoM).sqrMagnitude;

                if (currentDistanceSqr <= previousDistanceSqr)
                {
                    Debug.Log("[BDArmory.MissileGuidance]: Accurate time to impact failed");

                    timeToImpact = 0;
                    return false;
                }

                previousDistanceSqr = currentDistanceSqr;
                iterations++;
            } while (currentDistanceSqr < targetDistance * targetDistance);

            timeToImpact = Time.fixedDeltaTime * iterations;
            return true;
        }

        /// <summary>
        /// Air-2-Air fire solution used by the AI for steering, WM checking if a missile can be launched, unguided missiles
        /// </summary>
        /// <param name="missile"></param>
        /// <param name="targetVessel"></param>
        /// <returns></returns>
        public static Vector3 GetAirToAirFireSolution(MissileBase missile, Vessel targetVessel)
        {
            if (!targetVessel)
            {
                return missile.transform.position + (missile.GetForwardTransform() * 1000);
            }
            Vector3 targetPosition = targetVessel.transform.position;
            float leadTime = 0;
            float targetDistance = Vector3.Distance(targetVessel.transform.position, missile.transform.position);

            //Vector3 simMissileVel = 500 * (targetPosition - missile.transform.position).normalized;

            MissileLauncher launcher = missile as MissileLauncher;
            /*
            float optSpeed = 400; //TODO: Add parameter
            if (launcher != null)
            {
                optSpeed = launcher.optimumAirspeed; //so it assumes missiles start out immediately possessing all their velocity instead of having to accelerate? That explains alot.
            }
            simMissileVel = optSpeed * (targetPosition - missile.transform.position).normalized;

            leadTime = targetDistance / (float)(targetVessel.Velocity() - simMissileVel).magnitude;
            leadTime = Mathf.Clamp(leadTime, 0f, 8f);
            */
            Vector3 vel = missile.vessel.Velocity();
            Vector3 VelOpt = vel.normalized * (launcher != null ? launcher.optimumAirspeed : 1500);
            float accel = launcher.thrust / missile.part.mass;
            Vector3 deltaVel = targetVessel.Velocity() - vel;
            Vector3 DeltaOptvel = targetVessel.Velocity() - VelOpt;
            float T = Mathf.Clamp((VelOpt - vel).magnitude / accel, 0, 8); //time to optimal airspeed

            Vector3 relPosition = targetPosition - missile.transform.position;
            Vector3 relAcceleration = targetVessel.acceleration - missile.MissileReferenceTransform.forward * accel;
            leadTime = AIUtils.TimeToCPA(relPosition, deltaVel, relAcceleration, T); //missile accelerating, T is greater than our max look time of 8s
            if (T < 8 && leadTime == T)//missile has reached max speed, and is now cruising; sim positions ahead based on T and run CPA from there
            {
                relPosition = AIUtils.PredictPosition(targetPosition, targetVessel.Velocity(), targetVessel.acceleration, T) -
                    AIUtils.PredictPosition(missile.transform.position, vel, missile.MissileReferenceTransform.forward * accel, T);
                relAcceleration = targetVessel.acceleration; // - missile.MissileReferenceTransform.forward * 0; assume missile is holding steady velocity at optimumAirspeed
                leadTime = AIUtils.TimeToCPA(relPosition, DeltaOptvel, relAcceleration, 8 - T) + T;
            }

            targetPosition = targetPosition + (targetVessel.Velocity() * leadTime);

            if (targetVessel && targetDistance < 800) //TODO - investigate if this would throw off aim accuracy
            {
                targetPosition += (Vector3)targetVessel.acceleration * 0.05f * leadTime * leadTime;
            }

            return targetPosition;
        }
        /// <summary>
        /// Air-2-Air lead offset calcualtion used for guided missiles
        /// </summary>
        /// <param name="missile"></param>
        /// <param name="targetPosition"></param>
        /// <param name="targetVelocity"></param>
        /// <returns></returns>
        public static Vector3 GetAirToAirFireSolution(MissileBase missile, Vector3 targetPosition, Vector3 targetVelocity)
        {
            MissileLauncher launcher = missile as MissileLauncher;
            float leadTime = 0;
            Vector3 leadPosition = targetPosition;
            Vector3 vel = missile.vessel.Velocity();
            Vector3 leadDirection, velOpt;
            float accel = launcher.thrust / missile.part.mass;
            float leadTimeError = 1f;
            int count = 0;
            do
            {
                leadDirection = leadPosition - missile.transform.position;
                float targetDistance = leadDirection.magnitude;
                leadDirection.Normalize();
                velOpt = leadDirection * (launcher != null ? launcher.optimumAirspeed : 1500);
                float deltaVel = Vector3.Dot(targetVelocity - vel, leadDirection);
                float deltaVelOpt = Vector3.Dot(targetVelocity - velOpt, leadDirection);
                float T = Mathf.Clamp((velOpt - vel).magnitude / accel, 0, 8); //time to optimal airspeed, clamped to at most 8s
                float D = deltaVel * T + 1 / 2 * accel * (T * T); //relative distance covered accelerating to optimum airspeed
                leadTimeError = -leadTime;
                if (targetDistance > D) leadTime = (targetDistance - D) / deltaVelOpt + T;
                else leadTime = (-deltaVel - BDAMath.Sqrt((deltaVel * deltaVel) + 2 * accel * targetDistance)) / accel;
                leadTime = Mathf.Clamp(leadTime, 0f, 8f);
                leadTimeError += leadTime;
                leadPosition = AIUtils.PredictPosition(targetPosition, targetVelocity, Vector3.zero, leadTime);
            } while (++count < 5 && Mathf.Abs(leadTimeError) > 1e-3f);  // At most 5 iterations to converge. Also, 1e-2f may be sufficient.
            return leadPosition;
        }

        public static Vector3 GetCruiseTarget(Vector3 targetPosition, Vessel missileVessel, float radarAlt)
        {
            Vector3 upDirection = VectorUtils.GetUpDirection(missileVessel.transform.position);
            float currentRadarAlt = GetRadarAltitude(missileVessel);
            float distanceSqr =
                (targetPosition - (missileVessel.transform.position - (currentRadarAlt * upDirection))).sqrMagnitude;

            Vector3 planarDirectionToTarget = (targetPosition - missileVessel.transform.position).ProjectOnPlanePreNormalized(upDirection).normalized;

            float error;

            if (currentRadarAlt > 1600)
            {
                error = 500000;
            }
            else
            {
                Vector3 tRayDirection = (planarDirectionToTarget * 10) - (10 * upDirection);
                Ray terrainRay = new Ray(missileVessel.transform.position, tRayDirection);
                RaycastHit rayHit;

                if (Physics.Raycast(terrainRay, out rayHit, 8000, (int)(LayerMasks.Scenery | LayerMasks.EVA))) // Why EVA?
                {
                    float detectedAlt =
                        Vector3.Project(rayHit.point - missileVessel.transform.position, upDirection).magnitude;

                    error = Mathf.Min(detectedAlt, currentRadarAlt) - radarAlt;
                }
                else
                {
                    error = currentRadarAlt - radarAlt;
                }
            }

            error = Mathf.Clamp(0.05f * error, -5, 3);
            return missileVessel.transform.position + (10 * planarDirectionToTarget) - (error * upDirection);
        }

        public static Vector3 GetTerminalManeuveringTarget(Vector3 targetPosition, Vessel missileVessel, float radarAlt)
        {
            Vector3 upDirection = -FlightGlobals.getGeeForceAtPosition(missileVessel.GetWorldPos3D()).normalized;
            Vector3 planarVectorToTarget = (targetPosition - missileVessel.transform.position).ProjectOnPlanePreNormalized(upDirection);
            Vector3 planarDirectionToTarget = planarVectorToTarget.normalized;
            Vector3 crossAxis = Vector3.Cross(planarDirectionToTarget, upDirection).normalized;
            float sinAmplitude = Mathf.Clamp(Vector3.Distance(targetPosition, missileVessel.transform.position) - 850, 0,
                4500);
            Vector3 sinOffset = (Mathf.Sin(1.25f * Time.time) * sinAmplitude * crossAxis);
            Vector3 targetSin = targetPosition + sinOffset;
            Vector3 planarSin = missileVessel.transform.position + planarVectorToTarget + sinOffset;

            Vector3 finalTarget;
            float finalDistance = 2500 + GetRadarAltitude(missileVessel);
            if ((targetPosition - missileVessel.transform.position).sqrMagnitude > finalDistance * finalDistance)
            {
                finalTarget = targetPosition;
            }
            else if (!GetBallisticGuidanceTarget(targetSin, missileVessel, true, out finalTarget))
            {
                //finalTarget = GetAirToGroundTarget(targetSin, missileVessel, 6);
                finalTarget = planarSin;
            }
            return finalTarget;
        }

        public static FloatCurve DefaultLiftCurve = null;
        public static FloatCurve DefaultDragCurve = null;

        public static Vector3 DoAeroForces(MissileLauncher ml, Vector3 targetPosition, float liftArea, float steerMult,
            Vector3 previousTorque, float maxTorque, float maxAoA)
        {
            if (DefaultLiftCurve == null)
            {
                DefaultLiftCurve = new FloatCurve();
                DefaultLiftCurve.Add(0, 0);
                DefaultLiftCurve.Add(8, .35f);
                //	DefaultLiftCurve.Add(19, 1);
                //	DefaultLiftCurve.Add(23, .9f);
                DefaultLiftCurve.Add(30, 1.5f);
                DefaultLiftCurve.Add(65, .6f);
                DefaultLiftCurve.Add(90, .7f);
            }

            if (DefaultDragCurve == null)
            {
                DefaultDragCurve = new FloatCurve();
                DefaultDragCurve.Add(0, 0.00215f);
                DefaultDragCurve.Add(5, .00285f);
                DefaultDragCurve.Add(15, .007f);
                DefaultDragCurve.Add(29, .01f);
                DefaultDragCurve.Add(55, .3f);
                DefaultDragCurve.Add(90, .5f);
            }

            FloatCurve liftCurve = DefaultLiftCurve;
            FloatCurve dragCurve = DefaultDragCurve;

            return DoAeroForces(ml, targetPosition, liftArea, steerMult, previousTorque, maxTorque, maxAoA, liftCurve,
                dragCurve);
        }

        public static Vector3 DoAeroForces(MissileLauncher ml, Vector3 targetPosition, float liftArea, float steerMult,
            Vector3 previousTorque, float maxTorque, float maxAoA, FloatCurve liftCurve, FloatCurve dragCurve)
        {
            Rigidbody rb = ml.part.rb;
            if (rb == null || rb.mass == 0) return Vector3.zero;
            double airDensity = ml.vessel.atmDensity;
            double airSpeed = ml.vessel.srfSpeed;
            Vector3d velocity = ml.vessel.Velocity();

            //temp values
            Vector3 CoL = new Vector3(0, 0, -1f);
            float liftMultiplier = BDArmorySettings.GLOBAL_LIFT_MULTIPLIER;
            float dragMultiplier = BDArmorySettings.GLOBAL_DRAG_MULTIPLIER;

            //lift
            float AoA = Mathf.Clamp(Vector3.Angle(ml.transform.forward, velocity.normalized), 0, 90);
            if (AoA > 0)
            {
                double liftForce = 0.5 * airDensity * airSpeed * airSpeed * liftArea * liftMultiplier * liftCurve.Evaluate(AoA);
                Vector3 forceDirection = -velocity.ProjectOnPlanePreNormalized(ml.transform.forward).normalized;
                rb.AddForceAtPosition((float)liftForce * forceDirection,
                    ml.transform.TransformPoint(ml.part.CoMOffset + CoL));
            }

            //drag
            if (airSpeed > 0)
            {
                double dragForce = 0.5 * airDensity * airSpeed * airSpeed * liftArea * dragMultiplier * dragCurve.Evaluate(AoA);
                rb.AddForceAtPosition((float)dragForce * -velocity.normalized,
                    ml.transform.TransformPoint(ml.part.CoMOffset + CoL));
            }

            //guidance
            if (airSpeed > 1 || (ml.vacuumSteerable && ml.Throttle > 0))
            {
                Vector3 targetDirection;
                float targetAngle;
                if (AoA < maxAoA)
                {
                    targetDirection = (targetPosition - ml.transform.position);
                    targetAngle = Vector3.Angle(velocity.normalized, targetDirection) * 4;
                }
                else
                {
                    targetDirection = velocity.normalized;
                    targetAngle = AoA;
                }

                Vector3 torqueDirection = -Vector3.Cross(targetDirection, velocity.normalized).normalized;
                torqueDirection = ml.transform.InverseTransformDirection(torqueDirection);

                float torque = Mathf.Clamp(targetAngle * steerMult, 0, maxTorque);
                Vector3 finalTorque = Vector3.Lerp(previousTorque, torqueDirection * torque, 1).ProjectOnPlanePreNormalized(Vector3.forward);

                rb.AddRelativeTorque(finalTorque);
                return finalTorque;
            }
            else
            {
                Vector3 finalTorque = Vector3.Lerp(previousTorque, Vector3.zero, 0.25f).ProjectOnPlanePreNormalized(Vector3.forward);
                rb.AddRelativeTorque(finalTorque);
                return finalTorque;
            }
        }

        public static float GetRadarAltitude(Vessel vessel)
        {
            float radarAlt = Mathf.Clamp((float)(vessel.mainBody.GetAltitude(vessel.CoM) - vessel.terrainAltitude), 0,
                (float)vessel.altitude);
            return radarAlt;
        }

        public static float GetRadarAltitudeAtPos(Vector3 position)
        {
            double latitudeAtPos = FlightGlobals.currentMainBody.GetLatitude(position);
            double longitudeAtPos = FlightGlobals.currentMainBody.GetLongitude(position);

            float radarAlt = Mathf.Clamp(
                (float)(FlightGlobals.currentMainBody.GetAltitude(position) -
                         FlightGlobals.currentMainBody.TerrainAltitude(latitudeAtPos, longitudeAtPos)), 0,
                (float)FlightGlobals.currentMainBody.GetAltitude(position));
            return radarAlt;
        }

        public static float GetRaycastRadarAltitude(Vector3 position)
        {
            Vector3 upDirection = -FlightGlobals.getGeeForceAtPosition(position).normalized;

            float altAtPos = FlightGlobals.getAltitudeAtPos(position);
            if (altAtPos < 0)
            {
                position += 2 * Mathf.Abs(altAtPos) * upDirection;
            }

            Ray ray = new Ray(position, -upDirection);
            float rayDistance = FlightGlobals.getAltitudeAtPos(position);

            if (rayDistance < 0)
            {
                return 0;
            }

            RaycastHit rayHit;
            if (Physics.Raycast(ray, out rayHit, rayDistance, (int)(LayerMasks.Scenery | LayerMasks.EVA))) // Why EVA?
            {
                return rayHit.distance;
            }
            else
            {
                return rayDistance;
            }
        }
    }
}
