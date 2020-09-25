using System;
using System.Collections.Generic;
using BDArmory.Core;
using BDArmory.FX;
using BDArmory.Misc;
using BDArmory.UI;
using UniLinq;
using UnityEngine;

namespace BDArmory.CounterMeasure
{
    public class CMFlare : MonoBehaviour
    {
        List<KSPParticleEmitter> pEmitters; // = new List<KSPParticleEmitter>();
        List<BDAGaplessParticleEmitter> gaplessEmitters; // = new List<BDAGaplessParticleEmitter>();

        Light[] lights;
        float startTime;

        public bool alive = true;

        Vector3 upDirection;

        public Vector3 velocity;

        public float thermal; //heat value
        float minThermal;
        float startThermal;

        float lifeTime = 5;

        public void SetThermal(Vessel sourceVessel)
        {
            // OLD:
            //thermal = BDArmorySetup.FLARE_THERMAL*UnityEngine.Random.Range(0.45f, 1.25f);
            // NEW (1.9.1 and before): generate flare within spectrum of emitting vessel's heat signature
            //thermal = BDATargetManager.GetVesselHeatSignature(sourceVessel) * UnityEngine.Random.Range(0.65f, 1.75f);

            // NEW (1.10 and later): generate flare within spectrum of emitting vessel's heat signature, but narrow range for low heats
            float sourceHeat = BDATargetManager.GetVesselHeatSignature(sourceVessel);
            // float minMult = Mathf.Clamp(-0.265f * Mathf.Log(sourceHeat) + 2.3f, 0.65f, 0.8f);
            float minMult = Mathf.Clamp(((0.00093f * sourceHeat * sourceHeat - 1.4457f * sourceHeat + 1141.95f) / 1000f), 0.65f, 0.8f); // Equivalent to above, but uses polynomial for speed
            thermal = sourceHeat * UnityEngine.Random.Range(minMult, 1.75f - minMult + 0.65f);

            if (BDArmorySettings.DRAW_DEBUG_LABELS)
                Debug.Log("[BDArmory]: New flare generated from " + sourceVessel.GetDisplayName() + ":" + BDATargetManager.GetVesselHeatSignature(sourceVessel).ToString("0.0") + ", heat: " + thermal.ToString("0.0"));
        }

        void OnEnable()
        {
            startThermal = thermal;
            minThermal = startThermal * 0.4f; // 0.3 is original value, but doesn't work well for Tigers, 0.4f gives decent performance for Tigers, 0.65 decay gives best flare performance overall based on some monte carlo analysis

            if (gaplessEmitters == null || pEmitters == null)
            {
                gaplessEmitters = new List<BDAGaplessParticleEmitter>();

                pEmitters = new List<KSPParticleEmitter>();

                IEnumerator<KSPParticleEmitter> pe = gameObject.GetComponentsInChildren<KSPParticleEmitter>().Cast<KSPParticleEmitter>().GetEnumerator();
                while (pe.MoveNext())
                {
                    if (pe.Current == null) continue;
                    if (pe.Current.useWorldSpace)
                    {
                        BDAGaplessParticleEmitter gpe = pe.Current.gameObject.AddComponent<BDAGaplessParticleEmitter>();
                        gaplessEmitters.Add(gpe);
                        gpe.emit = true;
                    }
                    else
                    {
                        EffectBehaviour.AddParticleEmitter(pe.Current);
                        pEmitters.Add(pe.Current);
                        pe.Current.emit = true;
                    }
                }
                pe.Dispose();
            }
            List<BDAGaplessParticleEmitter>.Enumerator gEmitter = gaplessEmitters.GetEnumerator();
            while (gEmitter.MoveNext())
            {
                if (gEmitter.Current == null) continue;
                gEmitter.Current.emit = true;
            }
            gEmitter.Dispose();

            List<KSPParticleEmitter>.Enumerator pEmitter = pEmitters.GetEnumerator();
            while (pEmitter.MoveNext())
            {
                if (pEmitter.Current == null) continue;
                pEmitter.Current.emit = true;
            }
            pEmitter.Dispose();

            BDArmorySetup.numberOfParticleEmitters++;

            if (lights == null)
            {
                lights = gameObject.GetComponentsInChildren<Light>();
            }

            IEnumerator<Light> lgt = lights.AsEnumerable().GetEnumerator();
            while (lgt.MoveNext())
            {
                if (lgt.Current == null) continue;
                lgt.Current.enabled = true;
            }
            lgt.Dispose();
            startTime = Time.time;

            //ksp force applier
            //gameObject.AddComponent<KSPForceApplier>().drag = 0.4f;

            BDArmorySetup.Flares.Add(this);

            upDirection = VectorUtils.GetUpDirection(transform.position);
        }

        void FixedUpdate()
        {
            if (!gameObject.activeInHierarchy)
            {
                return;
            }

            //floating origin and velocity offloading corrections
            if (!FloatingOrigin.Offset.IsZero() || !Krakensbane.GetFrameVelocity().IsZero())
            {
                transform.position -= FloatingOrigin.OffsetNonKrakensbane;
            }

            if (velocity != Vector3.zero)
            {
                transform.rotation = Quaternion.LookRotation(velocity, upDirection);
            }

            //Particle effects
            //downforce
            Vector3 downForce = (Mathf.Clamp(velocity.magnitude, 0.1f, 150) / 150) * 20 * -upDirection;

            //turbulence
            List<BDAGaplessParticleEmitter>.Enumerator gEmitter = gaplessEmitters.GetEnumerator();
            while (gEmitter.MoveNext())
            {
                if (gEmitter.Current == null) continue;
                if (!gEmitter.Current.pEmitter) continue;
                try
                {
                    gEmitter.Current.pEmitter.worldVelocity = 2 * ParticleTurbulence.flareTurbulence + downForce;
                }
                catch (NullReferenceException)
                {
                    Debug.LogWarning("CMFlare NRE setting worldVelocity");
                }

                try
                {
                    if (FlightGlobals.ActiveVessel && FlightGlobals.ActiveVessel.atmDensity <= 0)
                    {
                        gEmitter.Current.emit = false;
                    }
                }
                catch (NullReferenceException)
                {
                    Debug.LogWarning("CMFlare NRE checking density");
                }
            }
            gEmitter.Dispose();
            //

            //thermal decay
            thermal = Mathf.MoveTowards(thermal, minThermal,
                ((thermal - minThermal) / lifeTime) * Time.fixedDeltaTime);

            if (Time.time - startTime > lifeTime) //stop emitting after lifeTime seconds
            {
                alive = false;
                BDArmorySetup.Flares.Remove(this);

                List<KSPParticleEmitter>.Enumerator pe = pEmitters.GetEnumerator();
                while (pe.MoveNext())
                {
                    if (pe.Current == null) continue;
                    pe.Current.emit = false;
                }
                pe.Dispose();

                List<BDAGaplessParticleEmitter>.Enumerator gpe = gaplessEmitters.GetEnumerator();
                while (gpe.MoveNext())
                {
                    if (gpe.Current == null) continue;
                    gpe.Current.emit = false;
                }
                gpe.Dispose();

                IEnumerator<Light> lgt = lights.AsEnumerable().GetEnumerator();
                while (lgt.MoveNext())
                {
                    if (lgt.Current == null) continue;
                    lgt.Current.enabled = false;
                }
                lgt.Dispose();
            }

            if (Time.time - startTime > lifeTime + 11) //disable object after x seconds
            {
                BDArmorySetup.numberOfParticleEmitters--;
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
                velocity += FlightGlobals.getGeeForceAtPosition(transform.position) * Time.fixedDeltaTime;

            transform.position += velocity * Time.fixedDeltaTime;
        }
    }
}