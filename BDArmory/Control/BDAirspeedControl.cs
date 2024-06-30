using System.Collections.Generic;
using UnityEngine;

using BDArmory.Extensions;
using BDArmory.Utils;

namespace BDArmory.Control
{
    public class BDAirspeedControl : MonoBehaviour //: PartModule
    {
        //[KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "TargetSpeed"),
        //	UI_FloatRange(minValue = 1f, maxValue = 420f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float targetSpeed = 0;
        public float throttleOverride = -1f;
        public bool useBrakes = true;
        public float brakingPriority = 0.5f;
        public bool allowAfterburner = true;
        public bool forceAfterburner = false;
        public float afterburnerPriority = 50f;
        public bool forceAfterburnerIfMaxThrottle = false;

        //[KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "ThrottleFactor"),
        //	UI_FloatRange(minValue = 1f, maxValue = 20f, stepIncrement = .5f, scene = UI_Scene.All)]
        public float throttleFactor = 2f;

        public Vessel vessel;

        AxisGroupsModule axisGroupsModule;
        bool hasAxisGroupsModule = false; // To avoid repeated null checks

        bool controlEnabled;

        float possibleAccel;
        private float smoothedAccel = 0; // smoothed acceleration, prevents super fast toggling of afterburner
        bool shouldSetAfterburners = false;
        bool setAfterburnersEnabled = false;
        float geeForce = 9.81f;
        float gravAccel = 0;
        public float TWR { get; private set; } = 1; // Maximum TWR for the current engine modes.

        //[KSPField(guiActive = true, guiName = "Thrust")]
        public float debugThrust;

        public List<MultiModeEngine> multiModeEngines;

        void Start()
        {
            axisGroupsModule = vessel.FindVesselModuleImplementingBDA<AxisGroupsModule>(); // Look for an axis group module.
            if (axisGroupsModule != null) hasAxisGroupsModule = true;
        }

        //[KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "ToggleAC")]
        public void Toggle()
        {
            if (controlEnabled)
            {
                Deactivate();
            }
            else
            {
                Activate();
            }
        }

        public void Activate()
        {
            controlEnabled = true;
            vessel.OnFlyByWire -= AirspeedControl;
            vessel.OnFlyByWire += AirspeedControl;
            multiModeEngines = new List<MultiModeEngine>();
        }

        public void Deactivate()
        {
            controlEnabled = false;
            vessel.OnFlyByWire -= AirspeedControl;
        }

        void AirspeedControl(FlightCtrlState s)
        {
            if (targetSpeed == 0)
            {
                if (useBrakes)
                    vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, true);
                SetThrottle(s, 0);
                return;
            }

            float currentSpeed = (float)vessel.srfSpeed;
            float speedError = targetSpeed - currentSpeed;

            float setAccel = speedError * throttleFactor;

            SetAcceleration(setAccel, s);

            if (forceAfterburnerIfMaxThrottle && s.mainThrottle == 1f)
                SetAfterBurners(true);
            else if (shouldSetAfterburners)
                SetAfterBurners(setAfterburnersEnabled);
        }

        void SetAcceleration(float accel, FlightCtrlState s)
        {
            gravAccel = GravAccel();
            float requestEngineAccel = accel - gravAccel;

            possibleAccel = 0; //gravAccel;

            float engineAccel = MaxEngineAccel(requestEngineAccel, out float dragAccel);

            if (throttleOverride >= 0)
            {
                SetThrottle(s, throttleOverride);
                return;
            }
            if (engineAccel == 0)
            {
                SetThrottle(s, accel > 0 ? 1 : 0);
                return;
            }

            requestEngineAccel = Mathf.Clamp(requestEngineAccel, -engineAccel, engineAccel);

            float requestThrottle = (requestEngineAccel - dragAccel) / engineAccel;

            SetThrottle(s, Mathf.Clamp01(requestThrottle));

            //use brakes if overspeeding too much
            if (useBrakes)
            {
                if (requestThrottle < brakingPriority - 1f)
                {
                    vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, true);
                }
                else
                {
                    vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, false);
                }
            }
        }

        /// <summary>
        /// Set the main throttle and the corresponding axis group.
        /// </summary>
        /// <param name="s">The flight control state</param>
        /// <param name="value">The throttle value</param>
        public void SetThrottle(FlightCtrlState s, float value)
        {
            s.mainThrottle = value;
            if (hasAxisGroupsModule)
            {
                axisGroupsModule.UpdateAxisGroup(KSPAxisGroup.MainThrottle, 2f * value - 1f); // Throttle is full-axis: 0—1 throttle maps to -1—1 axis.
            }
        }

        float MaxEngineAccel(float requestAccel, out float dragAccel)
        {
            float maxThrust = 0;
            float finalThrust = 0;
            multiModeEngines.Clear();

            using (var engines = VesselModuleRegistry.GetModuleEngines(vessel).GetEnumerator())
                while (engines.MoveNext())
                {
                    if (engines.Current == null) continue;
                    if (!engines.Current.EngineIgnited) continue;

                    MultiModeEngine mme = engines.Current.part.FindModuleImplementing<MultiModeEngine>();
                    if (IsAfterBurnerEngine(mme))
                    {
                        multiModeEngines.Add(mme);
                        mme.autoSwitch = false;
                    }

                    if (mme && mme.mode != engines.Current.engineID) continue;
                    float engineThrust = engines.Current.maxThrust;
                    if (engines.Current.atmChangeFlow)
                    {
                        engineThrust *= engines.Current.flowMultiplier;
                    }
                    maxThrust += Mathf.Max(0f, engineThrust * (engines.Current.thrustPercentage / 100f)); // Don't include negative thrust percentage drives (Danny2462 drives) as they don't contribute to the thrust.

                    finalThrust += engines.Current.finalThrust;
                }

            debugThrust = maxThrust;

            float vesselMass = vessel.GetTotalMass();

            float accel = maxThrust / vesselMass; // This assumes that all thrust is in the same direction.
            TWR = accel / geeForce; // GravAccel gets called before this.

            float alpha = 0.05f; // Approx 25 frame (0.5s) lag (similar to 50 frames moving average, but with more weight on recent values and much faster to calculate).
            smoothedAccel = smoothedAccel * (1f - alpha) + alpha * accel;

            //estimate drag
            float estimatedCurrentAccel = finalThrust / vesselMass - gravAccel;
            Vector3 vesselAccelProjected = Vector3.Project(vessel.acceleration_immediate, vessel.velocityD.normalized);
            float actualCurrentAccel = vesselAccelProjected.magnitude * Mathf.Sign(Vector3.Dot(vesselAccelProjected, vessel.velocityD.normalized));
            float accelError = (actualCurrentAccel - estimatedCurrentAccel); // /2 -- why divide by 2 here?
            dragAccel = accelError;

            possibleAccel += accel; // This assumes that the acceleration from engines is in the same direction as the original possibleAccel.
            forceAfterburner = forceAfterburner || (afterburnerPriority == 100f);
            allowAfterburner = allowAfterburner && (afterburnerPriority != 0f);

            //use multimode afterburner for extra accel if lacking
            if (allowAfterburner && (forceAfterburner || smoothedAccel < requestAccel * (1.5f / (Mathf.Exp(100f / 27f) - 1f) * (Mathf.Exp(Mathf.Clamp(afterburnerPriority, 0f, 100f) / 27f) - 1f))))
            { shouldSetAfterburners = true; setAfterburnersEnabled = true; }
            else if (!allowAfterburner || (!forceAfterburner && smoothedAccel > requestAccel * (1f + 0.5f / (Mathf.Exp(50f / 25f) - 1f) * (Mathf.Exp(Mathf.Clamp(afterburnerPriority, 0f, 100f) / 25f) - 1f))))
            { shouldSetAfterburners = true; setAfterburnersEnabled = false; }
            else
            { shouldSetAfterburners = false; }
            return accel;
        }

        void SetAfterBurners(bool enable)
        {
            using (var mmes = multiModeEngines.GetEnumerator())
                while (mmes.MoveNext())
                {
                    if (mmes.Current == null) continue;

                    bool afterburnerHasFuel = true;
                    if (!CheatOptions.InfinitePropellant)
                    {
                        using var fuel = mmes.Current.SecondaryEngine.propellants.GetEnumerator();
                        while (fuel.MoveNext())
                        {
                            if (!GetABresources(fuel.Current.id)) { afterburnerHasFuel = false; break; }
                        }
                    }
                    if (enable && afterburnerHasFuel)
                    {
                        if (mmes.Current.runningPrimary)
                        {
                            if (afterburnerHasFuel) mmes.Current.Events["ModeEvent"].Invoke();
                        }
                    }
                    else
                    {
                        if (!mmes.Current.runningPrimary)
                        {
                            mmes.Current.Events["ModeEvent"].Invoke();
                        }
                    }

                }
        }
        public bool GetABresources(int fuelID)
        {
            vessel.GetConnectedResourceTotals(fuelID, out double fuelCurrent, out double fuelMax);
            return fuelCurrent > 0;
        }
        private static bool IsAfterBurnerEngine(MultiModeEngine engine)
        {
            if (engine == null)
            {
                return false;
            }
            return engine.primaryEngineID == "Dry" && engine.secondaryEngineID == "Wet";
            //presumably there's a reason this is looking specifically for MMEs with "Wet" and "Dry" as the IDs instead of !String.IsNullOrEmpty(engine.primaryEngineID). To permit only properly configured Jets?

        }

        float GravAccel()
        {
            Vector3 geeVector = FlightGlobals.getGeeForceAtPosition(vessel.CoM);
            geeForce = geeVector.magnitude;
            return geeForce * Mathf.Cos(Mathf.Deg2Rad * Vector3.Angle(-geeVector, vessel.velocityD)); // -g.v/|v| ???
        }

        public float GetPossibleAccel()
        {
            return possibleAccel;
        }
    }

    public class BDLandSpeedControl : MonoBehaviour
    {
        public float targetSpeed;
        public float signedSrfSpeed;
        public Vessel vessel;
        public bool preventNegativeZeroPoint = false;

        AxisGroupsModule axisGroupsModule;
        bool hasAxisGroupsModule = false; // To avoid repeated null checks

        private float lastThrottle;
        public float zeroPoint { get; private set; }

        private const float gain = 0.5f;
        private const float zeroMult = 0.02f;

        void Start()
        {
            axisGroupsModule = vessel.FindVesselModuleImplementingBDA<AxisGroupsModule>(); // Look for an axis group module.
            if (axisGroupsModule != null) hasAxisGroupsModule = true;
        }

        public void Activate()
        {
            vessel.OnFlyByWire -= SpeedControl;
            vessel.OnFlyByWire += SpeedControl;
            zeroPoint = 0;
            lastThrottle = 0;
        }

        public void Deactivate()
        {
            vessel.OnFlyByWire -= SpeedControl;
        }

        void SpeedControl(FlightCtrlState s)
        {
            if (!vessel.LandedOrSplashed)
                SetThrottle(s, 0);
            else if (targetSpeed == 0)
            {
                vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, true);
                SetThrottle(s, 0);
            }
            else
            {
                float throttle = zeroPoint + (targetSpeed - signedSrfSpeed) * gain;
                lastThrottle = Mathf.Clamp(throttle, -1, 1);
                zeroPoint = (zeroPoint + lastThrottle * zeroMult) * (1 - zeroMult);
                if (preventNegativeZeroPoint && zeroPoint < 0) zeroPoint = 0;
                SetThrottle(s, lastThrottle);
                vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, (targetSpeed * signedSrfSpeed < -5));
            }
        }

        /// <summary>
        /// Set the wheel throttle and the corresponding axis group.
        /// </summary>
        /// <param name="s">The flight control state</param>
        /// <param name="value">The throttle value</param>
        public void SetThrottle(FlightCtrlState s, float value)
        {
            s.wheelThrottle = value;
            if (hasAxisGroupsModule)
            {
                axisGroupsModule.UpdateAxisGroup(KSPAxisGroup.MainThrottle, 2f * value - 1f); // Throttle is full-axis: 0—1 throttle maps to -1—1 axis.
            }
        }
    }

    public class BDVTOLSpeedControl : MonoBehaviour
    {
        public float targetAltitude;
        public Vessel vessel;
        public bool preventNegativeZeroPoint = false;

        AxisGroupsModule axisGroupsModule;
        bool hasAxisGroupsModule = false; // To avoid repeated null checks

        private float altIntegral;
        public float zeroPoint { get; private set; }

        private const float Kp = 0.5f;
        private const float Kd = 0.55f;
        private const float Ki = 0.03f;

        void Start()
        {
            axisGroupsModule = vessel.FindVesselModuleImplementingBDA<AxisGroupsModule>(); // Look for an axis group module.
            if (axisGroupsModule != null) hasAxisGroupsModule = true;
        }

        public void Activate()
        {
            vessel.OnFlyByWire -= AltitudeControl;
            vessel.OnFlyByWire += AltitudeControl;
            altIntegral = 0;
        }

        public void Deactivate()
        {
            vessel.OnFlyByWire -= AltitudeControl;
        }

        void AltitudeControl(FlightCtrlState s)
        {

            if (targetAltitude == 0)
            {
                vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, true);
                SetThrottle(s, 0);
            }
            else
            {
                float altError = (targetAltitude - (float)vessel.radarAltitude);
                float altP = Kp * (targetAltitude - (float)vessel.radarAltitude);
                float altD = Kd * (float)vessel.verticalSpeed;
                altIntegral = Ki * Mathf.Clamp(altIntegral + altError * Time.deltaTime, -1f, 1f);

                float throttle = altP + altIntegral - altD;
                SetThrottle(s, Mathf.Clamp01(throttle));

                vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, throttle < -5f);
            }
        }

        /// <summary>
        /// Set the main throttle and the corresponding axis group.
        /// </summary>
        /// <param name="s">The flight control state</param>
        /// <param name="value">The throttle value</param>
        public void SetThrottle(FlightCtrlState s, float value)
        {
            s.mainThrottle = value;
            if (hasAxisGroupsModule)
            {
                axisGroupsModule.UpdateAxisGroup(KSPAxisGroup.MainThrottle, 2f * value - 1f); // Throttle is full-axis: 0—1 throttle maps to -1—1 axis.
            }
        }
    }

    public class BDOrbitalControl : MonoBehaviour //: PartModule
    {

        // /////////////////////////////////////////////////////
        public Vessel vessel;
        public Vector3 attitude = Vector3.zero;
        private Vector3 attitudeLerped;
        private float error;
        private float angleLerp;
        public bool lerpAttitude = true;
        private float lerpRate;
        private bool lockAttitude = false;
        public bool PIDActive = false;

        private bool facingDesiredRotation;
        public float throttle;
        public float throttleActual;
        internal float throttleLerped;
        public float throttleLerpRate = 1;
        public bool lerpThrottle = true;
        public float rcsLerpRate = 5;
        public bool rcsRotate = false;
        public float alignmentToleranceforBurn = 5;

        AxisGroupsModule axisGroupsModule;
        bool hasAxisGroupsModule = false; // To avoid repeated null checks

        public Vector3 RCSVector;
        public Vector3 RCSVectorLerped = Vector3.zero;
        public float RCSPower = 3f;
        private Vector3 RCSThrust;
        private Vector3 up, right, forward;
        private float RCSThrottle;

        //[KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "ToggleAC")]

        void Start()
        {
            if (!vessel.IsMissile())
            {
                axisGroupsModule = vessel.FindVesselModuleImplementingBDA<AxisGroupsModule>(); // Look for an axis group module.
                if (axisGroupsModule != null) hasAxisGroupsModule = true;
            }
        }

        public void Activate()
        {
            vessel.OnFlyByWire -= OrbitalControl;
            vessel.OnFlyByWire += OrbitalControl;
        }

        public void Deactivate()
        {
            vessel.OnFlyByWire -= OrbitalControl;
        }

        void OrbitalControl(FlightCtrlState s)
        {
            error = Vector3.Angle(vessel.ReferenceTransform.up, attitude);

            if (!PIDActive)
                UpdateSAS(s);
            UpdateThrottle(s);
            UpdateRCS(s);
        }

        private void UpdateThrottle(FlightCtrlState s)
        {
            facingDesiredRotation = error < alignmentToleranceforBurn;
            throttleActual = facingDesiredRotation ? throttle : 0;

            // Move actual throttle towards throttle target gradually.
            throttleLerped = Mathf.MoveTowards(throttleLerped, throttleActual, throttleLerpRate * Time.fixedDeltaTime);

            SetThrottle(s, lerpThrottle ? throttleLerped : throttleActual);

        }

        /// <summary>
        /// Set the main throttle and the corresponding axis group.
        /// </summary>
        /// <param name="s">The flight control state</param>
        /// <param name="value">The throttle value</param>
        public void SetThrottle(FlightCtrlState s, float value)
        {
            s.mainThrottle = value;
            if (hasAxisGroupsModule)
            {
                axisGroupsModule.UpdateAxisGroup(KSPAxisGroup.MainThrottle, 2f * value - 1f); // Throttle is full-axis: 0—1 throttle maps to -1—1 axis.
            }
        }

        void UpdateRCS(FlightCtrlState s)
        {
            if (RCSVector == Vector3.zero) return;

            if (RCSVectorLerped == Vector3.zero)
                RCSVectorLerped = RCSVector;

            float rcsLerpMag = RCSVectorLerped.magnitude;
            float rcsLerpT = rcsLerpRate * Time.fixedDeltaTime * Mathf.Clamp01(rcsLerpMag / RCSPower);

            if (rcsRotate) // Quickly rotate RCS thrust towards commanded RCSVector
                RCSVectorLerped = Vector3.Slerp(RCSVectorLerped, RCSVector, rcsLerpT);
            else // Gradually lerp RCS thrust towards commanded RCSVector
                RCSVectorLerped = Vector3.Lerp(RCSVectorLerped, RCSVector, rcsLerpT);
            RCSThrottle = Mathf.Lerp(0, 1.732f, Mathf.InverseLerp(0, RCSPower, rcsLerpMag));
            RCSThrust = RCSVectorLerped.normalized * RCSThrottle;

            up = -vessel.ReferenceTransform.forward;
            forward = -vessel.ReferenceTransform.up;
            right = Vector3.Cross(up, forward);

            SetAxisControlState(s,
                Mathf.Clamp(Vector3.Dot(RCSThrust, right), -1, 1),
                Mathf.Clamp(Vector3.Dot(RCSThrust, up), -1, 1),
                Mathf.Clamp(Vector3.Dot(RCSThrust, forward), -1, 1));

        }

        void UpdateSAS(FlightCtrlState s)
        {
            if (attitude == Vector3.zero || lockAttitude) return;

            // SAS must be turned off. Don't know why.
            if (vessel.ActionGroups[KSPActionGroup.SAS])
                vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, false);

            var ap = vessel.Autopilot;
            if (ap == null) return;

            // The offline SAS must not be on stability assist. Normal seems to work on most probes.
            if (ap.Mode != VesselAutopilot.AutopilotMode.Normal)
                ap.SetMode(VesselAutopilot.AutopilotMode.Normal);

            // Lerp attitude while burning to reduce instability.
            if (lerpAttitude)
            {
                angleLerp = Mathf.InverseLerp(0, 10, error);
                lerpRate = Mathf.Lerp(1, 10, angleLerp);
                attitudeLerped = Vector3.Lerp(attitudeLerped, attitude, lerpRate * Time.deltaTime);
            }

            ap.SAS.SetTargetOrientation(throttleLerped > 0 && lerpAttitude ? attitudeLerped : attitude, false);
        }

        public void Stability(bool enable)
        {
            if (lockAttitude == enable) return;
            lockAttitude = enable;

            var ap = vessel.Autopilot;
            if (ap == null) return;

            vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, enable);
            ap.SetMode(enable ? VesselAutopilot.AutopilotMode.StabilityAssist : VesselAutopilot.AutopilotMode.Normal);
        }

        /// <summary>
        /// Set the axis control state and also the corresponding axis groups.
        /// </summary>
        /// <param name="s">The flight control state</param>
        /// <param name="X">x</param>
        /// <param name="Y">y</param>
        /// <param name="Z">z</param>
        protected virtual void SetAxisControlState(FlightCtrlState s, float X, float Y, float Z)
        {
            s.X = X;
            s.Y = Y;
            s.Z = Z;
            if (hasAxisGroupsModule)
            {
                axisGroupsModule.UpdateAxisGroup(KSPAxisGroup.TranslateX, X);
                axisGroupsModule.UpdateAxisGroup(KSPAxisGroup.TranslateY, Y);
                axisGroupsModule.UpdateAxisGroup(KSPAxisGroup.TranslateZ, Z);
            }
        }

    }
}
