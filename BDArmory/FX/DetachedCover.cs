/// <summary>
/// Motion for a launcher tube cover that has been popped off.
/// If the cover has colliders (and shell collisions are enabled) it becomes a real rigidbody using its own colliders, so irregular covers fall, tumble and settle on the terrain.
/// Otherwise it follows a simple ballistic path (as ShellCasing does).
/// After lifeTime the cover's GameObject is deactivated; the owning launcher may reactivate and re-attach it on reload.
/// </summary>

using System.Collections.Generic;
using UnityEngine;

using BDArmory.Settings;
using BDArmory.Utils;

namespace BDArmory.FX
{
    public class DetachedCover : MonoBehaviour
    {
        public float startTime;
        public float lifeTime = 30;
        public float spinPerEjectSpeed = 60f; // Degrees/s of tumble per m/s of ejection speed.
        public float mass = 0.002f; // Mass of the cover (tonnes) when simulated as a rigidbody.

        Vector3 velocity;
        Vector3 angularVelocity;
        float atmDensity;
        Rigidbody rb;
        Collider[] colliders;
        const int collisionLayerMask = (int)(LayerMasks.Parts | LayerMasks.Scenery | LayerMasks.EVA | LayerMasks.Wheels);

        void Awake()
        {
            enabled = false; // Only simulate once launched.
        }

        /// <summary>
        /// Start the cover's motion. The transform should already have been detached from its parent.
        /// </summary>
        /// <param name="initialVelocity">Velocity of the launcher (part.rb.velocity).</param>
        /// <param name="ejectVelocity">Additional velocity of the cover relative to the launcher.</param>
        /// <param name="life">Seconds until the cover is deactivated.</param>
        /// <param name="coverColliders">The cover's colliders (disabled while the cover is attached). Enabled here for physics.</param>
        /// <param name="ignoreColliders">Colliders the cover must not collide with (the launching vessel, sibling covers).</param>
        public void Launch(Vector3 initialVelocity, Vector3 ejectVelocity, float life, Collider[] coverColliders = null, Collider[] ignoreColliders = null)
        {
            velocity = initialVelocity + ejectVelocity;
            // A cover knocked off by the launch tumbles about an axis perpendicular to its direction of travel, faster the harder it's ejected.
            Vector3 axis = Vector3.Cross(ejectVelocity, Random.onUnitSphere);
            if (axis.sqrMagnitude < 1e-6f) axis = Random.onUnitSphere; // Degenerate: no ejection velocity, or the random direction was parallel to it.
            angularVelocity = axis.normalized * (ejectVelocity.magnitude * spinPerEjectSpeed * Random.Range(0.5f, 1f));
            lifeTime = life;
            startTime = Time.time;
            atmDensity = (float)FlightGlobals.getAtmDensity(
                FlightGlobals.getStaticPressure(transform.position, FlightGlobals.currentMainBody),
                FlightGlobals.getExternalTemperature(), FlightGlobals.currentMainBody);

            colliders = coverColliders;
            if (BDArmorySettings.SHELL_COLLISIONS && colliders != null && colliders.Length > 0)
                StartPhysics(ignoreColliders);
            enabled = true;
        }

        void StartPhysics(Collider[] ignoreColliders)
        {
            foreach (var collider in colliders)
            {
                if (collider == null) continue;
                if (collider is MeshCollider meshCollider) meshCollider.convex = true; // Dynamic rigidbodies can only use convex mesh colliders.
                collider.isTrigger = false;
                collider.enabled = true;
            }
            if (ignoreColliders != null)
            {
                foreach (var collider in colliders)
                {
                    if (collider == null) continue;
                    foreach (var other in ignoreColliders)
                    {
                        if (other == null || other == collider || !other.enabled || !other.gameObject.activeInHierarchy) continue;
                        Physics.IgnoreCollision(collider, other, true);
                    }
                }
            }
            rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(mass, 1e-4f);
            rb.useGravity = false; // KSP applies gravity itself.
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.velocity = velocity;
            rb.angularVelocity = angularVelocity * Mathf.Deg2Rad;
        }

        void StopPhysics()
        {
            if (rb != null)
            {
                rb.isKinematic = true; // Destroy is deferred to the end of the frame.
                rb.detectCollisions = false;
                Destroy(rb);
                rb = null;
            }
            if (colliders != null)
            {
                foreach (var collider in colliders) if (collider != null) collider.enabled = false; // Colliders are off while a cover is attached.
            }
        }

        // Stop moving (used when the cover is re-attached)
        public void Halt()
        {
            StopPhysics();
            enabled = false;
        }

        void FixedUpdate()
        {
            if (!gameObject.activeInHierarchy) return;
            if (Time.time - startTime > lifeTime)
            {
                StopPhysics();
                enabled = false;
                gameObject.SetActive(false);
                return;
            }

            if (rb != null)
            {
                rb.AddForce(FlightGlobals.getGeeForceAtPosition(rb.position), ForceMode.Acceleration);
                // Keep the velocity consistent with the vessel's frame when Krakensbane shifts it, and apply a simple drag (as ShellCasing does).
                var v = rb.velocity + Krakensbane.GetLastCorrection();
                v -= 0.005f * (v + BDKrakensbane.FrameVelocityV3f) * atmDensity;
                rb.velocity = v;
                return;
            }

            // Ballistic fallback (no colliders, or shell collisions disabled).
            velocity += FlightGlobals.getGeeForceAtPosition(transform.position) * TimeWarp.fixedDeltaTime
                + Krakensbane.GetLastCorrection();
            velocity -= 0.005f * (velocity + BDKrakensbane.FrameVelocityV3f) * atmDensity;
            transform.rotation *= Quaternion.Euler(angularVelocity * TimeWarp.fixedDeltaTime);
            transform.position += velocity * TimeWarp.fixedDeltaTime;
        }
    }
}
