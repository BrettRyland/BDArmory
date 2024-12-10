using System;
using UnityEngine;

using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.Utils;
using BDArmory.Weapons.Missiles;

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
        EmergencyAscent
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

        private Vector3 planarDirectionToTarget;
        private Vector3 upDirection;

        private float _popupCos = -1f;
        private float _popupSin = -1f;

        // Popup 1/g
        const float invG = 1f / 10f; // 1/maneuver G
        const float invg = 1f / 9.80665f; // 1/gravity on Earth/Kerbin

        public CruiseGuidance(MissileBase missile)
        {
            _missile = missile;
        }

        public ThrottleDecision ThrottleDecision { get; set; }
        public PitchDecision PitchDecision { get; set; }

        public GuidanceState GuidanceState { get; set; }

        public Vector3 GetDirection(MissileBase missile, Vector3 targetPosition, Vector3 targetVelocity)
        {
            //set up
            if (_missile.TimeIndex < 1)
                return _missile.vessel.CoM + _missile.vessel.Velocity() * 10;

            upDirection = _missile.vessel.up;

            planarDirectionToTarget = (targetPosition - _missile.vessel.CoM).ProjectOnPlanePreNormalized(upDirection).normalized;

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

                    return _missile.vessel.CoM + (planarDirectionToTarget + upDirection) * 10f;

                case GuidanceState.Cruising:

                    //Altitude control
                    UpdatePitch(missileAltitude);
                    UpdateThrottle();
                    CheckIfTerminal(missileAltitude, targetPosition, upDirection);

                    return _missile.vessel.CoM + 10 * planarDirectionToTarget + _pitchAngle * upDirection;

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

                    return _missile.vessel.CoM + 50f * (planarDirectionToTarget * _popupCos + upDirection * _popupSin);
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

        private bool CheckIfTerminal(double altitude, Vector3 targetPosition, Vector3 upDirection)
        {
            Vector3 surfacePos = _missile.vessel.CoM +
                                 Vector3.Project(targetPosition - _missile.vessel.CoM, -upDirection);

            float distanceToTarget = Vector3.Distance(surfacePos, targetPosition);

            if (_missile.CruisePopup)
            {
                if (_popupCos < 0)
                {
                    _popupCos = Mathf.Cos(_missile.CruisePopupAngle * Mathf.Deg2Rad);
                    _popupSin = Mathf.Sin(_missile.CruisePopupAngle * Mathf.Deg2Rad);
                }

                if (distanceToTarget < _missile.CruisePopupRange + _futureSpeed * _missile.CruisePredictionTime)
                {
                    float a = Vector3.Dot(_missile.GetForwardTransform(), upDirection);

                    _futureSpeed = CalculateFutureSpeed((_missile.CruisePopupAngle * Mathf.Deg2Rad - Mathf.Acos(a)) * (float)_lastHorizontalSpeed * invG * invg);

                    float turnDist = (float)(_futureSpeed * _futureSpeed) * invG * invg * (_popupSin - a);

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
                    _missile.debugString.AppendLine($"Distance to target" + distanceToTarget);
                    _missile.debugString.AppendLine($"freefallTime" + freefallTime);
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
            var terrainRay = new Ray(this._missile.vessel.CoM, this._missile.vessel.Velocity());
            RaycastHit hit;
            return Physics.Raycast(terrainRay, out hit, (float)(this._missile.vessel.srfSpeed * predictionTime), (int)(LayerMasks.Scenery | LayerMasks.EVA)); // Why EVA?
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

            if (this.GuidanceState != GuidanceState.Terminal &&
                (missileAltitude < 4d || CalculateFutureAltitude(1f) < 4d))
            {
                futureDecision = PitchDecision.EmergencyAscent;
            }
            else if (this.GuidanceState != GuidanceState.Terminal && CalculateFutureCollision(_missile.CruisePredictionTime))
            {
                futureDecision = PitchDecision.EmergencyAscent;
            }
            else if (_futureAltitude < missile.CruiseAltitude || missileAltitude < missile.CruiseAltitude)
            {
                futureDecision = PitchDecision.Ascent;
            }
            else if (_futureAltitude > missile.CruiseAltitude || missileAltitude > missile.CruiseAltitude)
            {
                futureDecision = PitchDecision.Descent;
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

                case PitchDecision.Ascent:
                    _pitchAngle = Mathf.Clamp(_pitchAngle + 0.0055f, -1.5f, 1.5f);
                    break;

                case PitchDecision.Descent:
                    _pitchAngle = Mathf.Clamp(_pitchAngle - 0.0025f, -1.5f, 1.5f);
                    break;

                case PitchDecision.Hold:
                    break;
            }

            PitchDecision = futureDecision;
        }

        private double CalculateFutureAltitude(float predictionTime)
        {
            Vector3 futurePosition = _missile.vessel.CoM + _missile.vessel.Velocity() * predictionTime
                + 0.5f * _missile.vessel.acceleration_immediate * predictionTime * predictionTime;

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
