using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using BDArmory.Armor;
using BDArmory.Competition;
using BDArmory.Damage;
using BDArmory.Extensions;
using BDArmory.FX;
using BDArmory.Settings;
using BDArmory.Shaders;
using BDArmory.Targeting;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.Weapons;

namespace BDArmory.Bullets
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class PooledBulletManager : MonoBehaviour
    {
        public static PooledBulletManager Instance;
        readonly HashSet<PooledBullet> activeBullets = [];

        public static void AddBullet(PooledBullet bullet) { Instance.activeBullets.Add(bullet); }
        public static void RemoveBullet(PooledBullet bullet) { Instance.activeBullets.Remove(bullet); }

        void Awake()
        {
            if (Instance != null) Destroy(Instance);
            Instance = this;
            activeBullets.Clear();
        }

        void FixedUpdate()
        {
            if (activeBullets.Count == 0) return;
            var autoSync = Physics.autoSyncTransforms;
            try
            {
                // Perform the various stages that pooled bullets go through in blocks to hopefully reduce physics sync delays.
                // Bullets should get deactivated and removed from activeBullets if they die.
                var bullets = activeBullets.ToList(); // Pre-convert to a list and skip null bullets. This avoids moving subprojectiles from flak rounds.
                foreach (var bullet in bullets) if (bullet != null && bullet.isActiveAndEnabled) bullet.PreCollisions();
                Physics.SyncTransforms(); Physics.autoSyncTransforms = false; // Sync the physics, then prevent any auto-syncing while we run our collision checks.
                foreach (var bullet in bullets) if (bullet != null && bullet.isActiveAndEnabled) bullet.DoCollisions(); // All the Physics calls occur here.
                Physics.autoSyncTransforms = autoSync; // Re-enable auto-syncing.
                foreach (var bullet in bullets) if (bullet != null && bullet.isActiveAndEnabled) bullet.PostCollisions();
            }
            catch (Exception e)
            { // This shouldn't happen, but if it does, some active bullets may get out of sync.
                Debug.LogError($"[BDArmory.PooledBulletManager]: DEBUG {e.Message}\n{e.StackTrace}");
                Physics.autoSyncTransforms = autoSync;
            }
        }
    }

    public class PooledBullet : MonoBehaviour
    {
        #region Declarations

        public BulletInfo bullet;
        //public float leftPenetration; //Not used by anything? Was this to provide a upper cap to how far a bullet could pen?

        public enum PooledBulletTypes
        {
            Slug,
            Explosive,
            Shaped
        }
        public enum BulletFuzeTypes
        {
            None,
            Impact,
            Timed,
            Proximity,
            Flak,
            Delay,
            Penetrating
        }
        public enum BulletDragTypes
        {
            None,
            AnalyticEstimate,
            NumericalIntegration
        }

        //public PooledBulletTypes bulletType;
        public BulletFuzeTypes fuzeType;
        public PooledBulletTypes HEType;
        public BulletDragTypes dragType;

        public Vessel sourceVessel;
        public string sourceVesselName;
        public Part sourceWeapon;
        public string team;
        public Color lightColor = GUIUtils.ParseColor255("255, 235, 145, 255");
        public Color projectileColor;
        public string bulletTexturePath;
        public string smokeTexturePath;
        public bool fadeColor;
        Color smokeColor = Color.white;
        public Color startColor;
        Color currentColor;
        public bool bulletDrop = true;
        public float tracerStartWidth = 1;
        public float tracerEndWidth = 1;
        public float tracerLength = 0;
        public float tracerDeltaFactor = 1.35f;
        public float tracerLuminance = 1;
        public Vector3 currentPosition { get { return _currentPosition; } set { _currentPosition = value; transform.position = value; } } // Local alias for transform.position speeding up access by around 100x.
        Vector3 _currentPosition = default;
        public Vector3 previousPosition { get; private set; } // Previous position, adjusted for the current Krakensbane. (Used for APS targeting.)

        //explosive parameters
        public float radius = 30;
        public float tntMass = 0;
        public float blastPower = 8;
        public float blastHeat = -1;
        public float bulletDmgMult = 1;
        public string explModelPath;
        public string explSoundPath;

        //general params
        public bool incendiary;
        public float apBulletMod = 0;
        public bool nuclear = false;
        public string flashModelPath;
        public string shockModelPath;
        public string blastModelPath;
        public string plumeModelPath;
        public string debrisModelPath;
        public string blastSoundPath;
        //public bool homing = false;
        public bool beehive = false;
        public string subMunitionType;
        public bool EMP = false;

        //gravitic parameters
        public float impulse = 0;
        public float massMod = 0;

        //mutator Param
        public bool stealResources;
        public float dmgMult = 1;

        Vector3 startPosition;
        public float detonationRange = 5f;
        public float timeToDetonation;
        float armingTime;
        float randomWidthScale = 1;
        LineRenderer[] bulletTrail;
        public float timeAlive = 0;
        public float timeToLiveUntil;
        Light lightFlash;
        bool wasInitiated;
        public Vector3 currentVelocity; // Current real velocity w/o offloading
        public float bulletMass;
        public float caliber = 1;
        public float bulletVelocity; //muzzle velocity
        public float guidanceDPS = 0;
        public bool sabot = false;
        private float HERatio = 0.06f;
        public float ballisticCoefficient;
        float currentSpeed; // Current speed of the bullet, for drag purposes.
        public float timeElapsedSinceCurrentSpeedWasAdjusted; // Time since the current speed was adjusted, to allow tracking speed changes of the bullet in air and water.
        bool underwater = false;
        bool startsUnderwater = false;
        public static Shader bulletShader;
        public static bool shaderInitialized;
        private float impactSpeed;
        private float dragVelocityFactor;

        public bool hasPenetrated = false;
        public bool hasDetonated = false;
        public bool hasRicocheted = false;
        public bool fuzeTriggered = false;
        private Part CurrentPart = null;

        public bool isAPSprojectile = false;
        public bool isSubProjectile = false;
        public PooledRocket tgtRocket = null;
        public PooledBullet tgtShell = null;

        public Vessel targetVessel;
        float atmosphereDensity;
        public int penTicker = 0;

        Ray bulletRay;

        #endregion Declarations

        static RaycastHit[] hits;
        static RaycastHit[] reverseHits;
        static Collider[] overlapSphereColliders;
        static List<RaycastHit> allHits;
        static Dictionary<Vessel, float> rayLength;
        static List<Vessel> vesselsInRange;
        private Vector3[] linePositions = new Vector3[2];
        private Vector3[] smokePositions = new Vector3[5];

        private List<Part> partsHit = new List<Part>();

        private double distanceTraveled = 0;
        private double distanceLastHit = double.PositiveInfinity;
        private double initialHitDistance = 0;
        private float kDist = 1;
        private float iTime = 0; // Consistent naming with ModuleWeapon: TimeWarp.fixedDeltaTime - timeToCPA of proxy detonation.

        public double DistanceTraveled { get { return distanceTraveled; } set { distanceTraveled = value; } }

        void Awake()
        {
            hits ??= new RaycastHit[100];
            reverseHits ??= new RaycastHit[100];
            overlapSphereColliders ??= new Collider[1000];
            allHits ??= [];
            vesselsInRange ??= [];
            rayLength ??= [];
        }

        void OnEnable()
        {
            currentPosition = transform.position; // In case something sets transform.position instead of currentPosition.
            previousPosition = currentPosition;
            startPosition = currentPosition;
            currentSpeed = currentVelocity.magnitude; // this is the velocity used for drag estimations (only), use total velocity, not muzzle velocity
            timeAlive = 0;
            armingTime = isSubProjectile ? 0 : 1.5f * ((beehive ? BlastPhysicsUtils.CalculateBlastRange(tntMass) : detonationRange) / bulletVelocity); //beehive rounds have artifically large detDists; only need explosive radius arming check
            fuzeTriggered = false;
            if (HEType != PooledBulletTypes.Slug)
            {
                HERatio = Mathf.Clamp(tntMass / (bulletMass < tntMass ? tntMass * 1.25f : bulletMass), 0.01f, 0.95f);
            }
            else
            {
                HERatio = 0;
            }
            bool hasNuclearSubMunitions = false;
            if (beehive)
            {
                try
                {
                    string projType = subMunitionType.Split([';'])[0];
                    if (BulletInfo.bulletNames.Contains(projType))
                        hasNuclearSubMunitions = BulletInfo.bullets[projType].nuclear;
                }
                catch { } // Ignored, non-nuclear submunitions.
            }
            if (nuclear && !isSubProjectile || hasNuclearSubMunitions) // Sub-projectiles get these set from the parent shell during beehive detonation.
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
            distanceTraveled = 0; // Reset the distance travelled for the bullet (since it comes from a pool).
            distanceLastHit = double.PositiveInfinity; // Reset variables used in post-penetration calculations.
            initialHitDistance = 0;
            kDist = 1;
            dragVelocityFactor = 1;

            startsUnderwater = FlightGlobals.getAltitudeAtPos(currentPosition) < 0;
            underwater = startsUnderwater;

            projectileColor.a = Mathf.Clamp(projectileColor.a, 0.25f, 1f);
            startColor.a = Mathf.Clamp(startColor.a, 0.25f, 1f);
            currentColor = projectileColor;
            if (fadeColor)
            {
                currentColor = startColor;
            }

            if (lightFlash == null || !gameObject.GetComponent<Light>())
            {
                lightFlash = gameObject.AddOrGetComponent<Light>();
                lightFlash.type = LightType.Point;
                lightFlash.range = 8;
                lightFlash.intensity = 1;
                lightFlash.color = lightColor;
                lightFlash.enabled = true;
            }

            //tracer setup
            if (bulletTrail == null || !gameObject.GetComponent<LineRenderer>())
            {
                bulletTrail = new LineRenderer[2];
                bulletTrail[0] = gameObject.AddOrGetComponent<LineRenderer>();

                GameObject bulletFX = new GameObject("bulletTrail");
                bulletFX.transform.SetParent(gameObject.transform);
                bulletTrail[1] = bulletFX.AddOrGetComponent<LineRenderer>();
            }

            if (!shaderInitialized)
            {
                shaderInitialized = true;
                bulletShader = BDAShaderLoader.BulletShader;
            }

            // Note: call SetTracerPosition() after enabling the bullet and making adjustments to it's position.
            if (!wasInitiated)
            {
                bulletTrail[0].positionCount = linePositions.Length;
                bulletTrail[1].positionCount = smokePositions.Length;
                bulletTrail[0].material = new Material(bulletShader);
                bulletTrail[1].material = new Material(bulletShader);
                randomWidthScale = UnityEngine.Random.Range(0.5f, 1f);
                gameObject.layer = 15;
            }
            smokeColor.r = 0.85f;
            smokeColor.g = 0.85f;
            smokeColor.b = 0.85f;
            smokeColor.a = 0.75f;
            bulletTrail[0].material.mainTexture = GameDatabase.Instance.GetTexture(bulletTexturePath, false);
            bulletTrail[0].material.SetColor("_TintColor", currentColor);
            bulletTrail[0].material.SetFloat("_Lum", tracerLuminance > 0 ? tracerLuminance : 0.5f);
            if (!string.IsNullOrEmpty(smokeTexturePath))
            {
                bulletTrail[1].material.mainTexture = GameDatabase.Instance.GetTexture(smokeTexturePath, false);
                bulletTrail[1].material.SetColor("_TintColor", smokeColor);
                bulletTrail[1].material.SetFloat("_Lum", 0.5f);
                bulletTrail[1].textureMode = LineTextureMode.Tile;
                bulletTrail[1].material.SetTextureScale("_MainTex", new Vector2(0.1f, 1));
                bulletTrail[1].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                bulletTrail[1].receiveShadows = false;
                bulletTrail[1].enabled = true;
            }
            else
            {
                bulletTrail[1].enabled = false;
            }

            tracerStartWidth *= 2f;
            tracerEndWidth *= 2f;

            //leftPenetration = 1;
            penTicker = 0;
            wasInitiated = true;
            StartCoroutine(FrameDelayedRoutine());

            // Log shots fired.
            if (sourceVessel)
            {
                sourceVesselName = sourceVessel.GetName(); // Set the source vessel name as the vessel might have changed its name or died by the time the bullet hits.
            }
            else
            {
                sourceVesselName = null;
            }
            if (caliber >= BDArmorySettings.APS_THRESHOLD) //if (caliber > 60)
            {
                BDATargetManager.FiredBullets.Add(this);
            }
            PooledBulletManager.AddBullet(this);
        }

        void OnDisable()
        {
            PooledBulletManager.RemoveBullet(this);
            sourceVessel = null;
            sourceWeapon = null;
            CurrentPart = null;
            sabot = false;
            partsHit.Clear();
            if (caliber >= BDArmorySettings.APS_THRESHOLD)  //if (caliber > 60)
            {
                BDATargetManager.FiredBullets.Remove(this);
            }
            isAPSprojectile = false;
            tgtRocket = null;
            tgtShell = null;
            smokeTexturePath = "";
        }

        void OnDestroy()
        {
            StopCoroutine(FrameDelayedRoutine());
        }

        IEnumerator FrameDelayedRoutine()
        {
            yield return new WaitForFixedUpdate();
            lightFlash.enabled = false;
        }

        void OnWillRenderObject()
        {
            if (!gameObject.activeInHierarchy)
            {
                return;
            }
            Camera currentCam = Camera.current;
            if (TargetingCamera.IsTGPCamera(currentCam))
            {
                UpdateWidth(currentCam, 4);
            }
            else
            {
                UpdateWidth(currentCam, 1);
            }
        }

        void Update()
        {
            if (!gameObject.activeInHierarchy) return;

            if (fadeColor)
            {
                FadeColor();
                bulletTrail[0].material.SetColor("_TintColor", currentColor * (tracerLuminance > 0 ? tracerLuminance : 0.5f));
            }
            if (tracerLuminance > 1 && bulletTrail[1].enabled)
            {
                float fade = Mathf.Lerp(0.75f, 0.05f, 0.07f);
                smokeColor.a = fade;
                bulletTrail[1].material.SetColor("_TintColor", smokeColor);
                bulletTrail[1].material.SetTextureOffset("_MainTex", new Vector2(-timeAlive / 3, 0));
                if (fade <= 0.05f) bulletTrail[1].enabled = false;
            }
            SetTracerPosition();
        }

        /// <summary>
        /// These functions replace FixedUpdate and are called by PooledBulletManager to try to group all the Physics calls together to reduce the need for physics sync calls.
        /// It seems to be slightly faster for large numbers of bullets.
        /// </summary>
        public void PreCollisions()
        {
            //floating origin and velocity offloading corrections
            if (BDKrakensbane.IsActive)
            {
                currentPosition -= BDKrakensbane.FloatingOriginOffsetNonKrakensbane;
                startPosition -= BDKrakensbane.FloatingOriginOffsetNonKrakensbane;
            }
            previousPosition = currentPosition;
            timeAlive += TimeWarp.fixedDeltaTime;

            if (Time.time > timeToLiveUntil) //kill bullet when TTL ends
            {
                if (isAPSprojectile)
                {
                    if (HEType != PooledBulletTypes.Explosive && tntMass > 0)
                        ExplosionFx.CreateExplosion(currentPosition, tntMass, explModelPath, explSoundPath, ExplosionSourceType.Bullet, caliber, null, sourceVesselName, null, null, default, -1, true, sourceVelocity: currentVelocity);
                }
                KillBullet();
                return;
            }
            /*
            if (fuzeTriggered)
            {
                if (!hasDetonated)
                {
                    ExplosionFx.CreateExplosion(currPosition, tntMass, explModelPath, explSoundPath, ExplosionSourceType.Bullet, caliber, null, sourceVesselName, null, default, -1, false, bulletMass, -1, dmgMult);
                    hasDetonated = true;
                    KillBullet();
                    return;
                }
            }
            */

            if (ProximityAirDetonation(true)) // Pre-move proximity detonation check.
            {
                //detonate
                if (HEType != PooledBulletTypes.Slug)
                    ExplosionFx.CreateExplosion(currentPosition, tntMass, explModelPath, explSoundPath, ExplosionSourceType.Bullet, caliber, null, sourceVesselName, null, null, HEType == PooledBulletTypes.Explosive ? default : currentVelocity, -1, false, bulletMass, -1, dmgMult, HEType == PooledBulletTypes.Shaped ? ExplosionFx.WarheadTypes.ShapedCharge : ExplosionFx.WarheadTypes.Standard, null, HEType == PooledBulletTypes.Shaped ? apBulletMod : 1f, ProjectileUtils.isReportingWeapon(sourceWeapon) ? (float)DistanceTraveled : -1, sourceVelocity: currentVelocity);
                if (nuclear)
                    NukeFX.CreateExplosion(currentPosition, ExplosionSourceType.Bullet, sourceVesselName, bullet.DisplayName, 0, tntMass * 200, tntMass, tntMass, EMP, blastSoundPath, flashModelPath, shockModelPath, blastModelPath, plumeModelPath, debrisModelPath, "", "", sourceVelocity: currentVelocity);
                if (beehive)
                    BeehiveDetonation();
                hasDetonated = true;
                KillBullet();
                return;
            }
        }
        public void DoCollisions()
        {
            CheckBulletCollisions(TimeWarp.fixedDeltaTime);
        }
        public void PostCollisions()
        {
            if (!hasRicocheted) MoveBullet(TimeWarp.fixedDeltaTime); // Ricochets perform movement internally.

            if (BDArmorySettings.BULLET_WATER_DRAG)
            {
                if (startsUnderwater && !underwater) // Bullets that start underwater can exit the water if fired close enough to the surface.
                {
                    startsUnderwater = false;
                }
                if (!startsUnderwater && underwater) // Bullets entering water from air either disintegrate or don't penetrate far enough to bother about. Except large caliber naval shells.
                {
                    if (caliber < 75f)
                    {
                        if (HEType != PooledBulletTypes.Slug)
                            ExplosionFx.CreateExplosion(currentPosition, tntMass, explModelPath, explSoundPath, ExplosionSourceType.Bullet, caliber, null, sourceVesselName, null, null, default, -1, false, bulletMass, -1, dmgMult);
                        if (nuclear)
                            NukeFX.CreateExplosion(currentPosition, ExplosionSourceType.Bullet, sourceVesselName, bullet.DisplayName, 0, tntMass * 200, tntMass, tntMass, EMP, blastSoundPath, flashModelPath, shockModelPath, blastModelPath, plumeModelPath, debrisModelPath, "", "");
                        hasDetonated = true;

                        KillBullet();
                        return;
                    }
                    else
                    {
                        if (HEType != PooledBulletTypes.Slug)
                        {
                            if (fuzeType == BulletFuzeTypes.Delay || fuzeType == BulletFuzeTypes.Penetrating)
                            {
                                fuzeTriggered = true;
                                StartCoroutine(DelayedDetonationRoutine());
                            }
                            else //if (fuzeType != BulletFuzeTypes.None)
                            {
                                if (HEType != PooledBulletTypes.Slug)
                                    ExplosionFx.CreateExplosion(currentPosition, tntMass, explModelPath, explSoundPath, ExplosionSourceType.Bullet, caliber, null, sourceVesselName, null, null, default, -1, false, bulletMass, -1, dmgMult);
                                if (nuclear)
                                    NukeFX.CreateExplosion(currentPosition, ExplosionSourceType.Bullet, sourceVesselName, bullet.DisplayName, 0, tntMass * 200, tntMass, tntMass, EMP, blastSoundPath, flashModelPath, shockModelPath, blastModelPath, plumeModelPath, debrisModelPath, "", "");
                                hasDetonated = true;
                                if (BDArmorySettings.waterHitEffect) FXMonger.Splash(currentPosition, caliber / 2);
                                KillBullet();
                                return;
                            }
                        }
                    }
                }
            }
            //////////////////////////////////////////////////
            //Flak Explosion (air detonation/proximity fuse)
            //////////////////////////////////////////////////

            if (ProximityAirDetonation(false)) // Post-move proximity (end-of-life) detonation check
            {
                //detonate
                if (HEType != PooledBulletTypes.Slug)
                    ExplosionFx.CreateExplosion(currentPosition, tntMass, explModelPath, explSoundPath, ExplosionSourceType.Bullet, caliber, null, sourceVesselName, null, null, HEType == PooledBulletTypes.Explosive ? default : currentVelocity, -1, false, bulletMass, -1, dmgMult, HEType == PooledBulletTypes.Shaped ? ExplosionFx.WarheadTypes.ShapedCharge : ExplosionFx.WarheadTypes.Standard, null, HEType == PooledBulletTypes.Shaped ? apBulletMod : 1f, ProjectileUtils.isReportingWeapon(sourceWeapon) ? (float)DistanceTraveled : -1, sourceVelocity: currentVelocity);
                if (nuclear)
                    NukeFX.CreateExplosion(currentPosition, ExplosionSourceType.Bullet, sourceVesselName, bullet.DisplayName, 0, tntMass * 200, tntMass, tntMass, EMP, blastSoundPath, flashModelPath, shockModelPath, blastModelPath, plumeModelPath, debrisModelPath, "", "", sourceVelocity: currentVelocity);
                if (beehive)
                    BeehiveDetonation();
                hasDetonated = true;
                KillBullet();
                return;
            }
        }

        /// <summary>
        /// Move the bullet for the period of time, tracking distance traveled and accounting for drag and gravity.
        /// This is now done using the second order symplectic leapfrog method.
        /// Note: water drag on bullets breaks the symplectic nature of the integrator (since it's modifying the Hamiltonian), which isn't accounted for during aiming.
        /// </summary>
        /// <param name="period">Period to consider, typically TimeWarp.fixedDeltaTime</param>
        public void MoveBullet(float period)
        {
            atmosphereDensity = (float)FlightGlobals.getAtmDensity(FlightGlobals.getStaticPressure(currentPosition), FlightGlobals.getExternalTemperature(currentPosition));
            // Initial half-timestep velocity change (leapfrog integrator)
            LeapfrogVelocityHalfStep(0.5f * period);

            if (targetVessel != null && atmosphereDensity > 0.05f)
            {
                if (penTicker == 0 && Vector3.Dot(targetVessel.CoM - currentPosition, currentVelocity) > 0) //don't circle around if it misses, or after it hits something
                {
                    Vector3 leadTargetOffset = targetVessel.CoM + Vector3.Distance(targetVessel.CoM, currentPosition) / bulletVelocity * targetVessel.Velocity();
                    //if (Vector3.Angle(currentVelocity, leadTargetOffset) > 1) currentVelocity *= 2f * ballisticCoefficient / (TimeWarp.fixedDeltaTime * currentVelocity.magnitude * atmosphereDensity + 2f * ballisticCoefficient); needs bulletdrop gravity accel factored in as well
                    //apply some drag to projectile if it's turning. Will mess up initial CPA aim calculations, true; on the other hand, its a guided homing bullet.                                                                                                                                                                                                                
                    currentVelocity = Vector3.RotateTowards(currentVelocity, leadTargetOffset - currentPosition, period * guidanceDPS * atmosphereDensity * Mathf.Deg2Rad, 0); //adapt to rockets for homing rockets?
                }
            }

            // Full-timestep position change (leapfrog integrator)
            currentPosition += period * currentVelocity; //move bullet
            distanceTraveled += period * currentVelocity.magnitude; // calculate flight distance for achievement purposes

            if (!underwater && FlightGlobals.getAltitudeAtPos(currentPosition) <= 0) // Check if the bullet is now underwater.
            {
                float hitAngle = Vector3.Angle(GetDragAdjustedVelocity(), -VectorUtils.GetUpDirection(currentPosition));
                if (RicochetScenery(hitAngle))
                {
                    tracerStartWidth /= 2;
                    tracerEndWidth /= 2;

                    currentVelocity = Vector3.Reflect(currentVelocity, VectorUtils.GetUpDirection(currentPosition));
                    currentVelocity = (hitAngle / 150) * 0.65f * currentVelocity;

                    Vector3 randomDirection = UnityEngine.Random.rotation * Vector3.one;

                    currentVelocity = Vector3.RotateTowards(currentVelocity, randomDirection,
                        UnityEngine.Random.Range(0f, 5f) * Mathf.Deg2Rad, 0);
                }
                else
                {
                    underwater = true;
                }
                if (BDArmorySettings.waterHitEffect) FXMonger.Splash(currentPosition, caliber / 2);
            }
            // Second half-timestep velocity change (leapfrog integrator) (should be identical code-wise to the initial half-step)
            LeapfrogVelocityHalfStep(0.5f * period);
        }

        private void LeapfrogVelocityHalfStep(float period)
        {
            timeElapsedSinceCurrentSpeedWasAdjusted += period; // Track flight time for drag purposes
            if (bulletDrop)
                currentVelocity += period * FlightGlobals.getGeeForceAtPosition(currentPosition);
            if (underwater)
            {
                UpdateDragEstimate(); //update the drag estimate, accounting for water/air environment changes. Note: changes due to bulletDrop aren't being applied to the drop.
                currentVelocity *= dragVelocityFactor; // Note: If applied to aerial flight, this screws up targeting, because the weapon's aim code doesn't know how to account for drag. Only have it apply when underwater for now. Review later?
                currentSpeed = currentVelocity.magnitude;
                timeElapsedSinceCurrentSpeedWasAdjusted = 0;
            }
        }

        /// <summary>
        /// Get the current velocity, adjusted for drag if necessary.
        /// </summary>
        /// <returns></returns>
        Vector3 GetDragAdjustedVelocity()
        {
            UpdateDragEstimate();
            if (timeElapsedSinceCurrentSpeedWasAdjusted > 0)
            {
                return currentVelocity * dragVelocityFactor;
            }
            return currentVelocity;
        }

        public bool CheckBulletCollisions(float period)
        {
            //reset our hit variables to default state
            hasPenetrated = true;
            hasDetonated = false;
            hasRicocheted = false;
            //CurrentPart = null; //this needs to persist, at least while the bullet is Enabled
            //penTicker = 0;

            if (BDArmorySettings.VESSEL_RELATIVE_BULLET_CHECKS)
            {
                allHits.Clear();
                rayLength.Clear();
                CheckBulletCollisionWithVessels(period);
                CheckBulletCollisionWithScenery(period);
                using var hitsEnu = allHits.OrderBy(x => x.distance).GetEnumerator(); // Check all hits in order of distance.
                while (hitsEnu.MoveNext()) if (BulletHitAnalysis(hitsEnu.Current, period)) return true;
                return false;
            }
            else
                return CheckBulletCollision(period);
        }

        /// <summary>
        /// This performs checks using the relative velocity to each vessel within the range of the movement of the bullet.
        /// This is particularly relevant at high velocities (e.g., in orbit) where the sideways velocity of co-moving objects causes a complete miss.
        /// </summary>
        /// <param name="period"></param>
        /// <returns></returns>
        public void CheckBulletCollisionWithVessels(float period)
        {
            if (!BDArmorySettings.VESSEL_RELATIVE_BULLET_CHECKS) return;

            const int layerMask = (int)(LayerMasks.Parts | LayerMasks.EVA | LayerMasks.Wheels);
            var overlapSphereRadius = GetOverlapSphereRadius(period); // OverlapSphere of sufficient size to catch all potential craft of <100m radius.
            var overlapSphereColliderCount = Physics.OverlapSphereNonAlloc(currentPosition, overlapSphereRadius, overlapSphereColliders, layerMask);
            if (overlapSphereColliderCount == overlapSphereColliders.Length)
            {
                overlapSphereColliders = Physics.OverlapSphere(currentPosition, overlapSphereRadius, layerMask);
                overlapSphereColliderCount = overlapSphereColliders.Length;
            }

            vesselsInRange.Clear();
            using (var hitsEnu = overlapSphereColliders.Take(overlapSphereColliderCount).GetEnumerator())
            {
                while (hitsEnu.MoveNext())
                {
                    if (hitsEnu.Current == null) continue;
                    try
                    {
                        Part partHit = hitsEnu.Current.GetComponentInParent<Part>();
                        if (partHit == null) continue;
                        if (partHit.vessel == sourceVessel) continue;
                        if (ProjectileUtils.IsIgnoredPart(partHit)) continue; // Ignore ignored parts.
                        if (partHit.vessel != null && !vesselsInRange.Contains(partHit.vessel)) vesselsInRange.Add(partHit.vessel);
                    }
                    catch (Exception e) // ignored
                    {
                        Debug.LogWarning("[BDArmory.PooledBullet]: Exception thrown in CheckBulletCollisionWithVessels: " + e.Message + "\n" + e.StackTrace);
                    }
                }
            }
            foreach (var vessel in vesselsInRange.OrderBy(v => (v.CoM - currentPosition).sqrMagnitude))
            {
                CheckBulletCollisionWithVessel(period, vessel);
            }
        }

        /// <summary>
        /// Calculate the required radius of the overlap sphere such that a craft <100m in radius could potentially collide with the bullet.
        /// </summary>
        /// <param name="period">The period of motion (TimeWarp.fixedDeltaTime).</param>
        /// <returns>The required radius.</returns>
        float GetOverlapSphereRadius(float period)
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
            return 100f + period * BDAMath.Sqrt(maxRelSpeedSqr); // Craft of radius <100m that could collide within the period.
        }

        public void CheckBulletCollisionWithVessel(float period, Vessel vessel)
        {
            var relativeVelocity = currentVelocity - (Vector3)vessel.Velocity();
            float dist = period * relativeVelocity.magnitude;
            bulletRay = new Ray(currentPosition, relativeVelocity + 0.5f * period * FlightGlobals.getGeeForceAtPosition(currentPosition));
            const int layerMask = (int)(LayerMasks.Parts | LayerMasks.EVA | LayerMasks.Wheels);

            var hitCount = Physics.RaycastNonAlloc(bulletRay, hits, dist, layerMask);
            if (hitCount == hits.Length) // If there's a whole bunch of stuff in the way (unlikely), then we need to increase the size of our hits buffer.
            {
                hits = Physics.RaycastAll(bulletRay, dist, layerMask);
                hitCount = hits.Length;
            }

            var reverseRay = new Ray(bulletRay.origin + dist * bulletRay.direction, -bulletRay.direction);
            var reverseHitCount = Physics.RaycastNonAlloc(reverseRay, reverseHits, dist, layerMask);
            if (reverseHitCount == reverseHits.Length)
            {
                reverseHits = Physics.RaycastAll(reverseRay, dist, layerMask);
                reverseHitCount = reverseHits.Length;
            }
            for (int i = 0; i < reverseHitCount; ++i)
            {
                reverseHits[i].distance = dist - reverseHits[i].distance;
                reverseHits[i].normal = -reverseHits[i].normal;
            }

            if (hitCount + reverseHitCount > 0)
            {
                bool hitFound = false;
                Part hitPart;
                using (var hit = hits.Take(hitCount).AsEnumerable().GetEnumerator())
                    while (hit.MoveNext())
                    {
                        hitPart = hit.Current.collider.gameObject.GetComponentInParent<Part>();
                        if (hitPart == null) continue;
                        if (hitPart.vessel == vessel) allHits.Add(hit.Current);
                        hitFound = true;
                    }
                using (var hit = reverseHits.Take(reverseHitCount).AsEnumerable().GetEnumerator())
                    while (hit.MoveNext())
                    {
                        hitPart = hit.Current.collider.gameObject.GetComponentInParent<Part>();
                        if (hitPart == null) continue;
                        if (hitPart.vessel == vessel) allHits.Add(hit.Current);
                        hitFound = true;
                    }
                if (hitFound) rayLength[vessel] = dist;
            }
        }

        public void CheckBulletCollisionWithScenery(float period)
        {
            float dist = period * currentVelocity.magnitude;
            bulletRay = new Ray(currentPosition, currentVelocity + 0.5f * period * FlightGlobals.getGeeForceAtPosition(currentPosition));
            var layerMask = (int)LayerMasks.Scenery;

            var hitCount = Physics.RaycastNonAlloc(bulletRay, hits, dist, layerMask);
            if (hitCount == hits.Length) // If there's a whole bunch of stuff in the way (unlikely), then we need to increase the size of our hits buffer.
            {
                hits = Physics.RaycastAll(bulletRay, dist, layerMask);
                hitCount = hits.Length;
            }
            allHits.AddRange(hits.Take(hitCount));

            var reverseRay = new Ray(bulletRay.origin + dist * bulletRay.direction, -bulletRay.direction);
            var reverseHitCount = Physics.RaycastNonAlloc(reverseRay, reverseHits, dist, layerMask);
            if (reverseHitCount == reverseHits.Length)
            {
                reverseHits = Physics.RaycastAll(reverseRay, dist, layerMask);
                reverseHitCount = reverseHits.Length;
            }
            for (int i = 0; i < reverseHitCount; ++i)
            {
                reverseHits[i].distance = dist - reverseHits[i].distance;
                reverseHits[i].normal = -reverseHits[i].normal;
            }
            allHits.AddRange(reverseHits.Take(reverseHitCount));
        }

        /// <summary>
        /// Check for bullet collision in the upcoming period. 
        /// This also performs a raycast in reverse to detect collisions from rays starting within an object.
        /// </summary>
        /// <param name="period">Period to consider, typically TimeWarp.fixedDeltaTime</param>
        /// <returns>true if a collision is detected, false otherwise.</returns>
        public bool CheckBulletCollision(float period)
        {
            float dist = period * currentVelocity.magnitude;
            bulletRay = new Ray(currentPosition, currentVelocity + 0.5f * period * FlightGlobals.getGeeForceAtPosition(currentPosition));
            const int layerMask = (int)(LayerMasks.Parts | LayerMasks.EVA | LayerMasks.Wheels | LayerMasks.Scenery);
            var hitCount = Physics.RaycastNonAlloc(bulletRay, hits, dist, layerMask);
            if (hitCount == hits.Length) // If there's a whole bunch of stuff in the way (unlikely), then we need to increase the size of our hits buffer.
            {
                hits = Physics.RaycastAll(bulletRay, dist, layerMask);
                hitCount = hits.Length;
            }

            var reverseRay = new Ray(bulletRay.origin + dist * bulletRay.direction, -bulletRay.direction);
            var reverseHitCount = Physics.RaycastNonAlloc(reverseRay, reverseHits, dist, layerMask);
            if (reverseHitCount == reverseHits.Length)
            {
                reverseHits = Physics.RaycastAll(reverseRay, dist, layerMask);
                reverseHitCount = reverseHits.Length;
            }
            for (int i = 0; i < reverseHitCount; ++i)
            {
                reverseHits[i].distance = dist - reverseHits[i].distance;
                reverseHits[i].normal = -reverseHits[i].normal;
            }

            if (hitCount + reverseHitCount > 0)
            {
                // Note: this should probably use something like the CollateHits function in ExplosionFX, but doesn't seem to be as performance critical here.
                var orderedHits = hits.Take(hitCount).Concat(reverseHits.Take(reverseHitCount)).OrderBy(x => x.distance);
                using (var hit = orderedHits.GetEnumerator())
                    while (hit.MoveNext()) if (BulletHitAnalysis(hit.Current, period)) return true;
            }
            return false;
        }

        /// <summary>
        /// Internals of the bullet collision hits loop in CheckBulletCollision so it can also be called from CheckBulletCollisionWithVessel.
        /// </summary>
        /// <param name="hit">The raycast hit.</param>
        /// <param name="vesselHit">Whether the hit is a vessel hit or not.</param>
        /// <param name="dist">The distance the bullet moved in the current reference frame.</param>
        /// <param name="period">The period the bullet moved for.</param>
        /// <returns>true if the bullet hits and dies, false otherwise.</returns>
        bool BulletHitAnalysis(RaycastHit hit, float period)
        {
            if (!hasPenetrated || hasRicocheted || hasDetonated)
            {
                return true;
            }
            Part hitPart;
            KerbalEVA hitEVA;
            try
            {
                hitPart = hit.collider.gameObject.GetComponentInParent<Part>();
                hitEVA = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
            }
            catch (NullReferenceException e)
            {
                Debug.Log("[BDArmory.PooledBullet]:NullReferenceException for Ballistic Hit: " + e.Message);
                return true;
            }

            if (hitPart != null && ProjectileUtils.IsIgnoredPart(hitPart)) return false; // Ignore ignored parts.
            if (hitPart != null && hitPart == sourceWeapon) return false; // Ignore weapon that fired the bullet.
            if (hitPart != null && (hitPart == CurrentPart && ProjectileUtils.IsArmorPart(CurrentPart))) return false; //only have bullet hit armor panels once - no back armor to hit if penetration

            CurrentPart = hitPart;
            if (hitEVA != null)
            {
                hitPart = hitEVA.part;
                // relative velocity, separate from the below statement, because the hitpart might be assigned only above
                if (hitPart.rb != null)
                    impactSpeed = (GetDragAdjustedVelocity() - (hitPart.rb.velocity + BDKrakensbane.FrameVelocityV3f)).magnitude;
                else
                    impactSpeed = GetDragAdjustedVelocity().magnitude;
                distanceTraveled += hit.distance;
                if (dmgMult < 0)
                {
                    hitPart.AddInstagibDamage();
                }
                else
                {
                    ProjectileUtils.ApplyDamage(hitPart, hit, dmgMult, 1, caliber, bulletMass, impactSpeed, bulletDmgMult, distanceTraveled, HEType != PooledBulletTypes.Slug ? true : false, incendiary, hasRicocheted, sourceVessel, bullet.DisplayName, team, ExplosionSourceType.Bullet, true, true, true);
                }
                ExplosiveDetonation(hitPart, hit, bulletRay);
                ResourceUtils.StealResources(hitPart, sourceVessel, stealResources);
                KillBullet(); // Kerbals are too thick-headed for penetration...
                return true;
            }

            if (hitPart != null && hitPart.vessel == sourceVessel) return false;  //avoid autohit;

            Vector3 impactVelocity = GetDragAdjustedVelocity();
            Vector3 hitPartVelocity = (hitPart != null && hitPart.rb != null) ? hitPart.rb.velocity + BDKrakensbane.FrameVelocityV3f : Vector3.zero;
            impactVelocity -= hitPartVelocity;

            impactSpeed = impactVelocity.magnitude;

            float length = ((bulletMass * 1000.0f * 400.0f) / ((caliber * caliber *
                    Mathf.PI) * (sabot ? 19.0f : 11.34f)) + 1.0f) * 10.0f;

            // New system to wear down hypervelocity projectiles over distance
            // This is based on an equation that was derived for shaped charges. Now this isn't
            // exactly accurate in our case, or rather it's not accurate at all, but it's something
            // in the ballpark and it's also more-or-less going to give the proper behavior for
            // spaced armor at high velocities. This is sourced from https://www.diva-portal.org/smash/get/diva2:643824/FULLTEXT01.pdf
            // and once again, I must emphasize. This is for shaped charges, it's not for post-penetration
            // behavior of hypervelocity projectiles, but I'm going to hand-wave away the difference between
            // the plasma that flies out after a penetration and the armor-piercing jet of a shaped charge.
            if (impactSpeed > 2500 || !double.IsPositiveInfinity(distanceLastHit))
            {
                // This only applies to anything that will actually survive an impact so EMP, impulse and any HE rounds that explode right away are out
                if (!EMP || impulse != 0 || ((HERatio > 0) && (fuzeType != BulletFuzeTypes.Penetrating || fuzeType != BulletFuzeTypes.Delay)))
                {

                    // This is just because if distanceSinceHit < (7f * caliber * 10f) penetration will be worse, this behavior is true of 
                    // shaped charges due to the jet formation distance, however we're going to ignore it since it isn't true of a hypervelocity
                    // projectile that's just smashed through some armor.
                    //if ((distanceTraveled + hit.distance - distanceLastHit) * 1000f > (7f * caliber * 10f))

                    // Changed from the previous 7 * caliber * 10 maximum to just > caliber since that no longer exists.
                    if ((distanceTraveled + hit.distance - distanceLastHit) * 1000f > caliber)
                    {
                        // The formula is based on distance and the caliber of the shaped charge, now since in our case we are talking
                        // about projectiles rather than shaped charges we'll take the projectile diameter and call that the jet size.
                        // Shaped charge jets have a diameter generally around 5% of the shaped charge's caliber, however in our case
                        // this means these projectiles wouldn't bleed off that hard with distance, thus we'll say they're 10% of the
                        // shaped charge's caliber.
                        // Calculating this term once since division is expensive
                        //float kTerm = ((float)(distanceTraveled + hit.distance - distanceLastHit) * 1000f - 7f * 10f *caliber) / (14f * 10f * caliber);
                        // Modified this from the original formula, commented out above to remove the standoff distance required for jet formation.
                        // Just makes more sense to me not to have it in there.
                        float kTerm = ((float)(distanceTraveled + hit.distance - distanceLastHit) * 1000f) / (14f * 10f * caliber);

                        kDist = 1f / (kDist * (1f + kTerm * kTerm)); // Then using it in the formula

                        // If the projectile gets too small things go wonky with the formulas for penetration
                        // they'll still work honestly, but I'd rather avoid those situations
                        /*if ((kDist * length) < 1.2f * caliber)
                        {
                            float massFactor = (1.2f * caliber / length);
                            bulletMass = bulletMass * massFactor;
                            length = (length - 10) * massFactor + 10;
                        }
                        else
                        {
                            bulletMass = bulletMass * kDist;
                            length = (length - 10) * kDist + 10;
                        }*/

                        // Deprecated above since the penetration formula was modified to
                        // deal with such cases
                        bulletMass = bulletMass * kDist;
                        length = (length - 10) * kDist + 10;

                        if (BDArmorySettings.DEBUG_WEAPONS)
                        {
                            Debug.Log("[BDArmory.PooledBullet] kDist: " + kDist + ". Distance Since Last Hit: " + (distanceTraveled + hit.distance - distanceLastHit) + " m.");
                        }
                    }

                    if (double.IsPositiveInfinity(distanceLastHit))
                    {
                        // Add the distance since this hit so the next part that gets hit has the proper distance
                        distanceLastHit = distanceTraveled + hit.distance;
                    }
                }
            }

            float hitAngle = Vector3.Angle(impactVelocity, -hit.normal);
            float dist = hitPart != null && hitPart.vessel != null && rayLength.ContainsKey(hitPart.vessel) ? rayLength[hitPart.vessel] : currentVelocity.magnitude * period;

            if (ProjectileUtils.CheckGroundHit(hitPart, hit, caliber))
            {
                if (!BDArmorySettings.PAINTBALL_MODE) ProjectileUtils.CheckBuildingHit(hit, bulletMass, impactVelocity, bulletDmgMult);
                if (!RicochetScenery(hitAngle))
                {
                    ExplosiveDetonation(hitPart, hit, bulletRay);
                    KillBullet();
                    distanceTraveled += hit.distance;
                    return true;
                }
                else
                {
                    if (fuzeType == BulletFuzeTypes.Impact)
                    {
                        ExplosiveDetonation(hitPart, hit, bulletRay);
                    }
                    DoRicochet(hitPart, hit, hitAngle, hit.distance / dist, period);
                    return true;
                }
            }
            if (hitPart == null) return false; // Hits below here are part hits.

            //Standard Pipeline Hitpoints, Armor and Explosives
            //impactSpeed = impactVelocity.magnitude; //Moved up for the projectile weardown calculation
            if (initialHitDistance == 0) initialHitDistance = distanceTraveled + hit.distance;
            if (massMod != 0)
            {
                var ME = hitPart.FindModuleImplementing<ModuleMassAdjust>();
                if (ME == null)
                {
                    ME = (ModuleMassAdjust)hitPart.AddModule("ModuleMassAdjust");
                }
                ME.massMod += massMod;
                ME.duration += BDArmorySettings.WEAPON_FX_DURATION;
            }
            if (EMP && !VesselModuleRegistry.ignoredVesselTypes.Contains(hitPart.vesselType))
            {
                var emp = hitPart.vessel.rootPart.FindModuleImplementing<ModuleDrainEC>();
                if (emp == null)
                {
                    emp = (ModuleDrainEC)hitPart.vessel.rootPart.AddModule("ModuleDrainEC");
                }
                emp.incomingDamage += (caliber * Mathf.Clamp(bulletMass - tntMass, 0.1f, 101)); //soft EMP caps at 100; can always add a EMP amount value to bulletcfg later, but this should work for now
                emp.softEMP = true;
            }
            if (impulse != 0 && hitPart.rb != null)
            {
                distanceTraveled += hit.distance;
                if (!BDArmorySettings.PAINTBALL_MODE)
                { hitPart.rb.AddForceAtPosition(impactVelocity.normalized * impulse, hit.point, ForceMode.Impulse); }
                // comment this out and allow impulse rounds to do damage?
                ProjectileUtils.ApplyScore(hitPart, sourceVessel.GetName(), distanceTraveled, 0, bullet.DisplayName, ExplosionSourceType.Bullet, true);
                if (BDArmorySettings.BULLET_HITS)
                {
                    BulletHitFX.CreateBulletHit(hitPart, hit.point, hit, hit.normal, false, caliber, 0, team);
                }
                KillBullet();
                return true; //impulse rounds shouldn't penetrate/do damage
                //
            }
            float anglemultiplier = (float)Math.Cos(Math.PI * hitAngle / 180.0);
            //calculate armor thickness
            float thickness = ProjectileUtils.CalculateThickness(hitPart, anglemultiplier);
            //calculate armor strength
            float penetration = 0;
            float penetrationFactor = 0;
            int armorType = 1;
            //float length = 0; //Moved up for the new bullet wear over distance system
            var Armor = hitPart.FindModuleImplementing<HitpointTracker>();
            if (Armor != null)
            {
                float Ductility = Armor.Ductility;
                float hardness = Armor.Hardness;
                float Strength = Armor.Strength;
                float safeTemp = Armor.SafeUseTemp;
                float Density = Armor.Density;

                float vFactor = Armor.vFactor;
                float muParam1;
                float muParam2;
                float muParam3;

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

                armorType = (int)Armor.ArmorTypeNum;
                if (BDArmorySettings.DEBUG_ARMOR)
                {
                    Debug.Log($"[BDArmory.PooledBullet]: ArmorVars found: Strength : {Strength}; Ductility: {Ductility}; Hardness: {hardness}; MaxTemp: {safeTemp}; Density: {Density}; thickness: {thickness}; hit angle: {hitAngle}");
                }

                //calculate bullet deformation
                float newCaliber = caliber;
                //length = ((bulletMass * 1000.0f * 400.0f) / ((caliber * caliber *
                //    Mathf.PI) * (sabot ? 19.0f : 11.34f)) + 1.0f) * 10.0f; //Moved up for the purposes of the new bullet wear over distance system

                /*
                if (Ductility > 0.05)
                {
                */

                if (!sabot)
                {
                    // Moved the bulletEnergy and armorStrength calculations here because
                    // they are no longer needed for CalculatePenetration. This should
                    // improve performance somewhat for sabot rounds, which is a good
                    // thing since that new model requires the use of Mathf.Log and
                    // Mathf.Exp.
                    float bulletEnergy = ProjectileUtils.CalculateProjectileEnergy(bulletMass, impactSpeed);
                    float armorStrength = ProjectileUtils.CalculateArmorStrength(caliber, thickness, Ductility, Strength, Density, safeTemp, hitPart);
                    newCaliber = ProjectileUtils.CalculateDeformation(armorStrength, bulletEnergy, caliber, impactSpeed, hardness, Density, HERatio, apBulletMod, sabot);

                    // Also set the params to the non-sabot ones
                    muParam1 = Armor.muParam1;
                    muParam2 = Armor.muParam2;
                    muParam3 = Armor.muParam3;
                }
                else
                {
                    // If it's a sabot just set the params to the sabot ones
                    muParam1 = Armor.muParam1S;
                    muParam2 = Armor.muParam2S;
                    muParam3 = Armor.muParam3S;
                }
                //penetration = ProjectileUtils.CalculatePenetration(caliber, newCaliber, bulletMass, impactSpeed, Ductility, Density, Strength, thickness, apBulletMod, sabot);
                penetration = ProjectileUtils.CalculatePenetration(caliber, impactSpeed, bulletMass, apBulletMod, Strength, vFactor, muParam1, muParam2, muParam3, sabot, length);

                if (BDArmorySettings.DEBUG_WEAPONS)
                {
                    Debug.Log("[BDArmory.PooledBullet] Penetration: " + penetration + "mm. impactSpeed: " + impactSpeed + "m/s. bulletMass = " + bulletMass + "kg. Caliber: " + caliber + "mm. Length: " + length + "mm. Sabot: " + sabot);
                }

                /*
                }
                else
                {
                    float bulletEnergy = ProjectileUtils.CalculateProjectileEnergy(bulletMass, impactSpeed);
                    float armorStrength = ProjectileUtils.CalculateArmorStrength(caliber, thickness, Ductility, Strength, Density, safeTemp, hitPart);
                    newCaliber = ProjectileUtils.CalculateDeformation(armorStrength, bulletEnergy, caliber, impactSpeed, hardness, Density, HERatio, apBulletMod, sabot);
                    penetration = ProjectileUtils.CalculateCeramicPenetration(caliber, newCaliber, bulletMass, impactSpeed, Ductility, Density, Strength, thickness, apBulletMod, sabot);
                }
                */

                caliber = newCaliber; //update bullet with new caliber post-deformation(if any)
                penetrationFactor = ProjectileUtils.CalculateArmorPenetration(hitPart, penetration, thickness);
                //Reactive Armor calcs
                //Round has managed to punch through front plate of RA, triggering RA
                //if NXRA, will activate on anything that can pen front plate

                var RA = hitPart.FindModuleImplementing<ModuleReactiveArmor>();
                if (RA != null)
                {
                    if (penetrationFactor > 1)
                    {
                        float thicknessModifier = RA.armorModifier;
                        if (BDArmorySettings.DEBUG_ARMOR) Debug.Log("[BDArmory.PooledBullet]: Beginning Reactive Armor Hit; NXRA: " + RA.NXRA + "; thickness Mod: " + RA.armorModifier);
                        if (RA.NXRA) //non-explosive RA, always active
                        {
                            thickness *= thicknessModifier;
                        }
                        else
                        {
                            if (sabot)
                            {
                                if (hitAngle < 80) //ERA isn't going to do much against near-perpendicular hits
                                {
                                    caliber = BDAMath.Sqrt((caliber * (((bulletMass * 1000) / ((caliber * caliber * Mathf.PI / 400) * 19)) + 1) * 4) / Mathf.PI); //increase caliber to sim sabot hitting perpendicualr instead of point-first
                                    bulletMass /= 2; //sunder sabot
                                                     //RA isn't going to stop sabot, but underlying part's armor will (probably)
                                    if (BDArmorySettings.DEBUG_ARMOR) Debug.Log("[BDArmory.PooledBullet]: Sabot caliber and mass now: " + caliber + ", " + bulletMass);
                                    RA.UpdateSectionScales();
                                }
                            }
                            else //standard rounds
                            {
                                if (caliber >= RA.sensitivity) //big enough round to trigger RA
                                {
                                    thickness *= thicknessModifier;
                                    if (fuzeType == BulletFuzeTypes.Delay || fuzeType == BulletFuzeTypes.Penetrating || fuzeType == BulletFuzeTypes.None) //non-explosive impact
                                    {
                                        RA.UpdateSectionScales(); //detonate RA section
                                                                  //explosive impacts handled in ExplosionFX
                                                                  //if explosive and contact fuze, kill bullet?
                                    }
                                }
                            }
                        }
                    }
                    penetrationFactor = ProjectileUtils.CalculateArmorPenetration(hitPart, penetration, thickness); //RA stop round?
                }
                else ProjectileUtils.CalculateArmorDamage(hitPart, penetrationFactor, caliber, hardness, Ductility, Density, impactSpeed, sourceVesselName, ExplosionSourceType.Bullet, armorType);
            }
            else
            {
                Debug.Log("[PooledBUllet].ArmorVars not found; hitPart null");
            }
            //determine what happens to bullet
            //pen < 1: bullet stopped by armor
            //pen > 1 && <2: bullet makes it into part, but can't punch through other side
            //pen > 2: bullet goes stragiht through part and out other side
            if (penetrationFactor < 1) //stopped by armor
            {
                if (RicochetOnPart(hitPart, hit, hitAngle, impactSpeed, hit.distance / dist, period))
                {
                    bool viableBullet = ProjectileUtils.CalculateBulletStatus(bulletMass, caliber, sabot);
                    if (!viableBullet)
                    {
                        distanceTraveled += hit.distance;
                        KillBullet();
                        return true;
                    }
                    else
                    {
                        //rounds w/ contact fuzes are going to detoante anyway
                        if (fuzeType == BulletFuzeTypes.Impact || fuzeType == BulletFuzeTypes.Timed)
                        {
                            ExplosiveDetonation(hitPart, hit, bulletRay);
                            ProjectileUtils.CalculateShrapnelDamage(hitPart, hit, caliber, tntMass, 0, sourceVesselName, ExplosionSourceType.Bullet, bulletMass, penetrationFactor); //calc daamge from bullet exploding 
                        }
                        if (fuzeType == BulletFuzeTypes.Delay)
                        {
                            fuzeTriggered = true;
                        }
                    }
                }
                if (!hasRicocheted) // explosive bullets that get stopped by armor will explode
                {
                    if (hitPart.rb != null && hitPart.rb.mass > 0)
                    {
                        float forceAverageMagnitude = impactSpeed * impactSpeed * (1f / hit.distance) * bulletMass;

                        float accelerationMagnitude = forceAverageMagnitude / (hitPart.vessel.GetTotalMass() * 1000);

                        hitPart.rb.AddForceAtPosition(impactVelocity.normalized * accelerationMagnitude, hit.point, ForceMode.Acceleration);

                        if (BDArmorySettings.DEBUG_WEAPONS)
                            Debug.Log("[BDArmory.PooledBullet]: Force Applied " + Math.Round(accelerationMagnitude, 2) + "| Vessel mass in kgs=" + hitPart.vessel.GetTotalMass() * 1000 + "| bullet effective mass =" + (bulletMass - tntMass));
                    }
                    distanceTraveled += hit.distance;
                    hasPenetrated = false;
                    if (dmgMult < 0)
                    {
                        hitPart.AddInstagibDamage();
                    }
                    if (fuzeTriggered)
                    {
                        //Debug.Log("[BDArmory.PooledBullet]: Active Delay Fuze failed to penetrate, detonating");
                        fuzeTriggered = false;
                        StopCoroutine(DelayedDetonationRoutine());
                    }
                    ExplosiveDetonation(hitPart, hit, bulletRay);
                    ProjectileUtils.CalculateShrapnelDamage(hitPart, hit, caliber, tntMass, 0, sourceVesselName, ExplosionSourceType.Bullet, bulletMass, penetrationFactor); //calc damage from bullet exploding 
                    ProjectileUtils.ApplyScore(hitPart, sourceVesselName, distanceTraveled, 0, bullet.DisplayName, ExplosionSourceType.Bullet, penTicker > 0 ? false : true);
                    hasDetonated = true;
                    KillBullet();
                    return true;
                }
            }
            else //penetration >= 1
            {
                // Old Post Pen Behavior
                //currentVelocity = currentVelocity * (1 - (float)Math.Sqrt(thickness / penetration));
                //impactVelocity = impactVelocity * (1 - (float)Math.Sqrt(thickness / penetration));

                // New Post Pen Behavior, this is quite game-ified and not really based heavily on
                // actual proper equations, however it does try to get the same kind of behavior as
                // would be found IRL. Primarily, this means high velocity impacts will be mostly
                // eroding the projectile rather than slowing it down (all studies of this behavior
                // show residual velocity only decreases slightly during penetration while the
                // projectile is getting eroded, then starts decreasing rapidly as the projectile
                // approaches a L/D ratio of 1. Erosion is drastically increased at 2500 m/s + in
                // order to try and replicate the projectile basically vaporizing at high velocities.
                // Note that the velocity thresholds are really mostly arbitrary and that the vaporizing
                // behavior isn't that accurate since the projectile would remain semi-coherent immediately
                // after penetration and would disperse over time, hence the spacing in stuff like
                // whipple shields but that behavior is fairly complex and I'm already in way over my head.

                // Calculating this ratio once since we're going to need it a bunch
                float adjustedPenRatio = (1 - BDAMath.Sqrt(thickness / penetration));
                float oldBulletMass = bulletMass;
                // If impact is at high speed
                if (impactSpeed > 1200f)
                {
                    // If the projectile is still above a L/D ratio of 1.1 (should be 1 but I want to
                    // avoid the edge case in the pen formula where L/D = 1)
                    if (length / caliber > 1.1f)
                    {
                        // Then we set the mass ratio to the default for impacts under 2500 m/s
                        // we take off 5% by default to decrease penetration efficiency through
                        // multiple plates a little more
                        float massRatio = 0.975f * adjustedPenRatio;

                        if (impactSpeed > 2500f)
                        {
                            // If impact speed is really high then spaced armor wil work exceptionally
                            // well, with increasing results as the velocity of the projectile goes
                            // higher. This is mostly to make whipple shields viable and desireable
                            // in railgun combat. Ideally we'd be modelling the projectile vaporizing
                            // and then losing coherence as it travels through empty space between the
                            // outer plate and the inner plate but I'm not quite sure how that behavior
                            // would look like. Best way to probably do that is to decrease projectile
                            // lifespan and to add a lastImpact timestamp and do some kind of decrease
                            // in mass as a function of the time between impacts.
                            //massRatio = 2375f / impactSpeed * adjustedPenRatio;

                            // Adjusted the above formula so only up to 50% of the mass could be lost
                            // immediately upon penetration (to being vaporized). At this point this
                            // stuff I've accepted is going to be purely gameified. If anybody has
                            // an expertise in hypervelocity impact mechanics they're welcome to
                            // change all this stuff I'm doing for hypervelocity stuff.
                            massRatio = (0.45f + 0.5f * (2500f / impactSpeed)) * adjustedPenRatio;
                        }

                        // We cap the minimum L/D to be 1.2 to avoid that edge case in the pen formula
                        if ((massRatio * (length - 10f) + 10f) < (1.1f * caliber))
                        {
                            float ratio;

                            if (caliber < 10f || length < 10.05f)
                            {
                                ratio = 1.1f * caliber / length;
                            }
                            else
                            {
                                ratio = (1.1f * caliber - 10f) / (length - 10f);
                            }

                            if (ratio > 1)
                            {
                                Debug.LogError($"DEBUG Bullet Ratio: {ratio} is greater than 1! Length: {length} Caliber: {caliber}.");
                                ratio = 1;
                            }

                            bulletMass *= ratio;

                            adjustedPenRatio /= ratio;

                            // In the case we are reaching that cap we decrease the velocity by
                            // the adjustedPenRatio minus the portion that went into erosion
                            //impactVelocity = impactVelocity * adjustedPenRatio;
                            //currentVelocity = hitPartVelocity + impactVelocity;
                        }
                        else
                        {
                            bulletMass = bulletMass * massRatio;

                            // If we don't, I.E. the round isn't completely eroded, we decrease
                            // the velocity by a max of 5%, proportional to the adjustedPenRatio
                            //impactVelocity = impactVelocity * (0.95f + 0.05f * adjustedPenRatio);
                            adjustedPenRatio *= 0.05f;
                            adjustedPenRatio += 0.95f;
                            //impactVelocity = impactVelocity * adjustedPenRatio;
                            //currentVelocity = hitPartVelocity + impactVelocity;
                        }
                        ExplosionFx.CreateExplosion(currentPosition, oldBulletMass - bulletMass, "BDArmory/Models/explosion/30mmExplosion", explSoundPath, ExplosionSourceType.Bullet, caliber, null, sourceVesselName, null, null, currentVelocity, 70, false, bulletMass, -1, dmgMult, ExplosionFx.WarheadTypes.Standard, null, 1f, -1, currentVelocity); //explosion simming ablated material flashing into plasma, HE amount = bullet mass lost on hit
                    }
                    else
                    {
                        // If the projectile has already been eroded away we just decrease the
                        // velocity by the adjustedPenRatio
                        //impactVelocity = impactVelocity * adjustedPenRatio;
                        //currentVelocity = hitPartVelocity + impactVelocity;
                    }
                }
                else
                {
                    // Low velocity impacts behave the same as before
                    //impactVelocity = impactVelocity * adjustedPenRatio;
                    //currentVelocity = hitPartVelocity + impactVelocity;
                }


                ballisticCoefficient = calcBulletBallisticCoefficient(caliber, bulletMass); // if bullet not killed by impact,
                // possbily deformed from impact; grab new ballistic coeff for drag

                //fully penetrated continue ballistic damage
                hasPenetrated = true;
                bool viableBullet = ProjectileUtils.CalculateBulletStatus(bulletMass, caliber, sabot);

                ResourceUtils.StealResources(hitPart, sourceVessel, stealResources);
                //ProjectileUtils.CheckPartForExplosion(hitPart);

                if (dmgMult < 0)
                {
                    hitPart.AddInstagibDamage();
                    ProjectileUtils.ApplyScore(hitPart, sourceVessel.GetName(), distanceTraveled, 0, bullet.DisplayName, ExplosionSourceType.Bullet, true);
                }
                else
                {
                    float cockpitPen = (float)(16f * impactVelocity.magnitude * BDAMath.Sqrt(bulletMass / 1000) / BDAMath.Sqrt(caliber) * apBulletMod); //assuming a 20mm steel armor plate for cockpit armor
                    ProjectileUtils.ApplyDamage(hitPart, hit, dmgMult, penetrationFactor, caliber, bulletMass, (impactVelocity * (armorType == 1 ? 1 : adjustedPenRatio)).magnitude, viableBullet ? bulletDmgMult : bulletDmgMult / 2, distanceTraveled, HEType != PooledBulletTypes.Slug ? true : false, incendiary, hasRicocheted, sourceVessel, bullet.name, team, ExplosionSourceType.Bullet, penTicker > 0 ? false : true, partsHit.Contains(hitPart) ? false : true, (cockpitPen > Mathf.Max(20 / anglemultiplier, 1)) ? true : false);
                    //need to add a check for if the bullet has already struck the part, since it doesn't make sense for some battledamage to apply on the second hit from the bullet exiting the part - wings/ctrl srfs, pilot kills, subsystem damage
                }

                impactVelocity = impactVelocity * adjustedPenRatio; //moving this here so unarmored parts take proper damage from the full impact speed and energy delivery of the round, vs everything else properly recieving reduced damage from a round that has to punch through armor first
                currentVelocity = hitPartVelocity + impactVelocity;

                currentSpeed = currentVelocity.magnitude;
                timeElapsedSinceCurrentSpeedWasAdjusted = 0;

                //Delay and Penetrating Fuze bullets that penetrate should explode shortly after
                //if penetration is very great, they will have moved on                            
                //if (explosive && penetrationFactor < 3 || !viableBullet)
                if (HEType != PooledBulletTypes.Slug)
                {
                    if (fuzeType == BulletFuzeTypes.Delay)
                    {
                        //currentPosition += (currentVelocity * period) / 3; //when using post-penetration currentVelocity, this yields distances pretty close to distance a Delay fuze would travel before detonation
                        //commented out, since this could cause explosions to phase through armor/parts between hit point and detonation point
                        //distanceTraveled += hit.distance;
                        if (!fuzeTriggered)
                        {
                            if (BDArmorySettings.DEBUG_WEAPONS) Debug.Log("[BDArmory.PooledBullet]: Delay Fuze Tripped at t: " + Time.time);
                            fuzeTriggered = true;
                            StartCoroutine(DelayedDetonationRoutine());
                        }
                    }
                    else if (fuzeType == BulletFuzeTypes.Penetrating) //should look into having this be a set depth. For now, assume fancy inertial/electrical mechanism for detecting armor thickness based on time spent passing through
                    {
                        if (penetrationFactor < 1.5f)
                        {
                            if (!fuzeTriggered)
                            {
                                if (BDArmorySettings.DEBUG_WEAPONS) Debug.Log("[BDArmory.PooledBullet]: Penetrating Fuze Tripped at t: " + Time.time);
                                fuzeTriggered = true;
                                StartCoroutine(DelayedDetonationRoutine());
                            }
                        }
                    }
                    else //impact by impact, Timed, Prox and Flak, if for whatever reason those last two have 0 proxi range
                    {
                        //if (BDArmorySettings.DEBUG_WEAPONS) Debug.Log("[BDArmory.PooledBullet]: impact Fuze detonation");
                        var calculateShrapnel = !ProjectileUtils.IsArmorPart(CurrentPart); //HE round that's punched through an armor panel would be exploding on the wrong side of it for shrapnel damage to be relevant. ExplosiveDetonation disables the bullet setting CurrentPart to null
                        ExplosiveDetonation(hitPart, hit, bulletRay, true);
                        if (calculateShrapnel)
                            ProjectileUtils.CalculateShrapnelDamage(hitPart, hit, caliber, tntMass, 0, sourceVesselName, ExplosionSourceType.Bullet, bulletMass, penetrationFactor); //calc daamge from bullet exploding 
                        hasDetonated = true;
                        KillBullet();
                        distanceTraveled += hit.distance;
                        return true;
                    }
                    if (!viableBullet)
                    {
                        if (BDArmorySettings.DEBUG_WEAPONS) Debug.Log("[BDArmory.PooledBullet]: !viable bullet, removing");
                        var calculateShrapnel = !ProjectileUtils.IsArmorPart(CurrentPart); //HE round that's punched through an armor panel would be exploding on the wrong side of it for shrapnel damage to be relevant. ExplosiveDetonation disables the bullet setting CurrentPart to null
                        ExplosiveDetonation(hitPart, hit, bulletRay, true);
                        if (calculateShrapnel)
                            ProjectileUtils.CalculateShrapnelDamage(hitPart, hit, caliber, tntMass, 0, sourceVesselName, ExplosionSourceType.Bullet, bulletMass, penetrationFactor); //calc daamge from bullet exploding
                        hasDetonated = true;
                        KillBullet();
                        distanceTraveled += hit.distance;
                        return true;
                    }
                }
                penTicker += 1;
            }
            if (!partsHit.Contains(hitPart)) partsHit.Add(hitPart);
            //bullet should not go any further if moving too slowly after hit
            //smaller caliber rounds would be too deformed to do any further damage
            if (hasPenetrated && (currentVelocity - hitPartVelocity).sqrMagnitude < 1e4f)
            {
                if (BDArmorySettings.DEBUG_WEAPONS)
                {
                    Debug.Log("[BDArmory.PooledBullet]: Bullet Velocity too low, stopping");
                }
                if (!fuzeTriggered) KillBullet();
                distanceTraveled += hit.distance;
                return true;
            }
            return false;
        }

        IEnumerator DelayedDetonationRoutine()
        {
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            fuzeTriggered = false;
            if (!hasDetonated)
            {
                if (HEType != PooledBulletTypes.Slug)
                    ExplosionFx.CreateExplosion(currentPosition, tntMass, explModelPath, explSoundPath, ExplosionSourceType.Bullet, caliber, null, sourceVesselName, null, null, HEType == PooledBulletTypes.Explosive ? default : currentVelocity, -1, false, bulletMass, -1, dmgMult, HEType == PooledBulletTypes.Shaped ? ExplosionFx.WarheadTypes.ShapedCharge : ExplosionFx.WarheadTypes.Standard, CurrentPart, HEType == PooledBulletTypes.Shaped ? apBulletMod : 1f, ProjectileUtils.isReportingWeapon(sourceWeapon) ? (float)DistanceTraveled : -1);
                if (nuclear)
                    NukeFX.CreateExplosion(currentPosition, ExplosionSourceType.Bullet, sourceVesselName, bullet.DisplayName, 0, tntMass * 200, tntMass, tntMass, EMP, blastSoundPath, flashModelPath, shockModelPath, blastModelPath, plumeModelPath, debrisModelPath, "", "", hitPart: CurrentPart);
                hasDetonated = true;

                if (tntMass > 1)
                {
                    if ((FlightGlobals.getAltitudeAtPos(currentPosition) <= 0) && (FlightGlobals.getAltitudeAtPos(currentPosition) > -detonationRange))
                    {
                        double latitudeAtPos = FlightGlobals.currentMainBody.GetLatitude(currentPosition);
                        double longitudeAtPos = FlightGlobals.currentMainBody.GetLongitude(currentPosition);
                        if (BDArmorySettings.waterHitEffect) FXMonger.Splash(FlightGlobals.currentMainBody.GetWorldSurfacePosition(latitudeAtPos, longitudeAtPos, 0), tntMass * 20);
                    }
                }
                if (BDArmorySettings.DEBUG_WEAPONS) Debug.Log("[BDArmory.PooledBullet]: Delayed Detonation at: " + Time.time);
                KillBullet();
            }
        }

        public struct SourceInfo
        {
            public Vessel vessel;
            public string team;
            public Part weapon;
            public Vector3 position;

            public SourceInfo(Vessel vesselIn, string teamIn, Part weaponIn, Vector3 positionIn)
            {
                vessel = vesselIn;
                team = teamIn;
                weapon = weaponIn;
                position = positionIn;
            }
        }

        public struct NukeInfo
        {
            public string flashModelPath;
            public string shockModelPath;
            public string blastModelPath;
            public string plumeModelPath;
            public string debrisModelPath;
            public string blastSoundPath;

            public NukeInfo()
            {
                flashModelPath = BDModuleNuke.defaultflashModelPath;
                shockModelPath = BDModuleNuke.defaultShockModelPath;
                blastModelPath = BDModuleNuke.defaultBlastModelPath;
                plumeModelPath = BDModuleNuke.defaultPlumeModelPath;
                debrisModelPath = BDModuleNuke.defaultDebrisModelPath;
                blastSoundPath = BDModuleNuke.defaultBlastSoundPath;
            }

            public NukeInfo(string flashModelPathIn, string shockModelPathIn, string blastModelPathIn,
                string plumeModelPathIn, string debrisModelPathIn, string blastSoundPathIn)
            {
                flashModelPath = flashModelPathIn;
                shockModelPath = shockModelPathIn;
                blastModelPath = blastModelPathIn;
                plumeModelPath = plumeModelPathIn;
                debrisModelPath = debrisModelPathIn;
                blastSoundPath = blastSoundPathIn;
            }
        }

        public struct GraphicsInfo
        {
            public string bulletTexturePath;
            public Color projectileColor;
            public Color startColor;
            public float tracerStartWidth;
            public float tracerEndWidth;
            public float tracerLength;
            public float tracerLuminance;
            public float tracerDeltaFactor;
            public string smokeTexturePath;
            public string explModelPath;
            public string explSoundPath;

            public GraphicsInfo(string bulletTexturePathIn, Color projectileColorIn, Color startColorIn,
                float tracerStartWidthIn, float tracerEndWidthIn, float tracerLengthIn, float tracerLuminescanceIn,
                float tracerDeltaFactorIn, string smokeTexturePathIn, string explModelPathIn, string explSoundPathIn)
            {
                bulletTexturePath = bulletTexturePathIn;
                projectileColor = projectileColorIn;
                startColor = startColorIn;
                tracerStartWidth = tracerStartWidthIn;
                tracerEndWidth = tracerEndWidthIn;
                tracerLength = tracerLengthIn;
                tracerLuminance = tracerLuminescanceIn;
                tracerDeltaFactor = tracerDeltaFactorIn;
                smokeTexturePath = smokeTexturePathIn;
                explModelPath = explModelPathIn;
                explSoundPath = explSoundPathIn;
        }
        }

        public static bool isSabot(float bulletMass, float caliber)
        {
            return ((((bulletMass * 1000f) / ((caliber * caliber * Mathf.PI / 400f) * 19f) + 1f) * 10f) > caliber * 4f);
        }

        public static float calcBulletBallisticCoefficient(float caliber, float bulletMass)
        {
            float bulletDragArea = Mathf.PI * (caliber * caliber / 4f); //if bullet not killed by impact, possbily deformed from impact; grab new ballistic coeff for drag
            return bulletMass / ((bulletDragArea / 1000000f) * 0.295f); // mm^2 to m^2
        }

        public static void FireBullet(BulletInfo bulletType, int projectileCount,
            SourceInfo sourceInfo, GraphicsInfo graphicsInfo, NukeInfo nukeInfo,
            Vector3[] firedVelocity, bool drop, float TTL, float timestep, float detRange, float detTime,
            bool isSubP = false, bool isAPSP = false,
            PooledRocket targetRocket = null, PooledBullet targetShell = null,
            bool steal = false, float damageMult = 1f,
            bool addSourcePartVel = true, float additionalVel = 0f,
            Vessel tgtVessel = null, float guidance = 0f, bool registerShot = false)
        {
            if (ModuleWeapon.bulletPool == null)
            {
                GameObject templateBullet = new GameObject("Bullet");
                templateBullet.AddComponent<PooledBullet>();
                templateBullet.SetActive(false);
                ModuleWeapon.bulletPool = ObjectPool.CreateObjectPool(templateBullet, 100, true, true);
            }

            for (int i = 0; i < projectileCount; i++)
            {
                GameObject firedBullet = ModuleWeapon.bulletPool.GetPooledObject();
                PooledBullet pBullet = firedBullet.GetComponent<PooledBullet>();

                pBullet.currentPosition = sourceInfo.position;

                pBullet.caliber = bulletType.caliber;
                pBullet.bulletVelocity = bulletType.bulletVelocity + additionalVel;
                pBullet.bulletMass = bulletType.bulletMass;
                pBullet.incendiary = bulletType.incendiary;
                pBullet.apBulletMod = bulletType.apBulletMod;
                pBullet.bulletDmgMult = damageMult;

                pBullet.ballisticCoefficient = bulletType.bulletBallisticCoefficient;

                pBullet.timeElapsedSinceCurrentSpeedWasAdjusted = timestep;
                // measure bullet lifetime in time rather than in distance, because distances get very relative in orbit
                pBullet.timeToLiveUntil = Time.time + TTL;

                pBullet.currentVelocity = firedVelocity[i]; 
                if (addSourcePartVel)
                {
                    pBullet.currentVelocity += BDKrakensbane.FrameVelocityV3f + sourceInfo.weapon.rb.velocity; // use the real vessel velocity, w/o offloading
                }

                pBullet.sourceWeapon = sourceInfo.weapon;
                pBullet.sourceVessel = sourceInfo.vessel;
                pBullet.team = sourceInfo.team;
                pBullet.bulletTexturePath = graphicsInfo.bulletTexturePath;
                pBullet.projectileColor = graphicsInfo.projectileColor;
                pBullet.startColor = graphicsInfo.startColor;
                pBullet.fadeColor = bulletType.fadeColor;
                pBullet.tracerStartWidth = graphicsInfo.tracerStartWidth;
                pBullet.tracerEndWidth = graphicsInfo.tracerEndWidth;
                pBullet.tracerLength = graphicsInfo.tracerLength;
                pBullet.tracerLuminance = graphicsInfo.tracerLuminance;
                pBullet.tracerDeltaFactor = graphicsInfo.tracerDeltaFactor;
                if (!string.IsNullOrEmpty(graphicsInfo.smokeTexturePath)) pBullet.smokeTexturePath = graphicsInfo.smokeTexturePath;
                pBullet.bulletDrop = drop;

                if (bulletType.tntMass > 0)
                {
                    pBullet.HEType = bulletType.eHEType;
                }
                else
                {
                    pBullet.HEType = PooledBulletTypes.Slug;
                }

                if (bulletType.tntMass > 0 || (!isSubP && bulletType.beehive))
                {
                    pBullet.tntMass = bulletType.tntMass;
                    pBullet.explModelPath = graphicsInfo.explModelPath;
                    pBullet.explSoundPath = graphicsInfo.explSoundPath;
                    pBullet.detonationRange = detRange;
                    pBullet.timeToDetonation = detTime;
                    pBullet.fuzeType = bulletType.eFuzeType;
                }
                else
                {
                    pBullet.fuzeType = BulletFuzeTypes.None;
                    pBullet.sabot = bulletType.sabot;
                }

                pBullet.EMP = bulletType.EMP;
                pBullet.nuclear = bulletType.nuclear;
                if (pBullet.nuclear) // Inherit the parent shell's nuke models.
                {
                    pBullet.flashModelPath = nukeInfo.flashModelPath;
                    pBullet.shockModelPath = nukeInfo.shockModelPath;
                    pBullet.blastModelPath = nukeInfo.blastModelPath;
                    pBullet.plumeModelPath = nukeInfo.plumeModelPath;
                    pBullet.debrisModelPath = nukeInfo.debrisModelPath;
                    pBullet.blastSoundPath = nukeInfo.blastSoundPath;
                }
                
                // No sub-sub projectiles!
                pBullet.beehive = bulletType.beehive && !isSubP;
                if (bulletType.beehive && !isSubP)
                {
                    pBullet.subMunitionType = bulletType.subMunitionType;
                }

                pBullet.impulse = bulletType.impulse;
                pBullet.massMod = bulletType.massMod;

                //pBullet.homing = BulletInfo.homing;
                pBullet.dragType = bulletType.bulletDragType;
                
                pBullet.tgtShell = targetShell;
                pBullet.tgtRocket = targetRocket;

                pBullet.bullet = bulletType;
                pBullet.stealResources = steal;
                pBullet.dmgMult = damageMult;
                pBullet.targetVessel = tgtVessel;
                pBullet.guidanceDPS = guidance;
                pBullet.isSubProjectile = isSubP;
                pBullet.isAPSprojectile = isAPSP;
                pBullet.gameObject.SetActive(true);

                if (registerShot)
                    BDACompetitionMode.Instance.Scores.RegisterShot(sourceInfo.vessel.GetName());

                if (!addSourcePartVel)
                {
                    pBullet.SetTracerPosition();
                    if (!pBullet.CheckBulletCollisions(timestep)) continue; // Bullet immediately hit something and died.
                    if (!pBullet.hasRicocheted) pBullet.MoveBullet(timestep); // Move the bullet the remaining part of the frame.
                    pBullet.currentPosition += (TimeWarp.fixedDeltaTime - timestep) * BDKrakensbane.FrameVelocityV3f; // Re-adjust for Krakensbane.
                    pBullet.timeAlive = timestep;
                }
                else if (!pBullet.CheckBulletCollisions(timestep)) // Check that the bullet won't immediately hit anything and die.
                {
                    // The following gets bullet tracers to line up properly when at orbital velocities.
                    // It should be consistent with how it's done in Aim().
                    // Technically, there could be a small gap between the collision check and the start position, but this should be insignificant.
                    if (!pBullet.hasRicocheted) // Movement is handled internally for ricochets.
                    {
                        Vector3 gravity = drop ? (Vector3)FlightGlobals.getGeeForceAtPosition(pBullet.currentPosition) : Vector3.zero;
                        pBullet.currentPosition = AIUtils.PredictPosition(pBullet.currentPosition, firedVelocity[i], gravity, timestep);
                        pBullet.currentVelocity += timestep * gravity; // Adjusting the velocity here mostly eliminates bullet deviation due to iTime.
                        pBullet.DistanceTraveled += timestep * pBullet.currentVelocity.magnitude; // Adjust the distance traveled to account for iTime.
                    }
                    if (!BDKrakensbane.IsActive) pBullet.currentPosition += TimeWarp.fixedDeltaTime * sourceInfo.weapon.rb.velocity; // If Krakensbane isn't active, bullets get an additional shift by this amount.
                    pBullet.timeAlive = timestep;
                    pBullet.SetTracerPosition();
                    pBullet.currentPosition += TimeWarp.fixedDeltaTime * (sourceInfo.weapon.rb.velocity + BDKrakensbane.FrameVelocityV3f); // Account for velocity off-loading after visuals are done.
                }
            }
        }

        public void BeehiveDetonation()
        {
            if (subMunitionType == null)
            {
                Debug.Log("[BDArmory.PooledBullet] Beehive round not configured with subMunitionType!");
                return;
            }
            string[] subMunitionData = subMunitionType.Split(new char[] { ';' });
            string projType = subMunitionData[0];
            if (subMunitionData.Length < 2 || !int.TryParse(subMunitionData[1], out int count)) count = 1;
            if (BulletInfo.bulletNames.Contains(projType))
            {
                BulletInfo sBullet = BulletInfo.bullets[projType];
 
                float incrementVelocity = 1000 / (bulletVelocity + sBullet.bulletVelocity); //using 1km/s as a reference Unit
                float dispersionAngle = sBullet.subProjectileDispersion > 0 ? sBullet.subProjectileDispersion : BDAMath.Sqrt(count) / 2; //fewer fragments/pellets are going to be larger-> move slower, less dispersion
                float dispersionVelocityforAngle = 1000 / incrementVelocity * Mathf.Sin(dispersionAngle * Mathf.Deg2Rad); // convert m/s despersion to angle, accounting for vel of round

                float subDetonationRange = 0;
                if (sBullet.tntMass > 0)
                {
                    subDetonationRange = sBullet.nuclear ? Mathf.Pow(sBullet.tntMass, 0.33333f) * 10 * (10 * atmosphereDensity) : BlastPhysicsUtils.CalculateBlastRange(sBullet.tntMass) * 0.666f;
                }

                SourceInfo sourceInfo = new SourceInfo(sourceVessel, team, sourceWeapon, currentPosition);
                GraphicsInfo graphicsInfo = new GraphicsInfo(bulletTexturePath, GUIUtils.ParseColor255(sBullet.projectileColor), GUIUtils.ParseColor255(sBullet.startColor),
                    sBullet.caliber / 300, sBullet.caliber / 750, tracerLength, tracerLuminance, tracerDeltaFactor, "",
                    sBullet.tntMass > 0.5f ? explModelPath : "BDArmory/Models/explosion/30mmExplosion", explSoundPath);
                NukeInfo nukeInfo = sBullet.nuclear ? new NukeInfo(flashModelPath, shockModelPath, blastModelPath,
                    plumeModelPath, debrisModelPath, blastSoundPath) : new NukeInfo();
                Vector3[] firedVelocities = new Vector3[count * sBullet.projectileCount];
                for (int s = 0; s < count * sBullet.projectileCount; s++)
                {
                    firedVelocities[s] = currentVelocity + UnityEngine.Random.onUnitSphere * dispersionVelocityforAngle;
                }

                float subTTL = Mathf.Max(sBullet.projectileTTL, detonationRange / sBullet.bulletVelocity * 1.1f);
                float subDetonationTime = sBullet.eFuzeType switch
                        {
                            BulletFuzeTypes.Timed => detonationRange / sBullet.bulletVelocity, //because beehive TimedFuze for the parent shell is timeToDetonation - detonationRange / bulletVelocity
                            BulletFuzeTypes.Flak => detonationRange / sBullet.bulletVelocity + Time.fixedDeltaTime, // Detonate at expected impact time for flak (plus 1 frame to allow proximity detection).
                            _ => subTTL // Otherwise detonate at the TTL.
                        };

                FireBullet(sBullet, count * sBullet.projectileCount, sourceInfo, graphicsInfo, nukeInfo,
                        firedVelocities, bulletDrop, subTTL, iTime, subDetonationRange, subDetonationTime,
                        true, isAPSprojectile, tgtRocket, tgtShell, stealResources, dmgMult,
                        false, GetDragAdjustedVelocity().magnitude);
            }
        }
        /// <summary>
        /// Proximity detection prior to and after moving
        /// The proximity check prior to moving needs to be done first in case moving the bullet would collide with a target, which would trigger that first.
        /// </summary>
        /// <param name="preMove"></param>
        /// <returns></returns>
        private bool ProximityAirDetonation(bool preMove)
        {
            if (!preMove && isAPSprojectile && (tgtShell != null || tgtRocket != null)) // APS can detonate at close range.
            {
                if (currentPosition.CloserToThan(tgtShell != null ? tgtShell.transform.position : tgtRocket.transform.position, detonationRange / 2))
                {
                    if (BDArmorySettings.DEBUG_WEAPONS)
                        Debug.Log("[BDArmory.PooledBullet]: bullet proximity to APS target | Distance overlap = " + detonationRange + "| tgt name = " + tgtShell != null ? tgtShell.name : tgtRocket.name);
                    return true;
                }
            }

            if (timeAlive < armingTime && (fuzeType == BulletFuzeTypes.Proximity || fuzeType == BulletFuzeTypes.Timed)) return false; // Not yet armed.

            if (preMove) // For proximity detonation.
            {
                if (fuzeType != BulletFuzeTypes.Proximity && fuzeType != BulletFuzeTypes.Flak) return false; // Invalid type.

                Vector3 bulletAcceleration = bulletDrop ? FlightGlobals.getGeeForceAtPosition(currentPosition) : Vector3.zero;
                using (var loadedVessels = BDATargetManager.LoadedVessels.GetEnumerator())
                {
                    while (loadedVessels.MoveNext())
                    {
                        if (loadedVessels.Current == null || !loadedVessels.Current.loaded) continue;
                        if (loadedVessels.Current == sourceVessel) continue;
                        Vector3 relativeVelocity = loadedVessels.Current.Velocity() - currentVelocity;
                        if (Vector3.Dot(relativeVelocity, loadedVessels.Current.CoM - currentPosition) >= 0) continue; // Ignore craft that aren't approaching.
                        float localDetonationRange = detonationRange + loadedVessels.Current.GetRadius(average: true); // Detonate when the (average) outermost part of the vessel is within the detonateRange.
                        float detRangeTime = TimeWarp.fixedDeltaTime + 2 * localDetonationRange / Mathf.Max(1f, relativeVelocity.magnitude); // Time for this frame's movement plus the relative separation to change by twice the detonation range + the vessel's radius (within reason). This is more than the worst-case time needed for the bullet to reach the CPA (ignoring relative acceleration, technically we should be solving x=v*t+1/2*a*t^2 for t).
                        var timeToCPA = loadedVessels.Current.TimeToCPA(currentPosition, currentVelocity, bulletAcceleration, detRangeTime);
                        if (timeToCPA > 0 && timeToCPA < detRangeTime) // Going to reach the CPA within the detRangeTime
                        {
                            Vector3 adjustedTgtPos = loadedVessels.Current.PredictPosition(timeToCPA);
                            Vector3 CPA = AIUtils.PredictPosition(currentPosition, currentVelocity, bulletAcceleration, timeToCPA);
                            float minSepSqr = (CPA - adjustedTgtPos).sqrMagnitude;
                            float localDetonationRangeSqr = localDetonationRange * localDetonationRange;
                            if (minSepSqr < localDetonationRangeSqr)
                            {
                                timeToCPA = Mathf.Max(0, timeToCPA - BDAMath.Sqrt((localDetonationRangeSqr - minSepSqr) / relativeVelocity.sqrMagnitude)); // Move the detonation time back to the point where it came within the detonation range, but not before the current time.
                                if (timeToCPA < TimeWarp.fixedDeltaTime) // Detonate if timeToCPA is this frame.
                                {
                                    currentPosition = AIUtils.PredictPosition(currentPosition, currentVelocity, bulletAcceleration, timeToCPA); // Adjust the bullet position back to the detonation position.
                                    iTime = TimeWarp.fixedDeltaTime - timeToCPA;
                                    if (BDArmorySettings.DEBUG_WEAPONS) Debug.Log($"[BDArmory.PooledBullet]: Detonating proxy round with detonation range {detonationRange}m at {currentPosition} at distance {(currentPosition - loadedVessels.Current.PredictPosition(timeToCPA)).magnitude}m from {loadedVessels.Current.vesselName} of radius {loadedVessels.Current.GetRadius(average: true)}m");
                                    currentPosition -= timeToCPA * BDKrakensbane.FrameVelocityV3f; // Adjust for Krakensbane.
                                    return true;
                                }
                            }
                        }
                    }
                }
                return false;
            }
            else // For end-of-life detonation.
            {
                if (!(((HEType != PooledBulletTypes.Slug || nuclear) && tntMass > 0) || beehive)) return false;
                if (!(fuzeType == BulletFuzeTypes.Timed || fuzeType == BulletFuzeTypes.Flak)) return false;
                if (timeAlive > (beehive ? timeToDetonation - detonationRange / bulletVelocity : timeToDetonation))
                {
                    iTime = 0;
                    currentPosition -= TimeWarp.fixedDeltaTime * BDKrakensbane.FrameVelocityV3f; // Adjust for Krakensbane.
                    if (BDArmorySettings.DEBUG_WEAPONS) Debug.Log($"[BDArmory.PooledBullet]: Proximity detonation from reaching max time {timeToDetonation}s");
                    return true;
                }
                return false;
            }
        }

        private void UpdateDragEstimate()
        {
            switch (dragType)
            {
                case BulletDragTypes.None: // Don't do anything else
                    return;

                case BulletDragTypes.AnalyticEstimate:
                    CalculateDragAnalyticEstimate(currentSpeed, timeElapsedSinceCurrentSpeedWasAdjusted);
                    break;

                case BulletDragTypes.NumericalIntegration: // Numerical Integration is currently Broken
                    CalculateDragNumericalIntegration();
                    break;
            }
        }

        private void CalculateDragNumericalIntegration()
        {
            Vector3 dragAcc = currentVelocity * currentVelocity.magnitude *
                              (float)
                              FlightGlobals.getAtmDensity(FlightGlobals.getStaticPressure(currentPosition),
                                  FlightGlobals.getExternalTemperature(currentPosition));
            dragAcc *= 0.5f;
            dragAcc /= ballisticCoefficient;

            currentVelocity -= dragAcc * TimeWarp.deltaTime;
            //numerical integration; using Euler is silly, but let's go with it anyway
        }

        private void CalculateDragAnalyticEstimate(float initialSpeed, float timeElapsed)
        {
            float atmDensity;
            if (underwater)
                atmDensity = 1030f; // Sea water (3% salt) has a density of 1030kg/m^3 at 4°C at sea level. https://en.wikipedia.org/wiki/Density#Various_materials
            else
                atmDensity = atmosphereDensity;

            dragVelocityFactor = 2f * ballisticCoefficient / (timeElapsed * initialSpeed * atmDensity + 2f * ballisticCoefficient);

            // Force Drag = 1/2 atmdensity*velocity^2 * drag coeff * area
            // Derivation:
            //   F = 1/2 * ρ * v^2 * Cd * A
            //   Cb = m / (Cd * A)
            //   dv/dt = F / m = -1/2 * ρ v^2 m / Cb  (minus due to direction being opposite velocity)
            //     => ∫ 1/v^2 dv = -1/2 * ∫ ρ/Cb dt
            //     => -1/v = -1/2*t*ρ/Cb + a
            //     => v(t) = 2*Cb / (t*ρ + 2*Cb*a)
            //   v(0) = v0 => a = 1/v0
            //     => v(t) = 2*Cb*v0 / (t*v0*ρ + 2*Cb)
            //     => drag factor at time t is 2*Cb / (t*v0*ρ + 2*Cb)

        }

        private bool ExplosiveDetonation(Part hitPart, RaycastHit hit, Ray ray, bool penetratingHit = false)
        {
            ///////////////////////////////////////////////////////////////////////
            // High Explosive Detonation
            ///////////////////////////////////////////////////////////////////////
            if (fuzeType == BulletFuzeTypes.None)
            {
                // if (BDArmorySettings.DEBUG_WEAPONS)
                // {
                //     Debug.Log($"[BDArmory.PooledBullet]: Bullet {bullet.DisplayName} attempted detonation, has improper fuze ({fuzeType}). Fix your bullet config."); // This is getting called regardless of fuzeType, so don't give a warning.
                // }
                return false;
            }
            if (hitPart == null || hitPart.vessel != sourceVessel)
            {
                //if bullet hits and is HE, detonate and kill bullet
                if ((HEType != PooledBulletTypes.Slug || nuclear) && tntMass > 0)
                {
                    if (BDArmorySettings.DEBUG_WEAPONS)
                    {
                        Debug.Log($"[BDArmory.PooledBullet]: Detonation Triggered | penetration: {hasPenetrated} penTick: {penTicker}; airDet: {(fuzeType == BulletFuzeTypes.Timed || fuzeType == BulletFuzeTypes.Flak)} {(fuzeType == BulletFuzeTypes.Timed ? "detRange: " + distanceTraveled : "")}");
                    }
                    if ((fuzeType == BulletFuzeTypes.Timed || fuzeType == BulletFuzeTypes.Flak) || HEType == PooledBulletTypes.Shaped)
                    {
                        if (HEType != PooledBulletTypes.Slug)
                            ExplosionFx.CreateExplosion(hit.point, GetExplosivePower(), explModelPath, explSoundPath, ExplosionSourceType.Bullet, caliber, null, sourceVesselName, null, null, HEType == PooledBulletTypes.Explosive ? default : ray.direction, -1, false, bulletMass, -1, dmgMult, HEType == PooledBulletTypes.Shaped ? ExplosionFx.WarheadTypes.ShapedCharge : ExplosionFx.WarheadTypes.Standard, hitPart, HEType == PooledBulletTypes.Shaped ? apBulletMod : 1f, ProjectileUtils.isReportingWeapon(sourceWeapon) ? (float)DistanceTraveled : -1);
                        if (nuclear)
                            NukeFX.CreateExplosion(hit.point, ExplosionSourceType.Bullet, sourceVesselName, bullet.DisplayName, 0, tntMass * 200, tntMass, tntMass, EMP, blastSoundPath, flashModelPath, shockModelPath, blastModelPath, plumeModelPath, debrisModelPath, "", "", hitPart: hitPart);
                    }
                    else
                    {
                        if (HEType != PooledBulletTypes.Slug)
                            ExplosionFx.CreateExplosion(hit.point - (ray.direction * 0.1f), GetExplosivePower(), explModelPath, explSoundPath, ExplosionSourceType.Bullet, caliber, null, sourceVesselName, null, null, HEType == PooledBulletTypes.Explosive ? default : ray.direction, -1, false, bulletMass, -1, dmgMult, HEType == PooledBulletTypes.Shaped ? ExplosionFx.WarheadTypes.ShapedCharge : ExplosionFx.WarheadTypes.Standard, hitPart, HEType == PooledBulletTypes.Shaped ? apBulletMod : 1f, ProjectileUtils.isReportingWeapon(sourceWeapon) ? (float)DistanceTraveled : -1);
                        if (nuclear)
                            NukeFX.CreateExplosion(hit.point - (ray.direction * 0.1f), ExplosionSourceType.Bullet, sourceVesselName, bullet.DisplayName, 0, tntMass * 200, tntMass, tntMass, EMP, blastSoundPath, flashModelPath, shockModelPath, blastModelPath, plumeModelPath, debrisModelPath, "", "", hitPart: hitPart);
                    }
                    KillBullet();
                    hasDetonated = true;
                    return true;
                }
                if (BDArmorySettings.DEBUG_WEAPONS)
                {
                    Debug.Log($"[BDArmory.PooledBullet]: Bullet {bullet.DisplayName} attempted detonation, has no tntmass amount ({tntMass}) or is a solid slug ({HEType}). Fix your bullet config.");
                }
            }
            return false;
        }

        public void UpdateWidth(Camera c, float resizeFactor)
        {
            if (c == null)
            {
                return;
            }
            if (bulletTrail == null)
            {
                return;
            }
            if (!gameObject.activeInHierarchy)
            {
                return;
            }

            float fov = c.fieldOfView;
            float factor = (fov / 60) * resizeFactor * Mathf.Clamp(Vector3.Distance(currentPosition, c.transform.position), 0, 3000) / 50;
            bulletTrail[0].startWidth = tracerStartWidth * factor * randomWidthScale;
            bulletTrail[0].endWidth = tracerEndWidth * factor * randomWidthScale;

            if (bulletTrail[1].enabled)
            {
                bulletTrail[1].startWidth = (tracerStartWidth / 2) * factor * 0.5f;
                bulletTrail[1].endWidth = (tracerEndWidth / 2) * factor * 0.5f;
            }
        }

        public void KillBullet()
        {
            if (HEType == PooledBulletTypes.Slug && partsHit.Count > 0)
            {
                if (ProjectileUtils.isReportingWeapon(sourceWeapon) && BDACompetitionMode.Instance.competitionIsActive)
                {
                    string msg = $"{partsHit[0].vessel.GetName()} was nailed by {sourceVesselName}'s {sourceWeapon.partInfo.title} at {initialHitDistance:F3}m, damaging {partsHit.Count} parts.";
                    //string message = $"{partsHit[0].vessel.GetName()} was nailed by {sourceVesselName}'s {bullet.DisplayName} at {initialHitDistance:F3}, damaging {partsHit.Count} parts.";
                    BDACompetitionMode.Instance.competitionStatus.Add(msg);
                }
            }
            gameObject.SetActive(false);
        }

        static Vector3 ViewerVelocity
        {
            get
            {
                if (Time.time != _viewerVelocity.Item1)
                {
                    if (FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.gameObject.activeInHierarchy) // Missiles don't become null on being killed.
                    {
                        _viewerVelocity = (Time.time, FlightGlobals.ActiveVessel.Velocity());
                    }
                    else
                    {
                        _viewerVelocity = (Time.time, _viewerVelocity.Item2); // Maintain the last velocity.
                    }
                }
                return _viewerVelocity.Item2;
            }
        }
        static (float, Vector3) _viewerVelocity = new(0, default);
        public void SetTracerPosition()
        {
            // visual tracer velocity is relative to the observer (which uses srf_vel when below 100km (f*&king KSP!), not orb_vel)
            var tracerDirection = currentVelocity - ViewerVelocity;
            if (tracerLength == 0)
            {
                linePositions[1] = currentPosition - Mathf.Min(tracerDeltaFactor * 0.45f * TimeWarp.fixedDeltaTime, timeAlive) * tracerDirection;
            }
            else
            {
                linePositions[1] = currentPosition - tracerLength * tracerDirection.normalized;
            }
            linePositions[0] = currentPosition;
            smokePositions[0] = startPosition;
            for (int i = 0; i < smokePositions.Length - 1; i++)
            {
                if (timeAlive < i)
                {
                    smokePositions[i] = currentPosition;
                }
            }
            if (timeAlive > smokePositions.Length)
            {
                //smokePositions[0] = smokePositions[1];
                startPosition = smokePositions[1]; //Start position isn't used for anything else, so modifying shouldn't be an issue. Vestigial value from some deprecated legacy function?
                for (int i = 0; i < smokePositions.Length - 1; i++)
                {
                    smokePositions[i] = smokePositions[i + 1];
                    //have it so each sec interval after timeAlive > smokePositions.length, have smokePositions[i] = smokePosition[i+1]if i < = smokePosition.length - 1;
                }
                timeAlive -= 1;
            }
            smokePositions[4] = currentPosition;
            if (BDKrakensbane.IsActive)
            {
                Vector3 offset = BDKrakensbane.FloatingOriginOffsetNonKrakensbane;
                for (int i = 0; i < linePositions.Length; ++i) linePositions[i] -= offset;
                for (int i = 0; i < smokePositions.Length; ++i) smokePositions[i] -= offset;
            }
            //if (Vector3.Distance(startPosition, currPosition) > 1000) smokePositions[0] = currPosition - ((currentVelocity - FlightGlobals.ActiveVessel.Velocity()).normalized * 1000);
            bulletTrail[0].SetPositions(linePositions);
            if (bulletTrail[1].enabled) bulletTrail[1].SetPositions(smokePositions);
        }

        void FadeColor()
        {
            Vector4 endColorV = new Vector4(projectileColor.r, projectileColor.g, projectileColor.b, projectileColor.a);
            float delta = TimeWarp.deltaTime;
            Vector4 finalColorV = Vector4.MoveTowards(currentColor, endColorV, delta);
            currentColor = new Color(finalColorV.x, finalColorV.y, finalColorV.z, Mathf.Clamp(finalColorV.w, 0.25f, 1f));
        }

        bool RicochetOnPart(Part p, RaycastHit hit, float angleFromNormal, float impactVel, float fractionOfDistance, float period)
        {
            float hitTolerance = p.crashTolerance;
            //15 degrees should virtually guarantee a ricochet, but 75 degrees should nearly always be fine
            float chance = (((angleFromNormal - 5) / 75) * (hitTolerance / 150)) * 100 / Mathf.Clamp01(impactVel / 600);
            float random = UnityEngine.Random.Range(0f, 100f);
            if (BDArmorySettings.DEBUG_WEAPONS) Debug.Log("[BDArmory.PooledBullet]: Ricochet chance: " + chance);
            if (random < chance)
            {
                DoRicochet(p, hit, angleFromNormal, fractionOfDistance, period);
                return true;
            }
            else
            {
                return false;
            }
        }

        bool RicochetScenery(float hitAngle)
        {
            float reflectRandom = UnityEngine.Random.Range(-75f, 90f);
            if (reflectRandom > 90 - hitAngle && caliber <= 30f)
            {
                return true;
            }

            return false;
        }

        public void DoRicochet(Part p, RaycastHit hit, float hitAngle, float fractionOfDistance, float period)
        {
            //ricochet
            if (BDArmorySettings.BULLET_HITS)
            {
                BulletHitFX.CreateBulletHit(p, hit.point, hit, hit.normal, true, caliber, 0, null);
            }

            tracerStartWidth /= 2;
            tracerEndWidth /= 2;

            MoveBullet(fractionOfDistance * period); // Move the bullet up to the impact point (including velocity and tracking updates).
            var hitPoint = p != null ? AIUtils.PredictPosition(hit.point, p.vessel.Velocity(), p.vessel.acceleration_immediate, fractionOfDistance * period) : hit.point; // Adjust the hit point for the movement of the part.
            currentPosition = hitPoint; // This is usually very accurate (<1mm), but is sometimes off by a couple of metres for some reason.
            Vector3 hitPartVelocity = p != null ? p.vessel.Velocity() : Vector3.zero;
            Vector3 relativeVelocity = currentVelocity - hitPartVelocity;
            relativeVelocity = Vector3.Reflect(relativeVelocity, hit.normal); // Change angle.
            relativeVelocity = Vector3.RotateTowards(relativeVelocity, UnityEngine.Random.onUnitSphere, UnityEngine.Random.Range(0f, 5f) * Mathf.Deg2Rad, 0); // Add some randomness to the new direction.
            relativeVelocity *= hitAngle / 150 * 0.65f; // Reduce speed.
            currentVelocity = hitPartVelocity + relativeVelocity; // Update the new current velocity.
            MoveBullet((1f - fractionOfDistance) * period); // Move the bullet the remaining distance in the new direction.
            bulletTrail[1].enabled = false;
            hasRicocheted = true;
        }

        private float GetExplosivePower()
        {
            return tntMass > 0 ? tntMass : blastPower;
        }
    }
}
