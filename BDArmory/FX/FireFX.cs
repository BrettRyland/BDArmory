using System;
using System.Linq;
using UnityEngine;

using BDArmory.Competition;
using BDArmory.Damage;
using BDArmory.Extensions;
using BDArmory.Modules;
using BDArmory.Settings;
using BDArmory.UI;
using BDArmory.Utils;

namespace BDArmory.FX
{
    class FireFX : MonoBehaviour
    {
        Part parentPart;
        // string parentPartName = "";
        // string parentVesselName = "";

        public static ObjectPool CreateFireFXPool(string modelPath)
        {
            var template = GameDatabase.Instance.GetModel(modelPath);
            var decal = template.AddComponent<FireFX>();
            template.SetActive(false);
            return ObjectPool.CreateObjectPool(template, 10, true, true);
        }

        private float disableTime = -1;
        private float _highestEnergy = 1;
        public float burnTime = -1;
        private float burnScale = -1;
        private float startTime;
        public bool hasFuel = true;
        public float burnRate = 1;
        private float fireIntensity = 0;
        private float tntMassEquivalent = 0;
        public bool surfaceFire = false;
        private bool isSRB = false;
        public string SourceVessel;
        private string explModelPath = "BDArmory/Models/explosion/explosion";
        private string explSoundPath = "BDArmory/Sounds/explode1";
        const int explosionLayerMask = (int)(LayerMasks.Parts | LayerMasks.Scenery | LayerMasks.EVA | LayerMasks.Unknown19 | LayerMasks.Unknown23 | LayerMasks.Wheels); // Why 19 and 23?
        bool parentBeingDestroyed = false;

        PartResource fuel;
        PartResource solid;
        PartResource ox;
        PartResource ec;
        PartResource mp;

        private KerbalSeat Seat;
        ModuleEngines engine;
        // bool lookedForEngine = false;

        KSPParticleEmitter[] pEmitters;

        Collider[] blastHitColliders = new Collider[100];
        bool vacuum = false;
        void OnEnable()
        {
            if (parentPart == null || !HighLogic.LoadedSceneIsFlight)
            {
                gameObject.SetActive(false);
                return;
            }
            if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log($"[BDArmory.FireFX]: Fire added to {parentPart.name}" + (parentPart.vessel != null ? $" on {parentPart.vessel.vesselName}" : ""));
            hasFuel = true;
            tntMassEquivalent = 0;
            startTime = Time.time;
            engine = parentPart.FindModuleImplementing<ModuleEngines>();
            foreach (var existingLeakFX in parentPart.GetComponentsInChildren<FuelLeakFX>())
            {
                existingLeakFX.lifeTime = 0; //kill leak FX
            }
            solid = parentPart.Resources.Where(pr => pr.resourceName == "SolidFuel").FirstOrDefault();
            if (engine != null)
            {
                if (solid != null)
                {
                    isSRB = true;
                }
            }
            fireIntensity = burnRate;
            BDArmorySetup.numberOfParticleEmitters++;
            pEmitters = gameObject.GetComponentsInChildren<KSPParticleEmitter>();
            vacuum = FlightGlobals.getAtmDensity(FlightGlobals.getStaticPressure(transform.position), FlightGlobals.getExternalTemperature(), FlightGlobals.currentMainBody) < 0.05f;

            using (var pe = pEmitters.AsEnumerable().GetEnumerator())
                while (pe.MoveNext())
                {
                    if (pe.Current == null) continue;
                    pe.Current.emit = true;
                    _highestEnergy = pe.Current.maxEnergy;
                    if (vacuum)
                    {
                        pe.Current.localVelocity = new Vector3(0, (float)parentPart.vessel.obt_speed, 0);
                    }
                    EffectBehaviour.AddParticleEmitter(pe.Current);
                }

            Seat = null;
            if (parentPart.parent != null)
            {
                var kerbalSeats = parentPart.parent.Modules.OfType<KerbalSeat>();
                if (kerbalSeats.Count() > 0)
                    Seat = kerbalSeats.First();
            }
            if (parentPart.protoModuleCrew.Count > 0) //crew can extingusih fire
            {
                burnTime = 10;
            }
            if (parentPart.parent != null && parentPart.parent.protoModuleCrew.Count > 0 || (Seat != null && Seat.Occupant != null))
            {
                burnTime = 20; //though adjacent parts will take longer to get to and extingusih
            }
            if (!surfaceFire)
            {
                if (parentPart.GetComponent<ModuleSelfSealingTank>() != null)
                {
                    ModuleSelfSealingTank FBX;
                    FBX = parentPart.GetComponent<ModuleSelfSealingTank>();
                    FBX.Extinguishtank();
                    if (FBX.InertTank) burnTime = 0.01f; //check is looking for > 0, value of 0 not getting caught.
                    /*
                    if (FBX.FireBottles > 0)
                    {
                        //FBX.FireBottles -= 1;
                        if (engine != null && engine.EngineIgnited && engine.allowRestart)
                        {
                            engine.Shutdown();
                            enginerestartTime = Time.time;
                        }
                        burnTime = 4;
                        GUIUtils.RefreshAssociatedWindows(parentPart);
                        Debug.Log("[FireFX] firebottles remaining in " + parentPart.name + ": " + FBX.FireBottles);
                    }
                    else
                    {
                        if (engine != null && engine.EngineIgnited && engine.allowRestart)
                        {
                            if (parentPart.vessel.verticalSpeed < 30) //not diving/trying to climb. With the vessel registry, could also grab AI state to add a !evading check
                            {
                                engine.Shutdown();
                                enginerestartTime = Time.time + 5;
                                burnTime = 10;
                            }
                            //though if it is diving, then there isn't a second call to cycle engines. Add an Ienumerator to check once every couple sec?
                        }
                    }
                    */
                }
            }
            parentBeingDestroyed = false;
        }

        void OnDisable()
        {
            // Clean up emitters.
            if (pEmitters is not null)
            {
                --BDArmorySetup.numberOfParticleEmitters;
                foreach (var pe in pEmitters)
                    if (pe != null)
                    {
                        pe.emit = false;
                        EffectBehaviour.RemoveParticleEmitter(pe);
                    }
            }
            // Clean up part and resource references.
            parentPart = null;
            Seat = null;
            engine = null;
            fuel = null;
            solid = null;
            ox = null;
            ec = null;
            mp = null;
            tntMassEquivalent = 0;
            fireIntensity = 1;
        }

        void FixedUpdate()
        {
            if (!gameObject.activeInHierarchy || !HighLogic.LoadedSceneIsFlight || BDArmorySetup.GameIsPaused)
            {
                return;
            }
            if (vacuum) transform.rotation = Quaternion.FromToRotation(Vector3.up, parentPart.vessel.obt_velocity.normalized);
            else transform.rotation = Quaternion.FromToRotation(Vector3.up, parentPart.vessel.up);
            fuel = parentPart.Resources.Where(pr => pr.resourceName == "LiquidFuel").FirstOrDefault();
            if (disableTime < 0) //only have fire do it's stuff while burning and not during FX timeout
            {
                if (!surfaceFire) //is fire inside tank, or an incendiary substance on the part's surface?
                {
                    // if (!lookedForEngine) // This is done in OnEnable.
                    // {
                    //     engine = parentPart.FindModuleImplementing<ModuleEngines>();
                    //     lookedForEngine = true; //have this only called once, not once per update tick
                    // }
                    if (engine != null)
                    {
                        if (isSRB)
                        {
                            if (parentPart.RequestResource("SolidFuel", (double)(burnRate * TimeWarp.fixedDeltaTime)) <= 0)
                            {
                                hasFuel = false;
                            }
                            solid = parentPart.Resources.Where(pr => pr.resourceName == "SolidFuel").FirstOrDefault();
                            if (solid != null && solid.amount > 0)
                            {
                                if (solid.amount < solid.maxAmount * 0.66f)
                                {
                                    engine.Activate(); //SRB lights from unintended ignition source
                                }
                                if (solid.amount < solid.maxAmount * 0.15f)
                                {
                                    tntMassEquivalent += Mathf.Clamp((float)solid.amount, ((float)solid.maxAmount * 0.05f), ((float)solid.maxAmount * 0.2f));
                                    Detonate(); //casing's full of holes and SRB fuel's burnt to the point it can easily start venting through those holes
                                    return;
                                }
                            }
                        }
                        else
                        {
                            if (engine.EngineIgnited)
                            {
                                if (parentPart.RequestResource("LiquidFuel", (double)(burnRate * TimeWarp.fixedDeltaTime)) <= 0)
                                {
                                    hasFuel = false;
                                }
                            }
                            else
                            {
                                hasFuel = false;
                            }
                        }
                    }
                    else
                    {
                        if (fuel != null)
                        {
                            if (parentPart.vessel.InNearVacuum() && ox == null)
                            {
                                hasFuel = false;
                            }
                            else
                            {
                                if (fuel.amount > 0)
                                {
                                    if (fuel.amount > (fuel.maxAmount * 0.15f) || (fuel.amount > 0 && fuel.amount < (fuel.maxAmount * 0.10f)))
                                    {
                                        fireIntensity = (burnRate * Mathf.Clamp((float)((1 - (fuel.amount / fuel.maxAmount)) * 4), 0.1f * BDArmorySettings.BD_TANK_LEAK_RATE, 4 * BDArmorySettings.BD_TANK_LEAK_RATE) * TimeWarp.fixedDeltaTime);
                                        fuel.amount -= fireIntensity;
                                        burnScale = Mathf.Clamp((float)((1 - (fuel.amount / fuel.maxAmount)) * 4), 0.1f * BDArmorySettings.BD_TANK_LEAK_RATE, 2 * BDArmorySettings.BD_TANK_LEAK_RATE);
                                    }
                                    else if (fuel.amount < (fuel.maxAmount * 0.15f) && fuel.amount > (fuel.maxAmount * 0.10f))
                                    {
                                        Detonate();
                                        return;
                                    }
                                }
                                else
                                {
                                    hasFuel = false;
                                }
                            }
                        }
                        ox = parentPart.Resources.Where(pr => pr.resourceName == "Oxidizer").FirstOrDefault();
                        if (ox != null && fuel != null)
                        {
                            if (ox.amount > 0)
                            {
                                fireIntensity *= 1.2f;
                                ox.amount -= (burnRate * Mathf.Clamp((float)((1 - (ox.amount / ox.maxAmount)) * 4), 0.1f * BDArmorySettings.BD_TANK_LEAK_RATE, 4 * BDArmorySettings.BD_TANK_LEAK_RATE) * TimeWarp.fixedDeltaTime);
                            }
                            else
                            {
                                hasFuel = false;
                            }
                        }
                        mp = parentPart.Resources.Where(pr => pr.resourceName == "MonoPropellant").FirstOrDefault();
                        if (mp != null)
                        {
                            if (mp.amount > (mp.maxAmount * 0.15f) || (mp.amount > 0 && mp.amount < (mp.maxAmount * 0.10f)))
                            {
                                mp.amount -= (burnRate * Mathf.Clamp((float)((1 - (mp.amount / mp.maxAmount)) * 4), 0.1f * BDArmorySettings.BD_TANK_LEAK_RATE, 4 * BDArmorySettings.BD_TANK_LEAK_RATE) * TimeWarp.fixedDeltaTime);
                                if (burnScale < 0)
                                {
                                    burnScale = Mathf.Clamp((float)((1 - (mp.amount / mp.maxAmount)) * 4), 0.1f * BDArmorySettings.BD_TANK_LEAK_RATE, 2 * BDArmorySettings.BD_TANK_LEAK_RATE);
                                }
                            }
                            else if (mp.amount < (mp.maxAmount * 0.15f) && mp.amount > (mp.maxAmount * 0.10f))
                            {
                                Detonate();
                                return;
                            }
                            else
                            {
                                hasFuel = false;
                            }
                        }
                        ec = parentPart.Resources.Where(pr => pr.resourceName == "ElectricCharge").FirstOrDefault();
                        if (ec != null)
                        {
                            if (parentPart.vessel.InNearVacuum())
                            {
                                hasFuel = false;
                            }
                            else
                            {
                                if (ec.amount > 0)
                                {
                                    ec.amount -= (burnRate * TimeWarp.deltaTime);
                                    Mathf.Clamp((float)ec.amount, 0, Mathf.Infinity);
                                    if (burnScale < 0)
                                    {
                                        burnScale = 1;
                                    }
                                }
                                if ((Time.time - startTime > 30) && engine == null)
                                {
                                    Detonate();
                                    return;
                                }
                            }
                        }
                    }
                }
                if (BDArmorySettings.BD_FIRE_HEATDMG)
                {
                    if (parentPart.temperature < 1300)
                    {
                        if (fuel != null)
                        {
                            parentPart.temperature += burnRate * Mathf.Clamp((float)((1 - (fuel.amount / fuel.maxAmount)) * 4), 0.1f * BDArmorySettings.BD_TANK_LEAK_RATE, 4 * BDArmorySettings.BD_TANK_LEAK_RATE) * Time.deltaTime;
                        }
                        else if (mp != null)
                        {
                            parentPart.temperature += burnRate * Mathf.Clamp((float)((1 - (mp.amount / mp.maxAmount)) * 4), 0.1f * BDArmorySettings.BD_TANK_LEAK_RATE, 4 * BDArmorySettings.BD_TANK_LEAK_RATE) * Time.deltaTime;
                        }
                        else //if (ec != null || ox != null)
                        {
                            parentPart.temperature += burnRate * BDArmorySettings.BD_FIRE_DAMAGE * Time.fixedDeltaTime;
                        }
                    }
                }
                if (BDArmorySettings.BATTLEDAMAGE && BDArmorySettings.BD_FIRE_DOT)
                {
                    if (BDArmorySettings.BD_INTENSE_FIRES)
                    {
                        parentPart.AddDamage(fireIntensity * BDArmorySettings.BD_FIRE_DAMAGE * Time.fixedDeltaTime);
                    }
                    else
                    {
                        if (BDArmorySettings.ENABLE_HOS && BDArmorySettings.HALL_OF_SHAME_LIST.Contains(parentPart.vessel.GetName()))
                        {
                            parentPart.AddDamage(BDArmorySettings.HOS_FIRE * Time.fixedDeltaTime);
                        }
                        else
                            parentPart.AddDamage(BDArmorySettings.BD_FIRE_DAMAGE * Time.fixedDeltaTime);
                    }

                    BDACompetitionMode.Instance.Scores.RegisterBattleDamage(SourceVessel, parentPart.vessel, BDArmorySettings.BD_FIRE_DAMAGE * Time.fixedDeltaTime);
                }
            }
            if (disableTime < 0 && ((!hasFuel && burnTime < 0) || (burnTime >= 0 && Time.time - startTime > burnTime)))
            {
                disableTime = Time.time; //grab time when emission stops
                foreach (var pe in pEmitters)
                    if (pe != null)
                        pe.emit = false;
            }
            else
            {
                foreach (var pe in pEmitters)
                {
                    pe.minSize = burnScale;
                    pe.maxSize = burnScale * 1.2f;
                }
            }
            if (surfaceFire && parentPart.vessel.horizontalSrfSpeed > 120 && SourceVessel != "GM") //blow out surface fires if moving fast enough
            {
                burnTime = 5;
            }
            // Note: the following can set the parentPart to null.
            if (disableTime > 0 && Time.time - disableTime > _highestEnergy) //wait until last emitted particle has finished
            {
                Deactivate();
            }
            if (vacuum || !FlightGlobals.currentMainBody.atmosphereContainsOxygen && (ox == null && mp == null))
            {
                Deactivate(); //only fuel+oxy or monoprop fires in vac/non-oxy atmo
            }
            if (FlightGlobals.getAltitudeAtPos(transform.position) <= 0)
            {
                Deactivate(); //don't burn underwater
            }
        }

        void Detonate()
        {
            if (!HighLogic.LoadedSceneIsFlight) { Deactivate(); return; }
            if (surfaceFire) return;
            if (!BDArmorySettings.BD_FIRE_FUELEX) return;
            if (!parentPart.partName.Contains("exploding"))
            {
                bool excessFuel = false;
                parentPart.partName += "exploding";
                PartResource fuel = parentPart.Resources.Where(pr => pr.resourceName == "LiquidFuel").FirstOrDefault();
                PartResource ox = parentPart.Resources.Where(pr => pr.resourceName == "Oxidizer").FirstOrDefault();
                float tntFuel = 0, tntOx = 0, tntMP = 0, tntEC = 0;
                if (fuel != null && fuel.amount > 0)
                {
                    tntFuel = (Mathf.Clamp((float)fuel.amount, ((float)fuel.maxAmount * 0.05f), ((float)fuel.maxAmount * 0.2f)) / 2);
                    tntMassEquivalent += tntFuel;
                    if (fuel != null && (ox != null && ox.amount > 0))
                    {
                        tntOx = (Mathf.Clamp((float)ox.amount, ((float)ox.maxAmount * 0.1f), ((float)ox.maxAmount * 0.3f)) / 2);
                        tntMassEquivalent += tntOx;
                        tntMassEquivalent *= 1.3f;
                    }
                    if (fuel.amount > fuel.maxAmount * 0.3f)
                    {
                        excessFuel = true;
                    }
                }
                PartResource mp = parentPart.Resources.Where(pr => pr.resourceName == "MonoPropellant").FirstOrDefault();
                if (mp != null && mp.amount > 0)
                {
                    tntMP = (Mathf.Clamp((float)mp.amount, ((float)mp.maxAmount * 0.1f), ((float)mp.maxAmount * 0.3f)) / 3);
                    tntMassEquivalent += tntMP;
                    if (mp.amount > mp.maxAmount * 0.3f)
                    {
                        excessFuel = true;
                    }
                }
                tntMassEquivalent /= 6f; //make this not have a 1 to 1 ratio of fuelmass -> tntmass
                PartResource ec = parentPart.Resources.Where(pr => pr.resourceName == "ElectricCharge").FirstOrDefault();
                if (ec != null && ec.amount > 0)
                {
                    tntEC = ((float)ec.maxAmount / 5000); //fix for cockpit batteries weighing a tonne+
                    tntMassEquivalent += tntEC;
                    ec.maxAmount = 0;
                    ec.isVisible = false;
                    if (!parentBeingDestroyed) parentPart.RemoveResource(ec);//destroy battery. not calling part.destroy, since some batteries in cockpits.
                    GUIUtils.RefreshAssociatedWindows(parentPart);
                }
                //tntMassEquivilent *= BDArmorySettings.BD_AMMO_DMG_MULT; //handled by EXP_DMG_MOD_BATTLE_DAMAGE
                if (BDArmorySettings.DEBUG_OTHER && tntMassEquivalent > 0)
                {
                    Debug.Log("[BDArmory.FireFX]: Fuel Explosion in " + this.parentPart.name + ", TNT mass equivalent " + tntMassEquivalent + $" (Fuel: {tntFuel / 6f}, Ox: {tntOx / 6f}, MP: {tntMP / 6f}, EC: {tntEC})");
                }
                if (excessFuel)
                {
                    float blastRadius = BlastPhysicsUtils.CalculateBlastRange(tntMassEquivalent);
                    var hitCount = Physics.OverlapSphereNonAlloc(parentPart.transform.position, blastRadius, blastHitColliders, explosionLayerMask);
                    if (hitCount == blastHitColliders.Length)
                    {
                        blastHitColliders = Physics.OverlapSphere(parentPart.transform.position, blastRadius, explosionLayerMask);
                        hitCount = blastHitColliders.Length;
                    }
                    using (var blastHits = blastHitColliders.Take(hitCount).GetEnumerator())
                        while (blastHits.MoveNext())
                        {
                            if (blastHits.Current == null) continue;
                            try
                            {
                                Part partHit = blastHits.Current.GetComponentInParent<Part>();
                                if (partHit == null) continue;
                                if (ProjectileUtils.IsIgnoredPart(partHit)) continue; // Ignore ignored parts.
                                if (partHit.Modules.GetModule<HitpointTracker>().Hitpoints <= 0) continue; // Ignore parts that are already dead.
                                if (partHit.Rigidbody != null && partHit.mass > 0)
                                {
                                    Vector3 distToG0 = parentPart.transform.position - partHit.transform.position;

                                    Ray LoSRay = new Ray(parentPart.transform.position, partHit.transform.position - parentPart.transform.position);
                                    RaycastHit hit;
                                    if (Physics.Raycast(LoSRay, out hit, distToG0.magnitude, explosionLayerMask))
                                    {
                                        KerbalEVA eva = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                                        Part p = eva ? eva.part : hit.collider.gameObject.GetComponentInParent<Part>();
                                        if (p == partHit)
                                        {
                                            BulletHitFX.AttachFire(hit.point, p, 1, SourceVessel, BDArmorySettings.WEAPON_FX_DURATION * (1 - (distToG0.magnitude / blastRadius)), 1, true);
                                            if (BDArmorySettings.DEBUG_OTHER)
                                            {
                                                Debug.Log("[BDArmory.FireFX]: " + this.parentPart.name + " hit by burning fuel");
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                Debug.LogWarning("[BDArmory.FireFX]: Exception thrown in Detonate: " + e.Message + "\n" + e.StackTrace);
                            }
                        }
                }
                if (tntMassEquivalent > 0) //don't explode if nothing to detonate if called from OnParentDestroy()
                {
                    ExplosionFx.CreateExplosion(parentPart.transform.position, tntMassEquivalent, explModelPath, explSoundPath, ExplosionSourceType.BattleDamage, 120, null, parentPart.vessel != null ? parentPart.vessel.vesselName : null, null, "Fuel", sourceVelocity: parentPart.vessel.Velocity());
                    if (BDArmorySettings.RUNWAY_PROJECT_ROUND != 42)
                    {
                        if (tntFuel > 0 || tntMP > 0)
                        {
                            var tmpParentPart = parentPart; // Temporarily store the parent part so we can destroy it without destroying ourselves.
                            Deactivate();
                            tmpParentPart.Destroy();
                        }
                    }
                }
            }
            if (parentPart != null && parentPart.Modules.GetModule<HitpointTracker>().ignitionTemp < 0) Deactivate(); //wooden batteries keep burning
        }

        public void AttachAt(Part hitPart, Vector3 hit, Vector3 offset, string sourcevessel)
        {
            if (hitPart is null) return;
            parentPart = hitPart;
            // parentPartName = parentPart.name;
            // parentVesselName = parentPart.vessel.vesselName;
            transform.SetParent(hitPart.transform);
            transform.position = hit + offset;
            transform.rotation = Quaternion.FromToRotation(Vector3.up, hitPart.vessel.up);
            parentPart.OnJustAboutToDie += OnParentDestroy;
            parentPart.OnJustAboutToBeDestroyed += OnParentDestroy;
            if ((Versioning.version_major == 1 && Versioning.version_minor > 10) || Versioning.version_major > 1) // onVesselUnloaded event introduced in 1.11
                OnVesselUnloaded_1_11(true); // Catch unloading events too.
            SourceVessel = sourcevessel;
            gameObject.SetActive(true);
        }

        public void OnParentDestroy()
        {
            if (parentPart is not null)
            {
                parentBeingDestroyed = true;
                parentPart.OnJustAboutToDie -= OnParentDestroy;
                parentPart.OnJustAboutToBeDestroyed -= OnParentDestroy;
                if (!surfaceFire) Detonate();
                Deactivate();
            }
        }

        public void OnVesselUnloaded(Vessel vessel)
        {
            if (parentPart is not null && (parentPart.vessel is null || parentPart.vessel == vessel))
            {
                OnParentDestroy();
            }
            else if (parentPart is null)
            {
                Deactivate(); // Sometimes (mostly when unloading a vessel) the parent becomes null without triggering OnParentDestroy.
            }
        }

        void OnVesselUnloaded_1_11(bool addRemove) // onVesselUnloaded event introduced in 1.11
        {
            if (addRemove)
                GameEvents.onVesselUnloaded.Add(OnVesselUnloaded);
            else
                GameEvents.onVesselUnloaded.Remove(OnVesselUnloaded);
        }

        void Deactivate()
        {
            if (gameObject is not null && gameObject.activeSelf) // Deactivate even if a parent is already inactive.
            {
                disableTime = -1;
                parentPart = null;
                transform.parent = null; // Detach ourselves from the parent transform so we don't get destroyed when it does.
                gameObject.SetActive(false);
            }
            if ((Versioning.version_major == 1 && Versioning.version_minor > 10) || Versioning.version_major > 1) // onVesselUnloaded event introduced in 1.11
                OnVesselUnloaded_1_11(false);
        }

        void OnDestroy() // This shouldn't be happening except on exiting KSP, but sometimes they get destroyed instead of disabled!
        {
            // if (HighLogic.LoadedSceneIsFlight) Debug.LogError($"[BDArmory.FireFX]: FireFX on {parentPartName} ({parentVesselName}) was destroyed!");
            // Clean up emitters.
            if (pEmitters is not null && pEmitters.Any(pe => pe is not null))
            {
                BDArmorySetup.numberOfParticleEmitters--;
                foreach (var pe in pEmitters)
                    if (pe != null)
                    {
                        pe.emit = false;
                        EffectBehaviour.RemoveParticleEmitter(pe);
                    }
            }
            if ((Versioning.version_major == 1 && Versioning.version_minor > 10) || Versioning.version_major > 1) // onVesselUnloaded event introduced in 1.11
                OnVesselUnloaded_1_11(false);
        }
    }
}
