﻿using System;
using System.Collections.Generic;
using UniLinq;
using UnityEngine;

using BDArmory.Armor;
using BDArmory.Competition;
using BDArmory.Damage;
using BDArmory.Extensions;
using BDArmory.FX;
using BDArmory.Weapons;
using BDArmory.Settings;
using BDArmory.UI;
using BDArmory.Utils;
using static BDArmory.Bullets.PooledBullet;

namespace BDArmory.Bullets
{
    public class PooledRocket : MonoBehaviour
    {
        public RocketInfo rocket; //get tracers, expFX urls moved to BulletInfo
                                  //Seeker/homing rocket code (Image-Recognition tracking?)

        public Transform spawnTransform;
        public Vessel sourceVessel;
        public Part sourceWeapon;
        public string sourceVesselName;
        public string team;
        public string rocketName;
        public float rocketMass;
        public float caliber;
        public float apMod;
        public float thrust;
        private Vector3 thrustVector;
        private Vector3 dragVector;
        public float thrustTime;
        public bool shaped;
        public float timeToDetonation;
        float armingTime;
        public bool flak;
        public bool detonateAtMinimumDistance = false; // Detonate flak rockets when they reach min distance instead of when they enter the proximity range.
        public bool concussion;
        public bool gravitic;
        public bool EMP;
        public bool choker;
        public bool thief;
        public float massMod = 0;
        public float impulse = 0;
        public bool incendiary;
        public float detonationRange;
        public float tntMass;
        public bool beehive;
        public string subMunitionType;
        bool explosive = true;
        public float bulletDmgMult = 1;
        public float dmgMult = 1;
        public float blastRadius = 0;
        public float randomThrustDeviation = 0.05f;
        public float massScalar = 0.012f;
        private float HERatio = 0.1f;
        public string explModelPath;
        public string explSoundPath;

        public bool nuclear;
        public string flashModelPath;
        public string shockModelPath;
        public string blastModelPath;
        public string plumeModelPath;
        public string debrisModelPath;
        public string blastSoundPath;

        public string rocketSoundPath;

        float startTime;
        public float lifeTime;

        public Vector3 currentPosition { get { return _currentPosition; } set { _currentPosition = value; transform.position = value; } } // Local alias for transform.position speeding up access by around 100x. Only use during FixedUpdates, as it may not be up-to-date otherwise.
        Vector3 _currentPosition = default;
        Vector3 startPosition;
        bool startUnderwater = false;
        Ray RocketRay;
        private float impactVelocity;
        public Vector3 currentVelocity = Vector3.zero; // Current real velocity w/o offloading
        Vector3 currentAcceleration = default;

        public bool hasPenetrated = false;
        public bool hasDetonated = false;
        public int penTicker = 0;
        private Part CurrentPart = null;
        private const int layerMask = (int)(LayerMasks.Parts | LayerMasks.Scenery | LayerMasks.EVA | LayerMasks.Wheels);

        private float distanceFromStart = 0;

        //bool isThrusting = true;
        public bool isAPSprojectile = false;
        public bool isSubProjectile = false;
        public PooledRocket tgtRocket = null;
        public PooledBullet tgtShell = null;

        Rigidbody rb;
        public Rigidbody parentRB;

        KSPParticleEmitter[] pEmitters;
        BDAGaplessParticleEmitter[] gpEmitters;

        float randThrustSeed;

        public AudioSource audioSource;

        static RaycastHit[] hits = new RaycastHit[10];
        static Collider[] detonateOverlapSphereColliders = new Collider[10];
        static List<RaycastHit> allHits;
        static Collider[] overlapSphereColliders;

        void Awake()
        {
            if (allHits == null) allHits = [];
            if (overlapSphereColliders == null) { overlapSphereColliders = new Collider[1000]; }
        }

        void OnEnable()
        {
            BDArmorySetup.numberOfParticleEmitters++;
            currentPosition = transform.position; // In case something sets transform.position instead of currentPosition.
            ApplyKrakensbane(true); // Preemptively undo the krakensbane for the initial frame so that it can be applied in the BetterLateThanNever timing phase.
            hasDetonated = false;

            rb = gameObject.AddOrGetComponent<Rigidbody>();

            pEmitters = gameObject.GetComponentsInChildren<KSPParticleEmitter>();

            using (var pe = pEmitters.AsEnumerable().GetEnumerator())
                while (pe.MoveNext())
                {
                    if (pe.Current == null) continue;
                    if (FlightGlobals.getStaticPressure(currentPosition) == 0 && pe.Current.useWorldSpace)
                    {
                        pe.Current.emit = false;
                    }

                    else if (pe.Current.useWorldSpace)
                    {
                        BDAGaplessParticleEmitter gpe = pe.Current.gameObject.AddComponent<BDAGaplessParticleEmitter>();
                        gpe.rb = rb;
                        gpe.emit = true;
                    }

                    else
                    {
                        pe.Current.emit = true;
                        EffectBehaviour.AddParticleEmitter(pe.Current);
                    }
                }
            gpEmitters = gameObject.GetComponentsInChildren<BDAGaplessParticleEmitter>();

            startPosition = currentPosition;
            transform.rotation = transform.parent.rotation;
            startTime = Time.time;
            armingTime = isSubProjectile ? 0 : BDAMath.Sqrt(4 * blastRadius * rocketMass / thrust); // d = a/2 * t^2 for initial 0 relative velocity
            if (FlightGlobals.getAltitudeAtPos(currentPosition) < 0)
            {
                startUnderwater = true;
            }
            else
                startUnderwater = false;
            massScalar = 0.012f / rocketMass;

            rb.mass = rocketMass;
            rb.isKinematic = false;
            rb.useGravity = false;
            rb.velocity = parentRB ? parentRB.velocity : Vector3.zero; // Use rb.velocity in the velocity frame reference. Use currentVelocity for absolute velocity.
            currentVelocity = rb.velocity + BDKrakensbane.FrameVelocityV3f;
            transform.parent = null; // Clear the parent transform so the rocket is now independent.

            randThrustSeed = UnityEngine.Random.Range(0f, 100f);
            thrustVector = new Vector3(0, 0, thrust);
            dragVector = new Vector3();

            SetupAudio();

            // Log rockets fired.
            if (sourceVessel)
            {
                sourceVesselName = sourceVessel.GetName(); // Set the source vessel name as the vessel might have changed its name or died by the time the rocket hits.
                BDACompetitionMode.Instance.Scores.RegisterRocketFired(sourceVesselName);
            }
            else
            {
                sourceVesselName = null;
            }
            if (tntMass <= 0)
            {
                explosive = false;
            }
            if (explosive)
            {
                HERatio = Mathf.Clamp(tntMass / ((rocketMass * 1000) < tntMass ? tntMass * 1.25f : (rocketMass * 1000)), 0.01f, 0.95f);
            }
            else
            {
                HERatio = 0;
            }
            if (caliber >= BDArmorySettings.APS_THRESHOLD) //if (caliber > 60)
            {
                BDATargetManager.FiredRockets.Add(this);
            }
            if (nuclear)
            {
                var nuke = sourceWeapon.FindModuleImplementing<BDModuleNuke>();
                if (nuke == null)
                {
                    flashModelPath = BDModuleNuke.defaultflashModelPath;
                    shockModelPath = BDModuleNuke.defaultShockModelPath;
                    blastModelPath = BDModuleNuke.defaultBlastModelPath;
                    plumeModelPath = BDModuleNuke.defaultPlumeModelPath;
                    debrisModelPath = BDModuleNuke.defaultDebrisModelPath;
                    blastSoundPath = BDModuleNuke.defaultBlastSoundPath;
                }
                else
                {
                    flashModelPath = nuke.flashModelPath;
                    shockModelPath = nuke.shockModelPath;
                    blastModelPath = nuke.blastModelPath;
                    plumeModelPath = nuke.plumeModelPath;
                    debrisModelPath = nuke.debrisModelPath;
                    blastSoundPath = nuke.blastSoundPath;
                }
            }

            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.BetterLateThanNever, BetterLateThanNever);
        }

        void OnDisable()
        {
            BDArmorySetup.OnVolumeChange -= UpdateVolume;
            BDArmorySetup.numberOfParticleEmitters--;
            foreach (var gpe in gpEmitters)
                if (gpe != null)
                {
                    gpe.emit = false;
                }
            foreach (var pe in pEmitters)
                if (pe != null)
                {
                    pe.emit = false;
                    EffectBehaviour.RemoveParticleEmitter(pe);
                }
            sourceVessel = null;
            sourceVesselName = null;
            spawnTransform = null;
            CurrentPart = null;
            if (caliber >= BDArmorySettings.APS_THRESHOLD) //if (caliber > 60)
            {
                BDATargetManager.FiredRockets.Remove(this);
            }
            isAPSprojectile = false;
            tgtRocket = null;
            tgtShell = null;
            rb.isKinematic = true;
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.BetterLateThanNever, BetterLateThanNever);
        }

        void FixedUpdate()
        {
            if (!gameObject.activeInHierarchy)
            {
                return;
            }
            currentPosition = transform.position; // Adjust our local copy for any adjustments that the physics engine has made.
            distanceFromStart = Vector3.Distance(currentPosition, startPosition);

            if (rb && !rb.isKinematic)
            {
                UpdateKinematics(); // Update forces and get current velocity.

                //guidance and attitude stabilisation scales to atmospheric density.
                float atmosMultiplier = Mathf.Clamp01(2.5f * (float)FlightGlobals.getAtmDensity(FlightGlobals.getStaticPressure(currentPosition), FlightGlobals.getExternalTemperature(), FlightGlobals.currentMainBody));
                if (atmosMultiplier > 0)
                {
                    //model transform. always points prograde
                    var atmosFactor = atmosMultiplier * 0.5f * 0.012f * currentVelocity.sqrMagnitude * TimeWarp.fixedDeltaTime; // aero-stabilize
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(currentVelocity, transform.up), atmosFactor);
                }
            }

            if (Time.time - startTime > thrustTime)
            {
                using (var pe = pEmitters.AsEnumerable().GetEnumerator())
                    while (pe.MoveNext())
                    {
                        if (pe.Current == null) continue;
                        pe.Current.emit = false;
                    }
                using (var gpe = gpEmitters.AsEnumerable().GetEnumerator())
                    while (gpe.MoveNext())
                    {
                        if (gpe.Current == null) continue;
                        gpe.Current.emit = false;
                    }
                if (audioSource)
                {
                    audioSource.loop = false;
                    audioSource.Stop();
                }
            }

            if (ProximityAirDetonation()) // Proximity detection should happen before collision detection.
            {
                Detonate(currentPosition, false, airDetonation: true);
                return;
            }
            if (CheckCollisions()) return; // Collided and detonated.

            if (BDArmorySettings.BULLET_WATER_DRAG)
            {
                if (FlightGlobals.getAltitudeAtPos(currentPosition) > 0 && startUnderwater)
                {
                    startUnderwater = false;
                    if (BDArmorySettings.waterHitEffect) FXMonger.Splash(currentPosition, caliber);
                }
                if (FlightGlobals.getAltitudeAtPos(currentPosition) <= 0 && !startUnderwater)
                {
                    if (tntMass > 0) //look into fuze options similar to bullets?
                    {
                        Detonate(currentPosition, false);
                    }
                    if (BDArmorySettings.waterHitEffect) FXMonger.Splash(currentPosition, caliber);
                }
            }

            if (Time.time - startTime > lifeTime)
            {
                Detonate(currentPosition, true, airDetonation: true);
                return;
            }
            if (beehive && Time.time - startTime >= timeToDetonation - 1)
            {
                Detonate(currentPosition, false, airDetonation: true);
                return;
            }
        }

        void BetterLateThanNever() => ApplyKrakensbane(); // This makes sure the KB corrections are applied to the correct frame in case of vessel changes.
        void ApplyKrakensbane(bool reverse = false)
        {
            if (BDKrakensbane.IsActive)
            {
                var offset = BDKrakensbane.FloatingOriginOffset; // Working with the RB in the velocity frame means we apply the KB offset instead of the nonKB one.
                if (reverse)
                {
                    currentPosition += offset;
                    startPosition += offset;
                }
                else
                {
                    currentPosition -= offset;
                    startPosition -= offset;
                }
            }
        }

        void UpdateKinematics()
        {
            var gravity = Vector3.zero;
            if (FlightGlobals.RefFrameIsRotating)
            {
                gravity = FlightGlobals.getGeeForceAtPosition(currentPosition);
                rb.AddForce(gravity, ForceMode.Acceleration);
            }
            currentAcceleration = gravity;

            if (Time.time - startTime <= thrustTime)
            {
                thrustVector.x = randomThrustDeviation * (1 - (Mathf.PerlinNoise(4 * Time.time, randThrustSeed) * 2)) / massScalar;//this needs to scale w/ rocket mass, or light projectiles will be 
                thrustVector.y = randomThrustDeviation * (1 - (Mathf.PerlinNoise(randThrustSeed, 4 * Time.time) * 2)) / massScalar;//far more affected than heavier ones
                rb.AddRelativeForce(thrustVector);
                currentAcceleration += Quaternion.FromToRotation(Vector3.forward, rb.transform.forward) * thrustVector / rb.mass;
            }//0.012/rocketmass - use .012 as baseline, it's the mass of the hydra, which the randomTurstdeviation was originally calibrated for
            if (BDArmorySettings.BULLET_WATER_DRAG)
            {
                if (FlightGlobals.getAltitudeAtPos(currentPosition) < 0)
                {
                    //atmosMultiplier *= 83.33f;
                    dragVector.z = -(0.5f * 1 * currentVelocity.sqrMagnitude * 0.5f * (Mathf.PI * caliber * caliber * 0.25f / 1000000));
                    rb.AddRelativeForce(dragVector); //this is going to throw off aiming code, but you aren't going to hit anything with rockets underwater anyway
                    currentAcceleration += Quaternion.FromToRotation(Vector3.forward, rb.transform.forward) * dragVector / rb.mass;
                }
                //dragVector.z = -(0.5f * (atmosMultiplier * 0.012f) * currentVelocity.sqrMagnitude * 0.5f * ((Mathf.PI * caliber * caliber * 0.25f) / 1000000));
                //rb.AddRelativeForce(dragVector);
                //Debug.Log("[ROCKETDRAG] current vel: " + currentVelocity.ToString("0.0") + "; current dragforce: " + dragVector.magnitude + "; current atm density: " + atmosMultiplier.ToString("0.00"));
            }
            currentVelocity = rb.velocity + BDKrakensbane.FrameVelocityV3f;// + 0.5f * TimeWarp.fixedDeltaTime * currentAcceleration; // Approximation to the average velocity throughout the coming physics.
        }

        /// <summary>
        /// 2nd-order approximation to the position on the next frame.
        /// (Close enough that phasing isn't an issue.)
        /// </summary>
        /// <param name="duration">TimeWarp.fixedDeltaTime</param>
        /// <param name="referenceVelocity"></param>
        Vector3 PredictPosition(float duration, Vector3 referenceVelocity = default) =>
            AIUtils.PredictPosition(currentPosition, currentVelocity - referenceVelocity, currentAcceleration, duration);

        /// <summary>
        /// Collision detection within the next frame.
        /// </summary>
        /// <returns>true the rocket detonates</returns>
        bool CheckCollisions()
        {
            hasPenetrated = true;
            penTicker = 0;

            if (BDArmorySettings.VESSEL_RELATIVE_BULLET_CHECKS)
            {
                allHits.Clear();
                CheckCollisionWithVessels();
                CheckCollisionWithScenery();
                using var hitsEnu = allHits.OrderBy(x => x.distance).GetEnumerator(); // Check all hits in order of distance.
                while (hitsEnu.MoveNext()) if (HitAnalysis(hitsEnu.Current)) return true;
                return false;
            }
            else
            {
                return CheckCollision();
            }
        }

        /// <summary>
        /// Collision detection between two points (for non-orbital speeds).
        /// Note: unlike for bullets, this is performing collision detection for the previous frame.
        /// </summary>
        /// <returns>true if the rocket has detonated</returns>
        bool CheckCollision()
        {
            var expectedPosition = PredictPosition(TimeWarp.fixedDeltaTime);
            float dist = (currentPosition - expectedPosition).magnitude;
            RocketRay = new Ray(currentPosition, expectedPosition - currentPosition);
            var hitCount = Physics.RaycastNonAlloc(RocketRay, hits, dist, layerMask);
            if (hitCount == hits.Length) // If there's a whole bunch of stuff in the way (unlikely), then we need to increase the size of our hits buffer.
            {
                hits = Physics.RaycastAll(RocketRay, dist, layerMask);
                hitCount = hits.Length;
            }
            if (hitCount > 0)
            {
                var orderedHits = hits.Take(hitCount).OrderBy(x => x.distance);
                using var hitsEnu = orderedHits.GetEnumerator();
                while (hitsEnu.MoveNext())
                    if (HitAnalysis(hitsEnu.Current)) return true;
            }
            return false;
        }

        void CheckCollisionWithVessels()
        {
            List<Vessel> nearbyVessels = [];
            const int layerMask = (int)(LayerMasks.Parts | LayerMasks.EVA | LayerMasks.Wheels);
            var overlapSphereRadius = GetOverlapSphereRadius(); // OverlapSphere of sufficient size to catch all potential craft of <100m radius.
            var overlapSphereColliderCount = Physics.OverlapSphereNonAlloc(currentPosition, overlapSphereRadius, overlapSphereColliders, layerMask);
            if (overlapSphereColliderCount == overlapSphereColliders.Length)
            {
                overlapSphereColliders = Physics.OverlapSphere(currentPosition, overlapSphereRadius, layerMask);
                overlapSphereColliderCount = overlapSphereColliders.Length;
            }

            using var hitsEnu = overlapSphereColliders.Take(overlapSphereColliderCount).GetEnumerator();
            while (hitsEnu.MoveNext())
            {
                if (hitsEnu.Current == null) continue;
                try
                {
                    Part partHit = hitsEnu.Current.GetComponentInParent<Part>();
                    if (partHit == null) continue;
                    if (partHit.vessel == sourceVessel) continue;
                    if (ProjectileUtils.IsIgnoredPart(partHit)) continue; // Ignore ignored parts.
                    if (partHit.vessel != null && !nearbyVessels.Contains(partHit.vessel)) nearbyVessels.Add(partHit.vessel);
                }
                catch (Exception e) // ignored
                {
                    Debug.LogWarning("[BDArmory.PooledRocket]: Exception thrown in CheckCollisionWithVessels: " + e.Message + "\n" + e.StackTrace);
                }
            }
            foreach (var vessel in nearbyVessels.OrderBy(v => (v.CoM - currentPosition).sqrMagnitude))
            {
                CheckCollisionWithVessel(vessel); // FIXME Convert this to use RaycastCommand to do all the raycasts in parallel.
            }

        }

        /// <summary>
        /// Calculate the required radius of the overlap sphere such that a craft <100m in radius could potentially have collided with the rocket.
        /// </summary>
        /// <returns>The required radius.</returns>
        float GetOverlapSphereRadius()
        {
            float maxRelSpeedSqr = 0, relVelSqr;
            Vector3 relativeVelocity;
            using var v = FlightGlobals.Vessels.GetEnumerator();
            while (v.MoveNext())
            {
                if (v.Current == null || !v.Current.loaded) continue; // Ignore invalid craft.
                relativeVelocity = v.Current.rb_velocity + BDKrakensbane.FrameVelocityV3f - currentVelocity;
                if (Vector3.Dot(relativeVelocity, v.Current.CoM - currentPosition) >= 0) continue; // Ignore craft that aren't approaching.
                relVelSqr = relativeVelocity.sqrMagnitude;
                if (relVelSqr > maxRelSpeedSqr) maxRelSpeedSqr = relVelSqr;
            }
            return 100f + TimeWarp.fixedDeltaTime * BDAMath.Sqrt(maxRelSpeedSqr); // Craft of radius <100m that could have collided within the period.
        }

        /// <summary>
        /// Check for having collided with a vessel in the last frame in a vessel-relative reference frame.
        /// </summary>
        /// <param name="vessel"></param>
        void CheckCollisionWithVessel(Vessel vessel)
        {
            var expectedPosition = PredictPosition(TimeWarp.fixedDeltaTime, vessel.rb_velocity + BDKrakensbane.FrameVelocityV3f);
            float dist = (expectedPosition - currentPosition).magnitude;
            RocketRay = new Ray(currentPosition, expectedPosition - currentPosition);
            const int layerMask = (int)(LayerMasks.Parts | LayerMasks.EVA | LayerMasks.Wheels);

            var hitCount = Physics.RaycastNonAlloc(RocketRay, hits, dist, layerMask);
            if (hitCount == hits.Length) // If there's a whole bunch of stuff in the way (unlikely), then we need to increase the size of our hits buffer.
            {
                hits = Physics.RaycastAll(RocketRay, dist, layerMask);
                hitCount = hits.Length;
            }

            if (hitCount > 0)
            {
                Part hitPart;
                using var hit = hits.Take(hitCount).AsEnumerable().GetEnumerator();
                while (hit.MoveNext())
                {
                    hitPart = hit.Current.collider.gameObject.GetComponentInParent<Part>();
                    if (hitPart == null) continue;
                    if (hitPart.vessel == vessel) allHits.Add(hit.Current);
                }
            }
        }

        void CheckCollisionWithScenery()
        {
            var expectedPosition = PredictPosition(TimeWarp.fixedDeltaTime);
            float dist = (currentPosition - expectedPosition).magnitude;
            RocketRay = new Ray(currentPosition, expectedPosition - currentPosition);
            const int layerMask = (int)LayerMasks.Scenery;
            var hitCount = Physics.RaycastNonAlloc(RocketRay, hits, dist, layerMask);
            if (hitCount == hits.Length) // If there's a whole bunch of stuff in the way (unlikely), then we need to increase the size of our hits buffer.
            {
                hits = Physics.RaycastAll(RocketRay, dist, layerMask);
                hitCount = hits.Length;
            }
            allHits.AddRange(hits.Take(hitCount));
        }

        /// <summary>
        /// Internals of the rocket collision hits loop in CheckCollision so it can also be called from CheckCollisionWithVessel.
        /// </summary>
        /// <param name="hit">The raycast hit</param>
        /// <returns>true if the rocket detonates, false otherwise</returns>
        bool HitAnalysis(RaycastHit hit)
        {
            if (!hasPenetrated || hasDetonated) return true;

            Part hitPart;
            KerbalEVA hitEVA;
            try
            {
                hitPart = hit.collider.gameObject.GetComponentInParent<Part>();
                hitEVA = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
            }
            catch (NullReferenceException e)
            {
                Debug.LogWarning("[BDArmory.PooledRocket]:NullReferenceException for Kinetic Hit: " + e.Message);
                return false;
            }

            if (hitPart != null)
            {
                if (ProjectileUtils.IsIgnoredPart(hitPart)) return false; // Ignore ignored parts.
                if (hitPart == CurrentPart && ProjectileUtils.IsArmorPart(CurrentPart)) return false; //only have bullet hit armor panels once - no back armor to hit if penetration
            }

            CurrentPart = hitPart;
            if (hitEVA != null)
            {
                hitPart = hitEVA.part;
                // relative velocity, separate from the below statement, because the hitpart might be assigned only above
                if (hitPart.rb != null)
                    impactVelocity = (currentVelocity - (hitPart.rb.velocity + BDKrakensbane.FrameVelocityV3f)).magnitude;
                else
                    impactVelocity = currentVelocity.magnitude;
                if (dmgMult < 0)
                {
                    hitPart.AddInstagibDamage();
                }
                else
                {
                    ProjectileUtils.ApplyDamage(hitPart, hit, dmgMult, 1, caliber, rocketMass * 1000, impactVelocity, bulletDmgMult, distanceFromStart, explosive, incendiary, false, sourceVessel, rocketName, team, ExplosionSourceType.Rocket, true, true, true);
                }
                ResourceUtils.StealResources(hitPart, sourceVessel, thief);
                Detonate(hit.point, false, hitPart);
                return true;
            }

            if (hitPart != null && hitPart.vessel == sourceVessel) return false;  //avoid autohit;

            Vector3 impactVector = currentVelocity;
            if (hitPart != null && hitPart.rb != null)
                // using relative velocity vector instead of just rocket velocity
                // since KSP vessels can easily be moving faster than rockets
                impactVector = currentVelocity - (hitPart.rb.velocity + BDKrakensbane.FrameVelocityV3f);

            float hitAngle = Vector3.Angle(impactVector, -hit.normal);

            if (ProjectileUtils.CheckGroundHit(hitPart, hit, caliber))
            {
                if (!BDArmorySettings.PAINTBALL_MODE) ProjectileUtils.CheckBuildingHit(hit, rocketMass * 1000, currentVelocity, bulletDmgMult);
                Detonate(hit.point, false);
                return true;
            }

            impactVelocity = impactVector.magnitude;
            if (gravitic)
            {
                var ME = hitPart.FindModuleImplementing<ModuleMassAdjust>();
                if (ME == null)
                {
                    ME = (ModuleMassAdjust)hitPart.AddModule("ModuleMassAdjust");
                }
                ME.massMod += massMod;
                ME.duration += BDArmorySettings.WEAPON_FX_DURATION;
            }
            if (concussion && hitPart.rb != null || BDArmorySettings.PAINTBALL_MODE)
            {
                if (concussion && hitPart.rb != null)
                {
                    hitPart.rb.AddForceAtPosition(impactVector.normalized * impulse, hit.point, ForceMode.Acceleration);
                }
                BDACompetitionMode.Instance.Scores.RegisterRocketStrike(sourceVesselName, hitPart.vessel.GetName());
                Detonate(hit.point, false, hitPart);
                return true; //impulse rounds shouldn't penetrate/do damage
            }
            float anglemultiplier = (float)Math.Cos(Math.PI * hitAngle / 180.0);

            float thickness = ProjectileUtils.CalculateThickness(hitPart, anglemultiplier);
            float penetration = 0;
            float penetrationFactor = 0;
            var Armor = hitPart.FindModuleImplementing<HitpointTracker>();
            if (Armor != null)
            {
                float Ductility = Armor.Ductility;
                float hardness = Armor.Hardness;
                float Strength = Armor.Strength;
                float safeTemp = Armor.SafeUseTemp;
                float Density = Armor.Density;
                float vFactor = Armor.vFactor;
                float muParam1 = Armor.muParam1;
                float muParam2 = Armor.muParam2;
                float muParam3 = Armor.muParam3;

                if (hitPart.skinTemperature > safeTemp) //has the armor started melting/denaturing/whatever?
                {
                    //vFactor *= 1/(1.25f*0.75f-0.25f*0.75f*0.75f);
                    vFactor *= 1.25490196078f; // Uses the above equation but just calculated out.
                                               // The equation 1/(1.25*x-0.25*x^2) approximates the effect of changing yield strength
                                               // by a factor of x
                    if (hitPart.skinTemperature > safeTemp * 1.5f)
                    {
                        vFactor *= 1.77777777778f; // Same as used above, but here with x = 0.5. Maybe this should be
                                                   // some kind of a curve?
                    }
                }

                int armorType = (int)Armor.ArmorTypeNum;
                if (BDArmorySettings.DEBUG_ARMOR)
                {
                    Debug.Log("[BDArmory.PooledRocket]: ArmorVars found: Strength : " + Strength + "; Ductility: " + Ductility + "; Hardness: " + hardness + "; MaxTemp: " + safeTemp + "; Density: " + Density);
                }
                float bulletEnergy = ProjectileUtils.CalculateProjectileEnergy(rocketMass * 1000, impactVelocity);
                float armorStrength = ProjectileUtils.CalculateArmorStrength(caliber, thickness, Ductility, Strength, Density, safeTemp, hitPart);
                //calculate bullet deformation
                float newCaliber = ProjectileUtils.CalculateDeformation(armorStrength, bulletEnergy, caliber, impactVelocity, hardness, Density, HERatio, 1, false);
                //calculate penetration
                /*if (Ductility > 0.05)
                {*/
                penetration = ProjectileUtils.CalculatePenetration(caliber, impactVelocity, rocketMass * 1000f, apMod, Strength, vFactor, muParam1, muParam2, muParam3);
                /*}
                else
                {
                    penetration = ProjectileUtils.CalculateCeramicPenetration(caliber, newCaliber, rocketMass * 1000, impactVelocity, Ductility, Density, Strength, thickness, 1);
                }*/

                caliber = newCaliber; //update bullet with new caliber post-deformation(if any)
                penetrationFactor = ProjectileUtils.CalculateArmorPenetration(hitPart, penetration, thickness);

                var RA = hitPart.FindModuleImplementing<ModuleReactiveArmor>();
                if (RA != null)
                {
                    if (penetrationFactor > 1)
                    {
                        float thicknessModifier = RA.armorModifier;
                        {
                            if (RA.NXRA) //non-explosive RA, always active
                            {
                                thickness *= thicknessModifier;
                            }
                            else
                            {
                                if (caliber >= RA.sensitivity) //big enough round to trigger RA
                                {
                                    thickness *= thicknessModifier;
                                    if (tntMass <= 0) //non-explosive impact
                                    {
                                        RA.UpdateSectionScales(); //detonate RA section
                                                                  //explosive impacts handled in ExplosionFX
                                    }
                                }
                            }
                        }
                    }
                    penetrationFactor = ProjectileUtils.CalculateArmorPenetration(hitPart, penetration, thickness); //RA stop round?
                }
                else ProjectileUtils.CalculateArmorDamage(hitPart, penetrationFactor, caliber, hardness, Ductility, Density, impactVelocity, sourceVessel.GetName(), ExplosionSourceType.Rocket, armorType);

                //calculate return bullet post-pen vel
                //calculate armor damage
                //FIXME later - if doing bullet style armor penetrtion, then immplement armor penetration, and let AP/kinetic warhead rockets (over?)penetrate parts
            }
            else
            {
                Debug.Log("[BDArmory.PooledRocket]: ArmorVars not found; hitPart null");
            }
            if (penetration > thickness)
            {
                currentVelocity *= BDAMath.Sqrt(thickness / penetration);
                if (penTicker > 0) currentVelocity *= 0.55f;
                rb.velocity = currentVelocity - BDKrakensbane.FrameVelocityV3f; // In case the rocket survives and has further physics updates.
            }

            if (penetrationFactor > 1)
            {
                hasPenetrated = true;

                bool viableBullet = ProjectileUtils.CalculateBulletStatus(rocketMass * 1000, caliber);
                if (dmgMult < 0)
                {
                    hitPart.AddInstagibDamage();
                }
                else
                {
                    float cockpitPen = (float)(16f * impactVelocity * BDAMath.Sqrt(rocketMass) / BDAMath.Sqrt(caliber));
                    if (cockpitPen > Mathf.Max(20 / anglemultiplier, 1))
                        ProjectileUtils.ApplyDamage(hitPart, hit, dmgMult, penetrationFactor, caliber, rocketMass * 1000, impactVelocity, bulletDmgMult, distanceFromStart, explosive, incendiary, false, sourceVessel, rocketName, team, ExplosionSourceType.Rocket, penTicker > 0 ? false : true, penTicker > 0 ? false : true, (cockpitPen > Mathf.Max(20 / anglemultiplier, 1)) ? true : false);
                    if (!explosive)
                    {
                        BDACompetitionMode.Instance.Scores.RegisterRocketStrike(sourceVesselName, hitPart.vessel.GetName()); //if non-explosive hit, add rocketstrike, else ExplosionFX adds rocketstrike from HE detonation
                    }
                }
                ResourceUtils.StealResources(hitPart, sourceVessel, thief);

                penTicker += 1;
                //ProjectileUtils.CheckPartForExplosion(hitPart);

                if (explosive || !viableBullet)
                {
                    currentPosition += currentVelocity * TimeWarp.fixedDeltaTime / 3;

                    Detonate(currentPosition, false, hitPart); //explode inside part
                    return true;
                }
            }
            else // stopped by armor
            {
                if (hitPart.rb != null && hitPart.rb.mass > 0)
                {
                    float forceAverageMagnitude = impactVelocity * impactVelocity *
                                          (1f / hit.distance) * (rocketMass * 1000);

                    float accelerationMagnitude =
                        forceAverageMagnitude / (hitPart.vessel.GetTotalMass() * 1000);

                    hitPart.rb.AddForceAtPosition(impactVector.normalized * accelerationMagnitude, hit.point, ForceMode.Acceleration);

                    if (BDArmorySettings.DEBUG_WEAPONS)
                        Debug.Log("[BDArmory.PooledRocket]: Force Applied " + Math.Round(accelerationMagnitude, 2) + "| Vessel mass in kgs=" + hitPart.vessel.GetTotalMass() * 1000 + "| rocket effective mass =" + rocketMass * 1000);
                }

                hasPenetrated = false;
                //ProjectileUtils.ApplyDamage(hitPart, hit, 1, penetrationFactor, caliber, rocketMass * 1000, impactVelocity, bulletDmgMult, distanceFromStart, explosive, incendiary, false, sourceVessel, rocketName, team);
                //not going to do ballistic damage if stopped by armor
                ProjectileUtils.CalculateShrapnelDamage(hitPart, hit, caliber, tntMass, 0, sourceVesselName, ExplosionSourceType.Rocket, (rocketMass * 1000), penetrationFactor);
                //the warhead exploding, on the other hand...
                Detonate(hit.point, false, hitPart);
                return true;
            }

            if (penTicker >= 2)
            {
                Detonate(hit.point, false, hitPart);
                return true;
            }

            if (currentVelocity.sqrMagnitude <= 10000 && hasPenetrated && (Time.time - startTime > thrustTime))
            {
                if (BDArmorySettings.DEBUG_WEAPONS)
                {
                    Debug.Log("[BDArmory.PooledRocket]: Rocket ballistic velocity too low, stopping");
                }
                Detonate(hit.point, false, hitPart);
                return true;
            }
            return false;
        }

        private bool ProximityAirDetonation()
        {
            if (isAPSprojectile && (tgtShell != null || tgtRocket != null))
            {
                if (currentPosition.CloserToThan(tgtShell != null ? tgtShell.currentPosition : tgtRocket.currentPosition, detonationRange / 2))
                {
                    if (BDArmorySettings.DEBUG_WEAPONS)
                        Debug.Log("[BDArmory.PooledRocket]: rocket proximity to APS target | Distance overlap = " + detonationRange + "| tgt name = " + tgtShell != null ? tgtShell.name : tgtRocket.name);
                    return true;
                }
            }

            if (Time.time - startTime < armingTime) return false;
            if (!(((explosive || nuclear) && tntMass > 0) || beehive)) return false;
            if (!flak) return false; // Invalid type.

            using var loadedVessels = BDATargetManager.LoadedVessels.GetEnumerator();
            while (loadedVessels.MoveNext())
            {
                if (loadedVessels.Current == null || !loadedVessels.Current.loaded) continue;
                if (loadedVessels.Current == sourceVessel) continue;
                Vector3 relativeVelocity = loadedVessels.Current.Velocity() - currentVelocity;
                float relativeSpeed = relativeVelocity.magnitude;
                if (Vector3.Dot(relativeVelocity, loadedVessels.Current.CoM - currentPosition) >= 0) continue; // Ignore craft that aren't approaching.
                float localDetonationRange = detonationRange + loadedVessels.Current.GetRadius(average: true); // Detonate when the (average) outermost part of the vessel is within the detonateRange.
                float detRangeTime = TimeWarp.fixedDeltaTime + 2 * localDetonationRange / Mathf.Max(1f, relativeSpeed); // Time for this frame's movement plus the relative separation to change by twice the detonation range + the vessel's radius (within reason). This is more than the worst-case time needed for the rocket to reach the CPA (ignoring relative acceleration, technically we should be solving x=v*t+1/2*a*t^2 for t).
                var timeToCPA = loadedVessels.Current.TimeToCPA(currentPosition, currentVelocity, currentAcceleration, detRangeTime);
                if (timeToCPA > 0 && timeToCPA < detRangeTime) // Going to reach the CPA within the detRangeTime
                {
                    Vector3 adjustedTgtPos = loadedVessels.Current.PredictPosition(timeToCPA);
                    Vector3 CPA = AIUtils.PredictPosition(currentPosition, currentVelocity, currentAcceleration, timeToCPA);
                    float minSepSqr = (CPA - adjustedTgtPos).sqrMagnitude;
                    float localDetonationRangeSqr = localDetonationRange * localDetonationRange;
                    if (minSepSqr < localDetonationRangeSqr)
                    {
                        if (!detonateAtMinimumDistance)
                        {
                            // Move the detonation time back to the point where it came within the detonation range, but not before the current time.
                            float correctionDistance = BDAMath.Sqrt(localDetonationRangeSqr - minSepSqr);
                            if (Time.time - startTime > thrustTime)
                            {
                                timeToCPA = Mathf.Max(0, timeToCPA - correctionDistance / relativeSpeed);
                            }
                            else
                            {
                                float acceleration = currentAcceleration.magnitude;
                                relativeSpeed += timeToCPA * acceleration; // Get the relative speed at the CPA for the correction.
                                float determinant = relativeSpeed * relativeSpeed - 2 * acceleration * correctionDistance;
                                timeToCPA = determinant > 0 ? Mathf.Max(0, timeToCPA - (relativeSpeed - BDAMath.Sqrt(determinant)) / acceleration) : 0;
                            }
                        }
                        if (timeToCPA < TimeWarp.fixedDeltaTime) // Detonate if timeToCPA is this frame.
                        {
                            currentPosition = AIUtils.PredictPosition(currentPosition, currentVelocity, currentAcceleration, timeToCPA); // Adjust the rocket position to the detonation position.
                            if (BDArmorySettings.DEBUG_WEAPONS) Debug.Log($"[BDArmory.PooledRocket]: Detonating proxy rocket with detonation range {detonationRange}m ({localDetonationRange}m) at distance {(currentPosition - loadedVessels.Current.PredictPosition(timeToCPA)).magnitude}m ({timeToCPA}s) from {loadedVessels.Current.vesselName} of radius {loadedVessels.Current.GetRadius(average: true)}m");
                            currentPosition -= timeToCPA * BDKrakensbane.FrameVelocityV3f; // Adjust for Krakensbane.
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        void Update()
        {
            if (!gameObject.activeInHierarchy)
            {
                return;
            }
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (BDArmorySetup.GameIsPaused || (Time.time - startTime > thrustTime))
                {
                    if (audioSource.isPlaying)
                    {
                        audioSource.Stop();
                    }
                }
                else
                {
                    if (!audioSource.isPlaying)
                    {
                        audioSource.Play();
                    }
                }
            }
        }

        void Detonate(Vector3 pos, bool missed, Part hitPart = null, bool airDetonation = false)
        {
            hasDetonated = true;
            if (!missed)
            {
                if (beehive)
                {
                    BeehiveDetonation();
                }
                else
                {
                    if (tntMass > 0)
                    {
                        Vector3 direction = default(Vector3);
                        if (shaped)
                        {
                            direction = currentVelocity.normalized;
                            //direction = transform.forward //ideal, but no guarantee that mod rockets have correct transform orientation
                        }
                        if (gravitic)
                        {
                            var overlapSphereColliderCount = Physics.OverlapSphereNonAlloc(currentPosition, blastRadius, detonateOverlapSphereColliders, layerMask);
                            if (overlapSphereColliderCount == detonateOverlapSphereColliders.Length)
                            {
                                detonateOverlapSphereColliders = Physics.OverlapSphere(currentPosition, blastRadius, layerMask);
                                overlapSphereColliderCount = detonateOverlapSphereColliders.Length;
                            }
                            using (var hitsEnu = detonateOverlapSphereColliders.Take(overlapSphereColliderCount).GetEnumerator())
                            {
                                while (hitsEnu.MoveNext())
                                {
                                    if (hitsEnu.Current == null) continue;

                                    Part partHit = hitsEnu.Current.GetComponentInParent<Part>();
                                    if (partHit == null) continue;
                                    if (ProjectileUtils.IsIgnoredPart(partHit)) continue; // Ignore ignored parts.
                                    float distance = Vector3.Distance(currentPosition, partHit.transform.position);
                                    if (gravitic)
                                    {
                                        if (partHit.mass > 0)
                                        {
                                            var ME = partHit.vessel.rootPart.FindModuleImplementing<ModuleMassAdjust>();
                                            if (ME == null)
                                            {
                                                ME = (ModuleMassAdjust)partHit.vessel.rootPart.AddModule("ModuleMassAdjust");
                                            }
                                            ME.massMod += (massMod * (1 - (distance / blastRadius))); //this way craft at edge of blast might only get disabled instead of bricked
                                            ME.duration += (BDArmorySettings.WEAPON_FX_DURATION * (1 - (distance / blastRadius))); //can bypass EMP damage cap
                                        }
                                    }
                                }
                            }
                        }
                        if (incendiary)
                        { // throw 20 random raytraces out in a cone and see what gets tagged
                            var RaycastCommands = new Unity.Collections.NativeArray<RaycastCommand>(20, Unity.Collections.Allocator.TempJob);
                            var RaycastHits = new Unity.Collections.NativeArray<RaycastHit>(20, Unity.Collections.Allocator.TempJob); // Note: RaycastCommands only return the first hit until Unity 2022.2.

                            for (int j = 0; j < 20; ++j)
                                RaycastCommands[j] = new RaycastCommand(currentPosition, VectorUtils.GaussianDirectionDeviation(currentVelocity, 80), blastRadius * 1.2f, (int)LayerMasks.Parts);
                            var job = RaycastCommand.ScheduleBatch(RaycastCommands, RaycastHits, 1, default);
                            job.Complete(); // Wait for the job to complete.
                            foreach (var hit in RaycastHits)
                            {
                                if (hit.collider != null)
                                {
                                    KerbalEVA eva = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                                    Part p = eva ? eva.part : hit.collider.gameObject.GetComponentInParent<Part>();
                                    if (p != null)
                                    {
                                        float distance = Vector3.Distance(currentPosition, hit.point);
                                        BulletHitFX.AttachFire(hit.point, p, caliber, sourceVesselName, BDArmorySettings.WEAPON_FX_DURATION * (1 - (distance / blastRadius)), 1, true); //else apply fire to occluding part
                                        if (BDArmorySettings.DEBUG_WEAPONS)
                                            Debug.Log("[BDArmory.PooledRocket]: Applying fire to " + p.name + " at distance " + distance + "m, for " + BDArmorySettings.WEAPON_FX_DURATION * (1 - (distance / blastRadius)) + " seconds"); ;
                                    }
                                    if (BDArmorySettings.DEBUG_WEAPONS)
                                        Debug.Log("[BDArmory.PooledRocket] incendiary raytrace: " + hit.point.x + "; " + hit.point.y + "; " + hit.point.z);
                                }
                            }
                            /*
                            for (int f = 0; f < 20; f++) //throw 20 random raytraces out in a sphere and see what gets tagged
                            {
                                Ray LoSRay = new Ray(prevPosition, VectorUtils.GaussianDirectionDeviation(currentVelocity, 80));
                                RaycastHit hit;
                                if (Physics.Raycast(LoSRay, out hit, blastRadius * 1.2f, layerMask)) // only add fires to parts in LoS of blast
                                {
                                    KerbalEVA eva = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                                    Part p = eva ? eva.part : hit.collider.gameObject.GetComponentInParent<Part>();
                                    if (p != null)
                                    {
                                        float distance = Vector3.Distance(currentPosition, hit.point);
                                        BulletHitFX.AttachFire(hit.point, p, caliber, sourceVesselName, BDArmorySettings.WEAPON_FX_DURATION * (1 - (distance / blastRadius)), 1, true); //else apply fire to occluding part
                                        if (BDArmorySettings.DEBUG_WEAPONS)
                                            Debug.Log("[BDArmory.PooledRocket]: Applying fire to " + p.name + " at distance " + distance + "m, for " + BDArmorySettings.WEAPON_FX_DURATION * (1 - (distance / blastRadius)) + " seconds"); ;
                                    }
                                    if (BDArmorySettings.DEBUG_WEAPONS)
                                        Debug.Log("[BDArmory.PooledRocket] incendiary raytrace: " + hit.point.x + "; " + hit.point.y + "; " + hit.point.z);
                                }
                            }    
                            */
                        }
                        if (concussion || EMP || choker)
                        {
                            var overlapSphereColliderCount = Physics.OverlapSphereNonAlloc(currentPosition, 25, detonateOverlapSphereColliders, layerMask);
                            if (overlapSphereColliderCount == detonateOverlapSphereColliders.Length)
                            {
                                detonateOverlapSphereColliders = Physics.OverlapSphere(currentPosition, 25, layerMask);
                                overlapSphereColliderCount = detonateOverlapSphereColliders.Length;
                            }
                            using (var hitsEnu = detonateOverlapSphereColliders.Take(overlapSphereColliderCount).GetEnumerator())
                            {
                                var craftHit = new HashSet<Vessel>();
                                while (hitsEnu.MoveNext())
                                {
                                    if (hitsEnu.Current == null) continue;
                                    if (hitsEnu.Current.gameObject == FlightGlobals.currentMainBody.gameObject) continue; // Ignore terrain hits.
                                    Part partHit = hitsEnu.Current.GetComponentInParent<Part>();
                                    if (partHit == null) continue;
                                    if (ProjectileUtils.IsIgnoredPart(partHit)) continue; // Ignore ignored parts.
                                    if (craftHit.Contains(partHit.vessel)) continue; // Don't hit the same craft multiple times.
                                    craftHit.Add(partHit.vessel);

                                    if (partHit != null)
                                    {
                                        float distance = Vector3.Distance(partHit.transform.position, currentPosition);
                                        if (concussion && partHit.mass > 0)
                                        {
                                            partHit.rb.AddForceAtPosition((partHit.transform.position - currentPosition).normalized * impulse, partHit.transform.position, ForceMode.Acceleration);
                                        }
                                        if (EMP && !VesselModuleRegistry.ignoredVesselTypes.Contains(partHit.vesselType))
                                        {
                                            var MDEC = partHit.vessel.rootPart.FindModuleImplementing<ModuleDrainEC>();
                                            if (MDEC == null)
                                            {
                                                MDEC = (ModuleDrainEC)partHit.vessel.rootPart.AddModule("ModuleDrainEC");
                                            }
                                            MDEC.incomingDamage = (25 - distance) * 5; //this way craft at edge of blast might only get disabled instead of bricked
                                            MDEC.softEMP = false; //can bypass EMP damage cap                                            
                                        }
                                        if (choker)
                                        {
                                            var ash = partHit.vessel.rootPart.FindModuleImplementing<ModuleDrainIntakes>();
                                            if (ash == null)
                                            {
                                                ash = (ModuleDrainIntakes)partHit.vessel.rootPart.AddModule("ModuleDrainIntakes");
                                            }
                                            ash.drainDuration += BDArmorySettings.WEAPON_FX_DURATION * (1 - (distance / 25)); //reduce intake knockout time based on distance from epicenter                                        
                                        }
                                    }
                                }
                            }
                            ExplosionFx.CreateExplosion(pos, tntMass, explModelPath, explSoundPath, ExplosionSourceType.Rocket, caliber, null, sourceVesselName, null, null, direction, -1, true, Hitpart: hitPart, sourceVelocity: airDetonation ? currentVelocity : default);
                        }
                        else
                        {
                            if (nuclear)
                                NukeFX.CreateExplosion(pos, ExplosionSourceType.Rocket, sourceVesselName, rocket.DisplayName, 0, tntMass * 200, tntMass, tntMass, EMP, blastSoundPath, flashModelPath, shockModelPath, blastModelPath, plumeModelPath, debrisModelPath, "", "", hitPart: hitPart, sourceVelocity: airDetonation ? currentVelocity : default);
                            else
                                ExplosionFx.CreateExplosion(pos, tntMass, explModelPath, explSoundPath, ExplosionSourceType.Rocket, caliber, null, sourceVesselName, null, null, direction, -1, false, rocketMass * 1000, -1, dmgMult, shaped ? ExplosionFx.WarheadTypes.ShapedCharge : ExplosionFx.WarheadTypes.Standard, hitPart, apMod, ProjectileUtils.isReportingWeapon(sourceWeapon) ? (float)distanceFromStart : -1, sourceVelocity: airDetonation ? currentVelocity : default);
                        }
                    }
                }
            }
            gameObject.SetActive(false);
        }

        public void BeehiveDetonation()
        {
            if (subMunitionType == null)
            {
                Debug.Log("[BDArmory.PooledRocket] Beehive round not configured with subMunitionType!");
                return;
            }
            string[] subMunitionData = subMunitionType.Split(new char[] { ';' });
            string projType = subMunitionData[0];
            if (subMunitionData.Length < 2 || !int.TryParse(subMunitionData[1], out int count)) count = 1;
            if (BulletInfo.bulletNames.Contains(projType))
            {
                BulletInfo sBullet = BulletInfo.bullets[projType];
                
                float relVelocity = (thrust / rocketMass) * Mathf.Clamp(Time.time - startTime, 0, thrustTime); //currVel is rocketVel + orbitalvel, if in orbit, which will dramatically increase dispersion cone angle, so using accel * time instad
                float incrementVelocity = 1000 / (relVelocity + sBullet.bulletVelocity); //using 1km/s as a reference Unit 
                float dispersionAngle = sBullet.subProjectileDispersion > 0 ? sBullet.subProjectileDispersion : BDAMath.Sqrt(count) / 2; //fewer fragments/pellets are going to be larger-> move slower, less dispersion
                float dispersionVelocityforAngle = 1000 / incrementVelocity * Mathf.Sin(dispersionAngle * Mathf.Deg2Rad); // convert m/s despersion to angle, accounting for vel of round

                SourceInfo sourceInfo = new SourceInfo(sourceVessel, team, sourceWeapon, currentPosition);
                GraphicsInfo graphicsInfo = new GraphicsInfo("BDArmory/Textures/bullet", GUIUtils.ParseColor255(sBullet.projectileColor), GUIUtils.ParseColor255(sBullet.startColor),
                    sBullet.caliber / 300, sBullet.caliber / 750, 0, 1.75f, 2.65f, "",
                    sBullet.tntMass > 0.5f ? explModelPath : "BDArmory/Models/explosion/30mmExplosion", explSoundPath);
                NukeInfo nukeInfo = sBullet.nuclear ? new NukeInfo(flashModelPath, shockModelPath, blastModelPath,
                    plumeModelPath, debrisModelPath, blastSoundPath) : new NukeInfo();
                Vector3[] firedVelocities = new Vector3[count * sBullet.projectileCount];
                for (int s = 0; s < count * sBullet.projectileCount; s++)
                {
                    firedVelocities[s] = currentVelocity + UnityEngine.Random.onUnitSphere * dispersionVelocityforAngle;
                }

                float subTTL = Mathf.Max(sBullet.projectileTTL, detonationRange / sBullet.bulletVelocity * 1.1f);

                FireBullet(sBullet, count * sBullet.projectileCount, sourceInfo, graphicsInfo, nukeInfo, firedVelocities, true, subTTL,
                        TimeWarp.fixedDeltaTime, detonationRange, detonationRange / Mathf.Max(1, currentVelocity.magnitude),
                        true, isAPSprojectile, tgtRocket, tgtShell, thief, dmgMult, false, currentVelocity.magnitude);
            }
            else
            {
                RocketInfo sRocket = RocketInfo.rockets[projType];
                for (int s = 0; s < count; s++)
                {
                    GameObject rocketObj = ModuleWeapon.rocketPool[sRocket.name].GetPooledObject();
                    rocketObj.transform.position = currentPosition;
                    //rocketObj.transform.rotation = currentRocketTfm.rotation;
                    rocketObj.transform.rotation = transform.rotation;
                    rocketObj.transform.localScale = transform.localScale;
                    PooledRocket rocket = rocketObj.GetComponent<PooledRocket>();
                    rocket.explModelPath = explModelPath;
                    rocket.explSoundPath = explSoundPath;
                    rocket.caliber = sRocket.caliber;
                    rocket.apMod = sRocket.apMod;
                    rocket.rocketMass = sRocket.rocketMass;
                    rocket.blastRadius = blastRadius = BlastPhysicsUtils.CalculateBlastRange(sRocket.tntMass);
                    rocket.thrust = sRocket.thrust;
                    rocket.thrustTime = sRocket.thrustTime;
                    rocket.flak = sRocket.flak;
                    rocket.detonateAtMinimumDistance = detonateAtMinimumDistance;
                    rocket.detonationRange = detonationRange;
                    rocket.timeToDetonation = detonationRange / Mathf.Max(1, currentVelocity.magnitude); // Only a short time remaining to the target.
                    rocket.tntMass = sRocket.tntMass;
                    rocket.shaped = sRocket.shaped;
                    rocket.concussion = sRocket.impulse;
                    rocket.gravitic = sRocket.gravitic;
                    rocket.EMP = sRocket.EMP;
                    rocket.nuclear = sRocket.nuclear;
                    rocket.beehive = sRocket.beehive;
                    //if (beehive) //no submunitions of submunitions, not while detoantionRange remains the sasme (sub-submunitions would instantly spawn)
                    //{
                    //    rocket.subMunitionType = sRocket.subMunitionType;
                    //}
                    rocket.choker = choker;
                    rocket.impulse = sRocket.force;
                    rocket.massMod = sRocket.massMod;
                    rocket.incendiary = sRocket.incendiary;
                    rocket.randomThrustDeviation = sRocket.thrustDeviation;
                    rocket.bulletDmgMult = bulletDmgMult;
                    rocket.sourceVessel = sourceVessel;
                    rocket.sourceWeapon = sourceWeapon;
                    rocketObj.transform.SetParent(transform);
                    rocket.rocketName = rocketName + " submunition";
                    rocket.team = team;
                    rocket.parentRB = parentRB;
                    rocket.rocket = RocketInfo.rockets[sRocket.name];
                    rocket.rocketSoundPath = rocketSoundPath;
                    rocket.thief = thief; //currently will only steal on direct hit
                    rocket.dmgMult = dmgMult;
                    if (isAPSprojectile)
                    {
                        rocket.isAPSprojectile = true;
                        rocket.tgtShell = tgtShell;
                        rocket.tgtRocket = tgtRocket;
                    }
                    rocket.isSubProjectile = true;
                    rocketObj.SetActive(true);
                }
            }
        }

        void SetupAudio()
        {
            audioSource = gameObject.GetComponent<AudioSource>();
            if (audioSource == null) { audioSource = gameObject.AddComponent<AudioSource>(); }
            audioSource.loop = true;
            audioSource.minDistance = 1;
            audioSource.maxDistance = 2000;
            audioSource.dopplerLevel = 0.5f;
            audioSource.volume = 0.9f * BDArmorySettings.BDARMORY_WEAPONS_VOLUME;
            audioSource.pitch = 1f;
            audioSource.priority = 255;
            audioSource.spatialBlend = 1;
            audioSource.clip = SoundUtils.GetAudioClip(rocketSoundPath);

            UpdateVolume();
            BDArmorySetup.OnVolumeChange += UpdateVolume;
        }

        void UpdateVolume()
        {
            if (audioSource)
            {
                audioSource.volume = BDArmorySettings.BDARMORY_WEAPONS_VOLUME;
            }
        }
        void OnGUI()
        {
            if (((HighLogic.LoadedSceneIsFlight && BDArmorySetup.GAME_UI_ENABLED && !MapView.MapIsEnabled && BDTISettings.TEAMICONS) || HighLogic.LoadedSceneIsFlight && !BDArmorySetup.GAME_UI_ENABLED && !MapView.MapIsEnabled && BDTISettings.TEAMICONS && BDTISettings.PERSISTANT) && BDTISettings.MISSILES)
            {
                if (distanceFromStart > 100)
                {
                    GUIUtils.DrawTextureOnWorldPos(transform.position, BDTISetup.Instance.TextureIconRocket, new Vector2(20, 20), 0);
                }
            }
        }
    }
}
