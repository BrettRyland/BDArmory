using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using BDArmory.Armor;
using BDArmory.Competition;
using BDArmory.Damage;
using BDArmory.Extensions;
using BDArmory.GameModes;
using BDArmory.Settings;
using BDArmory.Utils;
using BDArmory.Weapons;

namespace BDArmory.FX
{
    public class ExplosionFx : MonoBehaviour
    {
        public static Dictionary<string, ObjectPool> explosionFXPools = new Dictionary<string, ObjectPool>();
        public KSPParticleEmitter[] pEmitters { get; set; }
        public Light LightFx { get; set; }
        public float StartTime { get; set; }
        public AudioClip ExSound { get; set; }
        public AudioSource audioSource { get; set; }
        private float MaxTime { get; set; }
        public float Range { get; set; }
        public float Caliber { get; set; }
        public float ProjMass { get; set; }
        public ExplosionSourceType ExplosionSource { get; set; }
        public string SourceVesselName { get; set; }
        public string SourceWeaponName { get; set; }
        public float Power { get; set; }
        public Vector3 Position { get; set; }
        public Vector3 Direction { get; set; }
        public float AngleOfEffect { get; set; }
        public Part ExplosivePart { get; set; }
        public bool isFX { get; set; }
        public float CASEClamp { get; set; }
        public float dmgMult { get; set; }

        public Part hitpart { get; set; }
        public float TimeIndex => Time.time - StartTime;

        private bool disabled = true;

        float blastRange;
        int explosionLayerMask = (int)(LayerMasks.Parts | LayerMasks.Scenery | LayerMasks.EVA | LayerMasks.Unknown19 | LayerMasks.Unknown23); // Why 19 and 23?

        Queue<BlastHitEvent> explosionEvents = new Queue<BlastHitEvent>();
        List<BlastHitEvent> explosionEventsPreProcessing = new List<BlastHitEvent>();
        List<Part> explosionEventsPartsAdded = new List<Part>();
        List<DestructibleBuilding> explosionEventsBuildingAdded = new List<DestructibleBuilding>();
        Dictionary<string, int> explosionEventsVesselsHit = new Dictionary<string, int>();


        static RaycastHit[] lineOfSightHits;
        static RaycastHit[] reverseHits;
        static Collider[] overlapSphereColliders;
        public static List<Part> IgnoreParts;
        public static List<DestructibleBuilding> IgnoreBuildings;
        internal static readonly float ExplosionVelocity = 422.75f;

        private float particlesMaxEnergy;
        internal static HashSet<ExplosionSourceType> ignoreCasingFor = new HashSet<ExplosionSourceType> { ExplosionSourceType.Missile, ExplosionSourceType.Rocket };
        public enum WarheadTypes
        {
            Standard,
            ShapedCharge,
            ContinuousRod
        }

        public WarheadTypes warheadType;

        void Awake()
        {
            if (lineOfSightHits == null) { lineOfSightHits = new RaycastHit[100]; }
            if (reverseHits == null) { reverseHits = new RaycastHit[100]; }
            if (overlapSphereColliders == null) { overlapSphereColliders = new Collider[1000]; }
            if (IgnoreParts == null) { IgnoreParts = new List<Part>(); }
            if (IgnoreBuildings == null) { IgnoreBuildings = new List<DestructibleBuilding>(); }
        }

        private void OnEnable()
        {
            StartTime = Time.time;
            disabled = false;
            MaxTime = Mathf.Sqrt((Range / ExplosionVelocity) * 3f) * 2f; // Scale MaxTime to get a reasonable visualisation of the explosion.
            blastRange = warheadType == WarheadTypes.Standard ? Range * 2 : Range; //to properly account for shrapnel hits when compiling list of hit parts from the spherecast
            if (!isFX)
            {
                CalculateBlastEvents();
            }
            pEmitters = gameObject.GetComponentsInChildren<KSPParticleEmitter>();
            foreach (var pe in pEmitters)
                if (pe != null)
                {
                    if (pe.maxEnergy > particlesMaxEnergy)
                        particlesMaxEnergy = pe.maxEnergy;
                    pe.emit = true;
                    var emission = pe.ps.emission;
                    emission.enabled = true;
                    EffectBehaviour.AddParticleEmitter(pe);
                }

            LightFx = gameObject.GetComponent<Light>();
            LightFx.range = Range * 3f;

            if (BDArmorySettings.DEBUG_DAMAGE)
            {
                Debug.Log("[BDArmory.ExplosionFX]: Explosion started tntMass: {" + Power + "}  BlastRadius: {" + Range + "} StartTime: {" + StartTime + "}, Duration: {" + MaxTime + "}");
            }
            /*
            if (BDArmorySettings.PERSISTENT_FX && Caliber > 30 && BodyUtils.GetRadarAltitudeAtPos(transform.position) > Caliber / 60)
            {
                if (FlightGlobals.getAltitudeAtPos(transform.position) > Caliber / 60)
                {
                    FXEmitter.CreateFX(Position, (Caliber / 30), "BDArmory/Models/explosion/flakSmoke", "", 0.3f, Caliber / 6);                   
                }
            }
            */
        }

        void OnDisable()
        {
            foreach (var pe in pEmitters)
            {
                if (pe != null)
                {
                    pe.emit = false;
                    EffectBehaviour.RemoveParticleEmitter(pe);
                }
            }
            ExplosivePart = null; // Clear the Part reference.
            explosionEvents.Clear(); // Make sure we don't have any left over events leaking memory.
            explosionEventsPreProcessing.Clear();
            explosionEventsPartsAdded.Clear();
            explosionEventsBuildingAdded.Clear();
            explosionEventsVesselsHit.Clear();
        }

        private void CalculateBlastEvents()
        {
            //Let's convert this temporal list on a ordered queue
            // using (var enuEvents = temporalEventList.OrderBy(e => e.TimeToImpact).GetEnumerator())
            using (var enuEvents = ProcessingBlastSphere().OrderBy(e => e.TimeToImpact).GetEnumerator())
            {
                while (enuEvents.MoveNext())
                {
                    if (enuEvents.Current == null) continue;

                    if (BDArmorySettings.DEBUG_DAMAGE)
                    {
                        Debug.Log("[BDArmory.ExplosionFX]: Enqueueing Blast Event");
                    }

                    explosionEvents.Enqueue(enuEvents.Current);
                }
            }
        }

        private List<BlastHitEvent> ProcessingBlastSphere()
        {
            explosionEventsPreProcessing.Clear();
            explosionEventsPartsAdded.Clear();
            explosionEventsBuildingAdded.Clear();
            explosionEventsVesselsHit.Clear();

            string sourceVesselName = null;
            if (BDACompetitionMode.Instance)
            {
                switch (ExplosionSource)
                {
                    case ExplosionSourceType.Missile:
                        var explosivePart = ExplosivePart ? ExplosivePart.FindModuleImplementing<BDExplosivePart>() : null;
                        sourceVesselName = explosivePart ? explosivePart.sourcevessel.GetName() : SourceVesselName;
                        break;
                    default: // Everything else.
                        sourceVesselName = SourceVesselName;
                        break;
                }
            }
            if (warheadType == WarheadTypes.ShapedCharge)
            {
                Ray SCRay = new Ray(Position, (Direction.normalized * Range));
                var hits = Physics.RaycastAll(SCRay, Range, explosionLayerMask);
                if (BDArmorySettings.DEBUG_ARMOR) Debug.Log("[BDArmory.ExplosionFX]: SC plasmaJet raycast hits: " + hits.Length);
                if (hits.Length > 0)
                {
                    var orderedHits = hits.OrderBy(x => x.distance);

                    using (var hitsEnu = orderedHits.GetEnumerator())
                    {
                        while (hitsEnu.MoveNext())
                        {
                            RaycastHit SChit = hitsEnu.Current;
                            Part hitPart = null;

                            hitPart = SChit.collider.gameObject.GetComponentInParent<Part>();

                            if (hitPart != null)
                            {
                                if (ProjectileUtils.IsIgnoredPart(hitPart)) continue; // Ignore ignored parts.
                                if (hitPart.vessel.GetName() == SourceVesselName) continue;  //avoid autohit;
                                if (hitPart.mass > 0 && !explosionEventsPartsAdded.Contains(hitPart))
                                {
                                    var damaged = ProcessPartEvent(hitPart, sourceVesselName, explosionEventsPreProcessing, explosionEventsPartsAdded, true);
                                    // If the explosion derives from a missile explosion, count the parts damaged for missile hit scores.
                                    if (damaged && BDACompetitionMode.Instance)
                                    {
                                        bool registered = false;
                                        var damagedVesselName = hitPart.vessel != null ? hitPart.vessel.GetName() : null;
                                        switch (ExplosionSource)
                                        {
                                            case ExplosionSourceType.Rocket:
                                                if (BDACompetitionMode.Instance.Scores.RegisterRocketHit(sourceVesselName, damagedVesselName, 1))
                                                    registered = true;
                                                break;
                                            case ExplosionSourceType.Missile:
                                                if (BDACompetitionMode.Instance.Scores.RegisterMissileHit(sourceVesselName, damagedVesselName, 1))
                                                    registered = true;
                                                break;
                                        }
                                        if (registered)
                                        {
                                            if (explosionEventsVesselsHit.ContainsKey(damagedVesselName))
                                                ++explosionEventsVesselsHit[damagedVesselName];
                                            else
                                                explosionEventsVesselsHit[damagedVesselName] = 1;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                DestructibleBuilding building = SChit.collider.gameObject.GetComponentUpwards<DestructibleBuilding>();

                                if (building != null)
                                {
                                    if (!explosionEventsBuildingAdded.Contains(building))
                                    {
                                        ProcessBuildingEvent(building, explosionEventsPreProcessing, explosionEventsBuildingAdded);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            var overlapSphereColliderCount = Physics.OverlapSphereNonAlloc(Position, blastRange, overlapSphereColliders, explosionLayerMask);
            if (overlapSphereColliderCount == overlapSphereColliders.Length)
            {
                overlapSphereColliders = Physics.OverlapSphere(Position, blastRange, explosionLayerMask);
                overlapSphereColliderCount = overlapSphereColliders.Length;
            }
            using (var hitCollidersEnu = overlapSphereColliders.Take(overlapSphereColliderCount).ToList().GetEnumerator())
            {
                while (hitCollidersEnu.MoveNext())
                {
                    if (hitCollidersEnu.Current == null) continue;

                    Part partHit = hitCollidersEnu.Current.GetComponentInParent<Part>();
                    if (partHit == null) continue;

                    if (partHit != null)
                    {
                        if (ProjectileUtils.IsIgnoredPart(partHit)) continue; // Ignore ignored parts.
                        if (partHit.mass > 0 && !explosionEventsPartsAdded.Contains(partHit))
                        {
                            var damaged = ProcessPartEvent(partHit, sourceVesselName, explosionEventsPreProcessing, explosionEventsPartsAdded);
                            // If the explosion derives from a missile explosion, count the parts damaged for missile hit scores.
                            if (damaged && BDACompetitionMode.Instance)
                            {
                                bool registered = false;
                                var damagedVesselName = partHit.vessel != null ? partHit.vessel.GetName() : null;
                                switch (ExplosionSource)
                                {
                                    case ExplosionSourceType.Rocket:
                                        if (BDACompetitionMode.Instance.Scores.RegisterRocketHit(sourceVesselName, damagedVesselName, 1))
                                            registered = true;
                                        break;
                                    case ExplosionSourceType.Missile:
                                        if (BDACompetitionMode.Instance.Scores.RegisterMissileHit(sourceVesselName, damagedVesselName, 1))
                                            registered = true;
                                        break;
                                }
                                if (registered)
                                {
                                    if (explosionEventsVesselsHit.ContainsKey(damagedVesselName))
                                        ++explosionEventsVesselsHit[damagedVesselName];
                                    else
                                        explosionEventsVesselsHit[damagedVesselName] = 1;
                                }
                            }
                        }
                    }
                    else
                    {
                        DestructibleBuilding building = hitCollidersEnu.Current.GetComponentInParent<DestructibleBuilding>();

                        if (building != null)
                        {
                            if (!explosionEventsBuildingAdded.Contains(building))
                            {
                                ProcessBuildingEvent(building, explosionEventsPreProcessing, explosionEventsBuildingAdded);
                            }
                        }
                    }
                }
            }
            if (explosionEventsVesselsHit.Count > 0)
            {
                if (ExplosionSource != ExplosionSourceType.Rocket) // Bullet explosions aren't registered in explosionEventsVesselsHit.
                {
                    string message = "";
                    foreach (var vesselName in explosionEventsVesselsHit.Keys)
                        message += (message == "" ? "" : " and ") + vesselName + " had " + explosionEventsVesselsHit[vesselName];
                    if (ExplosionSource == ExplosionSourceType.Missile)
                    {
                        message += " parts damaged due to missile strike";
                    }
                    else //ExplosionType BattleDamage || Other
                    {
                        message += " parts damaged due to explosion";
                    }
                    message += (SourceWeaponName != null ? " (" + SourceWeaponName + ")" : "") + (sourceVesselName != null ? " from " + sourceVesselName : "") + ".";
                    BDACompetitionMode.Instance.competitionStatus.Add(message);
                }
                // Note: damage hasn't actually been applied to the parts yet, just assigned as events, so we can't know if they survived.
                foreach (var vesselName in explosionEventsVesselsHit.Keys) // Note: sourceVesselName is already checked for being in the competition before damagedVesselName is added to explosionEventsVesselsHitByMissiles, so we don't need to check it here.
                {
                    switch (ExplosionSource)
                    {
                        case ExplosionSourceType.Rocket:
                            BDACompetitionMode.Instance.Scores.RegisterRocketStrike(sourceVesselName, vesselName);
                            break;
                        case ExplosionSourceType.Missile:
                            BDACompetitionMode.Instance.Scores.RegisterMissileStrike(sourceVesselName, vesselName);
                            break;
                    }
                }
            }
            return explosionEventsPreProcessing;
        }

        private void ProcessBuildingEvent(DestructibleBuilding building, List<BlastHitEvent> eventList, List<DestructibleBuilding> buildingAdded)
        {
            Ray ray = new Ray(Position, building.transform.position - Position);
            RaycastHit rayHit;
            if (Physics.Raycast(ray, out rayHit, Range, explosionLayerMask))
            {
                //TODO: Maybe we are not hitting building because we are hitting explosive parts.

                DestructibleBuilding destructibleBuilding = rayHit.collider.gameObject.GetComponentUpwards<DestructibleBuilding>();

                // Is not a direct hit, because we are hitting a different part
                if (destructibleBuilding != null && destructibleBuilding.Equals(building) && building.IsIntact)
                {
                    var distance = Vector3.Distance(Position, rayHit.point);
                    eventList.Add(new BuildingBlastHitEvent() { Distance = Vector3.Distance(Position, rayHit.point), Building = building, TimeToImpact = distance / ExplosionVelocity });
                    buildingAdded.Add(building);
                }
            }
        }

        private bool ProcessPartEvent(Part part, string sourceVesselName, List<BlastHitEvent> eventList, List<Part> partsAdded, bool angleOverride = false)
        {
            RaycastHit hit;
            float distance = 0;
            List<Tuple<float, float, float>> intermediateParts;
            if (IsInLineOfSight(part, ExplosivePart, out hit, out distance, out intermediateParts))
            {
                //if (IsAngleAllowed(Direction, hit))
                //{
                //Adding damage hit
                if (distance <= Range)//part within blast
                {
                    eventList.Add(new PartBlastHitEvent()
                    {
                        Distance = distance,
                        Part = part,
                        TimeToImpact = distance / ExplosionVelocity,
                        HitPoint = hit.point,
                        Hit = hit,
                        SourceVesselName = sourceVesselName,
                        IntermediateParts = intermediateParts,
                        withinAngleofEffect = angleOverride ? true : (IsAngleAllowed(Direction, hit, part))
                    });
                }
                if (warheadType == WarheadTypes.Standard && ProjMass > 0 && distance <= blastRange) //maybe move this to ExecutePartBlastHitEvent so shrap hits aren't instantaneous
                {
                    ProjectileUtils.CalculateShrapnelDamage(part, hit, Caliber, Power, distance, sourceVesselName, ExplosionSource, ProjMass); //part hit by shrapnel, but not pressure wave
                }
                partsAdded.Add(part);
                return true;
                //}
            }
            return false;
        }

        private bool IsAngleAllowed(Vector3 direction, RaycastHit hit, Part p)
        {
            if (direction == default(Vector3))
            {
                //if (BDArmorySettings.DEBUG_LABELS) Debug.Log("[BDArmory.ExplosionFX]: Default Direction param! " + p.name + " angle from explosion dir irrelevant!");
                return true;
            }
            if (warheadType == WarheadTypes.ContinuousRod)
            {
                if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log("[BDArmory.ExplosionFX]: " + p.name + " at " + Vector3.Angle(direction, (hit.point - Position).normalized) + " angle from CR explosion direction");
                if (Vector3.Angle(direction, (hit.point - Position).normalized) >= 60 && Vector3.Angle(direction, (hit.point - Position).normalized) <= 90)
                {
                    return true;
                }
                else return false;
            }
            else
            {
                if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log("[BDArmory.ExplosionFX]: " + p.name + " at " + Vector3.Angle(direction, (hit.point - Position).normalized) + $" angle from {warheadType} explosion direction");
                return (Vector3.Angle(direction, (hit.point - Position).normalized) <= AngleOfEffect);
            }
        }

        /// <summary>
        /// This method will calculate if there is valid line of sight between the explosion origin and the specific Part
        /// In order to avoid collisions with the same missile part, It will not take into account those parts belonging to same vessel that contains the explosive part
        /// </summary>
        /// <param name="part"></param>
        /// <param name="explosivePart"></param>
        /// <param name="hit"> out property with the actual hit</param>
        /// <returns></returns>
        private bool IsInLineOfSight(Part part, Part explosivePart, out RaycastHit hit, out float distance, out List<Tuple<float, float, float>> intermediateParts)
        {
            Ray partRay = new Ray(Position, part.transform.position - Position);

            var hitCount = Physics.RaycastNonAlloc(partRay, lineOfSightHits, blastRange, explosionLayerMask);
            if (hitCount == lineOfSightHits.Length) // If there's a whole bunch of stuff in the way (unlikely), then we need to increase the size of our hits buffer.
            {
                lineOfSightHits = Physics.RaycastAll(partRay, blastRange, explosionLayerMask);
                hitCount = lineOfSightHits.Length;
            }
            int reverseHitCount = 0;
            //check if explosion is originating inside a part
            reverseHitCount = Physics.RaycastNonAlloc(new Ray(part.transform.position - Position, Position), reverseHits, blastRange, explosionLayerMask);
            if (reverseHitCount == reverseHits.Length)
            {
                reverseHits = Physics.RaycastAll(new Ray(part.transform.position - Position, Position), blastRange, explosionLayerMask);
                reverseHitCount = reverseHits.Length;
            }
            for (int i = 0; i < reverseHitCount; ++i)
            { reverseHits[i].distance = blastRange - reverseHits[i].distance; }

            intermediateParts = new List<Tuple<float, float, float>>();

            using (var hitsEnu = lineOfSightHits.Take(hitCount).Concat(reverseHits.Take(reverseHitCount)).OrderBy(x => x.distance).GetEnumerator())
                while (hitsEnu.MoveNext())
                {
                    Part partHit = hitsEnu.Current.collider.GetComponentInParent<Part>();
                    if (partHit == null) continue;
                    if (ProjectileUtils.IsIgnoredPart(partHit)) continue; // Ignore ignored parts.
                    hit = hitsEnu.Current;
                    distance = hit.distance;
                    if (partHit == part)
                    {
                        return true;
                    }
                    if (partHit != part)
                    {
                        // ignoring collisions against the explosive, or explosive vessel for certain explosive types (e.g., missile/rocket casing)
                        if (partHit == explosivePart || (explosivePart != null && ignoreCasingFor.Contains(ExplosionSource) && partHit.vessel == explosivePart.vessel))
                        {
                            continue;
                        }
                        if (FlightGlobals.currentMainBody != null && hit.collider.gameObject == FlightGlobals.currentMainBody.gameObject) return false; // Terrain hit. Full absorption. Should avoid NREs in the following.
                        var partHP = partHit.Damage();
						if (ProjectileUtils.IsArmorPart(partHit)) partHP = 100;
                        var partArmour = partHit.GetArmorThickness();
                        var RA = partHit.FindModuleImplementing<ModuleReactiveArmor>();
                        if (RA != null)
                        {
                            if (RA.NXRA)
                            {
                                partArmour *= RA.armorModifier;
                            }
                            else
                            {
                                if (((ExplosionSource == ExplosionSourceType.Bullet || ExplosionSource == ExplosionSourceType.Rocket) && (Caliber > RA.sensitivity && distance < 0.1f)) ||   //bullet/rocket hit
                                    ((ExplosionSource == ExplosionSourceType.Missile || ExplosionSource == ExplosionSourceType.BattleDamage) && (distance < Power / 2))) //or close range detonation likely to trigger ERA
                                {
                                    partArmour = 300 * RA.armorModifier;
                                }
                            }
                        }
                        if (partHP > 0) // Ignore parts that are already dead but not yet removed from the game.
                            intermediateParts.Add(new Tuple<float, float, float>(hit.distance, partHP, partArmour));
                    }
                }

            hit = new RaycastHit();
            distance = 0;
            return false;
        }

        public void Update()
        {
            if (!gameObject.activeInHierarchy) return;

            if (LightFx != null) LightFx.intensity -= 12 * Time.deltaTime;

            if (!disabled && TimeIndex > 0.3f && pEmitters != null) // 0.3s seems to be enough to always show the explosion, but 0.2s isn't for some reason.
            {
                foreach (var pe in pEmitters)
                {
                    if (pe == null) continue;
                    pe.emit = false;
                }
                disabled = true;
            }
        }

        public void FixedUpdate()
        {
            if (!gameObject.activeInHierarchy) return;

            //floating origin and velocity offloading corrections
            if (!FloatingOrigin.Offset.IsZero() || !Krakensbane.GetFrameVelocity().IsZero())
            {
                transform.position -= FloatingOrigin.OffsetNonKrakensbane;
            }
            if (!isFX)
            {
                while (explosionEvents.Count > 0 && explosionEvents.Peek().TimeToImpact <= TimeIndex)
                {
                    BlastHitEvent eventToExecute = explosionEvents.Dequeue();

                    var partBlastHitEvent = eventToExecute as PartBlastHitEvent;
                    if (partBlastHitEvent != null)
                    {
                        ExecutePartBlastEvent(partBlastHitEvent);
                    }
                    else
                    {
                        ExecuteBuildingBlastEvent((BuildingBlastHitEvent)eventToExecute);
                    }
                }
            }

            if (disabled && explosionEvents.Count == 0 && TimeIndex > MaxTime)
            {
                if (BDArmorySettings.DEBUG_OTHER)
                {
                    Debug.Log("[BDArmory.ExplosionFX]: Explosion Finished");
                }

                gameObject.SetActive(false);
                return;
            }
        }

        void OnGUI()
        {
            if (HighLogic.LoadedSceneIsFlight && BDArmorySettings.DEBUG_LINES)
            {
                if (warheadType == WarheadTypes.ContinuousRod)
                {
                    if (explosionEventsPartsAdded.Count > 0)
                    {
                        for (int i = 0; i < explosionEventsPartsAdded.Count; i++)
                        {
                            RaycastHit hit;
                            float distance;
                            List<Tuple<float, float, float>> intermediateParts;

                            try
                            {
                                Part part = explosionEventsPartsAdded[i];
                                if (IsInLineOfSight(part, null, out hit, out distance, out intermediateParts))
                                {
                                    if (IsAngleAllowed(Direction, hit, explosionEventsPartsAdded[i]))
                                    {
                                        GUIUtils.DrawLineBetweenWorldPositions(Position, hit.point, 2, Color.blue);
                                    }
                                    else if (distance < Range / 2)
                                    {
                                        GUIUtils.DrawLineBetweenWorldPositions(Position, hit.point, 2, Color.red);
                                    }
                                }
                            }
                            catch
                            {
                                Debug.Log("[BDArmory.ExplosioNFX] nullref in ContinuousRod Debug lines in  onGUI");
                            }
                        }
                    }
                }
                if (warheadType == WarheadTypes.ShapedCharge)
                {
                    GUIUtils.DrawLineBetweenWorldPositions(Position, (Position + (Direction.normalized * Range)), 4, Color.green);
                }
            }
        }


        private void ExecuteBuildingBlastEvent(BuildingBlastHitEvent eventToExecute)
        {
            //TODO: Review if the damage is sensible after so many changes
            //buildings
            DestructibleBuilding building = eventToExecute.Building;
            building.damageDecay = 600f;

            if (building)
            {
                var distanceFactor = Mathf.Clamp01((Range - eventToExecute.Distance) / Range);
                float damageToBuilding = (BDArmorySettings.DMG_MULTIPLIER / 100) * BDArmorySettings.EXP_DMG_MOD_BALLISTIC_NEW * Power * distanceFactor;

                damageToBuilding *= 2f;

                building.AddDamage(damageToBuilding);

                if (building.Damage > building.impactMomentumThreshold)
                {
                    building.Demolish();
                }
                if (BDArmorySettings.DEBUG_DAMAGE)
                {
                    Debug.Log("[BDArmory.ExplosionFX]: Explosion hit destructible building! Hitpoints Applied: " + Mathf.Round(damageToBuilding) +
                             ", Building Damage : " + Mathf.Round(building.Damage) +
                             " Building Threshold : " + building.impactMomentumThreshold);
                }
            }
        }

        private void ExecutePartBlastEvent(PartBlastHitEvent eventToExecute)
        {
            if (eventToExecute.Part == null || eventToExecute.Part.Rigidbody == null || eventToExecute.Part.vessel == null || eventToExecute.Part.partInfo == null) return;

            Part part = eventToExecute.Part;
            Rigidbody rb = part.Rigidbody;
            var realDistance = eventToExecute.Distance;
            var vesselMass = part.vessel.totalMass;
            if (vesselMass == 0) vesselMass = part.mass; // Sometimes if the root part is the only part of the vessel, then part.vessel.totalMass is 0, despite the part.mass not being 0.

            if (!eventToExecute.IsNegativePressure)
            {
                BlastInfo blastInfo;

                if (eventToExecute.withinAngleofEffect) //within AoE of shaped warheads, or otherwise standard blast
                {
                    blastInfo = BlastPhysicsUtils.CalculatePartBlastEffects(part, realDistance, vesselMass * 1000f, Power, Range);
                }
                else //majority of force concentrated in blast cone for shaped warheads, not going to apply much force to stuff outside 
                {
                    if (realDistance > Range / 2) //further away than half the blast range, falloff blast effect outside primary AoE
                    {
                        blastInfo = BlastPhysicsUtils.CalculatePartBlastEffects(part, realDistance, vesselMass * 1000f, Power / 4, Range / 2);
                    }
                    else return;
                }
                //if (BDArmorySettings.DEBUG_LABELS) Debug.Log("[BDArmory.ExplosionFX]: " + part.name + " Within AoE of detonation: " + eventToExecute.withinAngleofEffect);
                // Overly simplistic approach: simply reduce damage by amount of HP/2 and Armor in the way. (HP/2 to simulate weak parts not fully blocking damage.) Does not account for armour reduction or angle of incidence of intermediate parts.
                // A better approach would be to properly calculate the damage and pressure in CalculatePartBlastEffects due to the series of parts in the way.
                var damageWithoutIntermediateParts = blastInfo.Damage;
                var cumulativeHPOfIntermediateParts = eventToExecute.IntermediateParts.Select(p => p.Item2).Sum();
                var cumulativeArmorOfIntermediateParts = eventToExecute.IntermediateParts.Select(p => p.Item3).Sum();
                blastInfo.Damage = Mathf.Max(0f, blastInfo.Damage - 0.5f * cumulativeHPOfIntermediateParts - cumulativeArmorOfIntermediateParts);

                if (CASEClamp > 0)
                {
                    if (CASEClamp < 1000)
                    {
                        blastInfo.Damage = Mathf.Clamp(blastInfo.Damage, 0, Mathf.Min((part.Modules.GetModule<HitpointTracker>().GetMaxHitpoints() * 0.9f), CASEClamp));
                    }
                    else
                    {
                        blastInfo.Damage = Mathf.Clamp(blastInfo.Damage, 0, CASEClamp);
                    }
                }

                if (blastInfo.Damage > 0)
                {
                    if (BDArmorySettings.DEBUG_DAMAGE)
                    {
                        Debug.Log(
                            "[BDArmory.ExplosionFX]: Executing blast event Part: {" + part.name + "}, " +
                            " VelocityChange: {" + blastInfo.VelocityChange + "}," +
                            " Distance: {" + realDistance + "}," +
                            " TotalPressure: {" + blastInfo.TotalPressure + "}," +
                            " Damage: {" + blastInfo.Damage + "} (reduced from " + damageWithoutIntermediateParts + " by " + eventToExecute.IntermediateParts.Count + " parts)," +
                            " EffectiveArea: {" + blastInfo.EffectivePartArea + "}," +
                            " Positive Phase duration: {" + blastInfo.PositivePhaseDuration + "}," +
                            " Vessel mass: {" + Math.Round(vesselMass * 1000f) + "}," +
                            " TimeIndex: {" + TimeIndex + "}," +
                            " TimePlanned: {" + eventToExecute.TimeToImpact + "}," +
                            " NegativePressure: {" + eventToExecute.IsNegativePressure + "}");
                    }

                    // Add Reverse Negative Event
                    explosionEvents.Enqueue(new PartBlastHitEvent()
                    {
                        Distance = Range - realDistance,
                        Part = part,
                        TimeToImpact = 2 * (Range / ExplosionVelocity) + (Range - realDistance) / ExplosionVelocity,
                        IsNegativePressure = true,
                        NegativeForce = blastInfo.VelocityChange * 0.25f
                    });

                    if (rb != null && rb.mass > 0 && !BDArmorySettings.PAINTBALL_MODE)
                    {
                        AddForceAtPosition(rb,
                            (eventToExecute.HitPoint + rb.velocity * TimeIndex - Position).normalized *
                            blastInfo.VelocityChange *
                            BDArmorySettings.EXP_IMP_MOD,
                            eventToExecute.HitPoint + rb.velocity * TimeIndex);
                    }
                    var damage = 0f;
                    float penetrationFactor = 0.5f;
                    if (dmgMult < 0)
                    {
                        part.AddInstagibDamage();
                        //if (BDArmorySettings.DEBUG_LABELS) Debug.Log("[BDArmory.ExplosionFX]: applying instagib!");
                    }
                    var RA = part.FindModuleImplementing<ModuleReactiveArmor>();

                    if (RA != null && !RA.NXRA && (ExplosionSource == ExplosionSourceType.Bullet || ExplosionSource == ExplosionSourceType.Rocket) && (Caliber > RA.sensitivity && realDistance < 0.1f)) //bullet/rocket hit
                    {
                        RA.UpdateSectionScales();
                    }
                    else
                    {
                        if ((warheadType == WarheadTypes.ShapedCharge || warheadType == WarheadTypes.ContinuousRod) && eventToExecute.withinAngleofEffect)
                        {
                            float HitAngle = Vector3.Angle((eventToExecute.HitPoint + rb.velocity * TimeIndex - Position).normalized, -eventToExecute.Hit.normal);
                            float anglemultiplier = (float)Math.Cos(Math.PI * HitAngle / 180.0);
                            float thickness = ProjectileUtils.CalculateThickness(part, anglemultiplier);
                            if (BDArmorySettings.DEBUG_ARMOR) Debug.Log("[BDArmory.ExplosiveFX]: Part " + part.name + " hit by " + warheadType + "; " + HitAngle + " deg hit, armor thickness: " + thickness);
                            thickness += eventToExecute.IntermediateParts.Select(p => p.Item3).Sum(); //add armor thickness of intervening parts, if any
                            if (BDArmorySettings.DEBUG_ARMOR) Debug.Log("[BDArmory.ExplosiveFX]: Effective Armor thickness from intermediate parts: " + thickness);
                            float penetration = 0;
                            var Armor = part.FindModuleImplementing<HitpointTracker>();
                            if (Armor != null)
                            {
                                float Ductility = Armor.Ductility;
                                float hardness = Armor.Hardness;
                                float Strength = Armor.Strength;
                                float safeTemp = Armor.SafeUseTemp;
                                float Density = Armor.Density;
                                int type = (int)Armor.ArmorTypeNum;

                                penetration = ProjectileUtils.CalculatePenetration(Caliber, Caliber, warheadType == WarheadTypes.ShapedCharge ? Power / 2 : ProjMass, ExplosionVelocity, Ductility, Density, Strength, thickness, 1);
                                penetrationFactor = ProjectileUtils.CalculateArmorPenetration(part, penetration, thickness);

                                if (RA != null)
                                {
                                    if (penetrationFactor > 1)
                                    {
                                        float thicknessModifier = RA.armorModifier;
                                        if (BDArmorySettings.DEBUG_ARMOR) Debug.Log("[BDArmory.ExplosionFX]: Beginning Reactive Armor Hit; NXRA: " + RA.NXRA + "; thickness Mod: " + RA.armorModifier);
                                        if (RA.NXRA) //non-explosive RA, always active
                                        {
                                            thickness *= thicknessModifier;
                                        }
                                        else
                                        {
                                            RA.UpdateSectionScales();
                                        }
                                    }
                                    penetrationFactor = ProjectileUtils.CalculateArmorPenetration(part, penetration, thickness); //RA stop round?
                                }
                                else ProjectileUtils.CalculateArmorDamage(part, penetrationFactor, Caliber, hardness, Ductility, Density, ExplosionVelocity, SourceVesselName, ExplosionSourceType.Missile, type);
                            }
                            BulletHitFX.CreateBulletHit(part, eventToExecute.HitPoint, eventToExecute.Hit, eventToExecute.Hit.normal, true, Caliber, penetrationFactor, null);
                            if (penetrationFactor > 1)
                            {
                                damage = part.AddExplosiveDamage(blastInfo.Damage, Caliber, ExplosionSource, dmgMult);
                                if (float.IsNaN(damage)) Debug.LogError("DEBUG NaN damage!");
                            }
                        }
                        else
                        {
                            if ((part == hitpart && ProjectileUtils.IsArmorPart(part)) || !ProjectileUtils.CalculateExplosiveArmorDamage(part, blastInfo.TotalPressure, SourceVesselName, eventToExecute.Hit, ExplosionSource, Range-realDistance)) //false = armor blowthrough or bullet detonating inside part
                            {
                                if (RA != null && !RA.NXRA) //blast wave triggers RA; detonate all remaining RA sections
                                {
                                    for (int i = 0; i < RA.sectionsRemaining; i++)
                                    {
                                        RA.UpdateSectionScales();
                                    }
                                }
                                else
                                {
                                    damage = part.AddExplosiveDamage(blastInfo.Damage, Caliber, ExplosionSource, dmgMult);
                                    if (part == hitpart && ProjectileUtils.IsArmorPart(part)) //deal armor damage to armor panel, since we didn't do that earlier
                                    {
                                        ProjectileUtils.CalculateExplosiveArmorDamage(part, blastInfo.TotalPressure, SourceVesselName, eventToExecute.Hit, ExplosionSource, Range - realDistance); 
                                    }
                                    penetrationFactor = damage / 10; //closer to the explosion/greater magnitude of the explosion at point blank, the greater the blowthrough
                                    if (float.IsNaN(damage)) Debug.LogError("DEBUG NaN damage!");
                                }
                            }
                        }
                        if (damage > 0) //else damage from spalling done in CalcExplArmorDamage
                        {
                            if (BDArmorySettings.BATTLEDAMAGE)
                            {
                                BattleDamageHandler.CheckDamageFX(part, Caliber, penetrationFactor, true, warheadType == WarheadTypes.ShapedCharge ? true : false, SourceVesselName, eventToExecute.Hit);
                            }
                            // Update scoring structures
                            //damage = Mathf.Clamp(damage, 0, part.Damage()); //if we want to clamp overkill score inflation
                            var aName = eventToExecute.SourceVesselName; // Attacker
                            var tName = part.vessel.GetName(); // Target
                            switch (ExplosionSource)
                            {
                                case ExplosionSourceType.Bullet:
                                    BDACompetitionMode.Instance.Scores.RegisterBulletDamage(aName, tName, damage);
                                    break;
                                case ExplosionSourceType.Rocket:
                                    BDACompetitionMode.Instance.Scores.RegisterRocketDamage(aName, tName, damage);
                                    break;
                                case ExplosionSourceType.Missile:
                                    BDACompetitionMode.Instance.Scores.RegisterMissileDamage(aName, tName, damage);
                                    break;
                                case ExplosionSourceType.BattleDamage:
                                    BDACompetitionMode.Instance.Scores.RegisterBattleDamage(aName, part.vessel, damage);
                                    break;
                            }
                        }
                    }
                }
                else if (BDArmorySettings.DEBUG_DAMAGE)
                {
                    Debug.Log("[BDArmory.ExplosiveFX]: Part " + part.name + " at distance " + realDistance + "m took no damage due to parts with " + cumulativeHPOfIntermediateParts + "HP and " + cumulativeArmorOfIntermediateParts + " Armor in the way.");
                }
            }
            else
            {
                if (BDArmorySettings.DEBUG_DAMAGE)
                {
                    Debug.Log(
                        "[BDArmory.ExplosionFX]: Executing blast event Part: {" + part.name + "}, " +
                        " VelocityChange: {" + eventToExecute.NegativeForce + "}," +
                        " Distance: {" + realDistance + "}," +
                        " Vessel mass: {" + Math.Round(vesselMass * 1000f) + "}," +
                        " TimeIndex: {" + TimeIndex + "}," +
                        " TimePlanned: {" + eventToExecute.TimeToImpact + "}," +
                        " NegativePressure: {" + eventToExecute.IsNegativePressure + "}");
                }
                if (rb != null && rb.mass > 0 && !BDArmorySettings.PAINTBALL_MODE)
                    AddForceAtPosition(rb, (Position - part.transform.position).normalized * eventToExecute.NegativeForce * BDArmorySettings.EXP_IMP_MOD * 0.25f, part.transform.position);
            }
        }

        // We use an ObjectPool for the ExplosionFx instances as they leak KSPParticleEmitters otherwise.
        static void CreateObjectPool(string explModelPath, string soundPath)
        {
            var key = explModelPath + soundPath;
            if (!explosionFXPools.ContainsKey(key) || explosionFXPools[key] == null)
            {
                var explosionFXTemplate = GameDatabase.Instance.GetModel(explModelPath);
                if (explosionFXTemplate == null)
                {
                    Debug.LogError("[BDArmory.ExplosionFX]: " + explModelPath + " was not found, using the default explosion instead. Please fix your model.");
                    explosionFXTemplate = GameDatabase.Instance.GetModel(ModuleWeapon.defaultExplModelPath);
                }
                var soundClip = GameDatabase.Instance.GetAudioClip(soundPath);
                if (soundClip == null)
                {
                    Debug.LogError("[BDArmory.ExplosionFX]: " + soundPath + " was not found, using the default sound instead. Please fix your model.");
                    soundClip = GameDatabase.Instance.GetAudioClip(ModuleWeapon.defaultExplSoundPath);
                }
                var eFx = explosionFXTemplate.AddComponent<ExplosionFx>();
                eFx.ExSound = soundClip;
                eFx.audioSource = explosionFXTemplate.AddComponent<AudioSource>();
                eFx.audioSource.minDistance = 200;
                eFx.audioSource.maxDistance = 5500;
                eFx.audioSource.spatialBlend = 1;
                eFx.LightFx = explosionFXTemplate.AddComponent<Light>();
                eFx.LightFx.color = GUIUtils.ParseColor255("255,238,184,255");
                eFx.LightFx.intensity = 8;
                eFx.LightFx.shadows = LightShadows.None;
                explosionFXTemplate.SetActive(false);
                explosionFXPools[key] = ObjectPool.CreateObjectPool(explosionFXTemplate, 10, true, true, 0f, false);
            }
        }

        public static void CreateExplosion(Vector3 position, float tntMassEquivalent, string explModelPath, string soundPath, ExplosionSourceType explosionSourceType,
            float caliber = 120, Part explosivePart = null, string sourceVesselName = null, string sourceWeaponName = null, Vector3 direction = default(Vector3), float angle = 100f, bool isfx = false, float projectilemass = 0, float caseLimiter = -1, float dmgMutator = 1, string type = "standard", Part Hitpart = null)
        {
            CreateObjectPool(explModelPath, soundPath);

            Quaternion rotation;
            if (direction == default(Vector3))
            {
                rotation = Quaternion.LookRotation(VectorUtils.GetUpDirection(position));
            }
            else
            {
                rotation = Quaternion.LookRotation(direction);
            }

            GameObject newExplosion = explosionFXPools[explModelPath + soundPath].GetPooledObject();
            newExplosion.transform.SetPositionAndRotation(position, rotation);
            ExplosionFx eFx = newExplosion.GetComponent<ExplosionFx>();
            eFx.Range = BlastPhysicsUtils.CalculateBlastRange(tntMassEquivalent);
            eFx.Position = position;
            eFx.Power = tntMassEquivalent;
            eFx.ExplosionSource = explosionSourceType;
            eFx.SourceVesselName = !string.IsNullOrEmpty(sourceVesselName) ? sourceVesselName : explosionSourceType == ExplosionSourceType.Missile ? (explosivePart != null && explosivePart.vessel != null ? explosivePart.vessel.GetName() : null) : null; // Use the sourceVesselName if specified, otherwise get the sourceVesselName from the missile if it is one.
            eFx.SourceWeaponName = sourceWeaponName;
            eFx.Caliber = caliber;
            eFx.ExplosivePart = explosivePart;
            eFx.Direction = direction;
            eFx.isFX = isfx;
            eFx.ProjMass = projectilemass;
            eFx.CASEClamp = caseLimiter;
            eFx.dmgMult = dmgMutator;
            eFx.hitpart = Hitpart;
            eFx.pEmitters = newExplosion.GetComponentsInChildren<KSPParticleEmitter>();
            eFx.audioSource = newExplosion.GetComponent<AudioSource>();
            type = type.ToLower();
            switch (type)
            {
                case "continuousrod":
                    eFx.warheadType = WarheadTypes.ContinuousRod;
                    //eFx.AngleOfEffect = 165;
                    eFx.Caliber = caliber > 0 ? caliber / 4 : 30;
                    eFx.ProjMass = 0.3f + (tntMassEquivalent / 75);
                    break;
                case "shapedcharge":
                    eFx.warheadType = WarheadTypes.ShapedCharge;
                    eFx.AngleOfEffect = 10f;
                    eFx.Caliber = caliber > 0 ? caliber / 2 : 50;
                    break;
                default:
                    eFx.warheadType = WarheadTypes.Standard;
                    eFx.AngleOfEffect = angle >= 0f ? Mathf.Clamp(angle, 0f, 180f) : 100f;
                    break;
            }
            if (direction == default(Vector3) && explosionSourceType == ExplosionSourceType.Missile)
            {
                eFx.warheadType = WarheadTypes.Standard;
                if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log("[BDArmory.ExplosionFX]: No direction param specified, defaulting warhead type!");
            }
            if (tntMassEquivalent <= 5)
            {
                eFx.audioSource.minDistance = 4f;
                eFx.audioSource.maxDistance = 3000;
                eFx.audioSource.priority = 9999;
            }
            newExplosion.SetActive(true);
        }

        public static void AddForceAtPosition(Rigidbody rb, Vector3 force, Vector3 position)
        {
            //////////////////////////////////////////////////////////
            // Add The force to part
            //////////////////////////////////////////////////////////
            if (rb == null || rb.mass == 0) return;
            rb.AddForceAtPosition(force, position, ForceMode.VelocityChange);
            if (BDArmorySettings.DEBUG_DAMAGE)
            {
                Debug.Log("[BDArmory.ExplosionFX]: Force Applied | Explosive : " + Math.Round(force.magnitude, 2));
            }
        }
    }

    public abstract class BlastHitEvent
    {
        public float Distance { get; set; }
        public float TimeToImpact { get; set; }
        public bool IsNegativePressure { get; set; }
    }

    internal class PartBlastHitEvent : BlastHitEvent
    {
        public Part Part { get; set; }
        public Vector3 HitPoint { get; set; }
        public RaycastHit Hit { get; set; }
        public float NegativeForce { get; set; }
        public string SourceVesselName { get; set; }
        public List<Tuple<float, float, float>> IntermediateParts { get; set; } // distance, HP, armour
        public bool withinAngleofEffect { get; set; }
    }

    internal class BuildingBlastHitEvent : BlastHitEvent
    {
        public DestructibleBuilding Building { get; set; }
    }
}
