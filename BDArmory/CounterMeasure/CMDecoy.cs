using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using BDArmory.FX;
using BDArmory.Settings;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.Targeting;

namespace BDArmory.CounterMeasure
{
    public class CMDecoy : MonoBehaviour
    {
        List<KSPParticleEmitter> pEmitters;

        Light[] lights;
        float startTime;

        public bool alive = true;

        Vector3 upDirection;

        public Vector3 velocity;

        public float acousticSig;
        public float audio;
        //float minAudio;
        //float startAudio;

        float lifeTime = 30;

        public void SetAcoustics(Vessel sourceVessel)
        {
            // generate decoy sound prodile within spectrum of emitting vessel's acoustic signature, but narrow range for low heats
            acousticSig = BDATargetManager.GetVesselAcousticSignature(sourceVessel, Vector3.zero).Item1;
            audio = acousticSig;
            float audioMinMult = Mathf.Clamp(((0.00093f * acousticSig * acousticSig - 1.4457f * acousticSig + 1141.95f) / 1000f), 0.65f, 0.8f); // Equivalent to above, but uses polynomial for speed
            audio *= UnityEngine.Random.Range(audioMinMult, Math.Max(BDArmorySettings.FLARE_FACTOR, 0f) - audioMinMult + 0.8f);

            if (BDArmorySettings.DEBUG_OTHER)
                Debug.Log("[BDArmory.CMDecoy]: New decoy generated from " + sourceVessel.GetName() + ":" + acousticSig.ToString("0.0") + ", decoy sig: " + audio.ToString("0.0"));
        }

        void OnEnable()
        {
            //startAudio = audio;
            //minAudio = startAudio * 0.34f; 
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

            BDArmorySetup.Decoys.Add(this);

            upDirection = VectorUtils.GetUpDirection(transform.position);

            this.transform.localScale = Vector3.one;
        }

        void FixedUpdate()
        {
            if (!gameObject.activeInHierarchy) return;

            //floating origin and velocity offloading corrections
            if (BDKrakensbane.IsActive)
            {
                transform.localPosition -= BDKrakensbane.FloatingOriginOffsetNonKrakensbane;
            }

            if (velocity != Vector3.zero)
            {
                transform.localRotation = Quaternion.LookRotation(velocity, upDirection);
            }

            //turbulence
            using (var pEmitter = pEmitters.GetEnumerator())
                while (pEmitter.MoveNext())
                {
                    if (pEmitter.Current == null) continue;
                    try
                    {
                        pEmitter.Current.worldVelocity = 2 * ParticleTurbulence.flareTurbulence;
                    }
                    catch (NullReferenceException e)
                    {
                        Debug.LogWarning("[BDArmory.CMDecoy]: NRE setting worldVelocity: " + e.Message);
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
                        Debug.LogWarning("[BDArmory.CMDecoy]: NRE checking density: " + e.Message);
                    }
                }
            //

            if (Time.time - startTime > lifeTime) //stop emitting after lifeTime seconds
            {
                alive = false;
                transform.localScale = Vector3.zero;
                BDArmorySetup.Decoys.Remove(this);
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
            transform.localPosition += velocity * Time.fixedDeltaTime;
            if (FlightGlobals.getAltitudeAtPos(transform.position) > 0)
                velocity += FlightGlobals.getGeeForceAtPosition(transform.position) * Time.fixedDeltaTime;

            transform.position += velocity * Time.fixedDeltaTime;
        }

        public void EnableEmitters()
        {
            if (pEmitters == null) return;
            using (var emitter = pEmitters.GetEnumerator())
                while (emitter.MoveNext())
                {
                    if (emitter.Current == null) continue;
                    emitter.Current.emit = true;
                }
        }
    }
}