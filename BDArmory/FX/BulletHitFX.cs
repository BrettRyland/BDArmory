using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using BDArmory.Damage;
using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.UI;
using BDArmory.Utils;

namespace BDArmory.FX
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    class Decal : MonoBehaviour
    {
        Part parentPart;
        // string parentPartName = "";
        // string parentVesselName = "";
        static bool hasOnVesselUnloaded = false;
        public static ObjectPool CreateDecalPool(string modelPath)
        {
            if ((Versioning.version_major == 1 && Versioning.version_minor > 10) || Versioning.version_major > 1) hasOnVesselUnloaded = true; // onVesselUnloaded event introduced in 1.11
            var template = GameDatabase.Instance.GetModel(modelPath);
            var decal = template.AddComponent<Decal>();
            template.AddOrGetComponent<Renderer>();
            template.SetActive(false);
            return ObjectPool.CreateObjectPool(template, BDArmorySettings.MAX_NUM_BULLET_DECALS, false, true, 0, true);
        }

        public void AttachAt(Part hitPart, RaycastHit hit, Vector3 offset, Vector3 colliderLocalHitPoint = default)
        {
            if (hitPart is null) return;
            parentPart = hitPart;
            // parentPartName = parentPart.name;
            // parentVesselName = parentPart.vessel.vesselName;
            // Set the parent transform to the collider instead of the part in order to account for things like turrets and other moving parts
            transform.SetParent(hit.collider.transform);
            transform.position = (colliderLocalHitPoint == default ? hit.point : transform.parent.TransformPoint(colliderLocalHitPoint)) + offset;
            transform.rotation = Quaternion.FromToRotation(Vector3.forward, hit.normal);
            AddHandlers();
            gameObject.SetActive(true);
        }
        public void SetColor(Color color)
        {
            var r = gameObject.GetComponentInChildren<Renderer>();
            if (r != null)
            {
                r.material.shader = Shader.Find("KSP/Particles/Alpha Blended");
                r.material.SetColor("_TintColor", color);
                r.material.color = color;
            }
            else
            {
                Debug.Log("[PAINTBALL] no renderer found in decal");
            }

        }

        void AddHandlers()
        {
            // Note: part.explode triggers onPartWillDie, then sets the parent to null, then calls OnJustAboutToDie.
            // So, onPartWillDie should be used instead of OnJustAboutToDie for parent parts, however, sometimes part.Die is called via other routes, so that needs handling too.
            // FIXME There's still some route that occasionally leads to the Decal getting destroyed instead of disabled.
            GameEvents.onPartWillDie.Add(OnParentWillDie); // Exploding and usual part destruction.
            parentPart.OnJustAboutToDie += Deactivate; // Sometimes part.Die is called without triggering onPartWillDie, e.g., ConformalDecals does this if the parent part will die.
            parentPart.OnJustAboutToBeDestroyed += Deactivate; // Packing, unloading, etc.
            if (hasOnVesselUnloaded) OnVesselUnloaded_1_11(true); // Catch unloading events too.
        }

        void RemoveHandlers()
        {
            GameEvents.onPartWillDie.Remove(OnParentWillDie);
            if (parentPart is not null)
            {
                parentPart.OnJustAboutToDie -= Deactivate;
                parentPart.OnJustAboutToBeDestroyed -= Deactivate;
            }
            if (hasOnVesselUnloaded) OnVesselUnloaded_1_11(false);
        }

        void OnParentWillDie(Part p)
        {
            if (p != parentPart) return;
            Deactivate();
        }

        void OnVesselUnloaded(Vessel vessel)
        {
            if (parentPart is null || parentPart.vessel is null || parentPart.vessel == vessel)
                Deactivate();
        }

        void OnVesselUnloaded_1_11(bool addRemove)
        {
            if (addRemove)
                GameEvents.onVesselUnloaded.Add(OnVesselUnloaded);
            else
                GameEvents.onVesselUnloaded.Remove(OnVesselUnloaded);
        }

        void Deactivate()
        {
            transform.parent = null;
            gameObject.SetActive(false);
        }

        void OnDisable()
        {
            RemoveHandlers();
            parentPart = null;
            transform.localScale = Vector3.one; // Reset localScale so that Unity doesn't mess with the size.
        }

        public void OnDestroy() // This shouldn't be happening except on exiting KSP or reducing the pool size, but sometimes they get destroyed instead of disabled!
        {
            // if (HighLogic.LoadedSceneIsFlight) Debug.LogWarning($"[BDArmory.BulletHitFX]: BulletHitFX on {parentPartName} ({parentVesselName}) was destroyed!");
            RemoveHandlers();
        }
    }

    public class BulletHitFX : MonoBehaviour
    {
        KSPParticleEmitter[] pEmitters;
        AudioSource audioSource;
        enum AudioClipType { Ricochet1, Ricochet2, Ricochet3, BulletHit1, BulletHit2, BulletHit3, Artillery_Shot };
        static Dictionary<AudioClipType, AudioClip> audioClips;
        AudioClip hitSound;
        float startTime;
        public bool ricochet;
        public float caliber;

        public static ObjectPool decalPool_small;
        public static ObjectPool decalPool_large;
        public static ObjectPool decalPool_paint1;
        public static ObjectPool decalPool_paint2;
        public static ObjectPool decalPool_paint3;
        public static ObjectPool bulletHitFXPool;
        public static ObjectPool penetrationFXPool;
        public static ObjectPool leakFXPool;
        public static ObjectPool FireFXPool;
        public static ObjectPool flameFXPool;
        public static Dictionary<Vessel, List<float>> PartsOnFire = new Dictionary<Vessel, List<float>>();

        public static int MaxFiresPerVessel = 3;
        public static float FireLifeTimeInSeconds = 5f;

        private bool disabled = false;

        public static void SetupShellPool()
        {
            if (!BDArmorySettings.PAINTBALL_MODE)
            {
                if (decalPool_large == null)
                    decalPool_large = Decal.CreateDecalPool("BDArmory/Models/bulletDecal/BulletDecal2");

                if (decalPool_small == null)
                    decalPool_small = Decal.CreateDecalPool("BDArmory/Models/bulletDecal/BulletDecal1");
            }
            else
            {
                if (decalPool_paint1 == null)
                    decalPool_paint1 = Decal.CreateDecalPool("BDArmory/Models/bulletDecal/BulletDecal3");

                if (decalPool_paint2 == null)
                    decalPool_paint2 = Decal.CreateDecalPool("BDArmory/Models/bulletDecal/BulletDecal4");

                if (decalPool_paint3 == null)
                    decalPool_paint3 = Decal.CreateDecalPool("BDArmory/Models/bulletDecal/BulletDecal5");

            }
        }

        public static void AdjustDecalPoolSizes(int size)
        {
            if (decalPool_large != null) decalPool_large.AdjustSize(size);
            if (decalPool_small != null) decalPool_small.AdjustSize(size);
            if (decalPool_paint1 != null) decalPool_paint1.AdjustSize(size);
            if (decalPool_paint2 != null) decalPool_paint2.AdjustSize(size);
            if (decalPool_paint3 != null) decalPool_paint3.AdjustSize(size);
        }

        // We use an ObjectPool for the BulletHitFX and PenFX instances as they leak KSPParticleEmitters otherwise.
        public static void SetupBulletHitFXPool()
        {
            if (bulletHitFXPool == null)
            {
                var bulletHitFXTemplate = GameDatabase.Instance.GetModel("BDArmory/Models/bulletHit/bulletHit");
                var bFX = bulletHitFXTemplate.AddComponent<BulletHitFX>();
                bFX.audioSource = bulletHitFXTemplate.AddComponent<AudioSource>();
                bFX.audioSource.minDistance = 1;
                bFX.audioSource.maxDistance = 50;
                bFX.audioSource.spatialBlend = 1;
                bulletHitFXTemplate.SetActive(false);
                bulletHitFXPool = ObjectPool.CreateObjectPool(bulletHitFXTemplate, 10, true, true, 10f * Time.deltaTime, false);
            }
            if (penetrationFXPool == null)
            {
                var penetrationFXTemplate = GameDatabase.Instance.GetModel("BDArmory/FX/PenFX");
                var bFX = penetrationFXTemplate.AddComponent<BulletHitFX>();
                bFX.audioSource = penetrationFXTemplate.AddComponent<AudioSource>();
                bFX.audioSource.minDistance = 1;
                bFX.audioSource.maxDistance = 50;
                bFX.audioSource.spatialBlend = 1;
                penetrationFXTemplate.SetActive(false);
                penetrationFXPool = ObjectPool.CreateObjectPool(penetrationFXTemplate, 10, true, true, 10f * Time.deltaTime, false);
            }
            if (flameFXPool == null)
            {
                var flameTemplate = GameDatabase.Instance.GetModel("BDArmory/FX/FlameEffect2/model");
                flameTemplate.AddComponent<DecalEmitterScript>();
                DecalEmitterScript.shrinkRateFlame = 0.125f;
                DecalEmitterScript.shrinkRateSmoke = 0.125f;
                foreach (var pe in flameTemplate.GetComponentsInChildren<KSPParticleEmitter>())
                {
                    if (!pe.useWorldSpace) continue;
                    var gpe = pe.gameObject.AddComponent<DecalGaplessParticleEmitter>();
                    gpe.Emit = false;
                }
                flameTemplate.SetActive(false);
                flameFXPool = ObjectPool.CreateObjectPool(flameTemplate, 10, true, true);
            }
        }

        public static void SpawnDecal(RaycastHit hit, Part hitPart, float caliber, float penetrationfactor, string team, Vector3 colliderLocalHitPoint = default)
        {
            if (!BDArmorySettings.BULLET_DECALS) return;
            ObjectPool decalPool_;
            if (!BDArmorySettings.PAINTBALL_MODE)
            {
                if (caliber >= 90f)
                {
                    decalPool_ = decalPool_large;
                }
                else
                {
                    decalPool_ = decalPool_small;
                }
            }
            else
            {
                int i;
                i = UnityEngine.Random.Range(1, 4);
                if (i < 1.66)
                {
                    decalPool_ = decalPool_paint1;
                }
                else if (i > 2.33)
                {
                    decalPool_ = decalPool_paint2;
                }
                else
                {
                    decalPool_ = decalPool_paint3;
                }
            }

            //front hit
            var decalFront = decalPool_.GetPooledObject();
            if (decalFront != null && hitPart != null)
            {
                var decal = decalFront.GetComponentInChildren<Decal>();
                decal.AttachAt(hitPart, hit, new Vector3(0.05f, 0f, 0f), colliderLocalHitPoint);

                if (BDArmorySettings.PAINTBALL_MODE)
                {
                    if (team != null && BDTISetup.Instance.ColorAssignments.ContainsKey(team))
                    {
                        decal.SetColor(BDTISetup.Instance.ColorAssignments[team]);
                    }
                }
            }
            //back hole if fully penetrated
            if (penetrationfactor >= 1 && !BDArmorySettings.PAINTBALL_MODE)
            {
                var decalBack = decalPool_.GetPooledObject();
                if (decalBack != null && hitPart != null)
                {
                    var decal = decalBack.GetComponentInChildren<Decal>();
                    decal.AttachAt(hitPart, hit, new Vector3(-0.05f, 0f, 0f), colliderLocalHitPoint);
                }
            }
        }

        private static bool CanFlamesBeAttached(Part hitPart)
        {
            if (hitPart == null || hitPart.vessel == null) return false;
            if (!BDArmorySettings.FIRE_FX_IN_FLIGHT && !hitPart.vessel.LandedOrSplashed || !hitPart.HasFuel())
                return false;

            if (hitPart.vessel.LandedOrSplashed)
            {
                MaxFiresPerVessel = BDArmorySettings.MAX_FIRES_PER_VESSEL;
                FireLifeTimeInSeconds = BDArmorySettings.FIRELIFETIME_IN_SECONDS;
            }

            if (PartsOnFire.ContainsKey(hitPart.vessel) && PartsOnFire[hitPart.vessel].Count >= MaxFiresPerVessel)
            {
                var firesOnVessel = PartsOnFire[hitPart.vessel];

                firesOnVessel.Where(x => (Time.time - x) > FireLifeTimeInSeconds).Select(x => firesOnVessel.Remove(x));
                return false;
            }

            if (!PartsOnFire.ContainsKey(hitPart.vessel))
            {
                List<float> firesList = new List<float> { Time.time };

                PartsOnFire.Add(hitPart.vessel, firesList);
            }
            else
            {
                PartsOnFire[hitPart.vessel].Add(Time.time);
            }

            return true;
        }

        public static void CleanPartsOnFireInfo()
        {
            HashSet<Vessel> keysToRemove = new HashSet<Vessel>();
            foreach (var key in PartsOnFire.Keys.ToList())
            {
                PartsOnFire[key] = PartsOnFire[key].Where(x => (Time.time - x) < FireLifeTimeInSeconds).ToList(); // Remove expired fires.
                if (PartsOnFire[key].Count == 0) { keysToRemove.Add(key); } // Remove parts no longer on fire.
            }
            PartsOnFire = PartsOnFire.Where(kvp => kvp.Key != null && !keysToRemove.Contains(kvp.Key)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value); // Remove null keys (vessels) and those with no parts on fire.
        }

        void Awake()
        {
            if (audioClips == null)
            {
                audioClips = new Dictionary<AudioClipType, AudioClip>{
                    {AudioClipType.Ricochet1, SoundUtils.GetAudioClip("BDArmory/Sounds/ricochet1")},
                    {AudioClipType.Ricochet2, SoundUtils.GetAudioClip("BDArmory/Sounds/ricochet1")},
                    {AudioClipType.Ricochet3, SoundUtils.GetAudioClip("BDArmory/Sounds/ricochet3")},
                    {AudioClipType.BulletHit1, SoundUtils.GetAudioClip("BDArmory/Sounds/bulletHit1")},
                    {AudioClipType.BulletHit2, SoundUtils.GetAudioClip("BDArmory/Sounds/bulletHit2")},
                    {AudioClipType.BulletHit3, SoundUtils.GetAudioClip("BDArmory/Sounds/bulletHit3")},
                    {AudioClipType.Artillery_Shot, SoundUtils.GetAudioClip("BDArmory/Sounds/Artillery_Shot")},
                };
            }
        }

        void OnEnable()
        {
            startTime = Time.time;
            disabled = false;

            foreach (var pe in pEmitters)
            {
                if (pe == null) continue;
                EffectBehaviour.AddParticleEmitter(pe);
            }

            audioSource = gameObject.GetComponent<AudioSource>();
            audioSource.volume = BDArmorySettings.BDARMORY_WEAPONS_VOLUME;

            int random = UnityEngine.Random.Range(1, 3);

            if (ricochet)
            {
                if (caliber <= 30)
                {
                    switch (random)
                    {
                        case 1:
                            hitSound = audioClips[AudioClipType.Ricochet1];
                            break;
                        case 2:
                            hitSound = audioClips[AudioClipType.Ricochet2];
                            break;
                        case 3:
                            hitSound = audioClips[AudioClipType.Ricochet3];
                            break;
                    }
                }
                else
                {
                    hitSound = audioClips[AudioClipType.Artillery_Shot];
                }
            }
            else
            {
                if (caliber <= 30)
                {
                    switch (random)
                    {
                        case 1:
                            hitSound = audioClips[AudioClipType.BulletHit1];
                            break;
                        case 2:
                            hitSound = audioClips[AudioClipType.BulletHit2];
                            break;
                        case 3:
                            hitSound = audioClips[AudioClipType.BulletHit3];
                            break;
                    }
                }
                else
                {
                    hitSound = audioClips[AudioClipType.Artillery_Shot];
                }
            }

            audioSource.PlayOneShot(hitSound);
        }

        void OnDisable()
        {
            foreach (var pe in pEmitters)
                if (pe != null)
                {
                    pe.emit = false;
                    EffectBehaviour.RemoveParticleEmitter(pe);
                }
        }

        void Update()
        {
            if (!disabled && Time.time - startTime > Time.deltaTime)
            {
                using (var pe = pEmitters.AsEnumerable().GetEnumerator())
                    while (pe.MoveNext())
                    {
                        if (pe.Current == null) continue;
                        pe.Current.emit = false;
                    }
                disabled = true;
            }
        }

        public static void CreateBulletHit(Part hitPart, Vector3 position, RaycastHit hit, Vector3 normalDirection, bool ricochet, float caliber, float penetrationfactor, string team, Vector3 colliderLocalHitPoint = default)
        {
            if (decalPool_large == null || decalPool_small == null)
                SetupShellPool();
            if (BDArmorySettings.PAINTBALL_MODE && decalPool_paint1 == null)
                SetupShellPool();
            if (bulletHitFXPool == null || penetrationFXPool == null || flameFXPool == null)
                SetupBulletHitFXPool();

            if ((hitPart != null) && caliber != 0 && !hitPart.IgnoreDecal())
            {
                SpawnDecal(hit, hitPart, caliber, penetrationfactor, team, colliderLocalHitPoint); //No bullet decals for laser or ricochet
            }

            GameObject newExplosion = (caliber <= 30 || BDArmorySettings.PAINTBALL_MODE) ? bulletHitFXPool.GetPooledObject() : penetrationFXPool.GetPooledObject();
            newExplosion.transform.SetPositionAndRotation(position, Quaternion.LookRotation(normalDirection));
            var bulletHitComponent = newExplosion.GetComponent<BulletHitFX>();
            bulletHitComponent.ricochet = ricochet;
            bulletHitComponent.caliber = caliber;
            bulletHitComponent.pEmitters = newExplosion.GetComponentsInChildren<KSPParticleEmitter>();
            newExplosion.SetActive(true);
            foreach (var pe in bulletHitComponent.pEmitters)
            {
                if (pe == null) continue;
                pe.emit = true;

                if (pe.gameObject.name == "sparks")
                {
                    pe.force = 4.49f * FlightGlobals.getGeeForceAtPosition(position);
                }
                else if (pe.gameObject.name == "smoke")
                {
                    pe.force = 1.49f * FlightGlobals.getGeeForceAtPosition(position);
                }
            }
        }

        public static void AttachLeak(RaycastHit hit, Part hitPart, float caliber, bool explosive, bool incendiary, string sourcevessel, bool inertTank, Vector3 colliderLocalHitPoint = default)
        {
            if (BDArmorySettings.BATTLEDAMAGE && BDArmorySettings.BD_TANKS && hitPart.Modules.GetModule<HitpointTracker>().Hitpoints > 0)
            {
                if (leakFXPool == null)
                    leakFXPool = FuelLeakFX.CreateLeakFXPool("BDArmory/FX/FuelLeakFX/model");
                var fuelLeak = leakFXPool.GetPooledObject();
                var leakFX = fuelLeak.GetComponentInChildren<FuelLeakFX>();

                var leak = hitPart.GetComponentsInChildren<FuelLeakFX>();
                if (leak != null) //only apply one leak to engines
                {
                    if (!hitPart.isEngine())
                    {
                        var scale = caliber * caliber / 200f;
                        leakFX.transform.localScale = scale * Vector3.one;
                        leakFX.AttachAt(hitPart, hit, new Vector3(0.25f, 0f, 0f), colliderLocalHitPoint);
                        leakFX.drainRate = scale * BDArmorySettings.BD_TANK_LEAK_RATE;
                        leakFX.lifeTime = BDArmorySettings.BD_TANK_LEAK_TIME;
                        if (BDArmorySettings.BD_FIRES_ENABLED && !inertTank)
                        {
                            float ammoMod = BDArmorySettings.BD_FIRE_CHANCE_TRACER; //10% chance of AP rounds starting fires from sparks/tracers/etc
                            if (explosive)
                            {
                                ammoMod = BDArmorySettings.BD_FIRE_CHANCE_HE; //20% chance of starting fires from HE rounds
                            }
                            if (incendiary)
                            {
                                ammoMod = BDArmorySettings.BD_FIRE_CHANCE_INCENDIARY; //90% chance of starting fires from inc rounds
                            }
                            double Diceroll = UnityEngine.Random.Range(0, 100);
                            if (Diceroll <= ammoMod)
                            {
                                leakFX.lifeTime = 0;
                                int leakcount = 0;
                                foreach (var existingLeakFX in hitPart.GetComponentsInChildren<FuelLeakFX>())
                                {
                                    existingLeakFX.lifeTime = 0; //kill leakFX, start fire
                                    leakcount++;
                                }
                                //if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log("[BDArmory.BullethitFX]: Adding fire. HE? " + explosive + "; Inc? " + incendiary + "; inerttank? " + inertTank);
                                AttachFire(colliderLocalHitPoint == default ? hit.point : hit.collider.transform.TransformPoint(colliderLocalHitPoint), hitPart, caliber, sourcevessel, -1, leakcount);
                            }
                        }
                    }
                }
                else
                {
                    var scale = caliber * caliber / 200f;
                    leakFX.transform.localScale = scale * Vector3.one;
                    leakFX.AttachAt(hitPart, hit, new Vector3(0.25f, 0f, 0f), colliderLocalHitPoint);
                    leakFX.drainRate = scale * BDArmorySettings.BD_TANK_LEAK_RATE;
                    leakFX.lifeTime = BDArmorySettings.BD_TANK_LEAK_TIME;

                    if (hitPart.isEngine())
                    {
                        leakFX.lifeTime = (10 * BDArmorySettings.BD_TANK_LEAK_TIME);
                    }
                }
                if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log("[BDArmory.BulletHitFX]: BulletHit attaching fuel leak, drainrate: " + leakFX.drainRate);

                fuelLeak.SetActive(true);
            }
        }
        public static void AttachFire(Vector3 hit, Part hitPart, float caliber, string sourcevessel, float burntime = -1, int ignitedLeaks = 1, bool surfaceFire = false)
        {
            if (BDArmorySettings.BATTLEDAMAGE && BDArmorySettings.BD_FIRES_ENABLED && hitPart.Modules.GetModule<HitpointTracker>().Hitpoints > 0)
            {
                if (FireFXPool == null)
                    FireFXPool = FireFX.CreateFireFXPool("BDArmory/FX/FireFX/model");
                var fire = FireFXPool.GetPooledObject();
                var fireFX = fire.GetComponentInChildren<FireFX>();
                fireFX.burnTime = burntime; //this apparently never got implemented... !?
                fireFX.burnRate = (((caliber / 50) * BDArmorySettings.BD_TANK_LEAK_RATE) * ignitedLeaks);
                fireFX.surfaceFire = surfaceFire;
                fireFX.AttachAt(hitPart, hit, new Vector3(0.25f, 0f, 0f), sourcevessel);
                //fireFX.transform.localScale = Vector3.one * (caliber/10);

                if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log("[BDArmory.BulletHitFX]: BulletHit fire, burn rate: " + fireFX.burnRate + "; Surface fire: " + surfaceFire);
                fire.SetActive(true);
            }
        }
        public static void AttachFlames(Vector3 contactPoint, Part hitPart)
        {
            if (!CanFlamesBeAttached(hitPart)) return;

            if (flameFXPool == null) SetupBulletHitFXPool();
            var flameObject = flameFXPool.GetPooledObject();
            if (flameObject == null)
            {
                Debug.LogError("[BDArmory.BulletHitFX]: flameFXPool gave a null flameObject!");
                return;
            }
            flameObject.transform.SetParent(hitPart.transform);
            flameObject.SetActive(true);
        }

        public static void DisableAllFX()
        {
            if (leakFXPool != null && leakFXPool.pool != null)
            {
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.BulletHitFX]: Setting {leakFXPool.pool.Count(leak => leak != null && leak.activeInHierarchy)} leak FX inactive.");
                foreach (var leak in leakFXPool.pool)
                {
                    if (leak == null) continue;
                    leak.SetActive(false);
                }
            }
            if (FireFXPool != null && FireFXPool.pool != null)
            {
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.BulletHitFX]: Setting {FireFXPool.pool.Count(fire => fire != null && fire.activeInHierarchy)} fire FX inactive.");
                foreach (var fire in FireFXPool.pool)
                {
                    if (fire == null) continue;
                    fire.SetActive(false);
                }
            }
            if (flameFXPool != null && flameFXPool.pool != null)
            {
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.BulletHitFX]: Setting {flameFXPool.pool.Count(flame => flame != null && flame.activeInHierarchy)} flame FX inactive.");
                foreach (var flame in flameFXPool.pool)
                {
                    if (flame == null) continue;
                    flame.SetActive(false);
                }
            }
            if (bulletHitFXPool != null && bulletHitFXPool.pool != null)
            {
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.BulletHitFX]: Setting {bulletHitFXPool.pool.Count(hit => hit != null && hit.activeInHierarchy)} bullet hit FX inactive.");
                foreach (var hit in bulletHitFXPool.pool)
                {
                    if (hit == null) continue;
                    hit.SetActive(false);
                }
            }
            if (penetrationFXPool != null && penetrationFXPool.pool != null)
            {
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.BulletHitFX]: Setting {penetrationFXPool.pool.Count(pen => pen != null && pen.activeInHierarchy)} penetration FX inactive.");
                foreach (var pen in penetrationFXPool.pool)
                {
                    if (pen == null) continue;
                    pen.SetActive(false);
                }
            }
        }
    }
}
