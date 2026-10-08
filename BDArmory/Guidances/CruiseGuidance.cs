using System;
using UnityEngine;

using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.Utils;
using BDArmory.Weapons.Missiles;
using BDArmory.Radar;

namespace BDArmory.Guidances
{
    public enum GuidanceState
    {
        Ascending,
        Cruising,
        Descending,
        Terminal,
        Popup
    }

    public enum PitchDecision
    {
        Ascent,
        Descent,
        Hold,
        EmergencyAscent,
        CoarseAscent,
        CoarseDescent
    }

    public enum ThrottleDecision
    {
        Increase,
        Decrease,
        Hold
    }

    public class CruiseGuidance : IGuidance
    {
        private readonly MissileBase _missile;


        private float _pitchAngle;
        private double _futureAltitude;
        private double _futureSpeed;
        private double _horizontalAcceleration;

        private float _lastDataRead;
        private double _lastHorizontalSpeed;
        private double _lastSpeedDelta;
        private double _lastVerticalSpeed;

        private double _verticalAcceleration;

        private Vector3d planarDirectionToTarget;
        private Vector3d upDirection;

        private double _popupCos = -1f;
        private double _popupSin = -1f;

        // Popup 1/g
        double invG = 1d / 10d; // 1/maneuver G
        const double invg = 1d / 9.80665d; // 1/gravity on Earth/Kerbin

        public CruiseGuidance(MissileBase missile, double invManeuvergLimit = 0.1d) // 1/maneuver G
        {
            _missile = missile;
            invG = invManeuvergLimit;
        }

        public ThrottleDecision ThrottleDecision { get; set; }
        public PitchDecision PitchDecision { get; set; }

        public GuidanceState GuidanceState { get; set; }

        public Vector3d GetDirection(MissileBase missile, Vector3d targetPosition, Vector3d targetVelocity)
        {
            //set up
            // Not really necessary, especially with the new controller, if desired it can be implemented via guidance delay
            //if (_missile.TimeIndex < 1)
            //    return _missile.vessel.CoM + _missile.vessel.Velocity() * 10;

            upDirection = _missile.vessel.up;

            planarDirectionToTarget = (targetPosition - _missile.vessel.CoMD).ProjectOnPlanePreNormalized(upDirection).normalized;

            // Ascending
            var missileAltitude = GetCurrentAltitude(_missile.vessel);
            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_MISSILES)
            {
                _missile.debugString.AppendLine("State=" + GuidanceState);
                _missile.debugString.AppendLine("Altitude=" + missileAltitude);
                _missile.debugString.AppendLine("Apoapsis=" + _missile.vessel.orbit.ApA);
                _missile.debugString.AppendLine("Future Altitude=" + _futureAltitude);
                _missile.debugString.AppendLine("Pitch angle=" + _pitchAngle);
                _missile.debugString.AppendLine("Pitch decision=" + PitchDecision);
                _missile.debugString.AppendLine("lastVerticalSpeed=" + _lastVerticalSpeed);
                _missile.debugString.AppendLine("verticalAcceleration=" + _verticalAcceleration);
            }

            GetTelemetryData();

            switch (GuidanceState)
            {
                case GuidanceState.Ascending:
                    UpdateThrottle();

                    if (MissileWillReachAltitude(missileAltitude))
                    {
                        _pitchAngle = 0;
                        GuidanceState = GuidanceState.Cruising;

                        break;
                    }

                    CheckIfTerminal(missileAltitude, targetPosition, upDirection);

                    return _missile.vessel.CoMD + (planarDirectionToTarget + upDirection) * 10d;

                case GuidanceState.Cruising:

                    //Altitude control
                    UpdatePitch(missileAltitude);
                    UpdateThrottle();
                    CheckIfTerminal(missileAltitude, targetPosition, upDirection);

                    return _missile.vessel.CoMD + 10d * planarDirectionToTarget + _pitchAngle * upDirection;

                case GuidanceState.Terminal:

                    if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_MISSILES) _missile.debugString.AppendLine($"Descending");

                    _missile.Throttle = Mathf.Clamp((float)(_missile.vessel.atmDensity * 10f), 0.01f, 1f);

                    if (_missile is BDModularGuidance)
                        if (_missile.vessel.InVacuum())
                            return _missile.vessel.CoM + _missile.vessel.Velocity() * 10;

                    return MissileGuidance.GetAirToGroundTarget(targetPosition, targetVelocity, _missile.vessel, 1.85f);

                case GuidanceState.Popup:
                    _missile.Throttle = Mathf.Clamp((float)(_missile.vessel.atmDensity * 10f), 0.01f, 1f);

                    if (missileAltitude > _missile.CruisePopupAltitude)
                        GuidanceState = GuidanceState.Terminal;

                    return _missile.vessel.CoMD + 50d * (planarDirectionToTarget * _popupCos + upDirection * _popupSin);
            }

            return _missile.vessel.CoM + _missile.vessel.Velocity() * 10;
        }

        private double CalculateFreeFallTime(double missileAltitude)
        {
            double vi = -_missile.vessel.verticalSpeed;
            double a = 9.80665f;
            double d = missileAltitude;

            double temp = Math.Sqrt(vi * vi - 4 * (0.5f * a) * (-d));

            double time1 = (-vi + temp);
            double time2 = (-vi - temp);

            return Math.Max(time1, time2) / a;
        }

        private float GetProperDescentRatio(double missileAltitude)
        {
            float altitudePercentage = Mathf.Clamp01((float)(missileAltitude / 1000f));

            return Mathf.Lerp(-1f, 1.85f, altitudePercentage);
        }

        private void GetTelemetryData()
        {
            _lastDataRead = Time.time;

            _verticalAcceleration = (_missile.vessel.verticalSpeed - _lastVerticalSpeed);
            _lastVerticalSpeed = _missile.vessel.verticalSpeed;

            _horizontalAcceleration = (_missile.vessel.horizontalSrfSpeed - _lastHorizontalSpeed);
            _lastHorizontalSpeed = _missile.vessel.horizontalSrfSpeed;
        }

        private bool CheckIfTerminal(double altitude, Vector3d targetPosition, Vector3d upDirection)
        {
            Vector3d surfacePos = _missile.vessel.CoMD +
                                 Vector3d.Project(targetPosition - _missile.vessel.CoMD, -upDirection);

            double distanceToTarget = Vector3d.Distance(surfacePos, targetPosition);

            if (_missile.CruisePopup)
            {
                if (_popupCos < 0)
                {
                    _popupCos = Math.Cos(_missile.CruisePopupAngle * BDAMath.Deg2Rad);
                    _popupSin = Math.Sin(_missile.CruisePopupAngle * BDAMath.Deg2Rad);
                }

                if (distanceToTarget < _missile.CruisePopupRange + _futureSpeed * _missile.CruisePredictionTime)
                {
                    double a = Vector3d.Dot(_missile.GetForwardTransform(), upDirection);

                    // Time taken to sweep through the turning arc is arc length / cruise speed
                    // arc length = (angle in rad) * r
                    // mv^2/r = ma -> v^2/a = r, a = (9.81) * gLoad
                    // arc length = (angle in rad) * v^2 * invG * invg
                    // thus: t = (angle in rad) * v * invG  * invg
                    _futureSpeed = CalculateFutureSpeed((float)((_missile.CruisePopupAngle * BDAMath.Deg2Rad - Math.Acos(a)) * _lastHorizontalSpeed * invG * invg));

                    // turn dist is just r * (sin(popup angle) - cos(curr angle)) due to simple trigonometry
                    double turnDist = (_futureSpeed * _futureSpeed) * invG * invg * (_popupSin - a);

                    _missile.Throttle = 1f;

                    //if (BDArmorySettings.DEBUG_MISSILES)
                    //    Debug.Log($"[BDArmory.CruiseGuidance] a = {a}, futureSpeed = {_futureSpeed} m/s, turnDist = {turnDist} m.");

                    if (distanceToTarget < _missile.CruisePopupRange + turnDist)
                    {
                        GuidanceState = GuidanceState.Popup;
                        return true;
                    }
                }
                
                return false;
            }
            else
            {
                double freefallTime = CalculateFreeFallTime(altitude);

                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_MISSILES)
                {
                    _missile.debugString.AppendLine($"Distance to target={distanceToTarget}m");
                    _missile.debugString.AppendLine($"freefallTime={freefallTime}s");
                }

                if (distanceToTarget < (freefallTime * _missile.vessel.horizontalSrfSpeed))
                {
                    GuidanceState = GuidanceState.Terminal;
                    return true;
                }
            }
            return false;
        }

        private void UpdateThrottle()
        {
            MakeDecisionAboutThrottle(_missile);
        }

        private void UpdatePitch(double missileAltitude)
        {
            MakeDecisionAboutPitch(_missile, missileAltitude);
        }

        private double GetCurrentAltitude(Vessel missileVessel)
        {
            var currentRadarAlt = MissileGuidance.GetRadarAltitude(missileVessel);
            return currentRadarAlt;
        }

        private double GetCurrentAltitudeAtPosition(Vector3 position)
        {
            var currentRadarAlt = MissileGuidance.GetRadarAltitudeAtPos(position);

            return currentRadarAlt;
        }

        //private static double CalculateAltitude(Vector3 position, Vector3 upDirection, float currentRadarAlt, Vector3 tRayDirection)
        //{
        //    var terrainRay = new Ray(position, tRayDirection);
        //    RaycastHit rayHit;

        //    if (Physics.Raycast(terrainRay, out rayHit, 30000, (int)(LayerMasks.Scenery | LayerMasks.EVA))) // Why EVA?
        //    {
        //        var detectedAlt =
        //            Vector3.Project(rayHit.point - position, upDirection).magnitude;

        //        return Mathf.Min(detectedAlt, currentRadarAlt);
        //    }
        //    return currentRadarAlt;
        //}

        private bool CalculateFutureCollision(float predictionTime)
        {
            /*var terrainRay = new Ray(this._missile.vessel.CoM, this._missile.vessel.Velocity());
            RaycastHit hit;
            return Physics.Raycast(terrainRay, out hit, (float)(this._missile.vessel.srfSpeed * predictionTime), (int)(LayerMasks.Scenery | LayerMasks.EVA)); // Why EVA?*/
            //return RadarUtils.TerrainCheck(_missile.vessel.CoMD, _missile.vessel.CoMD + _missile.vessel.Velocity() * predictionTime, _missile.vessel.mainBody);
            return Physics.Linecast(this._missile.vessel.CoM, this._missile.vessel.CoM + this._missile.vessel.Velocity() * predictionTime);
        }

        private void MakeDecisionAboutThrottle(MissileBase missile)
        {
            const double maxError = 10;
            _futureSpeed = CalculateFutureSpeed(_missile.CruisePredictionTime);

            var currentSpeedDelta = missile.vessel.horizontalSrfSpeed - _missile.CruiseSpeed;

            if (_futureSpeed > missile.CruiseSpeed)
                ThrottleDecision = ThrottleDecision.Decrease;
            else if (Math.Abs(_futureSpeed - _missile.CruiseSpeed) < maxError)
                ThrottleDecision = ThrottleDecision.Hold;
            else
                ThrottleDecision = ThrottleDecision.Increase;

            switch (ThrottleDecision)
            {
                case ThrottleDecision.Increase:
                    missile.Throttle = Mathf.Clamp(missile.Throttle + 0.001f, 0, 1f);
                    break;

                case ThrottleDecision.Decrease:
                    missile.Throttle = Mathf.Clamp(missile.Throttle - 0.001f, 0, 1f);
                    break;

                case ThrottleDecision.Hold:
                    break;
            }

            _lastSpeedDelta = currentSpeedDelta;
        }

        private void MakeDecisionAboutPitch(MissileBase missile, double missileAltitude)
        {
            _futureAltitude = CalculateFutureAltitude(_missile.CruisePredictionTime);

            PitchDecision futureDecision;

            double terrainRate = Vector3d.Dot(missile.vessel.Velocity(), missile.vessel.terrainNormal);
            double cruiseAlt = (double)_missile.CruiseAltitude;

            if (this.GuidanceState != GuidanceState.Terminal &&
                ((missileAltitude < 2d && terrainRate < 0d) || CalculateFutureAltitude(1f) <= 0d))
            {
                futureDecision = PitchDecision.EmergencyAscent;
            }
            else if (this.GuidanceState != GuidanceState.Terminal && CalculateFutureCollision(_missile.CruisePredictionTime))
            {
                futureDecision = PitchDecision.EmergencyAscent;
            }
            else if (_futureAltitude < cruiseAlt || (missileAltitude < cruiseAlt && terrainRate < 0d))
            {
                if (_futureAltitude < Math.Min(0.5d * cruiseAlt, cruiseAlt - 20d) && terrainRate < 0d)
                {
                    futureDecision = PitchDecision.CoarseAscent;
                }
                else
                {
                    futureDecision = PitchDecision.Ascent;
                }
            }
            else if (_futureAltitude > cruiseAlt || (missileAltitude > cruiseAlt && terrainRate > 0d))
            {
                if (_futureAltitude > Math.Max(2d * cruiseAlt, cruiseAlt + 20d) && terrainRate > 0d)
                {
                    futureDecision = PitchDecision.CoarseDescent;
                }
                else
                {
                    futureDecision = PitchDecision.Descent;
                }
            }
            else
            {
                futureDecision = PitchDecision.Hold;
            }

            switch (futureDecision)
            {
                case PitchDecision.EmergencyAscent:
                    if (PitchDecision == futureDecision)
                    {
                        _pitchAngle = Mathf.Clamp(_pitchAngle + 1f, 1.5f, 100f);
                    }
                    else
                    {
                        _pitchAngle = 1.5f;
                    }
                    break;

                case PitchDecision.CoarseAscent:
                    _pitchAngle = Mathf.Clamp(_pitchAngle + 0.01f, -1.5f, 2.75f);
                    break;

                case PitchDecision.CoarseDescent:
                    _pitchAngle = Mathf.Clamp(_pitchAngle - 0.01f, -2.75f, 1.5f);
                    break;

                case PitchDecision.Ascent:
                    _pitchAngle = Mathf.Clamp(_pitchAngle + 0.0025f, -1.5f, 1.5f);
                    break;

                case PitchDecision.Descent:
                    _pitchAngle = Mathf.Clamp(_pitchAngle - 0.0025f, -1.5f, 1.5f);
                    break;

                case PitchDecision.Hold:
                    break;
            }

            PitchDecision = futureDecision;
        }

        private double CalculateFutureAltitude(double predictionTime)
        {
            Vector3d futurePosition = _missile.vessel.CoMD + _missile.vessel.Velocity() * predictionTime
                + 0.5d * _missile.vessel.acceleration_immediate * predictionTime * predictionTime;

            return GetCurrentAltitudeAtPosition(futurePosition);
        }

        private double CalculateFutureSpeed(float time)
        {
            return _missile.vessel.horizontalSrfSpeed + (_horizontalAcceleration / Time.fixedDeltaTime) * time;
        }

        private bool MissileWillReachAltitude(double currentAltitude)
        {
            return _missile.vessel.orbit.ApA > _missile.CruiseAltitude;
        }
    }
}
