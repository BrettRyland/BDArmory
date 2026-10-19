using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using BDArmory.Extensions;
using BDArmory.Utils;
using BDArmory.FX;
using BDArmory.Weapons.Missiles;

namespace BDArmory.Weapons
{
    public class ClusterBomb : BDAPartModule
    {
        public List<GameObject> submunitions;
        List<GameObject> fairings;
        MissileLauncher missileLauncher;

        bool deployed;

        [KSPField(isPersistant = false)]
        public string subExplModelPath = "BDArmory/Models/explosion/explosion";

        [KSPField(isPersistant = false)]
        public string subExplSoundPath = "BDArmory/Sounds/subExplode";

        [KSPField(isPersistant = false)]
        public float deployDelay = 2.5f;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_DeployAltitude"),//Deploy Altitude
         UI_FloatRange(minValue = 100f, maxValue = 1000f, stepIncrement = 10f, scene = UI_Scene.Editor)]
        public float deployAltitude = 400;

        [KSPField(isPersistant = false)]
        public float submunitionMaxSpeed = 10;

        [KSPField(isPersistant = false)]
        public bool swapCollidersOnDeploy = true;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (HighLogic.LoadedSceneIsFlight)
            {
                part.force_activate();
            }

            submunitions = new List<GameObject>();
            Transform[] sub = part.FindModelTransforms("submunition");
            
            for (int i = 0; i < sub.Length; i++)
            {
                Transform curr = sub[i];
                if (curr == null) continue;
                submunitions.Add(curr.gameObject);

                if (HighLogic.LoadedSceneIsFlight)
                {
                    Rigidbody subRb = curr.gameObject.GetComponent<Rigidbody>();
                    if (!subRb)
                    {
                        subRb = curr.gameObject.AddComponent<Rigidbody>();
                    }

                    subRb.isKinematic = true;
                    subRb.mass = part.mass / sub.Length;
                }
                curr.gameObject.SetActive(false);
            }

            /*IEnumerator<Transform> sub = part.FindModelTransforms("submunition").AsEnumerable().GetEnumerator();

            while (sub.MoveNext())
            {
                if (sub.Current == null) continue;
                submunitions.Add(sub.Current.gameObject);

                if (HighLogic.LoadedSceneIsFlight)
                {
                    Rigidbody subRb = sub.Current.gameObject.GetComponent<Rigidbody>();
                    if (!subRb)
                    {
                        subRb = sub.Current.gameObject.AddComponent<Rigidbody>();
                    }

                    subRb.isKinematic = true;
                    subRb.mass = part.mass / part.FindModelTransforms("submunition").Length;
                }
                sub.Current.gameObject.SetActive(false);
            }
            sub.Dispose();*/

            fairings = new List<GameObject>();
            IEnumerator<Transform> fairing = part.FindModelTransforms("fairing").AsEnumerable().GetEnumerator();
            while (fairing.MoveNext())
            {
                if (fairing.Current == null) continue;
                fairings.Add(fairing.Current.gameObject);
                if (!HighLogic.LoadedSceneIsFlight) continue;
                Rigidbody fairingRb = fairing.Current.gameObject.GetComponent<Rigidbody>();
                if (!fairingRb)
                {
                    fairingRb = fairing.Current.gameObject.AddComponent<Rigidbody>();
                }
                fairingRb.isKinematic = true;
                fairingRb.mass = 0.05f;
            }
            fairing.Dispose();

            missileLauncher = part.GetComponent<MissileLauncher>();
        }

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();
            if (missileLauncher != null && missileLauncher.HasFired &&
                missileLauncher.TimeIndex > deployDelay &&
                !deployed && AltitudeTrigger())
            {
                DeploySubmunitions();
            }
        }

        void DeploySubmunitions()
        {
            missileLauncher.sfAudioSource.PlayOneShot(SoundUtils.GetAudioClip("BDArmory/Sounds/flareSound"));
            FXMonger.Explode(part, transform.position + part.rb.velocity * Time.fixedDeltaTime, 0.1f);

            deployed = true;
            if (swapCollidersOnDeploy)
            {
                IEnumerator<Collider> col = part.GetComponentsInChildren<Collider>().AsEnumerable().GetEnumerator();
                while (col.MoveNext())
                {
                    if (col.Current == null) continue;
                    col.Current.enabled = !col.Current.enabled;
                }
                col.Dispose();
            }

            missileLauncher.sfAudioSource.priority = 999;

            using (List<GameObject>.Enumerator sub = submunitions.GetEnumerator())
                while (sub.MoveNext())
                {
                    if (sub.Current == null) continue;
                    sub.Current.SetActive(true);
                    sub.Current.transform.parent = null;
                    Vector3 direction = (sub.Current.transform.position - part.transform.position).normalized;
                    Rigidbody subRB = sub.Current.GetComponent<Rigidbody>();
                    subRB.isKinematic = false;
                    subRB.velocity = part.rb.velocity + BDKrakensbane.FrameVelocityV3f +
                                     (UnityEngine.Random.Range(submunitionMaxSpeed / 10, submunitionMaxSpeed) * direction);

                    Submunition subScript = sub.Current.AddComponent<Submunition>();
                    subScript.enabled = true;
                    subScript.deployed = true;
                    subScript.blastForce = missileLauncher.GetTntMass();
                    subScript.blastHeat = missileLauncher.blastHeat;
                    subScript.blastRadius = missileLauncher.GetBlastRadius();
                    subScript.subExplModelPath = subExplModelPath;
                    subScript.subExplSoundPath = subExplSoundPath;
                    subScript.sourceVesselName = missileLauncher.SourceVessel.vesselName;
                    sub.Current.AddComponent<KSPForceApplier>();
                }

            using (List<GameObject>.Enumerator fairing = fairings.GetEnumerator())
                while (fairing.MoveNext())
                {
                    if (fairing.Current == null) continue;
                    Vector3 direction = (fairing.Current.transform.position - part.transform.position).normalized;
                    Rigidbody fRB = fairing.Current.GetComponent<Rigidbody>();
                    fRB.isKinematic = false;
                    fRB.velocity = part.rb.velocity + BDKrakensbane.FrameVelocityV3f + ((submunitionMaxSpeed + 2) * direction);
                    fairing.Current.AddComponent<KSPForceApplier>();
                    fairing.Current.GetComponent<KSPForceApplier>().drag = 0.2f;
                    ClusterBombFairing fairingScript = fairing.Current.AddComponent<ClusterBombFairing>();
                    fairingScript.deployed = true;
                }

            part.explosionPotential = 0;
            missileLauncher.HasFired = false;

            part.Destroy();
        }

        bool AltitudeTrigger()
        {
            double asl = vessel.mainBody.GetAltitude(vessel.CoM);
            double radarAlt = asl - vessel.terrainAltitude;

            return (radarAlt < deployAltitude || asl < deployAltitude) && vessel.verticalSpeed < 0;
        }
    }

    public class Submunition : MonoBehaviour
    {
        public bool deployed;
        public float blastRadius;
        public float blastForce;
        public float blastHeat;
        public string subExplModelPath;
        public string subExplSoundPath;
        public string sourceVesselName;
        Vector3 currPosition;
        Vector3 prevPosition;

        float startTime;

        Rigidbody rb;
        const int explosionLayerMask = (int)(LayerMasks.Parts | LayerMasks.Scenery | LayerMasks.EVA | LayerMasks.PhysicalObjects | LayerMasks.AeroFXIgnore | LayerMasks.Wheels);

        void Start()
        {
            startTime = Time.time;
            currPosition = transform.position;
            prevPosition = transform.position;
            rb = GetComponent<Rigidbody>();
        }

        void OnCollisionEnter(Collision col)
        {
            ContactPoint contact = col.contacts[0];
            Vector3 pos = contact.point;
            ExplosionFx.CreateExplosion(pos, blastForce, subExplModelPath, subExplSoundPath, ExplosionSourceType.Missile, 0, null, sourceVesselName, null, null, default, -1, false, rb.mass * 1000, Hitpart: col.gameObject.GetComponentInParent<Part>());
        }

        void FixedUpdate()
        {
            if (deployed)
            {
                if (Time.time - startTime > 30)
                {
                    Destroy(gameObject);
                    return;
                }

                //floating origin and velocity offloading corrections
                if (BDKrakensbane.IsActive)
                {
                    transform.position -= BDKrakensbane.FloatingOriginOffsetNonKrakensbane;
                    prevPosition -= BDKrakensbane.FloatingOriginOffsetNonKrakensbane;
                }

                currPosition = transform.position;
                float dist = (currPosition - prevPosition).magnitude;
                Ray ray = new Ray(prevPosition, currPosition - prevPosition);
                RaycastHit hit;

                if (Physics.Raycast(ray, out hit, dist, explosionLayerMask))
                {
                    Part hitPart = null;
                    try
                    {
                        hitPart = hit.collider.gameObject.GetComponentInParent<Part>();
                    }
                    catch (NullReferenceException e)
                    {
                        Debug.LogWarning("[BDArmory.ClusterBomb]:NullReferenceException for Submunition Hit: " + e.Message);
                        return;
                    }

                    if (hitPart != null || CheckBuildingHit(hit))
                    {
                        Detonate(hit.point, hitPart);
                    }
                    else if (hitPart == null)
                    {
                        Detonate(currPosition);
                    }
                }
                else if (FlightGlobals.getAltitudeAtPos(currPosition) <= 0)
                {
                    Detonate(currPosition);
                }

                prevPosition = transform.position;
            }
        }

        void Detonate(Vector3 pos, Part hitPart = null)
        {
            ExplosionFx.CreateExplosion(pos, blastForce, subExplModelPath, subExplSoundPath, ExplosionSourceType.Missile, 0, null, sourceVesselName, null, null, default, -1, false, rb.mass * 1000, Hitpart: hitPart, sourceVelocity: rb.velocity + BDKrakensbane.FrameVelocityV3f);
            Destroy(gameObject);
        }

        private bool CheckBuildingHit(RaycastHit hit)
        {
            DestructibleBuilding building = null;
            try
            {
                building = hit.collider.gameObject.GetComponentUpwards<DestructibleBuilding>();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BDArmory.ClusterBomb]: Exception thrown in CheckBuildingHit: " + e.Message + "\n" + e.StackTrace);
            }

            if (building != null && building.IsIntact)
            {
                return true;
            }
            return false;
        }
    }

    public class ClusterBombFairing : MonoBehaviour
    {
        public bool deployed;

        Vector3 currPosition;
        Vector3 prevPosition;
        float startTime;

        Rigidbody rb;
        const int explosionLayerMask = (int)(LayerMasks.Parts | LayerMasks.Scenery | LayerMasks.EVA | LayerMasks.PhysicalObjects | LayerMasks.AeroFXIgnore | LayerMasks.Wheels);

        void Start()
        {
            startTime = Time.time;
            currPosition = transform.position;
            prevPosition = transform.position;
            rb = GetComponent<Rigidbody>();
        }

        void FixedUpdate()
        {
            if (deployed)
            {
                //floating origin and velocity offloading corrections
                if (BDKrakensbane.IsActive)
                {
                    transform.position -= BDKrakensbane.FloatingOriginOffsetNonKrakensbane;
                    prevPosition -= BDKrakensbane.FloatingOriginOffsetNonKrakensbane;
                }

                currPosition = transform.position;
                float dist = (currPosition - prevPosition).magnitude;
                Ray ray = new Ray(prevPosition, currPosition - prevPosition);
                RaycastHit hit;
                if (Physics.Raycast(ray, out hit, dist, explosionLayerMask))
                {
                    Destroy(gameObject);
                }
                else if (FlightGlobals.getAltitudeAtPos(currPosition) <= 0)
                {
                    Destroy(gameObject);
                }
                else if (Time.time - startTime > 20)
                {
                    Destroy(gameObject);
                }
            }
        }
    }
}
