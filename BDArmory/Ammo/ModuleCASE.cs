using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using BDArmory.Competition;
using BDArmory.Extensions;
using BDArmory.FX;
using BDArmory.Settings;
using BDArmory.Utils;
using BDArmory.VesselSpawning;
using BDArmory.Weapons;
using BDArmory.UI;
using BDArmory.Weapons.Missiles;

namespace BDArmory.Ammo
{
    class ModuleCASE : BDAPartModule, IPartMassModifier, IPartCostModifier
    {
        public static Dictionary<int, ObjectPool> detSpheres = new Dictionary<int, ObjectPool>();
        GameObject visSphere;
        Renderer r_sphere;
        GameObject visDome;
        Renderer r_dome;
        public float GetModuleMass(float baseMass, ModifierStagingSituation situation) => CASEmass;
        public ModifierChangeWhen GetModuleMassChangeWhen() => ModifierChangeWhen.FIXED;
        public float GetModuleCost(float baseCost, ModifierStagingSituation situation) => CASEcost;
        public ModifierChangeWhen GetModuleCostChangeWhen() => ModifierChangeWhen.FIXED;

        private double ammoMass = 0;
        private double ammoQuantity = 0;
        private double ammoExplosionYield = 0;

        private string explModelPath = "BDArmory/Models/explosion/explosion";
        private string explSoundPath = "BDArmory/Sounds/explode1";

        private string limitEdexploModelPath = "BDArmory/Models/explosion/30mmExplosion";
        private string shuntExploModelPath = "BDArmory/Models/explosion/CASEexplosion";
        private string detDomeModelpath = "BDArmory/Models/explosion/detHemisphere";
        public string SourceVessel = "";
        public bool hasDetonated = false;
        private float blastRadius = -1;
        const int explosionLayerMask = (int)(LayerMasks.Parts | LayerMasks.Scenery | LayerMasks.EVA | LayerMasks.Unknown19 | LayerMasks.Unknown23 | LayerMasks.Wheels);

        public bool externallyCalled = false;

        public override void OnStart(StartState state)
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                part.explosionPotential = 1.0f;
                part.force_activate();
            }
        }
        [KSPField(guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_AddedMass")]//CASE mass

        public float CASEmass = 0f;

        private float CASEcost = 0f;
        // private float origCost = 0;
        private float origMass = 0f;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_CASE"),//Cellular Ammo Storage Equipment Tier
        UI_FloatRange(minValue = 0f, maxValue = 2f, stepIncrement = 1f, scene = UI_Scene.All, affectSymCounterparts = UI_Scene.All)]
        public float CASELevel = 0; //tier of ammo storage. 0 = nothing, ammosplosion; 1 = base, ammosplosion contained(barely), 2 = blast safely shunted outside, minimal damage to surrounding parts

        [KSPField(guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_CASE_Sim"),//Detonation Sim
UI_FloatRange(minValue = 0f, maxValue = 100, stepIncrement = 0.5f, scene = UI_Scene.All, affectSymCounterparts = UI_Scene.All)]
        public float blastSim = 0;

        [KSPField(isPersistant = true)]
        public bool Case2 = false;

        private List<double> resourceAmount = new List<double>();

        static RaycastHit[] raycastHitBuffer = new RaycastHit[10]; // This gets enlarged as needed and is shared amongst all ModuleCASE instances.

        ModuleAmmoMagazine AdjustableBin = null;

        public void Start()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                AdjustableBin = part.FindModuleImplementing<ModuleAmmoMagazine>();
                var internalmag = part.FindModuleImplementing<ModuleWeapon>();
                if (internalmag != null)
                {
                    Fields[nameof(CASELevel)].guiActiveEditor = false;
                    Fields[nameof(CASEmass)].guiActiveEditor = false;
                }
                else
                {
                    using (IEnumerator<PartResource> resource = part.Resources.GetEnumerator())
                        while (resource.MoveNext())
                        {
                            if (resource.Current == null) continue;
                            resourceAmount.Add(resource.Current.maxAmount);
                        }
                    UI_FloatRange ATrangeEditor = (UI_FloatRange)Fields[nameof(CASELevel)].uiControlEditor;
                    ATrangeEditor.onFieldChanged = CASESetup;
                    origMass = part.mass;
                    //origScale = part.rescaleFactor;
                    CASESetup(null, null);
                }

                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                var collider = sphere.GetComponent<Collider>();
                if (collider)
                {
                    collider.enabled = false;
                    Destroy(collider);
                }
                Renderer r = sphere.GetComponent<Renderer>();
                var shader = Shader.Find("KSP/Alpha/Unlit Transparent");
                r.material = new Material(shader);
                r.receiveShadows = false;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.material.color = new Color(Color.red.r, 0, 0, 0.35f);
                r.enabled = true;
                sphere.SetActive(false);
                detSpheres[0] = ObjectPool.CreateObjectPool(sphere, 10, true, true);

                var dome = GameDatabase.Instance.GetModel(detDomeModelpath);
                if (dome == null)
                {
                    Debug.LogError("[BDArmory.ModuleCase]: model '" + detDomeModelpath + "' not found.");
                    dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    var dc = dome.GetComponent<Collider>();
                    if (dc)
                    {
                        dc.enabled = false;
                        Destroy(dc);
                    }
                }
                Renderer d = dome.GetComponentInChildren<Renderer>();
                if (d != null)
                {
                    d.material = new Material(Shader.Find("KSP/Particles/Alpha Blended"));
                    d.material.SetColor("_TintColor", Color.blue);
                }

                dome.SetActive(false);
                detSpheres[1] = ObjectPool.CreateObjectPool(dome, 10, true, true);
                if (detSpheres[0] != null)
                {
                    visSphere = detSpheres[0].GetPooledObject();
                    visSphere.transform.SetPositionAndRotation(transform.position, transform.rotation);
                    visSphere.transform.localScale = Vector3.zero;
                    r_sphere = visSphere.GetComponent<Renderer>();
                }
                if (detSpheres[1] != null)
                {
                    visDome = detSpheres[1].GetPooledObject();
                    visDome.transform.SetPositionAndRotation(transform.position, transform.rotation);
                    visDome.transform.localScale = Vector3.zero;
                    r_dome = visDome.GetComponentInChildren<Renderer>();
                }
            }
            if (HighLogic.LoadedSceneIsFlight)
            {
                SourceVessel = part.vessel.GetName(); //set default to vesselname for cases where no attacker, i.e. Ammo exploding on destruction cooking off adjacent boxes
                GameEvents.onGameSceneSwitchRequested.Add(HandleSceneChange);
            }
        }

        public void HandleSceneChange(GameEvents.FromToAction<GameScenes, GameScenes> fromTo)
        {
            if (fromTo.from == GameScenes.FLIGHT)
            { hasDetonated = true; } // Don't trigger explosions on scene changes.
        }

        void CASESetup(BaseField field, object obj)
        {
            if (externallyCalled) return;
            //CASEmass = ((origMass / 2) * CASELevel);
            CASEmass = (0.05f * CASELevel); //+50kg per level
            //part.mass = CASEmass;
            CASEcost = (CASELevel * 1000);
            //part.transform.localScale = (Vector3.one * (origScale + (CASELevel/10)));
            //Debug.Log("[BDArmory.ModuleCASE] part.mass = " + part.mass + "; CASElevel = " + CASELevel + "; CASEMass = " + CASEmass + "; Scale = " + part.transform.localScale);
            if (!AdjustableBin)
            {
                if (Case2 && CASELevel != 2)
                {
                    int i = 0;
                    using (IEnumerator<PartResource> resource = part.Resources.GetEnumerator())
                        while (resource.MoveNext())
                        {
                            if (resource.Current == null) continue;
                            //if (resource.Current.maxAmount < 80) //original value < 100, at risk of fractional amount
                            {
                                resource.Current.maxAmount = resourceAmount[i];
                                GUIUtils.RefreshPAWResource(part, resource.Current);
                            }
                            //else resource.Current.maxAmount = Math.Floor(resource.Current.maxAmount * 1.25);
                            i++;
                        }
                }
                if (!Case2 && CASELevel == 2)
                {
                    using (IEnumerator<PartResource> resource = part.Resources.GetEnumerator())
                        while (resource.MoveNext())
                        {
                            if (resource.Current == null) continue;
                            resource.Current.maxAmount *= 0.8;
                            resource.Current.maxAmount = Math.Floor(resource.Current.maxAmount);
                            resource.Current.amount = Math.Min(resource.Current.amount, resource.Current.maxAmount);
                            GUIUtils.RefreshPAWResource(part, resource.Current);
                        }
                }

                using (List<Part>.Enumerator pSym = part.symmetryCounterparts.GetEnumerator())
                    while (pSym.MoveNext())
                    {
                        if (pSym.Current == null) continue;

                        var CASE = pSym.Current.FindModuleImplementing<ModuleCASE>();
                        if (CASE == null) continue;
                        CASE.externallyCalled = true;
                        CASE.CASELevel = CASELevel;
                        CASE.CASEmass = CASEmass;
                        CASE.CASEcost = CASEcost;

                        if (CASE.Case2 && CASE.CASELevel != 2)
                        {
                            using (IEnumerator<PartResource> resource = pSym.Current.Resources.GetEnumerator())
                                while (resource.MoveNext())
                                {
                                    if (resource.Current == null) continue;
                                    resource.Current.maxAmount = Math.Floor(resource.Current.maxAmount * 1.25);
                                    resource.Current.amount = Math.Min(resource.Current.amount, resource.Current.maxAmount);
                                    GUIUtils.RefreshPAWResource(pSym.Current, resource.Current);
                                }
                        }
                        if (!CASE.Case2 && CASE.CASELevel == 2)
                        {
                            using (IEnumerator<PartResource> resource = pSym.Current.Resources.GetEnumerator())
                                while (resource.MoveNext())
                                {
                                    if (resource.Current == null) continue;
                                    resource.Current.maxAmount *= 0.8;
                                    resource.Current.amount = Math.Min(resource.Current.amount, resource.Current.maxAmount);
                                    GUIUtils.RefreshPAWResource(pSym.Current, resource.Current);
                                }
                        }
                        CASE.Case2 = CASE.CASELevel == 2 ? true : false;
                        CASE.externallyCalled = false;
                    }
            }
            else
            {
                if ((Case2 && CASELevel != 2) || (!Case2 && CASELevel == 2))
                {
                    AdjustableBin.hasCASEII = CASELevel == 2;
                    AdjustableBin.AmmoVolumeChanged();
                }

                using (List<Part>.Enumerator pSym = part.symmetryCounterparts.GetEnumerator())
                    while (pSym.MoveNext())
                    {
                        if (pSym.Current == null) continue;

                        var CASE = pSym.Current.FindModuleImplementing<ModuleCASE>();
                        if (CASE == null) continue;
                        CASE.externallyCalled = true;
                        CASE.CASELevel = CASELevel;
                        CASE.CASEmass = CASEmass;
                        CASE.CASEcost = CASEcost;
                        if (CASE.AdjustableBin != null)
                        {
                            if ((CASE.Case2 && CASE.CASELevel != 2) || (!CASE.Case2 && CASE.CASELevel == 2))
                            {
                                CASE.AdjustableBin.hasCASEII = CASELevel == 2;
                                CASE.AdjustableBin.AmmoVolumeChanged();
                            }
                        }
                        CASE.Case2 = CASE.CASELevel == 2 ? true : false;
                        CASE.externallyCalled = false;
                    }
            }
            Case2 = CASELevel == 2 ? true : false;
        }
        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            if (!HighLogic.LoadedSceneIsEditor && !HighLogic.LoadedSceneIsFlight) return;
            var internalmag = part.FindModuleImplementing<ModuleWeapon>();
            if (internalmag == null)
            {
                CASESetup(null, null); //don't apply mass/cost to weapons with integral ammo protection, assume it's baked into weapon mass/cost
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
            ammoMass = 0;
            ammoQuantity = 0;
            ammoExplosionYield = 0;
            blastRadius = 0;
            foreach (PartResource resource in GetResources())
            {
                var resources = part.Resources.ToList();
                using (IEnumerator<PartResource> ammo = resources.GetEnumerator())
                    while (ammo.MoveNext())
                    {
                        if (ammo.Current == null) continue;
                        if (ammo.Current.resourceName == resource.resourceName)
                        {
                            ammoMass = ammo.Current.info.density;
                            ammoQuantity = ammo.Current.amount;
                            ammoExplosionYield += (((ammoMass * 1000) * ammoQuantity) / 20);
                        }
                    }
            }
            if (ammoExplosionYield > 0)
            {
                switch (CASELevel)
                {
                    case 1:
                        ammoExplosionYield /= 2;
                        break;
                    case 2:
                        ammoExplosionYield /= 4;
                        break;
                    default:
                        break;
                }
                blastRadius = BlastPhysicsUtils.CalculateBlastRange(ammoExplosionYield * BDArmorySettings.EXP_DMG_MOD_BATTLE_DAMAGE);
            }
        }
        public float GetBlastRadius()
        {
            //if (blastRadius >= 0 && HighLogic.LoadedSceneIsEditor) return blastRadius; //only calc blast radius once in Editor if F2 weapon alignment/blast visualization enabeld
            CalculateBlast();
            return blastRadius;
        }
        public void DetonateIfPossible()
        {
            if (hasDetonated || part == null || part.vessel == null || !part.vessel.loaded || part.vessel.packed) return;
            hasDetonated = true; // Set hasDetonated here to avoid recursive calls due to ammo boxes exploding each other.
            var vesselName = vessel != null ? vessel.vesselName : null;
            Vector3 direction = default(Vector3);
            GetBlastRadius();
            if (ammoExplosionYield <= 0) return;
            if (CASELevel != 2) //a considerable quantity of explosives and propellants just detonated inside your ship
            {
                if (CASELevel == 0)
                {
                    ExplosionFx.CreateExplosion(part.transform.position, (float)ammoExplosionYield, explModelPath, explSoundPath, ExplosionSourceType.BattleDamage, 120, part, SourceVessel, null, $"{part.partInfo.title} ({Math.Floor(ammoQuantity)} rounds)(CASE-0)", direction, -1, false, part.mass + ((float)ammoExplosionYield * 10f), 1200 * BDArmorySettings.EXP_DMG_MOD_BATTLE_DAMAGE);
                    if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log("[BDArmory.ModuleCASE]: CASE 0 explosion, tntMassEquivilent: " + ammoExplosionYield);
                }
                else
                {
                    direction = part.transform.up;
                    ExplosionFx.CreateExplosion(part.transform.position, ((float)ammoExplosionYield), limitEdexploModelPath, explSoundPath, ExplosionSourceType.BattleDamage, 60, part, SourceVessel, null, $"{part.partInfo.title} ({Math.Floor(ammoQuantity)} rounds)(CASE-I)", direction, -1, false, part.mass + ((float)ammoExplosionYield * 10f), 600 * BDArmorySettings.EXP_DMG_MOD_BATTLE_DAMAGE);
                    if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log("[BDArmory.ModuleCASE]: CASE I explosion, tntMassEquivilent: " + ammoExplosionYield + ", part: " + part + ", vessel: " + vesselName);
                }
            }
            else //if (CASELevel == 2) //blast contained, shunted out side of hull, minimal damage
            {
                ExplosionFx.CreateExplosion(part.transform.position, (float)ammoExplosionYield, shuntExploModelPath, explSoundPath, ExplosionSourceType.BattleDamage, 30, part, SourceVessel, null, $"{part.partInfo.title} ({Math.Floor(ammoQuantity)} rounds)(CASE-II)", direction, -1, true);
                if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log("[BDArmory.ModuleCASE]: CASE II explosion, tntMassEquivilent: " + ammoExplosionYield);
                Ray BlastRay = new Ray(part.transform.position, part.transform.up);
                var hitCount = Physics.RaycastNonAlloc(BlastRay, raycastHitBuffer, blastRadius, explosionLayerMask);
                if (hitCount == raycastHitBuffer.Length) // If there's a whole bunch of stuff in the way (unlikely), then we need to increase the size of our hits buffer.
                {
                    raycastHitBuffer = Physics.RaycastAll(BlastRay, blastRadius, explosionLayerMask);
                    hitCount = raycastHitBuffer.Length;
                    if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log($"[BDArmory.ModuleCASE]: Enlarging hit raycast buffer size to {hitCount}.");
                }
                if (hitCount > 0)
                {
                    var orderedHits = raycastHitBuffer.Take(hitCount).OrderBy(x => x.distance);
                    using (var hitsEnu = orderedHits.GetEnumerator())
                    {
                        while (hitsEnu.MoveNext())
                        {
                            RaycastHit hit = hitsEnu.Current;
                            Part hitPart = null;
                            KerbalEVA hitEVA = null;

                            if (FlightGlobals.currentMainBody == null || hit.collider.gameObject != FlightGlobals.currentMainBody.gameObject)
                            {
                                try
                                {
                                    hitPart = hit.collider.gameObject.GetComponentInParent<Part>();
                                    hitEVA = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                                }
                                catch (NullReferenceException e)
                                {
                                    Debug.LogError("[BDArmory.ModuleCASE]: NullReferenceException for AmmoExplosion Hit: " + e.Message + "\n" + e.StackTrace);
                                    continue;
                                }

                                if (hitPart == null || hitPart == part) continue;
                                if (ProjectileUtils.IsIgnoredPart(hitPart)) continue; // Ignore ignored parts.


                                if (hitEVA != null)
                                {
                                    hitPart = hitEVA.part;
                                    if (hitPart.rb != null)
                                        ApplyDamage(hitPart, hit);
                                    break;
                                }

                                if (hitPart.vessel != part.vessel)
                                {
                                    float dist = (part.transform.position - hitPart.transform.position).magnitude;

                                    Ray LoSRay = new Ray(part.transform.position, hitPart.transform.position - part.transform.position);
                                    RaycastHit LOShit;
                                    if (Physics.Raycast(LoSRay, out LOShit, dist, explosionLayerMask))
                                    {
                                        if (FlightGlobals.currentMainBody == null || LOShit.collider.gameObject != FlightGlobals.currentMainBody.gameObject)
                                        {
                                            KerbalEVA eva = LOShit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                                            Part p = eva ? eva.part : LOShit.collider.gameObject.GetComponentInParent<Part>();
                                            /*if (p == hitPart)
                                            {
                                                ProjectileUtils.CalculateShrapnelDamage(hitPart, hit, 200, (float)ammoExplosionYield, dist, this.part.vessel.GetName(), ExplosionSourceType.BattleDamage, part.mass);
                                            }*/
                                        }
                                    }
                                }
                                else
                                {
                                    ApplyDamage(hitPart, hit);
                                }
                            }
                        }
                    }
                }
            }
            if (part.vessel != null) // Already in the process of being destroyed.
                part.Destroy();
        }
        private void ApplyDamage(Part hitPart, RaycastHit hit)
        {
            //hitting a vessel Part
            //No struts, they cause weird bugs :) -BahamutoD
            if (hitPart == null) return;
            if (hitPart.partInfo.name.Contains("Strut")) return;
            float explDamage;
            if (BDArmorySettings.BULLET_HITS)
            {
                BulletHitFX.CreateBulletHit(hitPart, hit.point, hit, hit.normal, false, 200, 3, null);
            }

            explDamage = 100;
            explDamage = Mathf.Clamp(explDamage, 0, ((float)ammoExplosionYield * 10));
            explDamage *= BDArmorySettings.EXP_DMG_MOD_BATTLE_DAMAGE;
            hitPart.AddDamage(explDamage);
            float armorToReduce = hitPart.GetArmorThickness() * 0.25f;
            hitPart.ReduceArmor(armorToReduce);

            if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log("[BDArmory.ModuleCASE]: " + hitPart.name + " damaged, armor reduced by " + armorToReduce);

            BDACompetitionMode.Instance.Scores.RegisterBattleDamage(SourceVessel, hitPart.vessel, explDamage);
        }

        void OnDestroy()
        {
            if (BDArmorySettings.BATTLEDAMAGE && BDArmorySettings.BD_AMMOBINS && BDArmorySettings.BD_VOLATILE_AMMO && HighLogic.LoadedSceneIsFlight && !VesselSpawnerStatus.vesselsSpawning)
            {
                if (!hasDetonated) DetonateIfPossible();
            }
            GameEvents.onGameSceneSwitchRequested.Remove(HandleSceneChange);
            if (visSphere != null) visSphere.SetActive(false);
            if (visDome != null) visDome.SetActive(false);
        }

        void OnGUI()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                bool disableCASESimulation = false;
                if (BDArmorySettings.BD_AMMOBINS) //having this on showWeaponAlignment could get really annoying if lots of ammo boxes on a craft and merely wanting to calibrate guns
                {
                    if (BDArmorySetup.showCASESimulation || blastSim >= 1)
                        DrawDetonationVisualization(); //though perhaps a per-box visualizer toggle would be smarter than a global one?
                    else if (blastTimeline > 0) disableCASESimulation = true;
                }
                else if (blastTimeline > 0) disableCASESimulation = true;
                if (disableCASESimulation)
                {
                    visSphere.SetActive(false);
                    visDome.SetActive(false);
                    simStartTime = 0;
                }
            }
        }

        float simStartTime = 0;
        float blastTimeline = 0;
        float simTimer => Time.time - simStartTime;
        Color blastColor = Color.red;
        public static FloatCurve blastCurve = new([
            new(0, 1640, -2922.85f, -2922.85f),
            new(1, 128, -81.1f, -81.1f),
            new(5, 20, 7.24f, 7.24f),
            new(10, 10),
            new(20, 7),
            new(40, 1)]); //'close enough' approximation for the rather more complex geometry of the actual blast dmg equations

        void DrawDetonationVisualization()
        {
            Vector2 guiPos;
            GetBlastRadius();
            if (!BDArmorySetup.showCASESimulation) simStartTime = 0;
            else if (simTimer > 5) simStartTime = Time.time; //another possible improvement would have a 'sim blast range' slider that would allow seeing damage at specific range instead of cycling the anim
            blastTimeline = BDArmorySetup.showCASESimulation ? Mathf.Clamp01(simTimer / 2) : blastSim / 100;
            float blastDmg = Mathf.Clamp(blastCurve.Evaluate(blastRadius * blastTimeline) + (11 - (blastRadius * blastTimeline * 0.4f)) * (float)ammoExplosionYield, 0, 1200) / (CASELevel == 1 ? 2 : 1); //CASE I clamps to 600, so mult CAS 0 dmg to maintian color per x dmg value
            blastColor = Color.HSVToRGB((((CASELevel == 1 ? 600 : 1200) - (float)blastDmg) / (CASELevel == 1 ? 600 : 1200)) / 4, 1, 1); //yellow = 200dmg, green, less, orange-> , more

            switch (CASELevel)
            {
                case 0:
                    visDome.SetActive(false);
                    visSphere.SetActive(true);
                    visSphere.transform.position = transform.position;
                    visSphere.transform.localScale = Vector3.one * Mathf.Lerp(0, blastRadius, blastTimeline);
                    r_sphere.material.color = new Color(blastColor.r, blastColor.g, blastColor.b, (1.3f - blastTimeline) / 2);
                    break;
                case 1:
                    visSphere.SetActive(false);
                    visDome.SetActive(true);
                    r_dome.material.SetColor("_TintColor", new Color(blastColor.r, blastColor.g, blastColor.b, 0.15f - (blastTimeline / 10)));
                    visDome.transform.position = transform.position;
                    visDome.transform.localScale = Vector3.one * Mathf.Lerp(0, blastRadius, blastTimeline);
                    visDome.transform.rotation = transform.rotation;
                    break;
                case 2:
                    Vector3 fwdPos = transform.position + (blastTimeline * blastRadius * transform.up);
                    GUIUtils.DrawLineBetweenWorldPositions(transform.position, fwdPos, 4, Color.red);
                    visSphere.SetActive(false);
                    visDome.SetActive(false);
                    blastDmg = Mathf.Clamp(blastDmg, 0, 100);
                    break;
            }
            if (GUIUtils.WorldToGUIPos(transform.position, out guiPos))
            {
                Rect labelRect = new Rect(guiPos.x + 64, guiPos.y + 32, 200, 100);
                string label = $"{Mathf.Round(blastDmg)} damage at {Math.Round(blastTimeline * blastRadius, 2)}m";
                GUI.Label(labelRect, label);
            }
        }

        public override string GetInfo()
        {
            StringBuilder output = new StringBuilder();
            output.Append(Environment.NewLine);
            var internalmag = part.FindModuleImplementing<ModuleWeapon>();
            if (internalmag != null)
            {
                output.AppendLine($" Has Intrinsic C.A.S.E. Type {CASELevel}");
            }
            else
            {
                output.AppendLine($"Can add Cellular Ammo Storage Equipment to reduce ammo explosion damage");
            }

            output.AppendLine("");

            return output.ToString();
        }
        void FixedUpdate()
        {
            if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && !vessel.packed)
            {
                if (BDArmorySettings.BD_FIRES_ENABLED && BDArmorySettings.BD_FIRE_HEATDMG)
                {
                    if (hasDetonated) return;
                    if (this.part.temperature > 900) //ammo cooks off, part is too hot
                    {
                        if (!hasDetonated) DetonateIfPossible();
                    }
                }
            }
        }
    }
}
