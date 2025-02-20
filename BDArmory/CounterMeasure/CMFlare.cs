using System;
using System.Collections.Generic;
using UniLinq;
using UnityEngine;

using BDArmory.FX;
using BDArmory.Settings;
using BDArmory.UI;
using BDArmory.Utils;

namespace BDArmory.CounterMeasure
{
    public class CMFlare : MonoBehaviour
    {
        List<KSPParticleEmitter> pEmitters;

        Light[] lights;
        float startTime;

        public bool alive = true;

        Vector3 upDirection;

        public Vector3 velocity;

        public Tuple<float, Part> thermalSig; //heat value
        public float thermal;
        float minThermal;
        float startThermal;

        float lifeTime = 5;

        public void SetThermal(Vessel sourceVessel)
        {
            // OLD:
            //thermal = BDArmorySetup.FLARE_THERMAL*UnityEngine.Random.Range(0.45f, 1.25f);
            // NEW (1.9.1 and before): generate flare within spectrum of emitting vessel's heat signature
            //thermal = BDATargetManager.GetVesselHeatSignature(sourceVessel) * UnityEngine.Random.Range(0.65f, 1.75f);

            // NEW NEW: Dynamic min/max based on engine heat, with larger multiplier for colder engines, and smaller for naturally hot engines
            // since range of values are too small for smaller heat values, and flares tend to decay to even colder values, rendering them useless

            /* Alternate flare gen code, adjusts curve towards high end up to 5000K heat engines. Polynomial versions available.

            thermal = BDATargetManager.GetVesselHeatSignature(sourceVessel);
            //float thermalMinMult = Mathf.Clamp(-0.166f * (float)Math.Log(thermal) + 1.9376f, 0.5f, 0.82f);
            //float thermalMaxMult = Mathf.Clamp(0.3534f * (float)Math.Log(thermal) - 1.0251f, 1.35f, 2.0f);

            thermal *= UnityEngine.Random.Range(thermalMinMult, thermalMaxMult);

            if (BDArmorySettings.DEBUG_LABELS)
                Debug.Log("[BDArmory.CMFlare]: New flare generated from " + sourceVessel.GetDisplayName() + ":" + BDATargetManager.GetVesselHeatSignature(sourceVessel).ToString("0.0") + ", heat: " + thermal.ToString("0.0") + " mult: " + thermalMinMult + "-" + thermalMaxMult);
            */

            // NEW (1.10 and later): generate flare within spectrum of emitting vessel's heat signature, but narrow range for low heats

            thermalSig = BDATargetManager.GetVesselHeatSignature(sourceVessel, Vector3.zero); //if enabling heatSig occlusion in IR missiles the thermal value of flares will have to be adjusted to compensate.
            thermal = thermalSig.Item1;
            //Then again, these are being ejected in a range of temps, which should cover potential differences in heatreturn from a target based on occlusion. Have vector3.Zero replaced with missile position to sim occlusion level missile owuld see and set flare temps accordingly?
            // float minMult = Mathf.Clamp(-0.265f * Mathf.Log(sourceHeat) + 2.3f, 0.65f, 0.8f);
            float thermalMinMult = Mathf.Clamp(((0.00093f * thermal * thermal - 1.4457f * thermal + 1141.95f) / 1000f), 0.65f, 0.8f); // Equivalent to above, but uses polynomial for speed
            thermal *= UnityEngine.Random.Range(thermalMinMult, Mathf.Max(BDArmorySettings.FLARE_FACTOR, 0f) - thermalMinMult + 0.8f);

            if (BDArmorySettings.DEBUG_OTHER)
                Debug.Log("[BDArmory.CMFlare]: New flare generated from " + sourceVessel.GetName() + ":" + thermalSig.Item1.ToString("0.0") + ", heat: " + thermal.ToString("0.0"));
        }

        void OnEnable()
        {
            startThermal = thermal;
            minThermal = startThermal * 0.34f; // 0.3 is original value, but doesn't work well for Tigers, 0.4f gives decent performance for Tigers, 0.65 decay gives best flare performance overall based on some monte carlo analysis
            if (pEmitters == null)
            {
                pEmitters = new List<KSPParticleEmitter>();

                using (var pe = gameObject.GetComponentsInChildren<KSPParticleEmitter>().Cast<KSPParticleEmitter>().GetEnumerator())
                    while (pe.MoveNext())
                    {
                        if (pe.Current == null) continue;
                        {
                            EffectBehaviour.AddParticleEmitter(pe.Current);
                            pEmitters.Add(pe.Current);
                        }
                    }
            }

            EnableEmitters();

            ++BDArmorySetup.numberOfParticleEmitters;

            if (lights == null)
            {
                lights = gameObject.GetComponentsInChildren<Light>();
            }

            using (IEnumerator<Light> lgt = lights.AsEnumerable().GetEnumerator())
                while (lgt.MoveNext())
                {
                    if (lgt.Current == null) continue;
                    lgt.Current.enabled = true;
                }
            startTime = Time.time;

            //ksp force applier
            //gameObject.AddComponent<KSPForceApplier>().drag = 0.4f;

            BDArmorySetup.Flares.Add(this);

            upDirection = VectorUtils.GetUpDirection(transform.position);

            this.transform.localScale = Vector3.one;
        }

        void FixedUpdate()
        {
            if (!gameObject.activeInHierarchy)
            {
                return;
            }

            //floating origin and velocity offloading corrections
            if (BDKrakensbane.IsActive)
            {
                transform.localPosition -= BDKrakensbane.FloatingOriginOffsetNonKrakensbane;
            }

            if (velocity != Vector3.zero)
            {
                transform.localRotation = Quaternion.LookRotation(velocity, upDirection);
            }

            //Particle effects
            //downforce
            Vector3 downForce = (Mathf.Clamp(velocity.magnitude, 0.1f, 150) / 150) * 20 * upDirection; //smoke should rise, not fall...

            //turbulence
            using (var pEmitter = pEmitters.GetEnumerator())
                while (pEmitter.MoveNext())
                {
                    if (pEmitter.Current == null) continue;
                    try
                    {
                        pEmitter.Current.worldVelocity = 2 * ParticleTurbulence.flareTurbulence + downForce; 
                    }
                    catch (NullReferenceException e)
                    {
                        Debug.LogWarning("[BDArmory.CMFlare]: NRE setting worldVelocity: " + e.Message);
                    }

                    try
                    {
                        if (FlightGlobals.ActiveVessel && FlightGlobals.ActiveVessel.atmDensity <= 0)
                        {
                            pEmitter.Current.emit = false;
                        }
                    }
                    catch (NullReferenceException e)
                    {
                        Debug.LogWarning("[BDArmory.CMFlare]: NRE checking density: " + e.Message);
                    }
                }
            //

            //thermal decay
            thermal = Mathf.MoveTowards(thermal, minThermal,
                ((thermal - minThermal) / lifeTime) * Time.fixedDeltaTime);

            if (Time.time - startTime > lifeTime) //stop emitting after lifeTime seconds
            {
                alive = false;
                BDArmorySetup.Flares.Remove(this);
                transform.localScale = Vector3.zero;
                using (var pe = pEmitters.GetEnumerator())
                    while (pe.MoveNext())
                    {
                        if (pe.Current == null) continue;
                        pe.Current.emit = false;
                    }
                using (var lgt = lights.AsEnumerable().GetEnumerator())
                    while (lgt.MoveNext())
                    {
                        if (lgt.Current == null) continue;
                        lgt.Current.enabled = false;
                    }
            }

            if (Time.time - startTime > lifeTime + 11) //disable object after x seconds
            {
                --BDArmorySetup.numberOfParticleEmitters;
                gameObject.SetActive(false);
                return;
            }

            //physics
            //atmospheric drag (stock)
            float simSpeedSquared = velocity.sqrMagnitude;
            Vector3 currPos = transform.position;
            const float mass = 0.001f;
            const float drag = 1f;
            Vector3 dragForce = (0.008f * mass) * drag * 0.5f * simSpeedSquared *
                                (float)
                                FlightGlobals.getAtmDensity(FlightGlobals.getStaticPressure(currPos),
                                    FlightGlobals.getExternalTemperature(), FlightGlobals.currentMainBody) *
                                velocity.normalized;

            velocity -= (dragForce / mass) * Time.fixedDeltaTime;
            //

            //gravity
            if (FlightGlobals.RefFrameIsRotating)
                velocity += FlightGlobals.getGeeForceAtPosition(currPos) * Time.fixedDeltaTime;

            transform.localPosition += velocity * Time.fixedDeltaTime;
        }

        public void EnableEmitters()
        {
            if (pEmitters == null) return;
            using (var emitter = pEmitters.GetEnumerator())
                while (emitter.MoveNext())
                {
                    if (emitter.Current == null) continue;
                    if (emitter.Current.name == "pEmitter") emitter.Current.emit = BDArmorySettings.FlareSmoke;
                    else emitter.Current.emit = true;
                }
        }
    }
}