using System.Collections;
using UnityEngine;

using BDArmory.Utils;
using BDArmory.Extensions;

namespace BDArmory.CounterMeasure
{
    public class CMChaff : MonoBehaviour
    {
        KSPParticleEmitter pe;

        const float drag = 5;

        Vector3d geoPos;
        Vector3 velocity;
        CelestialBody body;

        public void Emit(Vector3 position, Vector3 velocity)
        {
            transform.position = position;
            this.velocity = velocity;
            gameObject.SetActive(true);
        }

        void OnEnable()
        {
            if (!pe)
            {
                pe = gameObject.GetComponentInChildren<KSPParticleEmitter>();
                EffectBehaviour.AddParticleEmitter(pe);
            }

            body = FlightGlobals.currentMainBody;
            if (!body)
            {
                gameObject.SetActive(false);
                return;
            }
            pe.useWorldSpace = false; // Don't use worldspace, so that we can move the FX properly.
            StartCoroutine(LifeRoutine());
        }

        void OnDisable()
        {
            body = null;
        }

        IEnumerator LifeRoutine()
        {
            geoPos = VectorUtils.WorldPositionToGeoCoords(transform.position, body);

            pe.EmitParticle();

            float startTime = Time.time;
            var wait = new WaitForFixedUpdate();
            Vector3 position; // Optimisation: avoid getting/setting transform.position more than necessary.
            while (Time.time - startTime < pe.maxEnergy)
            {
                position = body.BDGetWorldSurfacePosition(geoPos);
                velocity += FlightGlobals.getGeeForceAtPosition(position, body) * Time.fixedDeltaTime;
                Vector3 dragForce = (0.008f) * drag * 0.5f * velocity.sqrMagnitude *
                                    (float)
                                    FlightGlobals.getAtmDensity(FlightGlobals.getStaticPressure(position),
                                        FlightGlobals.getExternalTemperature(), body) * velocity.normalized;
                velocity -= (dragForce) * Time.fixedDeltaTime;
                position += velocity * Time.fixedDeltaTime;
                transform.position = position;
                geoPos = VectorUtils.WorldPositionToGeoCoords(position, body);
                yield return wait;
            }

            gameObject.SetActive(false);
        }
    }
}
