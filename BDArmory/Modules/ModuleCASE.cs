
using BDArmory.Competition;
using BDArmory.Control;
using BDArmory.Core;
using BDArmory.Core.Extension;
using BDArmory.Core.Module;
using BDArmory.Core.Utils;
using BDArmory.FX;
using System;
using System.Collections.Generic;
using UniLinq;
using UnityEngine;

namespace BDArmory.Modules
{
    class ModuleCASE : PartModule
    {
        [KSPField]
        public int CASELevel = 0; //tier of ammo storage. 0 = nothing, ammosplosion; 1 = base, ammosplosion contained(barely), 2 = blast safely shunted outside, minimal damage to surrounding parts
        private double ammoMass;
        private double ammoQuantity;
        private double ammoExplosionYield = 100;

        private string explModelPath = "BDArmory/Models/explosion/explosion";
        private string explSoundPath = "BDArmory/Sounds/explode1";

        private string limitEdexploModelPath = "BDArmory/Models/explosion/30mmExplosion";
        private string shuntExploModelPath = "BDArmory/Models/explosion/CASEexplosion";

        public string SourceVessel;
        public bool hasDetonated;
        private float blastRadius;

        public override void OnStart(StartState state)
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                part.explosionPotential = 1.0f;
                part.force_activate();
            }
        }
        private List<PartResource> GetResources()
        {
            List<PartResource> resources = new List<PartResource>();

            foreach (PartResource resource in part.Resources)
            {
                if (!resources.Contains(resource)) { resources.Add(resource); }
            }
            return resources;
        }
        private void CalculateBlast()
        {
            foreach (PartResource resource in GetResources())
            {
                using (IEnumerator<PartResource> ammo = part.Resources.GetEnumerator())
                    while (ammo.MoveNext())
                    {
                        if (ammo.Current == null) continue;
                        ammoMass = ammo.Current.info.density;
                        ammoQuantity = ammo.Current.amount;
                        ammoExplosionYield += ((ammoMass / 6) * ammoQuantity);
                    }
            }
            blastRadius = BlastPhysicsUtils.CalculateBlastRange(ammoExplosionYield);
        }
        public float GetBlastRadius()
        {
            CalculateBlast();
            return blastRadius;
        }
        public void DetonateIfPossible()
        {
            if (!hasDetonated)
            {
                Vector3 direction = default(Vector3);
                GetBlastRadius();
                if (CASELevel == 0) //a considerable quantity of explosives and propellants just detonated inside your ship
                {
                    ExplosionFx.CreateExplosion(part.transform.position, (float)ammoExplosionYield, explModelPath, explSoundPath, ExplosionSourceType.Missile, 0, part, vessel.vesselName, direction);
                    Debug.Log("[BD DEBUG] CASE 0 explosion, tntMassEquivilent: " + ammoExplosionYield);
                }
                else if (CASELevel == 1) // the blast is reduced. Damage is severe, but (potentially) survivable
                {
                    ExplosionFx.CreateExplosion(part.transform.position, ((float)ammoExplosionYield / 2), limitEdexploModelPath, explSoundPath, ExplosionSourceType.Missile, 0, part, vessel.vesselName, direction, true);
                    Debug.Log("[BD DEBUG] CASE I explosion, tntMassEquivilent: " + ammoExplosionYield);
                    using (var blastHits = Physics.OverlapSphere(part.transform.position, (blastRadius / 2), 9076737).AsEnumerable().GetEnumerator())
                    {
                        while (blastHits.MoveNext())
                        {
                            if (blastHits.Current == null) continue;
                            try
                            {
                                Part partHit = blastHits.Current.GetComponentInParent<Part>();
                                if (partHit != null && partHit.mass > 0)
                                {
                                    Rigidbody rb = partHit.Rigidbody;
                                    Vector3 distToG0 = part.transform.position - partHit.transform.position;

                                    Ray LoSRay = new Ray(part.transform.position, partHit.transform.position - part.transform.position);
                                    RaycastHit hit;
                                    if (Physics.Raycast(LoSRay, out hit, distToG0.magnitude, 9076737))
                                    {
                                        KerbalEVA eva = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                                        Part p = eva ? eva.part : hit.collider.gameObject.GetComponentInParent<Part>();
                                        if (p == partHit)
                                        {
                                            if (rb == null) return;
                                            ApplyDamage(p, hit);
                                        }
                                    }
                                }
                            }
                            catch
                            {
                            }
                        }
                    }
                }
                else //if (CASELevel == 2) //blast contained, shunted out side of hull, minimal damage
                {
                    ExplosionFx.CreateExplosion(part.transform.position, (float)ammoExplosionYield, shuntExploModelPath, explSoundPath, ExplosionSourceType.Missile, 0, part, vessel.vesselName, direction, true);
                    Debug.Log("[BD DEBUG] CASE II explosion, tntMassEquivilent: " + ammoExplosionYield);
                    Ray BlastRay = new Ray(part.transform.position, part.transform.up);
                    var hits = Physics.RaycastAll(BlastRay, blastRadius, 9076737);
                    if (hits.Length > 0)
                    {
                        var orderedHits = hits.OrderBy(x => x.distance);

                        using (var hitsEnu = orderedHits.GetEnumerator())
                        {
                            while (hitsEnu.MoveNext())
                            {
                                RaycastHit hit = hitsEnu.Current;
                                Part hitPart = null;
                                KerbalEVA hitEVA = null;

                                try
                                {
                                    hitPart = hit.collider.gameObject.GetComponentInParent<Part>();
                                    hitEVA = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                                }
                                catch (NullReferenceException)
                                {
                                    Debug.Log("[BDArmory]:NullReferenceException for AmmoExplosion Hit");
                                    return;
                                }

                                if (hitEVA != null)
                                {
                                    hitPart = hitEVA.part;
                                    if (hitPart?.rb != null)
                                        ApplyDamage(hitPart, hit);
                                    break;
                                }
                                ApplyDamage(hitPart, hit);
                            }
                        }
                    }
                }
                hasDetonated = true;
                part.Destroy();
                this.part.explode();
            }
        }
        private void ApplyDamage(Part hitPart, RaycastHit hit)
        {
            //hitting a vessel Part
            //No struts, they cause weird bugs :) -BahamutoD
            if (hitPart == null) return;
            if (hitPart.partInfo.name.Contains("Strut")) return;
            float ExplDamage = 0;

            if (BDArmorySettings.BULLET_HITS)
            {
                BulletHitFX.CreateBulletHit(hitPart, hit.point, hit, hit.normal, false, 200, 3);
            }
            if (CASELevel == 2)
            {
                ExplDamage = 100;
                hitPart.AddDamage(100);
                float ArmortoReduce = hitPart.GetArmorThickness() * 0.25f;
                hitPart.ReduceArmor(ArmortoReduce);
                Debug.Log("[BD DEBUG]" + hitPart.name + "damaged, armor reduced by " + ArmortoReduce);
            }
            else //CASE I
            {
                ExplDamage = (hitPart.Modules.GetModule<HitpointTracker>().GetMaxHitpoints() * 0.9f);
                hitPart.AddDamage(ExplDamage);
                Debug.Log("[BD DEBUG]" + hitPart.name + "damaged for " + ExplDamage);
                Misc.BattleDamageHandler.CheckDamageFX(hitPart, 200, 3, true, SourceVessel, hit);
            }
            {
                var aName = SourceVessel;
                var tName = part.vessel.GetName();

                if (aName != null && tName != null && aName != tName && BDACompetitionMode.Instance.Scores.ContainsKey(aName) && BDACompetitionMode.Instance.Scores.ContainsKey(tName))
                {
                    if (BDArmorySettings.REMOTE_LOGGING_ENABLED)
                    {
                        BDAScoreService.Instance.TrackDamage(aName, tName, ExplDamage);
                    }
                    var aData = BDACompetitionMode.Instance.Scores[aName];
                    aData.Score += 1;

                    if (part.vessel.GetName() == "Pinata")
                    {
                        aData.PinataHits++;
                    }

                    var tData = BDACompetitionMode.Instance.Scores[tName];
                    tData.lastPersonWhoHitMe = aName;
                    tData.lastHitTime = Planetarium.GetUniversalTime();
                    tData.everyoneWhoHitMe.Add(aName);
                    // Track hits
                    if (tData.hitCounts.ContainsKey(aName))
                        ++tData.hitCounts[aName];
                    else
                        tData.hitCounts.Add(aName, 1);
                    // Track damage
                    if (tData.damageFromBullets.ContainsKey(aName))
                        tData.damageFromBullets[aName] += ExplDamage;
                    else
                        tData.damageFromBullets.Add(aName, ExplDamage);

                }
            }
        }
        void OnDestroy()
        {
            if (BDArmorySettings.BD_VOLATILE_AMMO)
            {
                DetonateIfPossible();
            }
        }
    }
}

