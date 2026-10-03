using System;
using UnityEngine;

using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.Utils;
using BDArmory.Weapons.Missiles;
using BDArmory.Weapons;

namespace BDArmory.Guidances
{
    public class MissileGuidance
    {
        const float invg = 1f / 9.80665f; // 1/gravity on Earth/Kerbin

        public static Vector3 GetAirToGroundTarget(Vector3 targetPosition, Vector3 targetVelocity, Vessel missileVessel, float descentRatio, float minSpeed = 200)
        {
            // Incorporate lead for target velocity
            Vector3 currVel = Mathf.Max((float)missileVessel.srfSpeed, minSpeed) * missileVessel.Velocity().normalized;
            float targetDistance = Vector3.Distance(targetPosition, missileVessel.CoM);
            float leadTime = Mathf.Clamp(targetDistance / (targetVelocity - currVel).magnitude, 0f, 8f);
            targetPosition += targetVelocity * leadTime;

            Vector3 upDirection = missileVessel.up;
            Vector3 surfacePos = missileVessel.CoM +
                                 Vector3.Project(targetPosition - missileVessel.CoM, upDirection);
            //((float)missileVessel.altitude*upDirection);
            Vector3 targetSurfacePos;

            targetSurfacePos = targetPosition;

            float distanceToTarget = Vector3.Distance(surfacePos, targetSurfacePos);

            if (missileVessel.srfSpeed < 75 && missileVessel.verticalSpeed < 10)
            //gain altitude if launching from stationary
            {
                return missileVessel.CoM + (5 * missileVessel.transform.forward) + (1 * upDirection);
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
            Vector3 up = missileVessel.up;
            Vector3 forward = (targetPosition - missileVessel.CoM).ProjectOnPlanePreNormalized(up);
            float speed = (float)missileVessel.srfSpeed;
            float sqrSpeed = speed * speed;
            float sqrSpeedSqr = sqrSpeed * sqrSpeed;
            float g = (float)FlightGlobals.getGeeForceAtPosition(missileVessel.CoM).magnitude;
            float height = FlightGlobals.getAltitudeAtPos(targetPosition) -
                           FlightGlobals.getAltitudeAtPos(missileVessel.CoM);
            float sqrRange = forward.sqrMagnitude;
            float range = BDAMath.Sqrt(sqrRange);

            float plusOrMinus = direct ? -1 : 1;

            float top = sqrSpeed + (plusOrMinus * BDAMath.Sqrt(sqrSpeedSqr - (g * ((g * sqrRange + (2 * height * sqrSpeed))))));
            float bottom = g * range;
            float theta = Mathf.Atan(top / bottom);

            if (!float.IsNaN(theta))
            {
                Vector3 finalVector = Quaternion.AngleAxis(theta * Mathf.Rad2Deg, Vector3.Cross(forward, up)) * forward;
                finalTarget = missileVessel.CoM + (100 * finalVector);
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

        public static Vector3 GetCLOSTarget(Vector3 sensorPos, Vector3 currentPos, Vector3 currentVelocity, Vector3 targetPos, Vector3 targetVel,
            float correctionFactor, float N, float deadzone, float beamWidth, out float gLimit)
        {
            targetPos += targetVel * Time.fixedDeltaTime;
            Vector3 accel = GetCLOSAccel(sensorPos, Vector3.zero, currentPos, currentVelocity, targetPos, targetVel, Vector3.zero, correctionFactor, N, beamWidth);
            gLimit = accel.magnitude / 9.80665f;

            if (gLimit < deadzone)
            {
                gLimit = 0f;
                return currentPos + 4f * currentVelocity;
            }

            return currentPos + 4f * currentVelocity + 16f * accel;
        }

        public static Vector3 GetThreePointTarget(Vector3 sensorPos, Vector3 sensorVel, Vector3 currentPos, Vector3 currentVelocity, Vector3 targetPos, Vector3 targetVel,
            float correctionFactor, float N, float deadzone, float beamWidth, out float gLimit)
        {
            Vector3 relVelocity = targetVel - sensorVel;
            Vector3 relRange = targetPos - sensorPos;
            Vector3 angVel = Vector3.Cross(relRange, relVelocity) / relRange.sqrMagnitude;

            Vector3 accel = GetCLOSAccel(sensorPos, sensorVel, currentPos, currentVelocity, targetPos, targetVel, angVel, correctionFactor, N, beamWidth);

            accel -= 2f * Vector3.Cross(currentVelocity, angVel);
            gLimit = accel.magnitude / 9.80665f;

            if (gLimit < deadzone)
            {
                gLimit = 0f;
                return currentPos + 4f * currentVelocity;
            }

            return currentPos + 4f * currentVelocity + 16f * accel;
        }

        public static Vector3 GetCLOSLeadTarget(Vector3 sensorPos, Vector3 sensorVel, Vector3 currentPos, Vector3 currentVelocity, Vector3 targetPos, Vector3 targetVel,
            float correctionFactor, float N, float deadzone, float beamWidth, float beamLeadFactor, float beamMinAlt, out float gLimit, MissileLauncher ml)
        {
            Vector3 relVelocity = targetVel - sensorVel;
            Vector3 relRange = targetPos - sensorPos;
            float RSqr = relRange.sqrMagnitude;

            float currVel = currentVelocity.magnitude;
            if (currVel < 200f)
            {
                currentVelocity *= 200f / currVel;
            }

            (float rangeM, Vector3 dirM) = (currentPos - sensorPos).MagNorm();
            (float rangeT, Vector3 dirT) = (targetPos - sensorPos).MagNorm();
            float leadTime = Mathf.Clamp((rangeT - rangeM) / (Mathf.Max(currVel, 200f) - Vector3.Dot(targetVel, dirT)), 0f, 8f);

            Vector3 angVel = Vector3.Cross(relRange, relVelocity) / RSqr;

            Vector3 deltaLOS = (Mathf.Clamp01(beamLeadFactor) * leadTime) * angVel;
            Quaternion rotation = Quaternion.AngleAxis(deltaLOS.magnitude * Mathf.Rad2Deg, deltaLOS);
            Vector3 corrRelRange = rotation * relRange;

            // If we are using "elevated by k" method and the target rel alt < beamMinAlt
            Vector3 up;
            float altDiff;
            if (beamMinAlt > 0 && (altDiff = Vector3.Dot(relRange, (up = ml.vessel.up))) < 1.25f * beamMinAlt)
            {
                // We do not allow lead in the vertical direction below the target
                float kCorrection = Mathf.Clamp01(4f * (beamMinAlt - altDiff) / beamMinAlt + 1f) * Vector3.Dot(relRange - corrRelRange, up);
                if (kCorrection > 0f)
                {
                    corrRelRange += (kCorrection * up);
                }
            }

            // Once below the max leadTime, the LoS vector moves towards the target at half of angVel due to the nature of half-rectification
            // guidance, hence when we get the CLOS accel we use half of angVel
            // Now that half-rectification has been generalized, this is beamLeadFactor rather than 0.5.
            if (leadTime < 8f)
            {
                angVel *= (1f - beamLeadFactor);
            }

            Vector3 accel = GetCLOSAccel(sensorPos, sensorVel, currentPos, currentVelocity, sensorPos + corrRelRange, targetVel, angVel, correctionFactor, N, beamWidth);

            ml.DrawDebugLine(sensorPos, sensorPos + corrRelRange);

            //accel -= 2f * Vector3.Cross(currentVelocity, angVel);
            gLimit = accel.magnitude / 9.80665f;

            if (gLimit < deadzone)
            {
                gLimit = 0f;
                return currentPos + 4f * currentVelocity;
            }

            return currentPos + 4f * currentVelocity + 16f * accel;
        }

        /*public static Vector3 GetCLOSAccel(Vector3 sensorPos, Vector3 currentPos, Vector3 currentVelocity, Vector3 targetPos, Vector3 targetVel,
            float correctionFactor, float correctionDamping)
        {
            Vector3 beamDir = (targetPos - sensorPos).normalized;
            float onBeamDistance = Vector3.Dot(currentPos - sensorPos, beamDir);
            Vector3 onBeamPos = sensorPos + beamDir * onBeamDistance;

            (float beamError, Vector3 beamErrorV) = (onBeamPos - currentPos).MagNorm();

            Vector3 rotVec = Vector3.Cross(beamDir, (currentPos - sensorPos).normalized);

            (float velAngularErr, Vector3 velAngularErrV) = Vector3.Cross(beamDir, currentVelocity.normalized).MagNorm();
            velAngularErr = Mathf.Acos(velAngularErr);

            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance] beamError: {beamError}, beamErrorV: {beamErrorV}, velAngularErr: {velAngularErr}, velAngularErrV: {velAngularErrV}.");

            Vector3 accel = Vector3.Cross(currentVelocity, (correctionFactor * beamError * rotVec + correctionFactor * correctionDamping * velAngularErr * velAngularErrV));

            return accel;
        }*/

        public static Vector3 GetCLOSAccel(Vector3 sensorPos, Vector3 sensorVel, Vector3 currentPos, Vector3 currentVelocity, Vector3 targetPos, Vector3 targetVel, Vector3 beamAngVel,
            float correctionFactor, float N, float beamWidth)
        {
            Vector3 beamDir = (targetPos - sensorPos).normalized;
            float onBeamDistance = Vector3.Dot(currentPos - sensorPos, beamDir);
            Vector3 onBeamPos = sensorPos + beamDir * onBeamDistance;

            Vector3 beamVelocity = Vector3.Cross(beamAngVel, beamDir * onBeamDistance);

            (float beamError, Vector3 beamErrorV) = (currentPos - onBeamPos).MagNorm();

            if (beamWidth > 0f)
            {
                // If beamWidth > 0 then reduce error by beamWidth at that point
                beamError -= onBeamDistance * beamWidth;
                if (beamError < 0)
                {
                    return Vector3.zero;
                }
            }

            (float currentSpeed, Vector3 velDir) = currentVelocity.MagNorm();

            currentSpeed = Mathf.Max(currentSpeed, 200f);

            // This gives the velocity command normal to the beam
            Vector3 velCommand = -correctionFactor * beamError * beamErrorV + beamVelocity + sensorVel;
            
            // We calculate how much remains of currentVelocity once we subtract away the velocity command
            float temp = 1f - velCommand.sqrMagnitude / (currentSpeed * currentSpeed);

            if (temp < 0f)
            {
                // If the velocity command is greater than the currentSpeed then pointing we maximize
                // our velocity normal to the beam
                velCommand = velCommand.normalized;
            }
            else
            {
                // Otherwise, we put what remains of currentVelocity into velocity along the beamDir
                velCommand = velCommand / currentSpeed + BDAMath.Sqrt(temp) * beamDir;
            }

            // We use velCommand crossed with velDir as our angular velocity command since it's more
            // efficient than using a linear angular error based command like was used in the previous
            // attempt at CLOS guidance. Velocity crossed with the angular velocity command gives us our
            // normal acceleration. This being a triple product we simplify to a vector difference and a
            // dot product. We use a proportional constant N just like in pronav to modify the acceleration
            Vector3 accel = N * currentSpeed * (velCommand - Vector3.Dot(velCommand, velDir) * velDir);

            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance] beamError: {beamError}, beamErrorV: {beamErrorV}, velCommand: {velCommand}, accel: {accel}.");

            return accel;
        }

        public static Vector3 GetBeamRideTarget(Ray beam, Vector3 currentPosition, Vector3 currentVelocity,
            float correctionFactor, float correctionDamping, Ray previousBeam)
        {
            float onBeamDistance = Vector3.Dot(currentPosition - beam.origin, beam.direction); //Vector3.Project(currentPosition - beam.origin, beam.direction).magnitude;
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

            Vector3 currVel = Mathf.Max((float)missileVessel.srfSpeed, minSpeed) * missileVessel.Velocity().normalized;

            Vector3 Rdir = (targetPosition - missileVessel.CoM);
            float RSqr = Rdir.sqrMagnitude;
            leadTime = -RSqr / Vector3.Dot(targetVelocity - currVel, Rdir);

            if (leadTime < 0)
            {
                timeToImpact = float.PositiveInfinity;
                leadTime = 8f;
            }
            else
            {
                timeToImpact = leadTime;
            }

            //leadTime = targetDistance / (targetVelocity - currVel).magnitude;
            leadTime = Mathf.Clamp(leadTime, 0f, 8f);

            return targetPosition + (targetVelocity * leadTime);
        }

        public static readonly float AoALDMax = BDAMath.Sqrt(0.00215f / (0.0125f * 2.864788975654117f)) * Mathf.Rad2Deg;
        
        public static Vector3 GetWeaveTarget(Vector3 targetPosition, Vector3 targetVelocity, Vessel missileVessel, ref float gVert, ref float gHorz, Vector3 gRand, ref float omega, float terminalAngle, float weaveFactor, bool useAGMDescentRatio, float agmDescentRatio, float maneuvergLimit, ref float weaveOffset, ref Vector3 weaveStart, ref float WeaveAlt, out float ttgo, out float gLimit)
        {
            // Based on https://www.sciencedirect.com/science/article/pii/S1474667015333437

            Vector3 missileVel = missileVessel.Velocity();
            float speed = (float)missileVessel.speed;

            // Time to go calculation according to instantaneous change in range (dR/dt)
            Vector3 Rdir = (targetPosition - missileVessel.CoM);
            ttgo = -Rdir.sqrMagnitude / Vector3.Dot(targetVelocity - missileVel, Rdir);

            if (BDArmorySettings.DEBUG_MISSILES)
                Debug.Log($"[BDArmory.MissileGuidance] targetPosition: {targetPosition}, targetVelocity: {targetVelocity}, missileVel: {missileVel}, missileSpeed: {speed}, Rdir.sqrMag: {Rdir.sqrMagnitude}, ttgo: {ttgo}");

            if (ttgo <= 0f)
            {
                // Missed target, use PN as backup
                return GetPNTarget(targetPosition, targetVelocity, missileVessel, 3, out ttgo, out gLimit);
            }

            // Get up direction at missile location
            Vector3 upDirection = missileVessel.upAxis;

            // High pass filter
            if (targetVelocity.sqrMagnitude > 100f)
                Rdir = new Vector3(Rdir.x + targetVelocity.x * ttgo,
                                    Rdir.y + targetVelocity.y * ttgo,
                                    Rdir.z + targetVelocity.z * ttgo);

            Vector3 planarDirToTarget = Rdir.ProjectOnPlanePreNormalized(upDirection).normalized;

            Vector3 right = Vector3.Cross(planarDirToTarget, upDirection);

            float pullUpCos = Vector3.Dot(missileVel.normalized, upDirection);

            float horizontalAngle = VectorUtils.GetAngleOnPlane(missileVel, planarDirToTarget, right);
            const float halfPi = Mathf.PI * 0.5f;
            float verticalAngle = halfPi - Mathf.Acos(pullUpCos);

            //verticalAngle *= Mathf.Deg2Rad;
            horizontalAngle *= Mathf.Deg2Rad;

            /*float verticalAngle = (Mathf.Deg2Rad * Mathf.Sign(pullUpCos)) * VectorUtils.Angle(missileVel.ProjectOnPlanePreNormalized(right), planarDirToTarget);

            float horizontalAngle = (Mathf.Deg2Rad * Mathf.Sign(Vector3.Dot(missileVel, right))) * VectorUtils.Angle(missileVel.ProjectOnPlanePreNormalized(upDirection), planarDirToTarget);*/

            const float PI2 = 2f * Mathf.PI;

            float weaveDist;

            float ttgoWeave;
            if (weaveOffset < 0)
            {
                weaveOffset = PI2 * omega * ttgo;
                weaveStart = VectorUtils.WorldPositionToGeoCoords(missileVessel.CoM, missileVessel.mainBody);
                weaveDist = Vector3.Dot(Rdir, planarDirToTarget);
                ttgoWeave = ttgo;
                if (UnityEngine.Random.value < 0.5)
                    gHorz = -gHorz;

                if (gHorz != 0.0f)
                    gHorz += gRand.x * (2f * UnityEngine.Random.value - 1f);
                if (gVert != 0.0f)
                    gVert += gRand.y * (2f * UnityEngine.Random.value - 1f);
                if (gRand.z != 0.0f)
                    omega += gRand.z * (2f * UnityEngine.Random.value - 1f);
            }
            else
            {
                Vector3 weaveDir = (targetPosition - VectorUtils.GetWorldSurfacePostion(weaveStart, missileVessel.mainBody)).ProjectOnPlanePreNormalized(upDirection).normalized;
                weaveDist = Vector3.Dot(Rdir, weaveDir);
                ttgoWeave = weaveFactor * 1.5f * weaveDist / speed;
                right = Vector3.Cross(weaveDir, upDirection);
            }

            float omegaBeta = PI2 * omega * ttgoWeave;
            float sinOmegaBeta = Mathf.Sin(omegaBeta);
            float cosOmegaBeta = Mathf.Cos(omegaBeta);

            float sinOmegaBetaOff = Mathf.Sin(omegaBeta - weaveOffset);
            float cosOmegaBetaOff = Mathf.Cos(omegaBeta - weaveOffset);

            const float g = 9.80665f;
            float ka = 2f * omegaBeta * sinOmegaBeta + 6f * cosOmegaBeta - 6f;
            float kj = -2f * omegaBeta * cosOmegaBeta + 6f * sinOmegaBeta - 4f * omegaBeta;

            float ttgoWeaveInv = 1f / ttgoWeave;
            float omegaBetaInv = 1f / (Mathf.Max(omegaBeta * omegaBeta, 0.000001f));

            float gVertTemp = gVert;
            float vertGuidanceAngle;
            float ttgoWeaveInvVert = ttgoWeaveInv;
            bool useA_BPN = (terminalAngle > 0);

            if (useAGMDescentRatio)
            {
                float currAlt = (float)missileVessel.altitude;

                if (WeaveAlt < 0f)
                    WeaveAlt = currAlt;

                float altitudeClamp = Mathf.Clamp(
                    (weaveDist - ((float)missileVessel.srfSpeed * agmDescentRatio)) * 0.22f, 0f,
                    WeaveAlt);// + Mathf.Max(VectorUtils.AnglePreNormalized(upDirection, missileVel.normalized) - 90f, 0f) * Mathf.Deg2Rad * weaveDist);

                float curvatureCompensation = (1f - Vector3.Dot(upDirection, VectorUtils.GetUpDirection(targetPosition))) * (float) FlightGlobals.currentMainBody.Radius;

                Rdir += (altitudeClamp + curvatureCompensation) * upDirection;

                vertGuidanceAngle = Mathf.Asin(Vector3.Dot(upDirection, Rdir) / Rdir.magnitude);

                /*
                // If we have a vertical weave
                if (pullUpCos < 0)
                {
                    // Get distance to pull up
                    float pullUpSin = BDAMath.Sqrt(1f - pullUpCos * pullUpCos);
                    // Turn radius is mv^2/r = ma -> v^2/r = a -> v^2/a = r, a = 6 g -> v^2 * 1/6 g = r
                    float pullUpg = gVertTemp > 0.0f ? gVertTemp * 0.8f : (gHorz > 0.0f ? Mathf.Min(gHorz * 0.8f, 6f) : 6f);
                    float invG = invg / pullUpg;
                    float pullUpDist = (speed * speed * invG) * (1f - pullUpSin);
                    float altDiff = currAlt - altitudeClamp;

                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance] speed: {speed}, altitude: {missileVessel.altitude}, altitudeClamp: {altitudeClamp}, pullUpDist: {pullUpDist}, altDiff: {altDiff}, pullUpCos: {pullUpCos}, pullUpSin: {pullUpSin}, verticalAngle: {verticalAngle}, ttgoWeaveInvVert: {ttgoWeaveInvVert}.");

                    if (pullUpDist > currAlt)
                    {
                        // If we desperately need to pull up
                        gVertTemp = 0f;
                        // Calculate the vertical acceleration required to pull up in time
                        aVert = pullUpDist / currAlt * pullUpg * 9.8066f;
                        calcaVert = false;
                    }
                    // If we're above the target altitude and we need to pull up
                    if (altDiff > 0 && altDiff < pullUpDist)
                        gVertTemp = 0f;
                }
                */

                float altDiff = currAlt - altitudeClamp;
                if (pullUpCos < 0 || altDiff < 0)
                {
                    // Get angle relative to vertical
                    float pullUpSin = BDAMath.Sqrt(1f - pullUpCos * pullUpCos);
                    // Turn radius is mv^2/r = ma -> v^2/r = a -> v^2/a = r, a = 6 g -> v^2 * 1/6 g = r
                    float invG = invg / maneuvergLimit;
                    float pullUpDist = (speed * speed * invG) * (1f - pullUpSin);

                    if (altDiff < 0)
                    {
                        gVertTemp = 0f;
                        float remainingSin = pullUpSin + altDiff / (speed * speed * invG);
                        float remainingCos = BDAMath.Sqrt(1f - remainingSin * remainingSin);
                        float remainingAngle = Mathf.Asin(remainingSin);

                        Vector3 turnLead = (speed * speed * invG * -0.8f) * ((pullUpCos + remainingCos) * planarDirToTarget) - altDiff * upDirection;
                        vertGuidanceAngle = Mathf.Asin(Vector3.Dot(upDirection, turnLead) / turnLead.magnitude);
                        ttgoWeaveInvVert = 1f / (Mathf.Max(-verticalAngle + remainingAngle, 0.00001f) * speed * invG * 0.8f);
                        //terminalAngle = 0f;
                    }
                    else if (altDiff < pullUpDist)
                    {
                        gVertTemp = 0f;
                        Vector3 turnLead = (speed * speed * invG * -0.8f) * (pullUpCos * planarDirToTarget) - altDiff * upDirection;
                        vertGuidanceAngle = Mathf.Asin(Vector3.Dot(upDirection, turnLead) / turnLead.magnitude);
                        ttgoWeaveInvVert = 1f / (Mathf.Max(-verticalAngle, 0.00001f) * speed * invG * 0.8f);
                        terminalAngle = 0f;
                        useA_BPN = true;
                    }

                    //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance] speed: {speed}, altitude: {missileVessel.altitude}, altitudeClamp: {altitudeClamp}, pullUpDist: {pullUpDist}, altDiff: {altDiff}, curvatureComp: {curvatureCompensation}, leadx: {speed * speed * invG * -0.8f * pullUpCos}, pullUpCos: {pullUpCos}, pullUpSin: {pullUpSin}, verticalAngle: {verticalAngle}, ttgoWeaveInvVert: {ttgoWeaveInvVert}.");
                }
                
            }
            else
                vertGuidanceAngle = Mathf.Asin(Vector3.Dot(upDirection, Rdir) / Rdir.magnitude);

            float aVert = (useA_BPN ? (speed * (6f * vertGuidanceAngle - 4f * verticalAngle + 2f * terminalAngle * Mathf.Deg2Rad) * ttgoWeaveInvVert) : 0.0f) // A_BPN                                                                                                                                    
                    + ((gVertTemp != 0.0f) ? (gVertTemp * g * ((ka + omegaBeta * omegaBeta) * sinOmegaBetaOff + kj * cosOmegaBetaOff) * omegaBetaInv) : 0.0f); // A_W
            float aHor = (useA_BPN ? (-6f * speed * horizontalAngle) * ttgoWeaveInv : 0.0f) // A_BPN            
                + ((gHorz != 0.0f) ? (gHorz * g * ((ka + omegaBeta * omegaBeta) * cosOmegaBetaOff + kj * sinOmegaBetaOff) * omegaBetaInv) : 0.0f); // A_W

            Quaternion rotationPitch = Quaternion.AngleAxis(verticalAngle, right);
            Quaternion rotationYaw = Quaternion.AngleAxis(horizontalAngle, upDirection);

            Vector3 accel = (aVert * (rotationPitch * rotationYaw * upDirection) + aHor * (rotationYaw * right));// + GetPNAccel(targetPosition, targetVelocity, missileVessel, 3f);
            if (useA_BPN)
                gLimit = BDAMath.Sqrt(aVert * aVert + aHor * aHor) / (float)PhysicsGlobals.GravitationalAcceleration;
            else
            {
                accel += GetPNAccel(targetPosition, targetVelocity, missileVessel, 3f);
                gLimit = accel.magnitude / (float)PhysicsGlobals.GravitationalAcceleration;
            }

            if (BDArmorySettings.DEBUG_MISSILES)
                Debug.Log($"[BDArmory.MissileGuidance] Weave guidance ttgoWeave: {ttgoWeave}, omegaBeta: {omegaBeta}, ka: {ka}, kj: {kj}, vertAngle: {Mathf.Rad2Deg * verticalAngle}, horAngle: {Mathf.Rad2Deg * horizontalAngle}, aVert: {aVert} m/s^2, aHor: {aHor} m/s^2.");

            float leadTime = Mathf.Min(4f, ttgoWeave);

            Vector3 aimPos = missileVessel.CoM + leadTime * missileVel + accel * (0.5f * leadTime * leadTime);

            return aimPos;
        }

        // Kappa/Trajectory Curvature Optimal Guidance 
        public static Vector3 GetKappaTarget(Vector3 targetPosition, Vector3 targetVelocity, Vector3 targetAccel,
            MissileLauncher ml, float thrust, float shapingAngle, float rangeFac, float vertVelComp,
            float targetAlt, float terminalHomingRange, float loftAngle, float loftSin, float termSin,
            float midcourseRange, float maxAltitude, bool usegLimEq, out float ttgo, out float gLimit, out float AoALimit,
            ref MissileBase.LoftStates loftState)
        {
            // Get surface velocity direction
            Vector3 velDirection = ml.vessel.srf_vel_direction;

            // Get range
            Vector3 Rdir = (targetPosition - ml.vessel.CoM);
            float R = Rdir.magnitude; // It is tempting to swap to only using Rsqr, but given
            // the number of multiplications this entails, such a micro-opt is pointless.
            float Rsqr = R * R;

            // Kappa Guidance needs an accurate measure of speed to function so no minSpeed application here
            float currSpeed = (float)ml.vessel.srfSpeed;
            // Set current velocity
            Vector3 currVel = currSpeed * velDirection;

            // Old Method
            //float leadTime = R / (targetVelocity - currVel).magnitude;
            //leadTime = Mathf.Clamp(leadTime, 0f, 16f);

            // Time to go calculation according to instantaneous change in range (dR/dt)
            Vector3 velDiff = targetVelocity - currVel;
            ttgo = -Rsqr / Vector3.Dot(velDiff, Rdir);

            // Get up direction at missile location
            Vector3 upDirection = ml.vessel.upAxis; //VectorUtils.GetUpDirection(ml.vessel.CoM);

            // Get ttgo on the horizontal plane
            (float vertR, Vector3 RPlanar) = Rdir.DotProjectOnPlanePreNormalized(upDirection);
            (float vertVel, Vector3 VPlanar) = velDiff.DotProjectOnPlanePreNormalized(upDirection);
            float ttgoPlanar = -RPlanar.sqrMagnitude / Vector3.Dot(VPlanar, RPlanar);
            vertVel = -vertVel;
            float ttgoVert = vertR / vertVel;

            //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: Kappa ttgo: {ttgo}, ttgoPlanar: {ttgoPlanar}, ttgoVert: {ttgoVert}, vertVel: {vertVel}, vertR: {vertR}");

            // If going up and target is below, or target is below and going up
            if (vertVel * vertR < 0f)
            {
                // Limit our ttgo to twice ttgoPlanar
                ttgo = Mathf.Min(0.5f * (ttgo + ttgoPlanar), 1.5f * ttgoPlanar);
            }
            else
            {
                // Average the two values if ttgoPlanar is > 0
                // that way we don't overshoot the target
                if (ttgoPlanar > 0f)
                    ttgo = Mathf.Min(0.5f * (ttgo + ttgoPlanar), 1.5f * ttgoPlanar);
                if (loftState > MissileBase.LoftStates.Boost && ttgoVert > 0f)
                {
                    ttgo = Mathf.Min(ttgo, (vertVel > 0 ? 1.1f : 1.5f) * ttgoVert);
                    if (ttgoVert < ttgo && vertVel > 0)
                    {
                        shapingAngle = 0f;
                    }
                }
            }

            //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: Kappa ttgo post-clamp: {ttgo}");

            // Lead limiting
            if (ttgo <= 0f)
                ttgo = 2f * Mathf.Max(ml.maxStaticLaunchRange, R) / ml.optimumAirspeed;
            else if (ml.optimumAirspeed * ttgo > 2f * Mathf.Max(ml.maxStaticLaunchRange, R))
                ttgo = R / ml.optimumAirspeed; // Force a rapid turn towards the target

            bool boostGuidance = (loftState < MissileBase.LoftStates.Midcourse);

            float ttgoInv = 1f / ttgo;

            Vector3 accel;
            float leadTime;

            float sinTarget = 0f;
            
            // ------------------- Calculate Aerodynamic Parameters -------------------
            // Dynamic pressure
            double sqrSpeed = ml.vessel.srfSpeed * ml.vessel.srfSpeed;
            float q = (float)(0.5 * ml.vessel.atmDensity * sqrSpeed);

            double vesselMass = ml.vessel.totalMass;

            float maneuverCapability = (ml.maxAoA > 30f ? 1.5f : ml.maxAoA * 0.05f);
            const float AoAtoRad = 1.9098593171027f; // 30 deg * Mathf.Deg2Rad

            // Needs to be changed if the lift and drag curves are changed
            // CLmax/AoA(CLmax) * q * S * Lift Multiplier, I.E. linearized Lift/AoA (not CL/AoA)
            // The above wasn't working too well so this has been replaced with C_L,max as is used in Bonalli, Hérissé and Trélat's paper
            float Lalpha = (maneuverCapability * AoAtoRad) * q * ml.currLiftArea * BDArmorySettings.GLOBAL_LIFT_MULTIPLIER; //2.864788975654117f * q * ml.currLiftArea * BDArmorySettings.GLOBAL_LIFT_MULTIPLIER;
            // Thrust normalized by Lalpha
            float TL;
            float gLimLalpha;
            if (ml.gLimit > 0 && Lalpha > (gLimLalpha = (float)(vesselMass * PhysicsGlobals.GravitationalAcceleration) * ml.gLimit * AoAtoRad))
            {
                Lalpha = Mathf.Min(Lalpha, 2f * gLimLalpha);
                TL = 0f;
                maneuverCapability *= (gLimLalpha / Lalpha);
            }
            else
            {
                TL = thrust / Lalpha;
            }

            // ---------------------------- Lead Limiting -----------------------------
            //Vector3 relVelNorm = velDiff.normalized;
            CelestialBody currentBody = FlightGlobals.currentMainBody;
            double currDensity = ml.vessel.atmDensity;
            // Excess accel available to the missile
            maneuverCapability *= ml.currLiftArea * BDArmorySettings.GLOBAL_LIFT_MULTIPLIER * (float)(((Math.Abs(targetAlt - ml.vessel.altitude) < 1000d) ? q : (0.5 * (currDensity = 0.5 * (currentBody.GetDensity(currentBody.GetPressure(targetAlt), currentBody.GetTemperature(targetAlt)) + ml.vessel.atmDensity)) * sqrSpeed)) / vesselMass) * (1f + TL);
            float accelSqr = Mathf.Max(maneuverCapability - 0.5f * Rdir.ProjectOnPlanePreNormalized(velDirection).magnitude * ttgoInv * ttgoInv, 0f);
            //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: Kappa Guidance - maneuverCapability: {maneuverCapability}, reqAccel: {0.5f * Rdir.ProjectOnPlanePreNormalized(velDirection).magnitude * ttgoInv * ttgoInv}, excessAccel: {accelSqr}");
            // Target accel
            accelSqr *= targetAccel.magnitude;
            float leadTimeMax = (accelSqr > 1e-15f) ? Mathf.Clamp(2f * BDAMath.Sqrt(targetVelocity.sqrMagnitude / accelSqr), 8f, 16f) : 16f;
            //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: Kappa Guidance - targetVel: {targetVelocity.magnitude}, targetAccel: {targetAccel.magnitude}, accelSqr: {accelSqr}, leadTimeMax: {leadTimeMax}, unclampedLeadTimeMax: {((accelSqr > 1e-15f) ? 2f * BDAMath.Sqrt(targetVelocity.sqrMagnitude / accelSqr) : 16f)}");

            leadTime = Mathf.Clamp(ttgo, 0f, leadTimeMax);

            if (leadTime > 8f)
            {
                float factor = (leadTimeMax - 8f) * 0.125f;
                targetAccel *= factor * factor;
            }
            else
            {
                targetAccel = Vector3.zero;
            }

            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_MISSILES)
            {
                ml.debugString.AppendLine($"Kappa Guidance, Loft State: {loftState}, ttgo: {ttgo:F2}, Lead Time: {leadTime:F2}");
            }

            Vector3 predictedImpactPoint = AIUtils.PredictPosition(targetPosition, targetVelocity, targetAccel, leadTime + TimeWarp.fixedDeltaTime);
            /*new Vector3(
                        targetPosition.x + leadTime * targetVelocity.x,
                        targetPosition.y + leadTime * targetVelocity.y,
                        targetPosition.z + leadTime * targetVelocity.z);*/ //AIUtils.PredictPosition(targetPosition, targetVelocity, leadTimeMax > 8f ? ((leadTimeMax - 8f / (boostGuidance ? 12f : 16f - 8f)) * targetAccel) : Vector3.zero, leadTime + TimeWarp.fixedDeltaTime);
            Vector3 planarDirectionToTarget = ((predictedImpactPoint - ml.vessel.CoM).ProjectOnPlanePreNormalized(upDirection)).normalized;

            if (boostGuidance)
            {
                // Get angle relative to vertical
                float pullDownCos = Vector3.Dot(velDirection, upDirection);
                float pullDownSin = BDAMath.Sqrt(1f - pullDownCos * pullDownCos);
                // Turn radius is mv^2/r = ma -> v^2/r = a -> v^2/a = r, a = 6 g -> v^2 * 1/6 g = r
                float invG = invg / (ml.GetManeuvergLimit(1));
                float turnRadius = currSpeed * currSpeed * invG;

                //float curvatureCompensation = (1f - Vector3.Dot(upDirection, VectorUtils.GetUpDirection(predictedImpactPoint))) * (float)FlightGlobals.currentMainBody.Radius;
                //float termSin = Mathf.Sin(loftTermAngle * Mathf.Deg2Rad);

                // turnRadius * (pullDownCos + termSin) -> horizontal dist required to go from current orientation to terminal angle
                // turnRadius * (1 - pullDownSin) -> vertical dist required to go from current orientation to horizontal
                // curvatureCompensation -> dist we must move target up / origin down by if we "unwrap" the world beneath them
                // to put them on a flat surface (though in this case we *only* affect the vertical axis)
                Vector3 turnLead = currVel * Time.fixedDeltaTime +
                                   (turnRadius * (pullDownCos + termSin)) * planarDirectionToTarget + 
                                   (turnRadius * (1f - pullDownSin)) * upDirection; //(currSpeed * currSpeed * 0.0169952698051929473876953125f) * (pullDownSin * planarDirectionToTarget + (1f - pullDownCos) * upDirection);
                //float turnTimeOffset = (loftTermAngle * Mathf.Deg2Rad + 0.5f * Mathf.PI - Mathf.Acos(pullDownCos)) * currSpeed * invG;

                (float targetTermDist, Vector3 targetTermVec) = (predictedImpactPoint - ml.vessel.CoM - turnLead).MagNorm();
                sinTarget = Vector3.Dot(targetTermVec, -upDirection); //Vector3.Dot((targetPosition - ml.vessel.CoM), -upDirection) / R;

                boostGuidance = (midcourseRange > 0f) && (R > midcourseRange) && (sinTarget < termSin) && (-sinTarget < loftSin);

                //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: Kappa Guidance - sinTarget: {sinTarget}, termSin: {termSin}, angle cond: {sinTarget > termSin}, targetTermDist: {targetTermDist}, boostGuidance: {boostGuidance}");

                if (!boostGuidance && targetTermDist < 2f * terminalHomingRange && shapingAngle > 0)
                {
                    // Modulate kappaAngle if we're really close.
                    float fac = (targetTermDist - terminalHomingRange);
                    if (fac < 0)
                    {
                        shapingAngle = 0;
                        ml.kappaAngle = 0;
                        loftState = MissileBase.LoftStates.Terminal;
                        //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: Kappa Guidance - Gone terminal at: {targetTermDist} m from PIP, shapingAngle clamped to 0!");
                    }
                    else
                    {
                        shapingAngle *= fac * fac / (terminalHomingRange * terminalHomingRange);
                        ml.kappaAngle = shapingAngle;
                        loftState = MissileBase.LoftStates.Midcourse;
                        //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: Kappa Guidance - Gone terminal at: {targetTermDist} m from PIP, shapingAngle clamped to {shapingAngle}!");
                    }
                }
                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_MISSILES)
                {
                    ml.debugString.AppendLine($"Range/Midcourse Range: {(R * 0.001f):F2}/{(midcourseRange * 0.001f):F2} km, sinTarget/termSin/loftSin: {sinTarget:F2}/{termSin:F2}/{loftSin:F2}, Loft? {boostGuidance}");
                }
            }

            // If still in boost phase
            if (boostGuidance)
            {
                //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileGuidance]: Lofting");

                //float altitudeClamp = Mathf.Clamp(targetAlt + 10f * rangeFac * Mathf.Pow(Vector3.Dot(targetPosition - ml.vessel.CoM, planarDirectionToTarget), Mathf.Abs(vertVelComp)), targetAlt, Mathf.Max(maxAltitude, targetAlt));

                float g = (float)PhysicsGlobals.GravitationalAcceleration;

                if (thrust > 0)
                {
                    currSpeed = Mathf.Min(ml.optimumAirspeed, currSpeed);
                }

                // Stolen from my AAMloft guidance
                // Limit climb angle by turnFactor, turnFactor goes negative when above target alt
                float turnFactorMult = (Mathf.Rad2Deg * g * ml.GetManeuvergLimit(2) * 0.25f) / (Mathf.Min(600f, currSpeed) * currSpeed * loftSin * loftAngle);
                float turnFactor = Mathf.Clamp((float)(maxAltitude - ml.vessel.altitude) * turnFactorMult, -1f, 1f);

                float turnSin = Mathf.Sin(loftAngle * turnFactor * Mathf.Deg2Rad);
                float turnCos = BDAMath.Sqrt(1f - turnSin * turnSin);
                Vector3 turnDir = ((turnCos * planarDirectionToTarget) + (turnSin * upDirection));

                if (turnFactor < 1f && turnFactor > -1f)
                {
                    accel = (currSpeed * currSpeed * turnFactorMult * loftAngle * Mathf.Deg2Rad * turnSin) * Vector3.Cross(turnDir, Vector3.Cross(planarDirectionToTarget, upDirection)).normalized + // Acceleration required to follow the curve
                    (2f / currSpeed) * VelTripleProduct(currVel, turnDir); // Proportional command, turn towards the target direction
                    /*Vector3 propAccel = (3f / currSpeed) * VelTripleProduct(currVel, turnDir); // Proportional command, turn towards the target direction
                    float propAccelSqr = propAccel.sqrMagnitude;
                    float lim = currSpeed * currSpeed * 4f * Mathf.Deg2Rad;
                    if (propAccelSqr < lim)
                    {
                        accel += (propAccelSqr / lim) * propAccel;
                    }
                    else
                    {
                        accel += propAccel;
                    }*/
                }
                else
                {
                    accel = (2f / currSpeed) * VelTripleProduct(currVel, turnDir); // Proportional command, turn towards the target direction
                    /*float propAccelSqr = accel.sqrMagnitude;
                    float lim = currSpeed * currSpeed * 4f * Mathf.Deg2Rad;
                    if (propAccelSqr < lim)
                    {
                        accel *= propAccelSqr / lim;
                    }*/
                }

                if (usegLimEq)
                {
                    accel = (accel - ml.vessel.gravityForPos).ProjectOnPlanePreNormalized(velDirection);
                }
                else
                {
                    accel = accel.ProjectOnPlanePreNormalized(velDirection);
                }

                // Limit gs during climb
                gLimit = Mathf.Min(accel.magnitude / g, ml.GetManeuvergLimit(0));
                AoALimit = turnFactor < 1f ? AoALDMax : 30f;

                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_MISSILES)
                {
                    ml.debugString.AppendLine($"turnAngle: {(loftAngle * turnFactor):F2}°, turnSin: {turnSin:F2}, currTurng: {((turnFactor < 1f && turnFactor > -1f) ? (currSpeed * currSpeed * turnFactorMult * loftAngle * Mathf.Deg2Rad * turnSin) : 0f) / g:F2}");
                }

                if (-sinTarget > turnSin && targetAlt > maxAltitude)
                {
                    loftState = MissileBase.LoftStates.Midcourse;
                }

                //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: turnFactor: {turnFactor}, vel: {currSpeed}, turnFactorMult: {turnFactorMult}, invTurnFactorMult: {1f / turnFactorMult}, tempgLim: {tempgLim}, propAccel: {(3f / currSpeed) * VelTripleProduct(currVel, turnDir).ProjectOnPlanePreNormalized(velDirection).magnitude / g}, curveAccel: {(currSpeed * currSpeed * turnFactorMult * loftAngle * Mathf.Deg2Rad * turnSin) / g}, gLimit: {gLimit}, AoALimit: {AoALimit}");

                return ml.vessel.CoM + currVel * 3f + accel * 9f;
            }

            // Set up final velocity vector
            Vector3 vF;

            // Set up AoALimit
            AoALimit = -1f;

            // Gains for velocity error and positional error
            float K1;
            float K2;

            // -------------- Calculate Remaining Aerodynamic Parameters --------------
            // Drag at 0 AoA
            float D0 = 0.00215f * q * ml.currDragArea * BDArmorySettings.GLOBAL_DRAG_MULTIPLIER;
            // eta needs to change if the lift/drag curves are changed. Note this is for small angles (in radians)
            // Though we're now using C_L,max instead of C_L/alpha, the eta numbers resulting from this are approx. comparable to those used in Bonalli, Hérissé and Trélat's work
            float eta = 0.025f * BDArmorySettings.GLOBAL_DRAG_MULTIPLIER * ml.currDragArea / (BDArmorySettings.GLOBAL_LIFT_MULTIPLIER * ml.currLiftArea); // D = D0 + eta*Lalpha*AoA^2, quadratic approximation of drag.

            // If we're above terminal homing range
            if ((loftState < MissileBase.LoftStates.Terminal) && (R > terminalHomingRange) && 
                shapingAngle != 0f && // Shaping angle isn't 0
                (targetAlt < 1.2f * maxAltitude)) // And the target's altitude isn't exceeding the max alt by too much
            {
                loftState = MissileBase.LoftStates.Midcourse;

                float Fsqr;
                float FR;

                //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: Kappa Guidance - Lalpha: {Lalpha}, c_m: {Lalpha / (ml.vessel.totalMass * ml.vessel.srfSpeed * ml.vessel.srfSpeed)}, D0: {D0}, d_0: {D0 / (ml.vessel.totalMass * ml.vessel.srfSpeed * ml.vessel.srfSpeed)}, eta: {eta}, thrust: {thrust}, TL: {TL}, q: {q}, m^2v^4: {(ml.vessel.totalMass * ml.vessel.totalMass * ml.vessel.srfSpeed * ml.vessel.srfSpeed * ml.vessel.srfSpeed * ml.vessel.srfSpeed)}, R: {R}");
                // The below derivation doesn't really work, I tried replacing the sin and cos with sinh and cosh, but
                // looking at the derivation and the diff. eq. it is trying to satisfy, it is clear that the desired
                // functions are actually sin and cos and not sinh and cosh, since the (F_2)^2 term is positive instead
                // of negative, leading to the use of e^(ix) and e^(-ix) instead of e^(x) and e^(-x) to satisfy the diff.
                // eq. The resulting functions for the gains do match expected behavior at R = 0 (they go to -2, and 6)
                // but due to their periodic nature, away from R = 0 they begin oscillating wildly. I cannot tell if the
                // intended behavior of these is to shoot off to infinity or if the oscillation is part of the solution,
                // but either way, in practice they do not perform as expected.
                /*if (thrust > D0)
                {
                    // Ching-Fang Lin's derivation of a missile under thrust. Requires thrust > D0
                    float F2sqr = Lalpha * (thrust - D0) * (TL + 1f) * (TL + 1f) / ((float)(vesselMass * vesselMass * sqrSpeed * sqrSpeed) * (2f * eta + TL));
                    float F2R = BDAMath.Sqrt(F2sqr) * R;

                    if (F2R > 16f)
                    {
                        K1 = -F2sqr * Rsqr / (2f + F2R);
                        K2 = F2sqr * Rsqr / (2f + F2R);

                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]:Kappa Guidance, under thrust - F2sqr: {F2sqr}, F2R: {F2R}, eFR: Infinity, enFR: 0");
                    }
                    else
                    {
                        float eFR = Mathf.Exp(F2R);
                        float enFR = Mathf.Exp(-F2R);

                        float sinhF2R = 0.5f * (eFR - enFR);
                        float coshF2R = 0.5f * (eFR + enFR);
                        float denom = (2f - 2f * coshF2R - F2R * sinhF2R);

                        K1 = F2R * (sinhF2R - F2R) / denom;
                        K2 = F2sqr * Rsqr * (1f - coshF2R) / denom;
                    }
                }
                else
                {*/
                    // General derivation of aerodynamic constant for Kappa guidance
                    Fsqr = D0 * Lalpha * (TL + 1f) * (TL + 1f) / ((float)(ml.vessel.totalMass * ml.vessel.totalMass * sqrSpeed * sqrSpeed) * (2f * eta + TL));
                    FR = BDAMath.Sqrt(Fsqr) * R;
                //}

                if (FR > 16f)
                {
                    // The asymptotic behavior of the functions allows us to both skip
                    // performing the calculations and avoid numerical issues with large
                    // FR values in Mathf.exp()
                    K1 = FR / (2f - FR);
                    K2 = Fsqr * Rsqr / (FR - 2f);

                    //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: Kappa Guidance, gliding - Fsqr: {Fsqr}, FR: {FR}, eFR: Infinity, enFR: 0, K1: {K1}, K2: {K2}");
                }
                else if (FR < 0.25f)
                {
                    // When the value starts getting really small there can also be numerical
                    // issues, so we just set the values to the limit as FR -> 0
                    K1 = -2;
                    K2 = 6;
                    //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: Kappa Guidance, gliding - Fsqr: {Fsqr}, FR: {FR}, K1: -2, K2: 6");
                }
                else
                {
                    float eFR = Mathf.Exp(FR);
                    float enFR = Mathf.Exp(-FR);
                    float denom = (eFR * (FR - 2f) - enFR * (FR + 2f) + 4f);

                    K1 = (2f * Fsqr * Rsqr - FR * (eFR - enFR)) / denom;
                    K2 = (Fsqr * Rsqr * (eFR + enFR - 2f)) / denom;


                    //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: Kappa Guidance, gliding - Fsqr: {Fsqr}, FR: {FR}, eFR: {eFR}, enFR: {enFR}, denom: {denom}, K1: {K1}, K2: {K2}");
                }
            }
            else
            {
                if (shapingAngle != 0)
                {
                    loftState = MissileBase.LoftStates.Terminal;
                }
                K1 = 0f;
                shapingAngle = 0f;
                float Fsqr;
                float FR;
                /*if (thrust > D0)
                {
                    // Ching-Fang Lin's derivation of a missile under thrust. Requires thrust > D0
                    F2sqr = Lalpha * (thrust - D0) * (TL + 1f) * (TL + 1f) / ((float)(vesselMass * vesselMass * sqrSpeed * sqrSpeed) * (2f * eta + TL));
                    F2R = BDAMath.Sqrt(Fsqr) * R;

                    K2 = F2sqr * Rsqr * Mathf.Sin(F2R) / (Mathf.Sin(F2R) - F2R * Mathf.Cos(F2R));
                }
                else
                {*/
                    // General derivation of aerodynamic constant for Kappa guidance
                    Fsqr = D0 * Lalpha * (TL + 1f) * (TL + 1f) / ((float)(ml.vessel.totalMass * ml.vessel.totalMass * sqrSpeed * sqrSpeed) * (2f * eta + TL));
                    FR = BDAMath.Sqrt(Fsqr) * R;
                //}

                if (FR > 16f)
                {
                    K2 = Fsqr * Rsqr / (FR - 1f);

                    //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: Kappa Guidance, {(thrust > D0 ? "under thrust - No Shaping - F2sqr" : "gliding - No Shaping - Fsqr")}: {Fsqr}, {(thrust > D0 ? "F2R" : "FR")}: {FR}, eFR: Infinity, enFR: 0, K1: 0, K2: {K2}");
                }
                else if (FR < 0.25f)
                {
                    // If FR is small, then we just avoid numerical weirdness and extra processing by setting
                    // K2 directly to 3, which is what the value converges to (which is the ideal gain for
                    // proportional navigation)
                    K2 = 3f;
                    //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: Kappa Guidance, {(thrust > D0 ? "under thrust - No Shaping - F2sqr" : "gliding - No Shaping - Fsqr")}: {Fsqr}, {(thrust > D0 ? "F2R" : "FR")}: {FR}, K1: 0, K2: 3");
                }
                else
                {
                    float eFR = Mathf.Exp(FR);
                    float enFR = Mathf.Exp(-FR);

                    K2 = (Fsqr * Rsqr * (eFR - enFR)) / (eFR * (FR - 1f) + enFR * (FR + 1f));

                    //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: Kappa Guidance, {(thrust > D0 ? "under thrust - No Shaping - F2sqr" : "gliding - No Shaping - Fsqr")}: {Fsqr}, {(thrust > D0 ? "F2R" : "FR")}: {FR}, eFR: {eFR}, enFR: {enFR}, K1: 0, K2: {K2}");
                }
            }

            // To compensate for planet curvature, we'll "unroll" the target position, and pretend the world is flat
            (double predictedImpactAlt, Vector3d targetPredUp) = (predictedImpactPoint - currentBody.position).MagNorm();
            // Don't go below ocean, or below current alt, if currently on an ocean-less planet
            double clampedPredictedImpactAlt = Math.Max(predictedImpactAlt, currentBody.ocean ? currentBody.Radius : (currentBody.Radius + Math.Min((ml.vessel.altitude - ml.vessel.radarAltitude), 0d)));

            float unrolledAngle = Mathf.Deg2Rad * VectorUtils.AnglePreNormalized(upDirection, targetPredUp);
            Vector3 adjustedPredictedRelImpactPoint;
            // We only unroll if the angle is > 0.01
            if (unrolledAngle > 0.01f)
            {
                double missileRad = ml.vessel.altitude + currentBody.Radius; // Radius of the missile relative to the center of the planet
                adjustedPredictedRelImpactPoint = (unrolledAngle * (float)(0.5 * (missileRad + clampedPredictedImpactAlt))) * planarDirectionToTarget + // Arc length at a radius equal to the average of the missile and the target
                                                    (float)(clampedPredictedImpactAlt - missileRad) * upDirection; // Vertical coord
            }
            else
            {
                // When below that angle, we'll just use the predictedImpactPoint as is, since quite frankly, it's not gonna make much of a difference
                adjustedPredictedRelImpactPoint = predictedImpactPoint + (clampedPredictedImpactAlt - predictedImpactAlt) * targetPredUp - ml.vessel.CoM;
            }

            // Acceleration per Kappa guidance
            // Final velocity is shaped by shapingAngle, we want the missile to dive onto the target but we don't want to affect the horizontal components of velocity
            if (shapingAngle == 0f)
            {
                vF = currVel;
                accel = (K2 * ttgoInv * ttgoInv) * (adjustedPredictedRelImpactPoint);
                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_MISSILES)
                {
                    ml.debugString.AppendLine($"K1: {K1:F2}, K2: {K2:F2}, posErr: {adjustedPredictedRelImpactPoint.ProjectOnPlanePreNormalized(velDirection)}, Shaping Guidance: False");
                }
            }
            else
            {
                if (targetAlt > maxAltitude)
                {
                    // Uh oh, really high altitude target... clamp that shapingAngle down a bit
                    shapingAngle *= Mathf.Clamp01(1.2f - targetAlt / maxAltitude);
                }
                vF = velDirection.ProjectOnPlanePreNormalized(targetPredUp).normalized;
                float cos = Mathf.Cos(shapingAngle * Mathf.Deg2Rad);
                float sin = BDAMath.Sqrt(1 - cos * cos);
                //vF = currSpeed * (Mathf.Cos(shapingAngle * Mathf.Deg2Rad) * vF - Mathf.Sin(shapingAngle * Mathf.Deg2Rad) * targetPredUp);
                vF = new Vector3(
                    currSpeed * (cos * vF.x - sin * (float)targetPredUp.x),
                    currSpeed * (cos * vF.y - sin * (float)targetPredUp.y),
                    currSpeed * (cos * vF.z - sin * (float)targetPredUp.z));

                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_MISSILES)
                {
                    ml.debugString.AppendLine($"K1: {K1:F2}, K2: {K2:F2}, posErr: {adjustedPredictedRelImpactPoint.ProjectOnPlanePreNormalized(velDirection)}, Shaping Guidance: True, velErr: {vF - currVel}, shapingAngle: {shapingAngle:F2}°");
                }

                K1 *= ttgoInv;
                K2 *= ttgoInv * ttgoInv;
                accel = new Vector3(
                    K1 * (vF.x - currVel.x) + K2 * adjustedPredictedRelImpactPoint.x,
                    K1 * (vF.y - currVel.y) + K2 * adjustedPredictedRelImpactPoint.y,
                    K1 * (vF.z - currVel.z) + K2 * adjustedPredictedRelImpactPoint.z);
                //accel = (K1 * ttgoInv) * (vF - currVel) + (K2 * ttgoInv * ttgoInv) * (adjustedPredictedRelImpactPoint);
            }

            // Angle Based Method -> Guarantees Normal Accel, but requires more processing
            /*float temp = Vector3.Dot(currVel, Rdir) / (currSpeed * currSpeed * Rsqr); // Magnitude of 1 / V * R cos(sigma), where sigma is the angle between currVel and Rdir
            Vector3 accel1 = K1 * temp * VelTripleProduct(currVel, vF); // Magnitude of K1 * V^2 / R * sin(del) * cos(sigma)
            Vector3 accel2 = K2 / (Rsqr) * VelTripleProduct(currVel, Rdir); // Magnitude of K2 * V^2 / R * sin(sigma)*/

            // Technically pos error should be: (predictedImpactPoint - ml.vessel.CoM - currVel * ttgo); but because we're projecting onto the vel plane, the latter is irrelevant.
            // It is important to remember that the ZEM, assuming no acceleration, is the positional error projected onto the vel plane.
            // Because adjustedPredictedRelImpactPoint is directly relative to the missile's CoM, we can directly use it instead of subtracting of the CoM
            //Vector3 accel = (K1 * ttgoInv) * (vF - currVel) + (K2 * ttgoInv * ttgoInv) * (adjustedPredictedRelImpactPoint);
            //accel = accel.ProjectOnPlanePreNormalized(velDirection);
            if (usegLimEq)
            {
                accel = (accel - ml.vessel.gravityForPos).ProjectOnPlanePreNormalized(velDirection);
                // gLimit is based solely on acceleration normal to the velocity vector, technically this guidance law gives
                // both normal acceleration and tangential acceleration but we can only really manage normal acceleration
                gLimit = (accel).magnitude / (float)PhysicsGlobals.GravitationalAcceleration;
            }
            else
            {
                accel = accel.ProjectOnPlanePreNormalized(velDirection);
                gLimit = -1f;
            }

            // Debug output, useful for tuning
            // NOTE: Think of better way to present these, right now they're just spamming the log, maybe add them to the debug string and display it via the UI?
            //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: Kappa Guidance - R: {R}, accel: {accel}, accelVert: {Vector3.Dot(accel, upDirection)}, accelVertVel: {Vector3.Dot(((K1 * ttgoInv) * (vF - currVel)).ProjectOnPlanePreNormalized(velDirection), upDirection)}, vF-currVel: {vF - currVel}, posError: {(predictedImpactPoint - ml.vessel.CoM).ProjectOnPlanePreNormalized(velDirection)}, posErrorUnrolled: {(adjustedPredictedRelImpactPoint - ml.vessel.CoM).ProjectOnPlanePreNormalized(velDirection)}, vertPosError: {Vector3.Dot((predictedImpactPoint - ml.vessel.CoM).ProjectOnPlanePreNormalized(velDirection), upDirection)}, vertPosErrorUnrolled: {Vector3.Dot((adjustedPredictedRelImpactPoint - ml.vessel.CoM).ProjectOnPlanePreNormalized(velDirection), upDirection)}, unrolledAngle: {unrolledAngle}, altDiff: {clampedPredictedImpactAlt - predictedImpactAlt}, missileAltDiff: {clampedPredictedImpactAlt - ml.vessel.altitude - FlightGlobals.currentMainBody.Radius}, vesselVertPos: {Vector3.Dot(ml.vessel.CoM, upDirection)}, g: {gLimit}, ttgo: {ttgo}");

            //accel = accel.ProjectOnPlanePreNormalized(velDirection);
            leadTime = Mathf.Min(leadTime, 3f);
            float leadTime2 = leadTime * leadTime;
            Vector3 currPos = ml.vessel.CoM;
            return new Vector3(
                    currPos.x + currVel.x * leadTime + accel.x * leadTime2,
                    currPos.y + currVel.y * leadTime + accel.y * leadTime2,
                    currPos.z + currVel.z * leadTime + accel.z * leadTime2);
        }

        public static Vector3 VelTripleProduct(Vector3 currVel, Vector3 desiredDir)
        {
            float velSqr = currVel.sqrMagnitude;
            float velDot = Vector3.Dot(currVel, desiredDir);

            return new Vector3(velSqr * desiredDir.x - velDot * currVel.x,
                               velSqr * desiredDir.y - velDot * currVel.y,
                               velSqr * desiredDir.z - velDot * currVel.z);
        }

        public static Vector3 GetAirToAirLoftTarget(Vector3 targetPosition, Vector3 targetVelocity,
            Vector3 targetAcceleration, Vessel missileVessel, float targetAlt, float maxAltitude,
            float rangeFactor, float vertVelComp, float velComp, float loftAngle, float termAngle,
            float termDist, float maneuvergLimit, float invManeuvergLimit, ref MissileBase.LoftStates loftState,
            out float timeToImpact, out float gLimit, out float targetDistance,
            MissileBase.GuidanceModes homingModeTerminal, float N, float optimumAirspeed = 200)
        {
            Vector3 velDirection = missileVessel.srf_vel_direction; //missileVessel.Velocity().normalized;

            targetDistance = Vector3.Distance(targetPosition, missileVessel.CoM);

            float currSpeed;// = Mathf.Max((float)missileVessel.srfSpeed, minSpeed);

            if (loftState == MissileBase.LoftStates.Boost)
                currSpeed = Mathf.Max((float)missileVessel.srfSpeed, optimumAirspeed);
            else
            {
                // If still accelerating
                if (Vector3.Dot(missileVessel.acceleration_immediate, velDirection) > 0)
                    currSpeed = Mathf.Max((float)missileVessel.srfSpeed, optimumAirspeed);
                else
                    currSpeed = (float)missileVessel.srfSpeed;
            }
                

            Vector3 currVel = currSpeed * velDirection;

            //Vector3 Rdir = (targetPosition - missileVessel.transform.position).normalized;
            //float rDot = Vector3.Dot(targetVelocity - currVel, Rdir);

            float leadTime = targetDistance / (targetVelocity - currVel).magnitude;
            //float leadTime = (targetDistance / rDot);

            timeToImpact = leadTime;
            leadTime = Mathf.Clamp(leadTime, 0f, 16f);

            gLimit = -1f;

            // If loft is not terminal
            if ((loftState < MissileBase.LoftStates.Terminal) && (targetDistance > termDist))
            {
                //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileGuidance]: Lofting");

                // Get up direction
                Vector3 upDirection = missileVessel.upAxis; //VectorUtils.GetUpDirection(missileVessel.CoM);

                // Use the gun aim-assist logic to determine ballistic angle (assuming no drag)
                Vector3 missileRelativePosition, missileRelativeVelocity, missileAcceleration, missileRelativeAcceleration, targetPredictedPosition, missileDropOffset, lastVelDirection, ballisticTarget, targetHorVel, targetCompVel;

                var firePosition = missileVessel.CoM; //+ (currSpeed * velDirection) * Time.fixedDeltaTime; // Bullets are initially placed up to 1 frame ahead (iTime). Not offsetting by part vel gives the correct initial placement.
                missileRelativePosition = targetPosition - firePosition;
                float timeToCPA = timeToImpact; // Rough initial estimate.
                targetPredictedPosition = AIUtils.PredictPosition(targetPosition, targetVelocity, Vector3.zero, timeToCPA);

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

                // Use simple lead compensation to minimize over-compensation
                // Get planar direction to target
                Vector3 planarDirectionToTarget =
                    ((AIUtils.PredictPosition(targetPosition, targetVelocity, Vector3.zero, leadTime + TimeWarp.fixedDeltaTime) - missileVessel.CoM).ProjectOnPlanePreNormalized(upDirection)).normalized;

                //float turnTimeOffset = 0f;

                if (loftState == MissileBase.LoftStates.Boost)
                {
                    // Get angle relative to vertical
                    float pullDownCos = Vector3.Dot(velDirection, upDirection);
                    // Make sure we're actually pulling down, otherwise our assumptions won't hold,
                    // Specifically, pullDownSin would require a negative sign in our turnLead
                    // calculation as it represents distance already covered. Similarly,
                    // instead of termAngle + turnAngleOffset, it'd be termAngle - turnAngleOffset
                    // in turnTimeOffset. Either way, this calculation is not required as it is
                    // expected that the user will have accounted for turn time required from
                    // horizontal to termAngle in the termAngle trigger point
                    if (pullDownCos > 0)
                    {
                        // If the target isn't above the loft angle
                        if (Mathf.Cos(loftAngle) * targetDistance > Vector3.Dot(missileRelativePosition, upDirection))
                        {
                            float pullDownSin = BDAMath.Sqrt(1f - pullDownCos * pullDownCos);
                            // Turn radius is mv^2/r = ma -> v^2/r = a -> v^2/a = r, a = 10 g -> v^2 * 1/(10 g) = r
                            // We use 1.5f * currSpeed to account for accelerating missiles
                            float tempSpeed = Mathf.Max(currSpeed * 1.1f, optimumAirspeed);

                            float curvatureCompensation = (1f - Vector3.Dot(upDirection, VectorUtils.GetUpDirection(targetPosition))) * (float)FlightGlobals.currentMainBody.Radius;
                            float turnRadius = (tempSpeed * tempSpeed * invg * invManeuvergLimit);

                            Vector3 turnLead = (turnRadius * (pullDownCos + Mathf.Sin(termAngle * Mathf.Deg2Rad))) * planarDirectionToTarget + (turnRadius * (1f - pullDownSin) - curvatureCompensation) * upDirection;

                            firePosition += turnLead;
                            //turnTimeOffset = (termAngle * Mathf.Deg2Rad + (Mathf.PI * 0.5f - Mathf.Acos(pullDownCos))) * tempSpeed * 0.0169952698051929473876953125f;
                        }
                        else
                            loftState = MissileBase.LoftStates.Midcourse;
                    }
                }

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
                    targetPredictedPosition = AIUtils.PredictPosition(targetPosition, targetCompVel, Vector3.zero, Mathf.Min(timeToCPA, 16f));
                    missileDropOffset = -0.5f * missileAcceleration * timeToCPA * timeToCPA;
                    ballisticTarget = targetPredictedPosition + missileDropOffset;
                    velDirection = (ballisticTarget - firePosition).normalized;
                } while (++count < 10 && VectorUtils.Angle(lastVelDirection, velDirection) > 1f); // 1° margin of error is sufficient to prevent premature firing (usually)


                // Determine horizontal and up components of velocity, calculate the elevation angle
                float velUp = Vector3.Dot(velDirection, upDirection);
                float velForwards = (velDirection - upDirection * velUp).magnitude;
                float angle = Mathf.Atan2(velUp, velForwards);

                //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: Loft Angle: [{(angle * Mathf.Rad2Deg):G3}]");

                // Check if termination angle agrees with termAngle
                if ((loftState < MissileBase.LoftStates.Midcourse) && (angle > -termAngle * Mathf.Deg2Rad))
                {
                    /*// If not yet at termination, simple lead compensation
                    targetPosition += targetVelocity * leadTime + 0.5f * leadTime * leadTime * targetAcceleration;

                    // Get planar direction to target
                    Vector3 planarDirectionToTarget = //(velDirection - upDirection * Vector3.Dot(velDirection, upDirection)).normalized;
                        ((targetPosition - missileVessel.transform.position).ProjectOnPlanePreNormalized(upDirection)).normalized;*/

                    // Altitude clamp based on rangeFactor and maxAlt, cannot be lower than target
                    float altitudeClamp = Mathf.Clamp(targetAlt + rangeFactor * Vector3.Dot(targetPosition - missileVessel.CoM, planarDirectionToTarget), targetAlt, Mathf.Max(maxAltitude, targetAlt));

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

                    //loftAngle = Mathf.Max(loftAngle, angle);

                    gLimit = maneuvergLimit;

                    //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: AAM Loft altitudeClamp: [{altitudeClamp:G6}] COS: [{Mathf.Cos(loftAngle * turnFactor * Mathf.Deg2Rad):G3}], SIN: [{Mathf.Sin(loftAngle * turnFactor * Mathf.Deg2Rad):G3}], turnFactor: [{turnFactor:G3}].");
                    return missileVessel.CoM + (float)missileVessel.srfSpeed * ((Mathf.Cos(loftAngle * turnFactor * Mathf.Deg2Rad) * planarDirectionToTarget) + (Mathf.Sin(loftAngle * turnFactor * Mathf.Deg2Rad) * upDirection));

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
                    loftState = MissileBase.LoftStates.Midcourse;

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

                    Vector3 finalTargetPos;

                    if (velUp > 0f)
                    {
                        // If the missile is told to go up, then we either try to go above the target or remain at the current altitude
                        /*return missileVessel.transform.position + (float)missileVessel.srfSpeed * new Vector3(velDirection.x - upDirection.x * velUp,
                            velDirection.y - upDirection.y * velUp,
                            velDirection.z - upDirection.z * velUp) + Mathf.Max(targetAlt - (float)missileVessel.altitude, 0f) * upDirection;*/
                        finalTargetPos = missileVessel.CoM + (float)missileVessel.srfSpeed * planarDirectionToTarget + Mathf.Max(targetAlt - (float)missileVessel.altitude, 0f) * upDirection;
                    } else
                    {
                        // Otherwise just fly towards the target according to velUp and velForwards
                        float spdUp = 0.25f * leadTime * (float)missileVessel.srfSpeed * velUp, spdF = 0.25f * leadTime * (float)missileVessel.srfSpeed * velForwards;
                        finalTargetPos = new Vector3(missileVessel.CoM.x + spdUp * upDirection.x + spdF * planarDirectionToTarget.x,
                            missileVessel.CoM.y + spdUp * upDirection.y + spdF * planarDirectionToTarget.y,
                            missileVessel.CoM.z + spdUp * upDirection.z + spdF * planarDirectionToTarget.z);
                    }

                    // If the target is at <  2 * termDist start mixing
                    if (targetDistance < 3f * termDist)
                    {
                        float blendFac = (targetDistance - termDist) / termDist;
                        blendFac *= 0.25f * blendFac;

                        Vector3 aamTarget;
                        
                        switch (homingModeTerminal)
                        {
                            case MissileBase.GuidanceModes.PN:
                                {
                                    aamTarget = (1f - blendFac) * GetPNTarget(targetPosition, targetVelocity, missileVessel, N, out timeToImpact, out gLimit) + blendFac * finalTargetPos;
                                    break;
                                }
                            case MissileBase.GuidanceModes.APN:
                                {
                                    aamTarget = (1f - blendFac) * GetAPNTarget(targetPosition, targetVelocity, targetAcceleration, missileVessel, N, out timeToImpact, out gLimit) + blendFac * finalTargetPos;
                                    break;
                                }
                            case MissileBase.GuidanceModes.AAMPure:
                                {
                                    return (1f - blendFac) * targetPosition + blendFac * finalTargetPos;
                                }
                            case MissileBase.GuidanceModes.AAMLead:
                                {
                                    return (1f - blendFac) * AIUtils.PredictPosition(targetPosition, targetVelocity, targetAcceleration, leadTime + TimeWarp.fixedDeltaTime) + blendFac * finalTargetPos;
                                }
                            default:
                                {
                                    aamTarget = (1f - blendFac) * GetPNTarget(targetPosition, targetVelocity, missileVessel, N, out timeToImpact, out gLimit) + blendFac * finalTargetPos; // Default to PN
                                    break;
                                }
                        }

                        gLimit += 10f * blendFac;

                        return aamTarget;
                    }
                    //else
                    //    gLimit = Mathf.Clamp(20f * (1 - (targetDistance - termDist - 100f) / Mathf.Clamp(termDist * 4f, 5000f, 25000f)), 10f, 20f);


                    // No mixing if targetDistance > 3 * termDist
                    return finalTargetPos;

                    //if (velUp > 0f)
                    //{
                    //    // If the missile is told to go up, then we either try to go above the target or remain at the current altitude
                    //    /*return missileVessel.transform.position + (float)missileVessel.srfSpeed * new Vector3(velDirection.x - upDirection.x * velUp,
                    //        velDirection.y - upDirection.y * velUp,
                    //        velDirection.z - upDirection.z * velUp) + Mathf.Max(targetAlt - (float)missileVessel.altitude, 0f) * upDirection;*/
                    //    return missileVessel.transform.position + (float)missileVessel.srfSpeed * planarDirectionToTarget + Mathf.Max(targetAlt - (float)missileVessel.altitude, 0f) * upDirection;
                    //}

                    //// Otherwise just fly towards the target according to velUp and velForwards
                    ////return missileVessel.transform.position + (float)missileVessel.srfSpeed *  velDirection;
                    //return missileVessel.transform.position + (float)missileVessel.srfSpeed * new Vector3(velUp * upDirection.x + velForwards * planarDirectionToTarget.x,
                    //    velUp * upDirection.y + velForwards * planarDirectionToTarget.y,
                    //    velUp * upDirection.z + velForwards * planarDirectionToTarget.z);
                }
            }
            else
            {
                // If terminal just go straight for target + lead
                loftState = MissileBase.LoftStates.Terminal;
                //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileGuidance]: Terminal");

                if (targetDistance < 3f * termDist)
                {
                    float blendFac = 0f;
                    Vector3 targetPos = Vector3.zero;

                    if ((targetDistance > termDist) && (homingModeTerminal != MissileBase.GuidanceModes.AAMLead) && (homingModeTerminal != MissileBase.GuidanceModes.AAMPure))
                    {
                        blendFac = (targetDistance - termDist) / termDist;
                        blendFac *= 0.25f * blendFac;
                        targetPos = AIUtils.PredictPosition(targetPosition, targetVelocity, Vector3.zero, leadTime + TimeWarp.fixedDeltaTime);
                    }

                    Vector3 aamTarget;

                    switch (homingModeTerminal)
                    {
                        case MissileBase.GuidanceModes.PN:
                            {
                                aamTarget = (1f - blendFac) * GetPNTarget(targetPosition, targetVelocity, missileVessel, N, out timeToImpact, out gLimit) + blendFac * targetPos;
                                break; 
                            }
                        case MissileBase.GuidanceModes.APN:
                            {
                                aamTarget = (1f - blendFac) * GetAPNTarget(targetPosition, targetVelocity, targetAcceleration, missileVessel, N, out timeToImpact, out gLimit) + blendFac * targetPos;
                                break;
                            }
                        case MissileBase.GuidanceModes.AAMLead:
                            {
                                return AIUtils.PredictPosition(targetPosition, targetVelocity, targetAcceleration, leadTime + TimeWarp.fixedDeltaTime);
                            }
                        case MissileBase.GuidanceModes.AAMPure:
                            {
                                return targetPosition; 
                            }
                        default:
                            {
                                return (1f - blendFac) * GetPNTarget(targetPosition, targetVelocity, missileVessel, N, out timeToImpact, out gLimit) + blendFac * targetPos; // Default to PN
                            }
                    }

                    gLimit += 10f * blendFac;

                    return aamTarget;
                }
                else
                {
                    return AIUtils.PredictPosition(targetPosition, targetVelocity, Vector3.zero, leadTime + TimeWarp.fixedDeltaTime); //targetPosition + targetVelocity * leadTime + 0.5f * leadTime * leadTime * targetAcceleration;
                    //return targetPosition + targetVelocity * leadTime;
                }
            }
        }

/*        public static Vector3 GetAirToAirHybridTarget(Vector3 targetPosition, Vector3 targetVelocity,
            Vector3 targetAcceleration, Vessel missileVessel, float termDist, out float timeToImpact,
            MissileBase.GuidanceModes homingModeTerminal, float N, float minSpeed = 200)
        {
            Vector3 velDirection = missileVessel.srf_vel_direction; //missileVessel.Velocity().normalized;

            float targetDistance = Vector3.Distance(targetPosition, missileVessel.transform.position);

            float currSpeed = Mathf.Max((float)missileVessel.srfSpeed, minSpeed);
            Vector3 currVel = currSpeed * velDirection;

            float leadTime = targetDistance / (targetVelocity - currVel).magnitude;

            timeToImpact = leadTime;
            leadTime = Mathf.Clamp(leadTime, 0f, 8f);

            if (targetDistance < termDist)
            {
                if (homingModeTerminal == MissileBase.GuidanceModes.APN)
                    return GetAPNTarget(targetPosition, targetVelocity, targetAcceleration, missileVessel, N, out timeToImpact);
                else if (homingModeTerminal == MissileBase.GuidanceModes.PN)
                    return GetPNTarget(targetPosition, targetVelocity, missileVessel, N, out timeToImpact);
                else if (homingModeTerminal == MissileBase.GuidanceModes.AAMPure)
                    return targetPosition;
                else
                    return AIUtils.PredictPosition(targetPosition, targetVelocity, targetAcceleration, leadTime + TimeWarp.fixedDeltaTime);
            }
            else
            {
                return AIUtils.PredictPosition(targetPosition, targetVelocity, targetAcceleration, leadTime + TimeWarp.fixedDeltaTime); //targetPosition + targetVelocity * leadTime + 0.5f * leadTime * leadTime * targetAcceleration;
                                                                                                                                        //return targetPosition + targetVelocity * leadTime;
            }
        }*/

        public static Vector3 GetAirToAirTargetModular(Vector3 targetPosition, Vector3 targetVelocity, Vector3 targetAcceleration, Vessel missileVessel, out float timeToImpact)
        {
            float targetDistance = Vector3.Distance(targetPosition, missileVessel.CoM);

            //Basic lead time calculation
            Vector3 currVel = missileVessel.Velocity();
            timeToImpact = targetDistance / (targetVelocity - currVel).magnitude;

            // Calculate time to CPA to determine target position
            float timeToCPA = missileVessel.TimeToCPA(targetPosition, targetVelocity, targetAcceleration, 16f);
            timeToImpact = (timeToCPA < 16f) ? timeToCPA : timeToImpact;
            // Ease in velocity from 16s to 8s, ease in acceleration from 8s to 2s using the logistic function to give smooth adjustments to target point.
            float easeAccel = Mathf.Clamp01(1.1f / (1f + Mathf.Exp((timeToCPA - 5f))) - 0.05f);
            float easeVel = Mathf.Clamp01(2f - timeToCPA / 8f);
            return AIUtils.PredictPosition(targetPosition, targetVelocity * easeVel, targetAcceleration * easeAccel, timeToCPA + TimeWarp.fixedDeltaTime); // Compensate for the off-by-one frame issue.
        }

        public static Vector3 GetPNTarget(Vector3 targetPosition, Vector3 targetVelocity, Vessel missileVessel, float N, out float timeToGo, out float gLimit)
        {
            Vector3 missileVel = missileVessel.Velocity();
            Vector3 relVelocity = targetVelocity - missileVel;
            Vector3 relRange = targetPosition - missileVessel.CoM;
            float dotP = Vector3.Dot(relRange, relVelocity);
            // If the target is receding or we have zero relative velocity, both of which produce undefined behavior
            if (dotP >= 0)
            {
                gLimit = -1f;
                return GetAirToAirTarget(targetPosition, targetVelocity, Vector3.zero, missileVessel, out timeToGo);
            }
            timeToGo = -relRange.sqrMagnitude / dotP;
            Vector3 RotVector = Vector3.Cross(relRange, relVelocity) / relRange.sqrMagnitude;
            Vector3 RefVector = missileVel.normalized;
            Vector3 normalAccel = -N * relVelocity.magnitude * Vector3.Cross(RefVector, RotVector);
            gLimit = normalAccel.magnitude / (float)PhysicsGlobals.GravitationalAcceleration;
            //timeToGo = missileVessel.TimeToCPA(targetPosition, targetVelocity, Vector3.zero, 120f);
            float leadTime = Mathf.Clamp(timeToGo, 0f, 8f);
            //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance] PN gLimit: {gLimit}, dot: {Vector3.Dot(relRange, relVelocity)}, ttgo: {timeToGo}"); 
            return missileVessel.CoM + leadTime * missileVel + 0.5f * leadTime * leadTime * normalAccel;
        }

        private static Vector3 GetPNAccel(Vector3 targetPosition, Vector3 targetVelocity, Vessel missileVessel, float N)
        {
            Vector3 missileVel = missileVessel.Velocity();
            Vector3 relVelocity = targetVelocity - missileVel;
            Vector3 relRange = targetPosition - missileVessel.CoM;
            Vector3 RotVector = Vector3.Cross(relRange, relVelocity) / relRange.sqrMagnitude;
            Vector3 RefVector = missileVel.normalized;
            Vector3 normalAccel = -N * relVelocity.magnitude * Vector3.Cross(RefVector, RotVector);
            //gLimit = normalAccel.magnitude / (float)PhysicsGlobals.GravitationalAcceleration;
            //timeToGo = missileVessel.TimeToCPA(targetPosition, targetVelocity, Vector3.zero, 120f);
            return normalAccel;
        }

        public static Vector3 GetAPNTarget(Vector3 targetPosition, Vector3 targetVelocity, Vector3 targetAcceleration, Vessel missileVessel, float N, out float timeToGo, out float gLimit)
        {
            Vector3 missileVel = missileVessel.Velocity();
            Vector3 relVelocity = targetVelocity - missileVel;
            Vector3 relRange = targetPosition - missileVessel.CoM;
            timeToGo = -relRange.sqrMagnitude / Vector3.Dot(relRange, relVelocity);
            if (timeToGo < 0)
            {
                gLimit = -1f;
                return GetAirToAirTarget(targetPosition, targetVelocity, Vector3.zero, missileVessel, out timeToGo);
            }
            Vector3 RotVector = Vector3.Cross(relRange, relVelocity) / Vector3.Dot(relRange, relRange);
            Vector3 RefVector = missileVel.normalized;
            Vector3 normalAccel = -N * relVelocity.magnitude * Vector3.Cross(RefVector, RotVector);
            // float tgo = relRange.magnitude / relVelocity.magnitude;
            Vector3 accelBias = Vector3.Cross(relRange.normalized, targetAcceleration);
            accelBias = Vector3.Cross(RefVector, accelBias);
            normalAccel -= 0.5f * N * accelBias;
            gLimit = normalAccel.magnitude / (float)PhysicsGlobals.GravitationalAcceleration;
            float leadTime = Mathf.Clamp(timeToGo, 0f, 8f);
            //timeToGo = missileVessel.TimeToCPA(targetPosition, targetVelocity, targetAcceleration, 120f);
            return missileVessel.CoM + leadTime * missileVel + 0.5f * leadTime * leadTime * normalAccel;
        }
        public static float GetLOSRate(Vector3 targetPosition, Vector3 targetVelocity, Vessel missileVessel)
        {
            Vector3 missileVel = missileVessel.Velocity();
            Vector3 relVelocity = targetVelocity - missileVel;
            Vector3 relRange = targetPosition - missileVessel.CoM;
            Vector3 RotVector = Vector3.Cross(relRange, relVelocity) / Vector3.Dot(relRange, relRange);
            Vector3 LOSRate = Mathf.Rad2Deg * RotVector;
            return LOSRate.magnitude;
        }

        public static Vector3 GetAirToAirFireSolution(MissileBase missile, Vessel targetVessel)
        {
            return GetAirToAirFireSolution(missile, targetVessel, out float temp);
        }

        /// <summary>
        /// Air-2-Air fire solution used by the AI for steering, WM checking if a missile can be launched, unguided missiles
        /// </summary>
        /// <param name="missile"></param>
        /// <param name="targetVessel"></param>
        /// <returns></returns>
        public static Vector3 GetAirToAirFireSolution(MissileBase missile, Vessel targetVessel, out float timetogo)
        {
            if (!targetVessel)
            {
                timetogo = float.PositiveInfinity;
                return missile.vessel.CoM + (missile.GetForwardTransform() * 1000);
            }
            Vector3 targetPosition = targetVessel.CoM;
            Vector3 vel = missile.vessel.Velocity();
            Vector3 startPosition = missile.vessel.CoM;
            if (missile.GetWeaponClass() == WeaponClasses.SLW && !missile.vessel.LandedOrSplashed)
            {
                vel = Vector3.zero;  //impact w/ water is going to bring starting torp speed basically down to 0, not whatever plane airspeed was
                float torpDropTime = BDAMath.Sqrt(2 * (float)missile.vessel.altitude / (float)FlightGlobals.getGeeForceAtPosition(missile.vessel.CoM).magnitude);
                startPosition += missile.vessel.srf_vel_direction * (missile.vessel.horizontalSrfSpeed * torpDropTime); //torp will spend multiple seconds dropping falling at parent vessel speed
                startPosition -= (float)FlightGlobals.getAltitudeAtPos(startPosition) * missile.vessel.up;
                targetPosition += targetVessel.Velocity() * torpDropTime; //so offset start positions appropriately
            }
            float leadTime = 0;
            float targetDistance = Vector3.Distance(targetPosition, startPosition);

            MissileLauncher launcher = missile as MissileLauncher;
            BDModularGuidance modLauncher = missile as BDModularGuidance;
    
            float accel = launcher != null ? (launcher.thrust / missile.part.mass) : modLauncher != null ? (modLauncher.thrust/modLauncher.mass) : 10;
            
            if (missile.vessel.InNearVacuum() && missile.vessel.InOrbit()) // In orbit, use orbital calc
            {
                float timeToImpact;
                Vector3 relPos = targetVessel.CoM - missile.vessel.CoM;
                Vector3 relVel = vel - targetVessel.Velocity();
                Vector3 relAccel = targetVessel.acceleration_immediate - missile.GetForwardTransform() * accel;

                float thrustTime = launcher != null ? launcher.boostTime : modLauncher != null ? (modLauncher.MaxSpeed / accel) : 8;

                timeToImpact = AIUtils.TimeToCPA(relPos, relVel, relAccel, thrustTime);
                if (timeToImpact == thrustTime)
                {
                    relPos = AIUtils.PredictPosition(targetPosition, targetVessel.Velocity(), targetVessel.acceleration_immediate, thrustTime) -
                    AIUtils.PredictPosition(missile.vessel.CoM, vel, missile.GetForwardTransform() * accel, thrustTime);
                    relVel += relAccel * timeToImpact;
                    relAccel = targetVessel.acceleration_immediate;
                    timeToImpact = AIUtils.TimeToCPA(relPos, relVel, relAccel, 60f);
                    leadTime = thrustTime + timeToImpact;   
                }
                else
                    leadTime = timeToImpact;
                targetPosition += leadTime * (targetVessel.Velocity() - vel) + 0.5f * leadTime * leadTime * targetVessel.acceleration_immediate;
            }
            else // In atmo, use in-atmo calculations
            {
                Vector3 VelOpt = missile.GetForwardTransform() * (launcher != null ? launcher.optimumAirspeed : 1500);
                Vector3 deltaVel = targetVessel.Velocity() - vel;
                Vector3 DeltaOptvel = targetVessel.Velocity() - VelOpt;
                float T = Mathf.Clamp(Vector3.Project(VelOpt - vel, missile.GetForwardTransform()).magnitude / accel, 0, 8); //time to optimal airspeed

                Vector3 relPosition = targetPosition - startPosition;
                Vector3 relAcceleration = targetVessel.acceleration_immediate - missile.GetForwardTransform() * accel;
                leadTime = AIUtils.TimeToCPA(relPosition, deltaVel, relAcceleration, T); //missile accelerating, T is greater than our max look time of 8s
                if (T < 8 && leadTime == T)//missile has reached max speed, and is now cruising; sim positions ahead based on T and run CPA from there
                {
                    relPosition = AIUtils.PredictPosition(targetPosition, targetVessel.Velocity(), targetVessel.acceleration_immediate, T) -
                        AIUtils.PredictPosition(startPosition, vel, missile.GetForwardTransform() * accel, T);
                    relAcceleration = targetVessel.acceleration_immediate; // - missile.MissileReferenceTransform.forward * 0; assume missile is holding steady velocity at optimumAirspeed
                    leadTime = AIUtils.TimeToCPA(relPosition, DeltaOptvel, relAcceleration, 8 - T) + T;
                }

                targetPosition += leadTime * targetVessel.Velocity();

                if (targetVessel && targetDistance < 800) //TODO - investigate if this would throw off aim accuracy
                {
                    targetPosition += (Vector3)targetVessel.acceleration_immediate * 0.05f * leadTime * leadTime;
                }
            }

            timetogo = leadTime;
            return targetPosition;
        }
        /// <summary>
        /// Air-2-Air lead offset calcualtion used for aiming missile turrets
        /// </summary>
        /// <param name="missile"></param>
        /// <param name="targetPosition"></param>
        /// <param name="targetVelocity"></param>
        /// <returns></returns>
        public static Vector3 GetAirToAirFireSolution(MissileBase missile, Vector3 targetPosition, Vector3 targetVelocity, bool turretLoft = false, float turretLoftFac = 0.5f)
        {
            MissileLauncher launcher = missile as MissileLauncher;
            BDModularGuidance modLauncher = missile as BDModularGuidance;
            bool inSpace = missile.vessel.InNearVacuum() && missile.vessel.InOrbit();
            float leadTime = 0;
            float maxSimTime = 8f;
            Vector3 leadPosition = targetPosition;
            Vector3 vel = missile.vessel.Velocity();
            Vector3 leadDirection, velOpt;
            float accel = launcher != null ? ((Mathf.Clamp01(launcher.boostTime / maxSimTime)) * launcher.thrust + Mathf.Clamp01((maxSimTime - launcher.cruiseDelay - launcher.boostTime) / maxSimTime) * launcher.cruiseThrust) / missile.part.mass
                : modLauncher.thrust / modLauncher.mass;
            float leadTimeError = 1f;
            float missileVelOpt = launcher != null ? launcher.optimumAirspeed : 1500;
                int count = 0;
                do
                {
                    leadDirection = leadPosition - missile.vessel.CoM;
                    float targetDistance = leadDirection.magnitude;
                    leadDirection.Normalize();
                    velOpt = (inSpace ? BDAMath.Sqrt(2f * accel * targetDistance) * leadDirection + vel : missileVelOpt * leadDirection);
                    float deltaVel = Vector3.Dot(targetVelocity - vel, leadDirection);
                    float deltaVelOpt = Vector3.Dot(targetVelocity - velOpt, leadDirection);
                    float T = Mathf.Clamp((velOpt - vel).magnitude / accel, 0, maxSimTime); //time to optimal airspeed, clamped to at most 8s
                    float D = deltaVel * T + 0.5f * accel * (T * T); //relative distance covered accelerating to optimum airspeed
                    leadTimeError = -leadTime;
                    if (targetDistance > D) leadTime = (targetDistance - D) / deltaVelOpt + T;
                    else leadTime = (-deltaVel - BDAMath.Sqrt((deltaVel * deltaVel) + 2f * accel * targetDistance)) / accel;
                    leadTime = Mathf.Clamp(leadTime, 0f, maxSimTime);
                    leadTimeError += leadTime;
                    leadPosition = AIUtils.PredictPosition(targetPosition, targetVelocity - (inSpace ? vel : Vector3.zero), Vector3.zero, leadTime);
                } while (++count < 5 && Mathf.Abs(leadTimeError) > 1e-3f);  // At most 5 iterations to converge. Also, 1e-2f may be sufficient.

            if (!missile.vessel.InNearVacuum() && turretLoft)
            {
                Vector3 relPos = leadPosition - missile.vessel.CoM;
                float vertDist = Vector3.Dot(relPos, missile.vessel.upAxis);
                float horzDist = (float)(relPos - vertDist * missile.vessel.upAxis).magnitude;
                float g = (float)missile.vessel.mainBody.GeeASL;
                float theta;

                missileVelOpt *= turretLoftFac;
                float missileVelOptSqr = missileVelOpt * missileVelOpt;

                float det = missileVelOptSqr * missileVelOptSqr - g * (g * horzDist * horzDist + 2f * vertDist * missileVelOptSqr);
                if (det > 0f)
                {
                    // Regular angle based on projectile motion
                    theta = Mathf.Atan((missileVelOptSqr - BDAMath.Sqrt(det)) / (g * horzDist));
                }
                else
                {
                    // Angle to hit the furthest possible target at that elevation
                    theta = Mathf.Atan(missileVelOpt / (BDAMath.Sqrt(missileVelOptSqr - 2f * g * vertDist)));
                }
                theta *= Mathf.Rad2Deg;

                float angle = 90f - VectorUtils.Angle(relPos, missile.vessel.upAxis);
                if (theta > angle)
                {
                    leadPosition = missile.vessel.CoM + Vector3.RotateTowards(relPos, missile.vessel.upAxis, (theta - angle) * Mathf.Deg2Rad, vertDist);
                    //leadPosition = missile.vessel.CoM + Vector3.Slerp(relPos, missile.vessel.upAxis, (theta - angle) / (angle - 90f));
                }
            }

            // Don't lead so much you end up leading behind the vehicle...
            if (Vector3.Dot(targetPosition - missile.vessel.CoM, leadPosition - missile.vessel.CoM) > 0)
            {
                return leadPosition;
            }
                
            return targetPosition;
        }

        public static Vector3 GetCruiseTarget(Vector3 targetPosition, Vessel missileVessel, float radarAlt)
        {
            Vector3 upDirection = missileVessel.upAxis; //VectorUtils.GetUpDirection(missileVessel.transform.position);
            float currentRadarAlt = GetRadarAltitude(missileVessel);
            float distanceSqr =
                (targetPosition - (missileVessel.CoM - (currentRadarAlt * upDirection))).sqrMagnitude;

            Vector3 planarDirectionToTarget = (targetPosition - missileVessel.CoM).ProjectOnPlanePreNormalized(upDirection).normalized;

            float error;

            if (currentRadarAlt > 1600)
            {
                error = 500000;
            }
            else
            {
                Vector3 tRayDirection = (planarDirectionToTarget * 10) - (10 * upDirection);
                Ray terrainRay = new Ray(missileVessel.CoM, tRayDirection);
                RaycastHit rayHit;

                if (Physics.Raycast(terrainRay, out rayHit, 8000, (int)(LayerMasks.Scenery | LayerMasks.EVA))) // Why EVA?
                {
                    float detectedAlt =
                        Vector3.Project(rayHit.point - missileVessel.CoM, upDirection).magnitude;

                    error = Mathf.Min(detectedAlt, currentRadarAlt) - radarAlt;
                }
                else
                {
                    error = currentRadarAlt - radarAlt;
                }
            }

            error = Mathf.Clamp(0.05f * error, -5, 3);
            return missileVessel.CoM + (10 * planarDirectionToTarget) - (error * upDirection);
        }

        public static Vector3 GetTerminalManeuveringTarget(Vector3 targetPosition, Vessel missileVessel, float radarAlt)
        {
            Vector3 upDirection = missileVessel.upAxis;
            Vector3 planarVectorToTarget = (targetPosition - missileVessel.CoM).ProjectOnPlanePreNormalized(upDirection);
            Vector3 planarDirectionToTarget = planarVectorToTarget.normalized;
            Vector3 crossAxis = Vector3.Cross(planarDirectionToTarget, upDirection).normalized;
            float sinAmplitude = Mathf.Clamp(Vector3.Distance(targetPosition, missileVessel.CoM) - 850, 0,
                4500);
            Vector3 sinOffset = (Mathf.Sin(1.25f * Time.time) * sinAmplitude * crossAxis);
            Vector3 targetSin = targetPosition + sinOffset;
            Vector3 planarSin = missileVessel.CoM + planarVectorToTarget + sinOffset;

            Vector3 finalTarget;
            float finalDistance = 2500 + GetRadarAltitude(missileVessel);
            if ((targetPosition - missileVessel.CoM).sqrMagnitude > finalDistance * finalDistance)
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

        public static FloatCurve DefaultLiftCurve = new([
            new(0, 0, 0.04375f, 0.04375f),
            new(8, 0.35f, 0.04801136f, 0.04801136f),
            //new(19, 1f),
            //new(23, 0.9f),
            new(30, 1.5f),
            new(65, 0.6f),
            new(90, 0.7f)
            ]);

        public static FloatCurve DefaultDragCurve = new([
            //new(0, 0.00215f, 0.00014f, 0.00014f),
            //new(5, .00285f, 0.0002775f, 0.0002775f),
            //new(15, .007f, 0.0003146428f, 0.0003146428f),
            //new(29, .01f, 0.0002142857f, 0.01115385f),
            //new(55, .3f, 0.008434067f, 0.008434067f),
            //new(90, .5f, 0.005714285f, 0.005714285f)
            new(0f, 0.00215f, 0f, 0f),
            new(5f, 0.00285f, 0.0002775f, 0.0002775f),
            new(30f, 0.01f, 0.0002142857f, 0.01115385f),
            new(55f, 0.3f, 0.008434067f, 0.008434067f),
            new(90f, 0.5f, 0.005714285f, 0.005714285f)
        ]);

        // The below curves and constants are derived from the lift and drag curves and will need to be re-calculated
        // if these are changed

        const float TRatioInflec1 = 1.181181181181181f; // Thrust to Lift Ratio (at AoA of 30) where the maximum occurs
        // after the 65 degree mark
        const float TRatioInflec2 = 2.242242242242242f; // Thrust to Lift Ratio (at AoA of 30) where a local maximum no
        // longer exists, above this every section must be searched

        public static FloatCurve AoACurve = new([
            new(0.0000000000f, 30.0000000000f, 5.577463f, 5.577463f),
            new(0.7107107107f, 33.9639639640f, 6.24605f, 6.24605f),
            new(1.5315315315f, 39.6396396396f, 8.396343f, 8.396343f),
            new(1.9419419419f, 43.6936936937f, 12.36403f, 12.36403f),
            new(2.1421421421f, 46.6666666667f, 19.63926f, 19.63926f),
            new(2.2122122122f, 48.3783783784f, 34.71423f, 34.71423f),
            new(2.2422422422f, 49.7297297297f, 44.99994f, 44.99994f)
        ]); // Floatcurve containing AoA of (local) max acceleration
        // for a given thrust to lift (at the max CL of 1.5 at 30 degrees of AoA) ratio. Limited to a max
        // of TRatioInflec2 where a local maximum no longer exists

        public static FloatCurve AoAEqCurve = new([
            new(1.1911911912f, 89.6396396396f, -53.40001f, -53.40001f),
            new(1.3413413413f, 81.6216216216f, -49.69999f, -49.69999f),
            new(1.5215215215f, 73.3333333333f, -37.62499f, -37.62499f),
            new(1.7217217217f, 67.4774774775f, -24.31731f, -24.31731f),
            new(1.9819819820f, 62.4324324324f, -24.09232f, -24.09232f),
            new(2.1821821822f, 56.6666666667f, -48.1499f, -48.1499f),
            new(2.2422422422f, 52.6126126126f, -67.49978f, -67.49978f)
        ]); // Floatcurve containing AoA after which the acceleration goes above
        // that of the local maximums'. Only exists between TRatioInflec1 and TRatioInflec2.

        public static FloatCurve gMaxCurve = new([
            new(0.0000000000f, 1.5000000000f, 0.8248255f, 0.8248255f),
            new(1.2012012012f, 2.4907813293f, 0.8942869f, 0.8942869f),
            new(1.9119119119f, 3.1757276995f, 1.019205f, 1.019205f),
            new(2.2422422422f, 3.5307206802f, 1.074661f, 1.074661f)
        ]); // Floatcurve containing max acceleration times the mass (total force)
        // normalized by q*S*GLOBAL_LIFT_MULTIPLIER for TRatio between 0 and TRatioInflec2. Note that after TRatioInflec1
        // this becomes a local maxima not a global maxima. This is used to narrow down what part of the curve we should
        // solve on.

        // Linearized CL v.s. AoA curve to enable fast solving. Algorithm performs bisection using the fast calculations of the bounds
        // and then performs a linear solve 
        public static float[] linAoA = { 0f, 10f, 24f, 30f, 38f, 57f, 65f, 90f };
        public static float[] linCL = { 0f, 0.454444597111092f, 1.34596044049850f, 1.5f, 1.38043381924198f, 0.719566180758018f, 0.6f, 0.7f };
        // Sin at the points
        public static float[] linSin = { 0f, 0.173648177666930f, 0.406736643075800f, 0.5f, 0.615661475325658f, 0.838670567945424f, 0.906307787036650f, 1f };
        // Slope of CL at the intervals
        public static float[] linSlope = { 0.0454444597111092f, 0.0636797030991005f, 0.0256732599169169f, -0.0149457725947522f, -0.0347825072886297f, -0.0149457725947522f, 0.004f };
        // y-Intercept of line at those intervals
        public static float[] linIntc = { 0f, -0.182352433879912f, 0.729802202492494f, 1.94837317784257f, 2.70216909620991f, 1.57147521865889f, 0.34f };

        public static float getGLimit(MissileLauncher ml, float thrust, float gLim, float margin, float maxAoA)//, out bool gLimited)
        {
            // Force required to reach g-limit
            gLim *= (float)(ml.vessel.totalMass * PhysicsGlobals.GravitationalAcceleration);

            if (ml.vessel.InVacuum())
            {
                return thrust > 0 ? Mathf.Acos(gLim / thrust) * Mathf.Rad2Deg : 180f;
                //return 180f; // if in vacuum g-limiting should be done via throttle modulation
            }
            
            //bool gLimited = false;

            //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance] gLim: {gLim}");

            //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance] force: {gLim}");

            //float maxAoA = ml.maxAoA;

            float currAoA = maxAoA;

            int interval = 0;

            // Factor by which to multiply the lift coefficient to get lift, it's the dynamic pressure times the lift area times
            // the global lift multiplier
            float qSk = (float) (0.5 * ml.vessel.atmDensity * ml.vessel.srfSpeed * ml.vessel.srfSpeed) * ml.currLiftArea * BDArmorySettings.GLOBAL_LIFT_MULTIPLIER;

            float currG = 0;

            // If we're in the post thrust state
            if (thrust == 0)
            {
                // If the maximum lift achievable is not enough to reach the request accel
                // the we turn to the AoA required for max lift
                if (gLim > 1.5f * qSk)
                {
                    currAoA = 30f;
                }
                else
                {
                    // Otherwise, first we calculate the lift in interval 2 (between 24 and 30 AoA)
                    currG = linCL[2] * qSk; // CL(alpha)*qSk + thrust*sin(alpha)

                    // If the resultant g at 24 AoA is < gLim then we're in interval 2
                    if (currG < gLim)
                    {
                        interval = 2;
                    }
                    else
                    {
                        // Otherwise check interval 1
                        currG = linCL[1] * qSk;
                        
                        if (currG > gLim)
                        {
                            // If we're still > gLim then we're in interval 0
                            interval = 0;
                        }
                        else
                        {
                            // Otherwise we're in interval 1
                            interval = 1;
                        }
                    }

                    // Calculate AoA for G, since no thrust we can use the faster linear equation
                    currAoA = calcAoAforGLinear(qSk, gLim, linSlope[interval], linIntc[interval], 0);
                }

                // Are we gLimited?
                //gLimited = currAoA < maxAoA;
                return currAoA < maxAoA ? currAoA : maxAoA;
            }
            else
            {
                // If we're under thrust, first calculate the ratio of Thrust to lift at max CL
                float TRatio = thrust / (1.5f * qSk);

                // Initialize bisection limits
                int LHS = 0;
                int RHS = 7;

                if (TRatio < TRatioInflec2)
                {
                    // If we're below TRatioInflec2 then we know there's a local max
                    currG = qSk * gMaxCurve.Evaluate(TRatio);

                    if (TRatio > TRatioInflec1)
                    {
                        // If we're above TRatioInflec1 then we know it's only a local max

                        // First calculate the allowable force margin
                        // This exists because drag gets very bad above the local max
                        margin = Mathf.Max(margin, 0f);
                        margin *= (float)ml.vessel.totalMass;

                        if (gLim < currG + margin)
                        {
                            // If we're within the margin
                            if (currG > gLim)
                            {
                                // And our local max is > gLim, then we know that 
                                // there is a solution. Calculate the AoAMax
                                // where the local max occurs
                                float AoAMax = AoACurve.Evaluate(TRatio);
                                
                                // And determine our right hand bound based on
                                // our AoAMax
                                if (AoAMax > linAoA[4])
                                {
                                    RHS = 5;
                                }
                                else if (AoAMax > linAoA[3])
                                {
                                    RHS = 4;
                                }
                                else
                                {
                                    RHS = 3;
                                }
                            }
                            else
                            {
                                // If our local max is < gLim then we can simply set
                                // our AoA to be the AoA of the local max
                                currAoA = AoACurve.Evaluate(TRatio);
                                //gLimited = currAoA < maxAoA;
                                return currAoA < maxAoA ? currAoA : maxAoA;
                            }
                        }
                        else
                        {
                            // If we're not within the margin then we need to consider
                            // the high AoA section. First calculate the absolute maximum
                            // g we can achieve
                            currG = 0.7f * qSk + thrust;

                            // If the absolute maximum g we can achieve is not enough, then return
                            // the local maximum in order to preserve energy
                            if (currG < gLim)
                            {
                                currAoA = AoACurve.Evaluate(TRatio);
                                //gLimited = currAoA < maxAoA;
                                return currAoA < maxAoA ? currAoA : maxAoA;
                            }

                            // If we're within the limit, then find the AoA where the normal force
                            // once again reaches the local max value
                            float AoAEq = AoAEqCurve.Evaluate(TRatio);

                            // And determine the left hand bound from there
                            if (AoAEq > linAoA[6])
                            {
                                // If we're in the final section then just calculate it directly
                                currAoA = calcAoAforGNonLin(qSk, gLim, linSlope[6], linIntc[6], 0);
                                //gLimited = currAoA < maxAoA;
                                return currAoA < maxAoA ? currAoA : maxAoA;
                            }
                            else if (AoAEq > linAoA[5])
                            {
                                LHS = 5;
                            }
                            else
                            {
                                LHS = 4;
                            }
                        }
                    }
                    else
                    {
                        // If we're not above TRatioInflec1 then we only have to consider the
                        // curve up to the local max
                        float AoAMax = AoACurve.Evaluate(TRatio);

                        // Determine the right hand bound for calculation
                        if (gLim < currG)
                        {
                            if (AoAMax > linAoA[3])
                            {
                                RHS = 4;
                            }
                            else
                            {
                                RHS = 3;
                            }
                        }
                        else
                        {
                            //gLimited = currAoA < maxAoA;
                            return currAoA < maxAoA ? currAoA : maxAoA;
                        }
                    }
                }
                else
                {
                    // If we're above TRatioInflec2 then we have to search the whole thing, but past that ratio
                    // the function is monotonically increasing so it's OK

                    // That being said, first calculate the absolute maximum
                    // g we can achieve
                    currG = 0.7f * qSk + thrust;

                    // If the absolute maximum g we can achieve is not enough, then return
                    // max AoA
                    if (currG < gLim)
                        return maxAoA;
                }

                currG = linCL[RHS] * qSk + thrust * linSin[RHS];
                if (currG < gLim)
                    return maxAoA;

                // Bisection search
                while ( (RHS - LHS) > 1)
                {
                    interval = Mathf.FloorToInt(0.5f * (RHS + LHS));

                    currG = linCL[interval] * qSk + thrust * linSin[interval];

                    //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: LHS: {LHS}, RHS: {RHS}, interval: {interval}, currG: {currG}, gLim: {gLim}");

                    if (currG < gLim)
                    {
                        LHS = interval;
                    }
                    else
                    {
                        RHS = interval;
                    }
                }

                if (LHS == 0)
                {
                    // If we're below 15 (here 10 degrees) then use the linear approximation for sin
                    currAoA = calcAoAforGLinear(qSk, gLim, linSlope[LHS], linIntc[LHS], thrust);
                }
                else
                {
                    // Otherwise use the second order approximation centered at pi/2
                    currAoA = calcAoAforGNonLin(qSk, gLim, linSlope[LHS], linIntc[LHS], thrust);
                }

                //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: Final Interval: {LHS}, currAoA: {currAoA}, gLim: {gLim}");

                //gLimited = currAoA < maxAoA;
                return currAoA < maxAoA ? currAoA : maxAoA;
            }
            // Pseudocode / logic
            // If T = 0
            // We know it's in the first section. If m*gReq > (1.5*q*k*s) then set to min of maxAoA and 30 (margin?). If
            // < then we first make linear estimate, then solve by bisection of intervals first -> solve on interval.
            // If TRatio < TRatioInflec2
            // First we check the endpoints -> both gMax, and, if TRatio > TRatioInflec1, then 0.7*q*S*k + T (90 degree case).
            // If gMax > m*gReq then the answer < AoACurve -> Determine where it is via calculating the pre-calculated points
            // then seeing which one has gCalc > m*gReq, using the interval bounded by the point with gCalc > m*gReq on the
            // right end. Use bisection -> we know it's bounded at the RHS by the 38 or the 57 section. We can compare the
            // AoACurve with 38, if > 38 then use 57 as the bound, otherwise bisection with 38 as the bound. Using this to
            // determine which interval we're looking at, we then calc AoACalc. Return the min of maxAoA and AoACalc.
            // If gMax < m*gReq, then if TRatio < TRatioInflec1, set to min of AoACurve and maxAoA. If TRatio > TRatioInflec1
            // then we look at the 0.3*q*S*k + T. If < m*gReq then we'll set it to the min of maxAoA and either AoACurve or
            // 90, depends on the margin. See below. If > m*gReq then it's in the last two sections, bound by AoAEq on the LHS.
            // If AoAEq > 65, then we solve on the last section. If AoAEq < 65 then we check the point at AoA = 65 using the
            // pre-calculated values. If > m*gReq then we know that it's in the 57-65 section, otherwise we know it's in the
            // 65-90 section.
            // Consider adding a margin, if gMax only misses m*gReq by a little we should probably avoid going to the higher
            // angles as it adds a lot of drag. Maybe distance based? User settable?
            // If TRatio > TRatioInflec2 then we have a continuously monotonically increasing function
            // We use the fraction m*gReq/(0.3*q*S*k + T) to determine along which interval we should solve, noting that this
            // is an underestimate of the thrust required. (Maybe use arcsin for a more accurate estimate? Costly.) Then simply
            // calculate the pre-calculated value at the next point -> bisection and solve on the interval.
            
            // For all cases, if AoA < 15 then we can use the linear approximation of sin, if an interval includes both AoA < 15
            // and AoA > 15 then try < 15 (interval 2) first, then if > 15 try the non-linear starting from 15. Otherwise we use
            // non-linear equation.
        }

        // Calculate AoA for a given g loading, given m*g, the dynamic pressure times the lift area times the lift multiplier,
        // the linearized approximation of the AoA curve (in slope, y-intercept form) and the thrust. Linear uses a linear
        // small angle approximation for sin and non-linear uses a 2nd order approximation of sin about pi/2
        public static float calcAoAforGLinear(float qSk, float mg, float CLalpha, float CLintc, float thrust)
        {
            //if (BDArmorySettings.DEBUG_MISSILES)
            //{
            //    float AoA = (mg - CLintc * qSk) / (CLalpha * qSk + thrust * Mathf.Deg2Rad);
            //    Debug.Log($"[BDArmory.MissileGuidance]: Linear: AoA: {AoA}, thrust: {thrust}, qSk: {qSk}, Predicted CL: {AoA * CLalpha + CLintc}, actual CL: {DefaultLiftCurve.Evaluate(AoA)}, CLa: {CLalpha}, CLintc: {CLintc}, predicted force: {qSk * (AoA * CLalpha + CLintc) + thrust * AoA * Mathf.Deg2Rad}, actual force: {qSk * DefaultLiftCurve.Evaluate(AoA) + thrust * Mathf.Sin(AoA * Mathf.Deg2Rad)}, desired: {mg}");
            //}
            return (mg - CLintc * qSk) / (CLalpha * qSk + thrust * Mathf.Deg2Rad);
        }

        public static float calcAoAforGNonLin(float qSk, float mg, float CLalpha, float CLintc, float thrust)
        {
            CLalpha *= qSk;

            //if (BDArmorySettings.DEBUG_MISSILES)
            //{
            //    float invqSk = 1f / qSk;
            //    float AoA = (2f * CLalpha + Mathf.PI * thrust * Mathf.Deg2Rad - 2f * BDAMath.Sqrt(CLalpha * CLalpha + Mathf.PI * thrust * Mathf.Deg2Rad * CLalpha + 2f * thrust * (CLintc * qSk + thrust - mg) * Mathf.Deg2Rad * Mathf.Deg2Rad)) / (2f * thrust * Mathf.Deg2Rad * Mathf.Deg2Rad);
            //    Debug.Log($"[BDArmory.MissileGuidance]: NonLin: AoA: {AoA}, thrust: {thrust}, qSk: {qSk}, Predicted CL: {AoA * CLalpha * invqSk + CLintc}, actual CL: {DefaultLiftCurve.Evaluate(AoA)}, CLa: {CLalpha * invqSk}, CLintc: {CLintc}, predicted force: {qSk * (AoA * CLalpha * invqSk + CLintc) + thrust * (1f - (-Mathf.PI * 0.5f + Mathf.Deg2Rad * AoA) * (-Mathf.PI * 0.5f + Mathf.Deg2Rad * AoA) * 0.5f)}, actual force: {qSk * DefaultLiftCurve.Evaluate(AoA) + thrust * Mathf.Sin(AoA * Mathf.Deg2Rad)}, desired: {mg}");
            //}
            return (2f * CLalpha + Mathf.PI * thrust * Mathf.Deg2Rad - 2f * BDAMath.Sqrt(CLalpha * CLalpha + Mathf.PI * thrust * Mathf.Deg2Rad * CLalpha + 2f * thrust * (CLintc * qSk + thrust - mg) * Mathf.Deg2Rad * Mathf.Deg2Rad)) / (2f * thrust * Mathf.Deg2Rad * Mathf.Deg2Rad);
        }

        // Linearized curves for cos*CL and sin*CD v.s. AoA for fast solving of the AoA at which maxTorque no longer is sufficient to maintain
        // control of the missile.

        public static FloatCurve torqueAoAReturn = new([
                new(2.6496350364963499f, 88.7129999999999939f, -106.9758f, -106.9758f),
                new(2.73134328358208922f, 79.9722000000000008f, -70.59726f, -70.59726f),
                new(3.14937759336099621f, 65.6675999999999931f, -28.9337f, -28.9337f),
                new(3.52488687782805465f, 56.7873000000000019f, -31.87921f, -31.87921f),
                new(3.69483568075117441f, 49.9707000000000008f, -61.73428f, -61.73428f),
                new(3.76190476190476275f, 44.3798999999999992f, -83.35883f, -18.59649f),
                new(3.83091787439613629f, 43.0964999999999989f, -23.74979f, -23.74979f),
                new(3.92610837438423754f, 40.3451999999999984f, -28.9031f, -28.9031f)
            ]);

        // Note we use linAoA for this as well
        public static float[] linLiftTorque = { 0f, 0.449212170675488687f, 1.23071251302548967f, 1.29903810567665712f, 1.08779669420507852f, 0.391903830317496704f, 0.253570957044423284f, 0f };
        public static float[] linDragTorque = { 0f, 0.000748453415988048856f, 0.00346671023416293559f, 0.00499999999999927499f, 0.0656669812489726473f, 0.26524150275361541f, 0.336257675049945692f, 0.5f };

        // Slope of cos * CL at the intervals
        public static float[] linLiftTorqueSlope = { 0.0449212178074f, 0.0558214214286f, 0.0113876666667f, -0.026405125f, -0.0366259562991f, -0.0172916091591f, -0.0101428382818f };
        // y-Intercept of line at those intervals
        public static float[] linLiftTorqueIntc = { 0f, -0.109002114286f, 0.957408f, 2.09119175f, 2.47958333937f, 1.37752555239f, 0.91285544536f };

        // Slope of sin * CD at the intervals
        public static float[] linDragTorqueSlope = { 0.000166666666667f, 0.000166666666667f, 0.000166666666667f, 0.00691309375f, 0.0107346842105f, 0.009046f, 0.00653472f };
        // y-Intercept of line at those intervals
        public static float[] linDragTorqueIntc = { 0f, 0f, 0f, -0.2023928125f, -0.347613f, -0.251358f, -0.0881248f };

        const float DLRatioInflec1 = 2.63636363636363624f;
        const float DLRatioInflec2 = 3.92610837438423754f;

        // Algorithm is similar to getGLimit, except in this case we only calculate which sections to search in whenever
        // the liftArea and dragArea change. We define this using a set of numbers, torqueAoAReturn, the AoA at which torque goes past the local
        // maximum which occurs at around 28° AoA, if this is set to -1, then we ONLY search the lower portion of the plot and torqueMaxLocal which
        // gives the non-dim torque (which needs to be pre-multiplied by the SUM of liftArea * liftMult and dragArea * dragMult before being saved)
        // which is +ve when there's a local maximum, it is set to the negative of the number when a local maximum does not exist, it is not set to
        // -1 instead as it still provides a useful bisection point, with which we can determine the first LHS/RHS index.
        //public static float[] linAoA = { 0f, 10f, 24f, 30f, 38f, 57f, 65f, 90f };
        public static void setupTorqueAoALimit(MissileLauncher ml, float liftArea, float dragArea)
        {
            // Drag / Lift ratio
            float DL = (BDArmorySettings.GLOBAL_DRAG_MULTIPLIER * dragArea)/(BDArmorySettings.GLOBAL_LIFT_MULTIPLIER * liftArea);
            // The % contribution of drag, note that this will error out if there's no drag,
            // but that's not supposed to happen.
            float SkR = DL / (DL + 1);

            // If we're above DLRationInflec2 then we must search the whole range of AoAs
            if (DL < DLRatioInflec2)
            {
                // If we're below DLRatioInflec1 then we're bounded on the right by 30° 
                if (DL < DLRatioInflec1)
                    ml.torqueBounds = [3, -1];
                else
                {
                    float AoARHS = torqueAoAReturn.Evaluate(DL);
                    if (AoARHS > linAoA[6])
                        ml.torqueBounds = [6, 7];
                    else if (AoARHS > linAoA[5])
                        ml.torqueBounds = [5, 7];
                    else
                        ml.torqueBounds = [4, 7];

                    ml.torqueAoABounds[2] = AoARHS;
                }
                // This AoA happens to be a linear function of D/L
                ml.torqueAoABounds[0] = 0.0307482f * DL + 28.49333f;
                // This non-dimensionalized torque happens to be a
                // linear function of SkR
                ml.torqueAoABounds[1] = -1.30417f * SkR + 1.30879f;
            }

            //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance] TorqueAoALimits for {ml} at SkR: {SkR}, D/L: {DL} are, torqueAoABounds: {ml.torqueAoABounds[0]}, {ml.torqueAoABounds[1]}, {ml.torqueAoABounds[2]}, torqueBounds: {ml.torqueBounds[0]}, {ml.torqueBounds[1]}");
        }

        public static float getTorqueAoALimit(MissileLauncher ml, float liftArea, float dragArea, float maxTorque)
        {
            // Dynamic pressure
            float q = (float)(0.5 * ml.vessel.atmDensity * ml.vessel.srfSpeed * ml.vessel.srfSpeed);
            // Technically not required, but in case anyone starts allowing for the CoL to vary
            float CoLDist = 1f;

            // Divide out the dynamic pressure and CoLDist components of torque
            maxTorque /= q * CoLDist;
            //maxTorque *= 0.9f; // Let's only go up to 90% of maxTorque to leave some leeway

            int LHS = 0;
            int RHS = 7;
            int interval = 3;

            // Drag and Lift Area multipliers
            float dragSk = dragArea * BDArmorySettings.GLOBAL_DRAG_MULTIPLIER;
            float liftSk = liftArea * BDArmorySettings.GLOBAL_LIFT_MULTIPLIER;

            // Here we store the AoA of local max torque, we set it to 180f as for the case
            // where the entire range must be searched, this gives the correct AoA
            float currAoA = 180f;

            if (ml.torqueBounds[0] > 0)
            {
                // If we have a left torque bound then we don't need to search the entire range
                float torqueMaxLocal = ml.torqueAoABounds[0];
                currAoA = ml.torqueAoABounds[1];

                if (ml.torqueBounds[1] > 0)
                {
                    // If we have a right torque bound then we need to determine if we're searching
                    // in the low AoA or the high AoA section, this is decided by if the maxTorque
                    // is greater than torqueAoABounds times dragSk + liftSk
                    if (maxTorque > torqueMaxLocal * (dragSk + liftSk))
                    {
                        // If maxTorque exceeds the max aerodynamic torque possible, then just return 180f
                        if (maxTorque > (liftSk * linLiftTorque[7] + dragSk * linDragTorque[7]))
                            return 180f;

                        LHS = ml.torqueBounds[0];
                        RHS = ml.torqueBounds[1];
                    }
                    else
                    {
                        RHS = ml.torqueBounds[0];
                    }
                }
                else
                {
                    // If we don't have a right torque bound then we're bound only by the low
                    // AoA section, and hence can return immediately if torque exceeds the localMax
                    if (maxTorque > torqueMaxLocal * (dragSk + liftSk))
                        return 180f;

                    // Otherwise we just search the low AoA portion
                    RHS = ml.torqueBounds[0];
                }
            }
            else
            {
                // If maxTorque exceeds the max aerodynamic torque possible, then just return 180f
                if (maxTorque > (liftSk * linLiftTorque[7] + dragSk * linDragTorque[7]))
                    return 180f;
            }

            float currTorque;

            // Bisection search
            while ((RHS - LHS) > 1)
            {
                interval = Mathf.FloorToInt(0.5f * (RHS + LHS));

                currTorque = liftSk * linLiftTorque[interval] + dragSk * linDragTorque[interval];

                //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: LHS: {LHS}, RHS: {RHS}, interval: {interval}, currTorque: {currTorque}, maxTorque: {maxTorque}");

                if (currTorque < maxTorque)
                {
                    LHS = interval;
                }
                else
                {
                    RHS = interval;
                }
            }

            currAoA = (maxTorque - (linLiftTorqueIntc[LHS] * liftSk + linDragTorqueIntc[LHS] * dragSk)) / (linLiftTorqueSlope[LHS] * liftSk + linDragTorqueSlope[LHS] * dragSk);

            //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: q: {q}, Final Interval: {LHS}, currAoA: {currAoA}, maxTorque: {maxTorque}");

            return currAoA;
        }

        public static Vector3 DoAeroForces(MissileLauncher ml, Vector3 targetPosition, float liftArea, float dragArea, float steerMult,
            Vector3 previousTorque, float maxTorque, float maxTorqueAero, float maxAoA)
        {

            FloatCurve liftCurve = DefaultLiftCurve;
            FloatCurve dragCurve = DefaultDragCurve;

            return DoAeroForces(ml, targetPosition, liftArea, dragArea, steerMult, previousTorque, maxTorque, maxAoA, maxTorqueAero,
                liftCurve, dragCurve, true, 0.9f);
        }

        public static Vector3 DoAeroForces(MissileLauncher ml, Vector3 targetPosition, float liftArea, float dragArea, float steerMult,
            Vector3 previousTorque, float maxTorque, float maxTorqueAero, float maxAoA, FloatCurve liftCurve, FloatCurve dragCurve,
            bool torqueLimiter, float torqueMargin, bool activeGuidance = true, bool _gCommand = false)
        {
            Rigidbody rb = ml.part.rb;
            if (rb == null || rb.mass == 0) return Vector3.zero;
            double airDensity = ml.vessel.atmDensity;
            double airSpeed = ml.vessel.srfSpeed;
            Vector3d velocity = ml.vessel.Velocity();
            Vector3d velNorm = velocity.normalized;
            Vector3 forward = ml.transform.forward;

            //temp values
            Vector3 CoL = new Vector3(0, 0, -1f);
            float liftMultiplier = BDArmorySettings.GLOBAL_LIFT_MULTIPLIER;
            float dragMultiplier = BDArmorySettings.GLOBAL_DRAG_MULTIPLIER;
            double dynamicq = 0.5 * airDensity * airSpeed * airSpeed;

            maxTorque += (float)dynamicq * maxTorqueAero;

            //lift
            float AoA = Mathf.Clamp(VectorUtils.AnglePreNormalized(forward, velNorm), 0, 90);
            Vector3 forcePos = ml.transform.TransformPoint(ml.part.CoMOffset + CoL);
            Vector3 forceDirection = -velocity.ProjectOnPlanePreNormalized(forward).normalized;
            double liftForce = 0.0;
            ml.smoothedAoA.Update(AoA);
            if (AoA > 0)
            {
                liftForce = dynamicq * liftArea * liftMultiplier * Mathf.Max(liftCurve.Evaluate(AoA), 0f);
                rb.AddForceAtPosition((float)liftForce * forceDirection,
                    forcePos);
            }

            //drag
            double dragForce = 0.0;
            if (airSpeed > 0)
            {
                dragForce = dynamicq * dragArea * dragMultiplier * Mathf.Max(dragCurve.Evaluate(AoA), 0f);
                rb.AddForceAtPosition((float)dragForce * -velNorm,
                    forcePos);
            }

            //guidance
            if ((airSpeed > 1f || (ml.vacuumSteerable && ml.Throttle > 0)) && maxTorque > 0)
            {
                /* This is what the torque on the missile due to aero forces would be
                Vector3 aeroTorque = Vector3.Cross(forcePos - ml.vessel.CoM,
                    new Vector3d(liftForce * forceDirection.x - dragForce * velNorm.x,
                                 liftForce * forceDirection.y - dragForce * velNorm.y,
                                 liftForce * forceDirection.z - dragForce * velNorm.z));
                */
                // So instead we take the opposite cross product to get the negative/opposing torque
                Vector3 aeroTorque = Vector3.Cross(new Vector3d(liftForce * forceDirection.x - dragForce * velNorm.x,
                                                                liftForce * forceDirection.y - dragForce * velNorm.y,
                                                                liftForce * forceDirection.z - dragForce * velNorm.z),
                                                                forcePos - ml.vessel.CoM);
                //Debug.Log($"[BDArmory.MissileGuidance]: aeroTorque = {aeroTorque}.");
                /* Legacy Missile Controller
                Vector3 targetDirection; // = (targetPosition - ml.transform.position);
                float targetAngle;
                if (AoA < maxAoA)
                {
                    targetDirection = (targetPosition - ml.vessel.CoM);
                    targetAngle = Mathf.Min(maxAoA,VectorUtils.Angle(velNorm, targetDirection) * 4f);
                }
                else
                {
                    targetDirection = velNorm;
                    targetAngle = 0f; //AoA;
                }

                Vector3 torqueDirection = -Vector3.Cross(targetDirection, velNorm).normalized;
                torqueDirection = ml.transform.InverseTransformDirection(torqueDirection);

                float torque = Mathf.Clamp(targetAngle * steerMult, 0, maxTorque);
                Vector3 finalTorque = Vector3.Lerp(previousTorque, torqueDirection * torque, 1).ProjectOnPlanePreNormalized(Vector3.forward);
                */

                //float currMaxAoA = maxAoA + Mathf.Min(0.1f * maxAoA, 2f);
                float AoALim = (torqueLimiter && !ml.vessel.InVacuum()) ? Mathf.Min(maxAoA, getTorqueAoALimit(ml, liftArea, dragArea, (1f - torqueMargin) * maxTorque)) : maxAoA;
                //if (ml.torqueAoALimit.x > 0)
                //    AoALim = Mathf.Min(maxAoA + Mathf.Min(0.1f * maxAoA, 2f), 1.2f * ml.torqueAoALimit.x * ml.torqueAoALimit.y / (float)airSpeed * BDAMath.Sqrt(ml.torqueAoALimit.z / (float)airDensity));

                float boresightAngle = -1f;
                Vector3 targetDirection;
                float targetAngle;
                if (activeGuidance)
                {
                    targetDirection = (targetPosition - ml.vessel.CoM).normalized;
                    targetAngle = VectorUtils.AnglePreNormalized(velNorm, targetDirection);
                    if (targetAngle > AoALim)
                    {
                        targetDirection = Vector3.Slerp(velNorm, targetDirection, AoALim / targetAngle);
                    }
                    else if (_gCommand && targetAngle < AoALim)
                    {
                        //targetDirection = QuaternionD.AngleAxis(AoALim, Vector3d.Cross(velNorm, targetDirection)) * velNorm;
                        targetDirection = Vector3.Slerp(velNorm, (10f * targetDirection).ProjectOnPlanePreNormalized(velNorm).normalized, AoALim / 90f);
                    }

                    Vector3 boresightVector;
                    if (ml.maxOffBoresight < 180f && (boresightAngle = VectorUtils.AnglePreNormalized(targetDirection, boresightVector = (ml.TargetPosition - ml.vessel.CoM).normalized)) > ml.maxOffBoresight)
                    {
                        Vector3 maneuverPlaneNormal = (Vector3.Cross(velNorm, targetDirection)).normalized;
                        float tempBoresight = ml.maxOffBoresight;
                        // If boresight > 180, we solve for the opposite cone
                        if (tempBoresight > 90f)
                        {
                            tempBoresight = 180f - tempBoresight;
                            boresightVector = -boresightVector;
                        }

                        targetDirection = VectorUtils.ConePlaneIntercept(targetDirection, maneuverPlaneNormal, boresightVector, Mathf.Cos(tempBoresight * Mathf.Deg2Rad), false);
                    }
                }
                else
                {
                    targetAngle = 0f;
                    targetDirection = velNorm;
                }
                float turningAngle = VectorUtils.AnglePreNormalized(forward, targetDirection);

                Vector3 finalTorque;
                if (turningAngle > 0.005f)
                {
                    Vector3 torqueDirection = Vector3.Cross(forward, targetDirection) / Mathf.Sin(turningAngle * Mathf.Deg2Rad);
                    //Debug.Log($"[BDArmory.MissileGuidance]: torqueDirection = {torqueDirection}, sqrMagnitude = {torqueDirection.sqrMagnitude}.");

                    //if (turningAngle < 1f)
                    //    turningAngle *= turningAngle;

                    if (turningAngle < 1f)
                    {
                        turningAngle = 0.3f * BDAMath.Sqrt(turningAngle) + 0.7f * turningAngle;
                    }
                    float torque = Mathf.Clamp(Mathf.Min(turningAngle, AoALim) * 4f * steerMult, 0f, maxTorque);

                    float aeroTorqueSqr = aeroTorque.sqrMagnitude;

                    if (aeroTorqueSqr > 0f)
                    {
                        // If aeroTorque < maxTorque we're not yet saturated
                        if (aeroTorqueSqr < maxTorque * maxTorque)
                        {
                            float temp = Vector3.Dot(aeroTorque, torqueDirection);
                            //Debug.Log($"[BDArmory.MissileGuidance]: aeroTorque not saturated, torque = {torque}.");
                            // If torque drives us over maxTorque, then using the quadratic formula, we determine the value that gets us maxTorque
                            if ((aeroTorque + torqueDirection * torque).sqrMagnitude > maxTorque * maxTorque)
                            {
                                // Solution to the quadratic formula for the intersection of a line with a sphere, note we use the +ve solution
                                // There is no need to check the determinant as any line that originates within the sphere will always intersect the sphere
                                torque = BDAMath.Sqrt(temp * temp - (aeroTorqueSqr - maxTorque * maxTorque)) - temp;
                                //Debug.Log($"[BDArmory.MissileGuidance]: torque saturation! torque = {torque}.");
                            }
                            //// If aeroTorque is within 50% of maxTorque then tone down torque marginally
                            //if (aeroTorqueSqr > 0.25f * maxTorque * maxTorque)// && temp > 0f)
                            //{
                            //    //Debug.Log($"[BDArmory.MissileGuidance] torque limiter: {(1f - (aeroTorqueSqr / (maxTorque * maxTorque) - 0.49f) * 1.96078f)}");
                            //    //torque *= (1f - (aeroTorqueSqr / (maxTorque * maxTorque) - 0.49f) * 1.96078f);
                            //    float aeroTorqueMag = BDAMath.Sqrt(aeroTorqueSqr);
                            //    float x = 1.7f - aeroTorqueMag / maxTorque;
                            //    //Debug.Log($"[BDArmory.MissileGuidance] torque limiter: {(x*x*x*x - 0.0625f) * 1.066f}");
                            //    torque *= (x * x * x * x - 0.0625f) * 1.066f;
                            //}

                            if (temp < 0f)
                            {
                                torque *= 0.5f;
                            }

                            // If we're approaching the limit (90% of maxTorque) and we're faster than the last time we reached it,
                            // recalculate the torqueAoALimit as the estimate is a bit more restrictive when going faster
                            //if (ml.torqueAoALimit.x > 0f && aeroTorqueSqr > 0.81f * maxTorque * maxTorque && airSpeed > 1.5f * ml.torqueAoALimit.y)
                            //{
                            //    // Here we assume the torqueAoALimit has more or less a quadratic relationship with AoA
                            //    ml.torqueAoALimit = new Vector3(1.2f * BDAMath.Sqrt(BDAMath.Sqrt(aeroTorqueSqr / (maxTorque * maxTorque))) * AoA, (float)airSpeed, (float)airDensity);
                            //}

                            // Otherwise we just use torque unmodified
                        }
                        else
                        {
                            //ml.torqueAoALimit = new Vector3(AoA, (float)airSpeed, (float)airDensity);
                            //Debug.Log($"[BDArmory.MissileGuidance]: aeroTorque saturated! torque = {torque}.");
                            // If we're saturated, then as long as torqueDirection somewhat opposes aeroTorque we can look
                            // at how much torque we can apply
                            float temp = Vector3.Dot(aeroTorque, torqueDirection);
                            // We check the determinant of the quadratic as well to ensure we actually intersect with the sphere
                            float det = temp * temp - (aeroTorqueSqr - maxTorque * maxTorque);
                            if (temp < 0f && det > 0f)
                            {
                                float temp2 = BDAMath.Sqrt(det);
                                // temp2 > 0 and temp < 0 so LHS < RHS
                                float LHS = -temp2 - temp;
                                float RHS = temp2 - temp;
                                //Debug.Log($"[BDArmory.MissileGuidance]: Possible to unsaturate! LHS: {LHS}, {RHS}, torque = {torque}.");
                                // There are three cases here, first is the case is if torque is insufficient to drive us under saturation,
                                // in which case we'll just apply enough to saturate, but only if LHS is < 2f * torque and maxTorque
                                if (torque < LHS)
                                {
                                    // This unsaturation method lead to some pretty poor results so I'm just disabling it
                                    //if (LHS < 2f * torque && LHS < maxTorque)
                                    //    torque = LHS;
                                    //else
                                    //{
                                        torque = 0f;
                                        aeroTorque = (maxTorque / aeroTorque.magnitude) * aeroTorque;
                                    //}
                                }
                                // The second case is where we've gone over in the opposite direction, in which case we must reduce our torque
                                else if (torque > RHS)
                                {
                                    torque = RHS;
                                }
                                // A special case occurs if |temp| < Mathf.Epsilon, which is the single point intersection solution, where
                                // torque can potentially approx. equal the single point solution but in that case we wouldn't have to modify
                                // the torque. If torque is not approx. equal one of these two cases should catch it
                                // The third case is where we're perfectly within bounds so we don't modify torque
                            }
                            else
                            {
                                // If all previous checks fail, we're saturated, so we limit the aeroTorque
                                torque = 0f;
                                aeroTorque = (maxTorque / aeroTorque.magnitude) * aeroTorque;
                                //Debug.Log($"[BDArmory.MissileGuidance]: Cannot unsaturate! aeroTorque = {aeroTorque}.");
                            }
                        }

                        finalTorque = torque > 0f ? (torque * torqueDirection + aeroTorque) : aeroTorque;
                    }
                    else
                    {
                        if (torque <= 0f)
                        {
                            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_MISSILES) ml.debugString.AppendLine($"achieved g: {(ml.vessel.acceleration.ProjectOnPlanePreNormalized(velNorm).magnitude) * (1f / 9.81f):F2}, lift g: {liftForce / ml.part.mass * (1f / 9.81f):F2}, CL: {liftCurve.Evaluate(AoA):F2}\nAoA: {AoA:F2}, AoALim: {AoALim:F2}, MaxAoA: {maxAoA:F2}{(boresightAngle > 0f ? $", Seeker Angle: {boresightAngle:F2}/{ml.maxOffBoresight:F2}" : "")}\nTargetAngle: {targetAngle:F2}, TurningAngle: {turningAngle:F2}, forward: {forward}, targetDirection: {targetDirection}\nmaxTorque (Aero): {maxTorque:F2} ({maxTorqueAero * dynamicq:F2}), currTorque: 0.00, aeroTorque: 0.00\nliftArea: {liftArea}, dragArea: {dragArea}");
                            return Vector3.zero;
                        }
                        finalTorque = torque * torqueDirection;
                    }
                }
                else
                {
                    if (aeroTorque.sqrMagnitude > maxTorque * maxTorque)
                    {
                        aeroTorque = (maxTorque / aeroTorque.magnitude) * aeroTorque;
                    }

                    finalTorque = aeroTorque;
                }

                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_MISSILES) ml.debugString.AppendLine($"achieved g: {(ml.vessel.acceleration.ProjectOnPlanePreNormalized(velNorm).magnitude) * (1f / 9.81f):F2}, lift g: {liftForce / ml.part.mass * (1f / 9.81f):F2}, CL: {liftCurve.Evaluate(AoA):F2}\nAoA: {AoA:F2}, AoALim: {AoALim:F2}, MaxAoA: {maxAoA:F2}{(boresightAngle > 0f ? $", Seeker Angle: {boresightAngle:F2}/{ml.maxOffBoresight:F2}" : "")}\nTargetAngle: {targetAngle:F2}, TurningAngle: {turningAngle:F2}, forward: {forward}, targetDirection: {targetDirection}\nmaxTorque (Aero): {maxTorque:F2} ({maxTorqueAero * dynamicq:F2}), currTorque: {(Vector3.Dot(finalTorque, aeroTorque) > 0 ? "+" : "-")}{finalTorque.magnitude:F2}, aeroTorque: -{aeroTorque.magnitude:F2}\nliftArea: {liftArea}, dragArea: {dragArea}");

                finalTorque = ml.transform.InverseTransformDirection(finalTorque).ProjectOnPlanePreNormalized(Vector3.forward);

                //Debug.Log($"[BDArmory.MissileGuidance]: torque = {torque}, torqueDirection = {torqueDirection}, aeroTorque = {aeroTorque}, finalTorque = {finalTorque}.");

                rb.AddRelativeTorque(finalTorque);
                return finalTorque;
            }
            else
            {
                Vector3 finalTorque = Vector3.Lerp(previousTorque, Vector3.zero, 0.25f).ProjectOnPlanePreNormalized(Vector3.forward);
                rb.AddRelativeTorque(finalTorque);

                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_MISSILES)
                {
                    Vector3 aeroTorque = Vector3.Cross(new Vector3d(liftForce * forceDirection.x - dragForce * velNorm.x,
                                                                liftForce * forceDirection.y - dragForce * velNorm.y,
                                                                liftForce * forceDirection.z - dragForce * velNorm.z),
                                                                forcePos - ml.vessel.CoM);
                    ml.debugString.AppendLine($"achieved g: {(ml.vessel.acceleration.ProjectOnPlanePreNormalized(velNorm).magnitude) * (1f / 9.81f):F2}, lift g: {liftForce / ml.part.mass * (1f / 9.81f):F2}, CL: {liftCurve.Evaluate(AoA):F2}\nAoA: {AoA:F2}\nmaxTorque (Aero): {maxTorque:F2} ({maxTorqueAero * dynamicq:F2}), currTorque: {(Vector3.Dot(finalTorque, aeroTorque) > 0 ? "+" : "-")}{finalTorque.magnitude:F2}, aeroTorque: {aeroTorque.magnitude:F2}\nliftArea: {liftArea}, dragArea: {dragArea}");
                }
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
            //double latitudeAtPos = FlightGlobals.currentMainBody.GetLatitude(position);
            //double longitudeAtPos = FlightGlobals.currentMainBody.GetLongitude(position);
            FlightGlobals.currentMainBody.GetLatLonAlt(position, out double latitudeAtPos, out double longitudeAtPos, out double altitudeAtPos);

            float radarAlt = Mathf.Clamp(
                (float)(altitudeAtPos -
                         FlightGlobals.currentMainBody.TerrainAltitude(latitudeAtPos, longitudeAtPos)), 0,
                (float)altitudeAtPos);
            return radarAlt;
        }

        public static float GetRaycastRadarAltitude(Vector3 position)
        {
            //Vector3 upDirection = VectorUtils.GetUpDirection(position);
            (double altAtPos, Vector3d upDirection) = (position - FlightGlobals.currentMainBody.position).MagNorm(); //VectorUtils.GetUpDirection(position);

            //float altAtPos = FlightGlobals.getAltitudeAtPos(position);
            altAtPos -= FlightGlobals.currentMainBody.Radius;
            if (altAtPos < 0)
            {
                position += (2d * Math.Abs(altAtPos)) * upDirection;
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
