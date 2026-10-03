using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using BDArmory.Competition;
using BDArmory.Control;
using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.Weapons;
using BDArmory.Weapons.Missiles;
using BDArmory.WeaponMounts;
using System;

namespace BDArmory.Targeting
{
    public class TargetInfo : MonoBehaviour
    {
        public BDTeam Team;
        public bool isMissile;
        public MissileBase MissileBaseModule;
        public MissileFire WeaponManager // Using a non-static target WM avoids continuing to target debris that has separated from the WM.
        {
            get
            {
                if (field == null || !field.IsPrimaryWM || field.vessel != vessel)
                    field = (vessel != null && vessel.loaded) ? vessel.ActiveController().WM : null;
                if (field != null && field.vessel != vessel) field = null;
                return field;
            }
        }

        Dictionary<BDTeam, List<MissileFire>> friendliesEngaging = [];
        public Dictionary<BDTeam, bool> detected = [];
        public Dictionary<BDTeam, float> detectedTime = [];

        public float radarBaseSignature = -1;
        public bool radarBaseSignatureNeedsUpdate = true;
        public float[,] radarSignatureMatrix = null;
        public bool radarSignatureMatrixNeedsUpdate = true;
        public float radarRCSReducedSignature;
        public float radarModifiedSignature;
        public float radarLockbreakFactor = 1;
        public float radarJammingDistance;
        public bool alreadyScheduledRCSUpdate = false;
        public float radarMassAtUpdate = 0f;
        public Vector3 bounds = Vector3.zero;

        public bool targetPartListNeedsUpdating = true; // Only update when needed — avoids excessive calling due to events.
        public List<Part> targetWeaponList { get { if (targetPartListNeedsUpdating) UpdateTargetPartList(); return _targetWeaponList; } }
        public List<Part> targetEngineList { get { if (targetPartListNeedsUpdating) UpdateTargetPartList(); return _targetEngineList; } }
        public List<Part> targetCommandList { get { if (targetPartListNeedsUpdating) UpdateTargetPartList(); return _targetCommandList; } }
        public List<Part> targetMassList { get { if (targetPartListNeedsUpdating) UpdateTargetPartList(); return _targetMassList; } }
        public List<Part> _targetWeaponList = new List<Part>();
        List<Part> _targetEngineList = new List<Part>();
        List<Part> _targetCommandList = new List<Part>();
        public List<Part> _targetMassList = new List<Part>();

        public bool isLandedOrSurfaceSplashed
        {
            get
            {
                if (!vessel) return false;
                if (
                    (vessel.situation == Vessel.Situations.PRELAUNCH || // Newly spawned
                    vessel.situation == Vessel.Situations.LANDED ||
                    vessel.situation == Vessel.Situations.SPLASHED) && // Boats should be included
                    !isUnderwater //refrain from shooting subs with missiles
                    )
                {
                    return true;
                }
                else
                    return false;
            }
        }

        public bool isFlying
        {
            get
            {
                if (!vessel) return false;
                if (vessel.situation == Vessel.Situations.FLYING || vessel.InOrbit()) return true;
                else
                    return false;
            }
        }

        public bool isUnderwater
        {
            get
            {
                if (!vessel) return false;
                if (vessel.altitude < -20) //some boats sit slightly underwater, this is only for submersibles
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        public bool isSplashed
        {
            get
            {
                if (!vessel) return false;
                if (vessel.situation == Vessel.Situations.SPLASHED) return true;
                return false;
            }
        }

        public Vector3 velocity
        {
            get
            {
                if (!vessel) return Vector3.zero;
                return vessel.Velocity();
            }
        }

        public Vector3 position
        {
            get
            {
                return vessel.CoM;
            }
        }

        private Vessel vessel;

        public Vessel Vessel
        {
            get
            {
                return vessel;
            }
            set
            {
                vessel = value;
            }
        }

        public string Name()
        {
            return (vessel ? vessel.vesselName : "Null");
        }

        public Guid ID()
        {
            return (vessel ? vessel.id : Guid.Empty);
        }

        public bool isThreat
        {
            get
            {
                if (!Vessel)
                {
                    return false;
                }

                if (isMissile && MissileBaseModule && !MissileBaseModule.HasMissed)
                {
                    return true;
                }
                else if (WeaponManager)
                {
                    return WeaponManager.vessel.isCommandable; //isn't debris / has command part
                    //return weaponManager.vessel.IsControllable; //vessel has probecore & EC/pilot && pilot is conscious
                    //enable this if you want exceedingly honorable pilots who hold fire if their target has GLOC'ed themselves
                    // GLOC'ed craft now go neutral stick, so they no longer get locked in a perma-stun deathloop                    
                }
                return false;
            }
        }
        public bool isDebilitated //has the vessel been EMP'd. Could also be used for more exotic munitions that would disable instead of kill
        {
            get
            {
                if (!Vessel)
                {
                    return false;
                }

                if (isMissile)
                {
                    return false;
                }
                else if (WeaponManager && WeaponManager.debilitated)
                {
                    return true;
                }
                return false;
            }
        }

        public List<(string, float)> debugTargetPriorities = []; // Debug info for target priorities.

        void Awake()
        {
            if (!vessel)
            {
                vessel = GetComponent<Vessel>();
            }

            if (!vessel)
            {
                //Debug.Log ("[BDArmory]: TargetInfo was added to a non-vessel");
                Destroy(this);
                return;
            }

            //destroy this if a target info is already attached to the vessel
            foreach (var otherInfo in vessel.gameObject.GetComponents<TargetInfo>())
            {
                if (otherInfo != this)
                {
                    Destroy(this);
                    return;
                }
            }

            Team = null;
            var mf = vessel.ActiveController().WM;
            if (mf != null)
            {
                Team = mf.Team; // While the primary WM may change, the Team shouldn't.
            }
            else if (vessel.IsMissile())
            {
                var ml = VesselModuleRegistry.GetMissileBase(vessel, true);
                if (ml != null)
                {
                    ml.SetTargetInfo(this);
                    /*isMissile = true;
                    MissileBaseModule = ml;
                    Team = ml.Team;*/
                }
            }

            boundsSorted = new int[3];

            vessel.OnJustAboutToBeDestroyed += AboutToBeDestroyed;

            //add delegate to peace enable event
            BDArmorySetup.OnPeaceEnabled += OnPeaceEnabled;

            //lifeRoutine = StartCoroutine(LifetimeRoutine());              // TODO: CHECK BEHAVIOUR AND SIDE EFFECTS!

            if (!isMissile && Team != null)
            {
                GameEvents.onVesselPartCountChanged.Add(VesselModified);
                //massRoutine = StartCoroutine(MassRoutine());              // TODO: CHECK BEHAVIOUR AND SIDE EFFECTS!
            }
            targetPartListNeedsUpdating = true;
            GameEvents.onVesselDestroy.Add(CleanFriendliesEngaging);
        }

        void OnPeaceEnabled()
        {
            //Destroy(this);
        }

        void OnDestroy()
        {
            //remove delegate from peace enable event
            BDArmorySetup.OnPeaceEnabled -= OnPeaceEnabled;
            if (vessel is not null) vessel.OnJustAboutToBeDestroyed -= AboutToBeDestroyed;
            GameEvents.onVesselPartCountChanged.Remove(VesselModified);
            GameEvents.onVesselDestroy.Remove(CleanFriendliesEngaging);
            BDATargetManager.RemoveTarget(this);
        }

        IEnumerator UpdateRCSDelayed()
        {
            if (radarMassAtUpdate > 0)
            {
                float massPercentageDifference = (radarMassAtUpdate - vessel.GetTotalMass()) / radarMassAtUpdate;
                var weaponManager = WeaponManager;
                if (massPercentageDifference > 0.025f && weaponManager && weaponManager.missilesAway.Count == 0 && !weaponManager.guardFiringMissile)
                {
                    alreadyScheduledRCSUpdate = true;
                    yield return new WaitForSeconds(1.0f);    // Wait for any explosions to finish
                    radarBaseSignatureNeedsUpdate = true;     // Update RCS if vessel mass changed by more than 2.5% after a part was lost
                    radarSignatureMatrixNeedsUpdate = true;
                    if (BDArmorySettings.DEBUG_RADAR) Debug.Log("[BDArmory.TargetInfo]: RCS mass update triggered for " + vessel.vesselName + ", difference: " + (massPercentageDifference * 100f).ToString("0.0"));
                }
            }
        }

        /*void OnGUI()
        {
            if (BDArmorySettings.DEBUG_LINES)
            {
                if (vesselTransform == null) UpdateBounds();
                Vector3 vesselPos = vesselTransform.position;
                //Vector3 vesselForward = vesselTransform.up;
                //Vector3 vesselUp = -vesselTransform.forward;
                //Vector3 vesselRight = vesselTransform.right;
                //Vector3 scaledDiff = vesselTransform.TransformDirection(localBoundsCenter);
                //GUIUtils.DrawLineBetweenWorldPositions(vesselPos + (0.5f * bounds.y) * vesselForward, vesselPos - (0.5f * bounds.y) * vesselForward, 5, Color.blue);
                //GUIUtils.DrawLineBetweenWorldPositions(vesselPos + (0.5f * bounds.x) * vesselRight, vesselPos - (0.5f * bounds.x) * vesselRight, 5, Color.green);
                //GUIUtils.DrawLineBetweenWorldPositions(vesselPos + (0.5f * bounds.z) * vesselUp, vesselPos - (0.5f * bounds.z) * vesselUp, 5, Color.red);
                //Vector3 rand = UnityEngine.Random.onUnitSphere;
                //GUIUtils.DrawLineBetweenWorldPositions(vesselPos + GetWorldAlignedVector(GetBoundsScaledBiasedVector(rand, 0.5f)), vessel.CoM, 5, Color.magenta);
                GUIUtils.DrawLineBetweenWorldPositions(vesselPos + GetWorldAlignedVector(GetBoundsScaledBiasedVector(Vector3.up, 0.5f)), vessel.CoM, 5, Color.yellow);
                GUIUtils.DrawLineBetweenWorldPositions(vesselPos + GetWorldAlignedVector(GetBoundsScaledBiasedVector(-Vector3.forward, 0.5f)), vessel.CoM, 5, Color.yellow);
                GUIUtils.DrawLineBetweenWorldPositions(vesselPos + GetWorldAlignedVector(GetLocalCoM()), vessel.CoM, 5, Color.blue);
                GUIUtils.DrawLineBetweenWorldPositions(vesselPos + GetWorldAlignedVector(localBoundsCenter), vesselPos, 5, Color.grey);
                //GUIUtils.DrawLineBetweenWorldPositions(vesselPos + (GetBoundsScaledVectorAlt(0.6f * Vector3.up)), vesselPos + scaledDiff, 5, Color.cyan);
                GUIUtils.DrawLineBetweenWorldPositions(vesselPos + GetWorldAlignedVector(GetBoundsScaledVector(Vector3.up, 0.5f)), vesselPos + GetWorldAlignedVector(GetBoundsScaledVector(-Vector3.up, 0.5f)), 5, Color.green);
                //GUIUtils.DrawLineBetweenWorldPositions(vesselPos + GetWorldAlignedVector(GetBoundsScaledVector(Vector3.right, 0.5f)), vesselPos + GetWorldAlignedVector(GetBoundsScaledVector(-Vector3.right, 0.5f)), 5, Color.green);
                GUIUtils.DrawLineBetweenWorldPositions(vesselPos + GetWorldAlignedVector(GetBoundsScaledVector(-Vector3.forward, 0.5f)), vesselPos + GetWorldAlignedVector(GetBoundsScaledVector(Vector3.forward, 0.5f)), 5, Color.red);
                GUIUtils.DrawLineBetweenWorldPositions(vesselPos + GetRadarGlint(), vessel.CoM, 5, Color.magenta);
                GUIUtils.DrawLabelOnWorldPos(vesselPos, $"bounds.length={bounds.y}\nbounds.width={bounds.x}\nbounds.height={bounds.z}", new Vector2(200, 200));
            }
        }*/

        public void GetVesselTransform()
        {
            vesselTransform = vessel.ReferenceTransform;
        }

        public Vector3 GetWorldAlignedVector(Vector3 localVector)
        {
            if (vesselTransform != vessel.ReferenceTransform) UpdateBounds();
            return vesselTransform.TransformDirection(localVector);
        }

        public Vector3 GetBoundsScaledWorldVector(Vector3 localVector, float boundScale)
        {
            if (vesselTransform != vessel.ReferenceTransform) UpdateBounds();
            return vesselTransform.TransformDirection(new Vector3(boundScale * bounds.x * localVector.x + localBoundsCenter.x, boundScale * bounds.y * localVector.y + localBoundsCenter.y, boundScale * bounds.z * localVector.z + localBoundsCenter.z));
        }

        public Vector3 GetBoundsScaledVector(Vector3 localVector, float boundScale)
        {
            if (vesselTransform != vessel.ReferenceTransform) UpdateBounds();
            return new Vector3(boundScale * bounds.x * localVector.x + localBoundsCenter.x, boundScale * bounds.y * localVector.y + localBoundsCenter.y, boundScale * bounds.z * localVector.z + localBoundsCenter.z);
        }

        public Vector3 GetBoundsScaledBiasedWorldVector(Vector3 localVector, float boundScale)
        {
            if (vesselTransform != vessel.ReferenceTransform) UpdateBounds();
            Vector3 localRelCoM = GetLocalCoM();
            Vector3 biasedVector = new Vector3(localVector.x * (localVector.x > 0f ? (boundScale * bounds.x + (localBoundsCenter.x - localRelCoM.x)) : (boundScale * bounds.x - (localBoundsCenter.x - localRelCoM.x))) + localRelCoM.x,
                                               localVector.y * (localVector.y > 0f ? (boundScale * bounds.y + (localBoundsCenter.y - localRelCoM.y)) : (boundScale * bounds.y - (localBoundsCenter.y - localRelCoM.y))) + localRelCoM.y,
                                               localVector.z * (localVector.z > 0f ? (boundScale * bounds.z + (localBoundsCenter.z - localRelCoM.z)) : (boundScale * bounds.z - (localBoundsCenter.z - localRelCoM.z))) + localRelCoM.z);
            return vesselTransform.TransformDirection(biasedVector);
        }

        public Vector3 GetBoundsScaledBiasedVector(Vector3 localVector, float boundScale)
        {
            if (vesselTransform != vessel.ReferenceTransform) UpdateBounds();
            Vector3 localRelCoM = GetLocalCoM();
            Vector3 biasedVector = new Vector3(localVector.x * (localVector.x > 0f ? (boundScale * bounds.x + (localBoundsCenter.x - localRelCoM.x)) : (boundScale * bounds.x - (localBoundsCenter.x - localRelCoM.x))) + localRelCoM.x,
                                               localVector.y * (localVector.y > 0f ? (boundScale * bounds.y + (localBoundsCenter.y - localRelCoM.y)) : (boundScale * bounds.y - (localBoundsCenter.y - localRelCoM.y))) + localRelCoM.y,
                                               localVector.z * (localVector.z > 0f ? (boundScale * bounds.z + (localBoundsCenter.z - localRelCoM.z)) : (boundScale * bounds.z - (localBoundsCenter.z - localRelCoM.z))) + localRelCoM.z);
            /*if (log)
            {
                Debug.Log($"[BDArmory.TargetInfo.GetBoundsScaledBiasedVector] localVector: {localVector}, localBoundsCenter: {localBoundsCenter}, localRelCoM: {localRelCoM}, bounds: {bounds}, biasedVector: {biasedVector}, x: {(localVector.x > 0f ? (boundScale * bounds.x + (localBoundsCenter.x - localRelCoM.x)) : (boundScale * bounds.x - (localBoundsCenter.x - localRelCoM.x)))}, y: {(localVector.y > 0f ? (boundScale * bounds.y + (localBoundsCenter.y - localRelCoM.y)) : (boundScale * bounds.y - (localBoundsCenter.y - localRelCoM.y)))}, z: {(localVector.z > 0f ? (boundScale * bounds.z + (localBoundsCenter.z - localRelCoM.z)) : (boundScale * bounds.z - (localBoundsCenter.z - localRelCoM.z)))}");
            }*/
            return biasedVector;
        }

        float timeOfLastCoMUpdate = 0f;
        Vector3 localCoM;
        public Vector3 GetLocalCoM()
        {
            if (Time.time > timeOfLastCoMUpdate)
            {
                localCoM = vesselTransform.InverseTransformDirection(vessel.CoM - vesselTransform.position);
                timeOfLastCoMUpdate = Time.time;
            }

            return localCoM;
        }

        Vector3 prevGlint = Vector3.zero;
        Vector3 futureGlint = Vector3.zero;
        float glintTime = 0f;
        const float glintInterval = 1f;
        readonly float glintIntInv = 1f / glintInterval;

        Vector3 currGlint = Vector3.zero;
        Vector3 currLocalGlint = Vector3.zero;
        float glintUpdateTime = 0f;
        public Vector3 GetRadarGlint()
        {
            float currTime = Time.time;
            if (glintUpdateTime >= currTime)
            {
                return currGlint;
            }
            glintUpdateTime = currTime;
            currTime -= glintTime;
            if (currTime > glintInterval)
            {
                prevGlint = futureGlint;
                futureGlint = GetBoundsScaledBiasedVector(UnityEngine.Random.onUnitSphere, 0.5f * VectorUtils.Gaussian());
                glintTime = glintUpdateTime;
                currLocalGlint = prevGlint;
                currGlint = GetWorldAlignedVector(prevGlint);
                return currGlint;
            }
            currLocalGlint = Vector3.LerpUnclamped(prevGlint, futureGlint, currTime * glintIntInv);
            currGlint = GetWorldAlignedVector(currLocalGlint);
            return currGlint;
        }

        void FixedUpdate()
        {
            if (vessel == null)
            {
                AboutToBeDestroyed();
            }
            else
            {
                if (vessel.vesselType == VesselType.Debris && WeaponManager == null)
                {
                    BDATargetManager.RemoveTarget(this);
                    Team = null;
                }
            }
        }

        /*Vector3 vesselSizeB;
        readonly static Quaternion vesselRot = Quaternion.Inverse(Quaternion.LookRotation(Vector3.up, -Vector3.forward));
        readonly static Quaternion vesselRot2 = Quaternion.LookRotation(Vector3.up, -Vector3.forward);*/

        Transform vesselTransform;
        Vector3 localBoundsCenter;
        public int[] boundsSorted; // Bound indexes of the largest to smallest dimension

        public void UpdateBounds()
        {
            GetVesselTransform();
            Bounds tempBounds = isMissile ? vessel.GetRendererBounds() : vessel.GetColliderBounds();
            localBoundsCenter = tempBounds.center;
            bounds = tempBounds.size;
            timeOfLastCoMUpdate = 0f;

            // We expect vessel forward/back to be large
            float vesselForward = bounds.y;
            // And vessel up/down to be small
            float vesselUp = bounds.z;
            float vesselRight = bounds.x;

            // Determine which direction the vessel is largest in
            // Check forward vs right ("length" vs "width")
            if (vesselForward > vesselRight)
            {
                // Check forward vs up ("length" vs "height")
                if (vesselForward > vesselUp)
                {
                    // Check right vs up ("width" vs "height")
                    if (vesselRight > vesselUp)
                    {
                        boundsSorted = [1, 0, 2];
                    }
                    else
                    {
                        boundsSorted = [1, 2, 0];
                    }
                }
                else
                {
                    // Since up > forward > right, we know the order
                    boundsSorted = [2, 1, 0];
                }
            }
            else
            {
                // Check right vs up ("width" vs "height")
                if (vesselRight > vesselUp)
                {
                    // Check forward vs up ("length" vs "height")
                    if (vesselForward > vesselUp)
                    {
                        boundsSorted = [0, 1, 2];
                    }
                    else
                    {
                        boundsSorted = [0, 2, 1];
                    }
                }
                else
                {
                    // Since up > right > forward, we know the order
                    boundsSorted = [2, 0, 1];
                }
            }
        }

        public void UpdateTargetPartList()
        {
            targetPartListNeedsUpdating = false;
            _targetCommandList.Clear();
            _targetWeaponList.Clear();
            _targetMassList.Clear();
            _targetEngineList.Clear();
            //anything else? fueltanks? - could be useful if incindiary ammo gets implemented
            //power generation? - radiators/generators - if doing CoaDE style fights/need reactors to power weapons

            if (vessel == null) return;

            UpdateBounds(); // Update vessel bounds on part changes
            /*vessel.UpdateVesselSize();
            Vector3 tempSize = vessel.vesselSize;
            Quaternion rot = Quaternion.Inverse(vessel.ReferenceTransform.rotation);
            Vector3 vesselForward = vessel.ReferenceTransform.up;
            Vector3 vesselUp = -vessel.ReferenceTransform.forward;
            Vector3 vesselRight = vessel.ReferenceTransform.right;
            vesselSizeB = new Vector3(Vector3.Dot(tempSize, vesselForward), Vector3.Dot(tempSize, vesselRight), Vector3.Dot(tempSize, vesselUp));
            // vesselSizeB and vesselSizeBQuat2 are in GLOBAL coords
            // vesselSizeBQuat is in local coords
            Debug.Log($"[BDArmory.TargetInfo] bounds: {bounds}, vesselSize: {tempSize}, vesselSizeB: {vesselSizeB}, vesselSizeBQuat: {rot * vesselRot * tempSize}, vesselSizeBQuat2: {rot * tempSize}, vesselSizeBQuat3: {rot * vesselRot2 * tempSize}");*/

            // Get the parts via the VesselModuleRegistry to avoid the expensive Find... commands.
            VesselModuleRegistry.OnVesselModified(vessel); // Make sure the vessel is up-to-date since this can happen as part of an event.
            _targetWeaponList.AddUniqueRange(VesselModuleRegistry.GetModuleWeapons(vessel).Select(m => m.part).Concat(VesselModuleRegistry.GetModules<MissileTurret>(vessel).Select(m => m.part)).Where(p => p is not null));
            _targetEngineList.AddUniqueRange(VesselModuleRegistry.GetModuleEngines(vessel).Select(m => m.part).Where(p => p is not null));
            _targetCommandList.AddUniqueRange(VesselModuleRegistry.GetModuleCommands(vessel).Select(m => m.part).Concat(VesselModuleRegistry.GetKerbalSeats(vessel).Select(m => m.part)).Where(p => p is not null));
            _targetMassList.AddRange(vessel.Parts.Where(p => p is not null));

            // Sort and cull target part lists.
            _targetMassList.Sort((p1, p2) => (p2.mass.CompareTo(p1.mass))); // Heaviest to lightest.
            if (_targetMassList.Count > 10)
                _targetMassList.RemoveRange(10, (_targetMassList.Count - 10)); //trim to max turret targets
            _targetCommandList.Sort((p1, p2) => (p2.mass.CompareTo(p1.mass)));
            if (_targetCommandList.Count > 10)
                _targetCommandList.RemoveRange(10, (_targetCommandList.Count - 10));
            _targetEngineList.Sort((p1, p2) => (p2.mass.CompareTo(p1.mass)));
            if (_targetEngineList.Count > 10)
                _targetEngineList.RemoveRange(10, (_targetEngineList.Count - 10));
            _targetWeaponList.Sort((p1, p2) => (p2.mass.CompareTo(p1.mass)));
            if (_targetWeaponList.Count > 10)
                _targetWeaponList.RemoveRange(10, (_targetWeaponList.Count - 10));
        }

        void CleanFriendliesEngaging(Vessel v)
        {
            var toRemove = friendliesEngaging.Where(kvp => kvp.Value == null).Select(kvp => kvp.Key).ToList();
            foreach (var key in toRemove)
            { friendliesEngaging.Remove(key); }
        }
        public int NumFriendliesEngaging(BDTeam team)
        {
            if (friendliesEngaging.TryGetValue(team, out var friendlies))
            {
                friendlies.RemoveAll(item => item == null);
                return friendlies.Count;
            }
            return 0;
        }

        public float MaxThrust(Vessel v)
        {
            float maxThrust = 0;
            float finalThrust = 0;

            var engines = VesselModuleRegistry.GetModules<ModuleEngines>(v);
            if (engines == null) return 0;
            using (var engine = engines.GetEnumerator())
                while (engine.MoveNext())
                {
                    if (engine.Current == null) continue;
                    if (!engine.Current.EngineIgnited) continue;

                    MultiModeEngine mme = engine.Current.part.FindModuleImplementing<MultiModeEngine>();
                    if (IsAfterBurnerEngine(mme))
                    {
                        mme.autoSwitch = false;
                    }

                    if (mme && mme.mode != engine.Current.engineID) continue;
                    float engineThrust = engine.Current.maxThrust;
                    if (engine.Current.atmChangeFlow)
                    {
                        engineThrust *= engine.Current.flowMultiplier;
                    }
                    maxThrust += Mathf.Max(0f, engineThrust * (engine.Current.thrustPercentage / 100f)); // Don't include negative thrust percentage drives (Danny2462 drives) as they don't contribute to the thrust.

                    finalThrust += engine.Current.finalThrust;
                }
            return maxThrust;
        }

        private static bool IsAfterBurnerEngine(MultiModeEngine engine)
        {
            if (engine == null)
            {
                return false;
            }
            if (!engine)
            {
                return false;
            }
            return engine.primaryEngineID == "Dry" && engine.secondaryEngineID == "Wet";
        }

        #region Target priority
        // Begin methods used for prioritizing targets
        public float TargetPriRange(MissileFire myMf) // 1- Target range normalized with max weapon range
        {
            if (myMf == null) return 0;
            float thisDist = (position - myMf.vessel.CoM).magnitude;
            float maxWepRange = 0;
            var weapons = VesselModuleRegistry.GetModules<ModuleWeapon>(myMf.vessel);
            if (weapons == null) return 0;
            using (var weapon = weapons.GetEnumerator())
                while (weapon.MoveNext())
                {
                    if (weapon.Current == null) continue;
                    maxWepRange = (weapon.Current.GetEngagementRangeMax() > maxWepRange) ? weapon.Current.GetEngagementRangeMax() : maxWepRange;
                }
            float targetPriRange = 1 - Mathf.Clamp(thisDist / maxWepRange, 0, 1);
            return targetPriRange;
        }

        public float TargetPriATA(MissileFire myMf) // Square cosine of antenna train angle
        {
            if (myMf == null) return 0;
            float ataDot = Vector3.Dot(myMf.vessel.srf_vel_direction, (position - myMf.vessel.CoM).normalized);
            ataDot = (ataDot + 1) / 2; // Adjust from 0-1 instead of -1 to 1
            return ataDot * ataDot;
        }
        public float TargetPriEngagement(MissileFire mf, double engagingAlt) // Differentiate between flying and surface targets
        {
            if (mf == null) return 0; // no WM, so no valid target, no impact on targeting score
            if (mf.vessel.LandedOrSplashed)
            {
                return -1; //ground target
            }
            else if (mf.vessel.horizontalSrfSpeed < 30 && (mf.vessel.radarAltitude < 200 && engagingAlt > 800)) //if craft is flatspinning or similar, and is lower than 200m, while the aircraft targeting it is higher than 800, regard as semi-landed
            {
                return -0.5f;
            }
            else
            {
                return 1; // Air target
            }
        }
        public float TargetPriAcceleration() // Normalized clamped acceleration for the target
        {
            float bodyGravity = (float)PhysicsGlobals.GravitationalAcceleration * (float)vessel.orbit.referenceBody.GeeASL; // Set gravity for calculations;
            float maxAccel = MaxThrust(vessel) / vessel.GetTotalMass(); // This assumes that all thrust is in the same direction.
            maxAccel = 0.1f * Mathf.Clamp(maxAccel / bodyGravity, 0f, 10f);
            maxAccel = maxAccel == 0f ? -1f : maxAccel; // If max acceleration is zero (no engines), set to -1 for stronger target priority
            return maxAccel; // Output is -1 or 0-1 (0.1 is equal to body gravity)
        }

        public float TargetPriClosureTime(MissileFire myMf) // Time to closest point of approach, normalized for one minute
        {
            if (myMf == null) return 0;
            float targetDistance = Vector3.Distance(vessel.CoM, myMf.vessel.CoM);
            Vector3 currVel = (float)myMf.vessel.srfSpeed * myMf.vessel.Velocity().normalized;
            float closureTime = Mathf.Clamp((float)(1 / ((vessel.Velocity() - currVel).magnitude / targetDistance)), 0f, 60f);
            return 1 - closureTime / 60f;
        }

        public float TargetPriWeapons(MissileFire mf, MissileFire myMf) // Relative number of weapons of target compared to own weapons
        {
            if (mf == null || mf.weaponArray == null || myMf == null) return 0; // The target is dead or has no weapons (or we're dead).
            float targetWeapons = mf.CountWeapons(); // Counts weapons
            float myWeapons = myMf.CountWeapons(); // Counts weapons
            // float targetWeapons = mf.weaponArray.Length - 1; // Counts weapon groups
            // float myWeapons = myMf.weaponArray.Length - 1; // Counts weapon groups
            if (mf.weaponArray.Length > 0)
            {
                return Mathf.Max((targetWeapons - myWeapons) / targetWeapons, 0); // Ranges 0-1, 0 if target has same # of weapons, 1 if they have weapons and we don't
            }
            else
            {
                return 0; // Target doesn't have any weapons
            }
        }

        public float TargetPriFriendliesEngaging(MissileFire myMf)
        {
            if (myMf == null || myMf.wingCommander == null) return 0;
            float teammates = myMf.wingCommander.friendlies.Count;
            if (teammates == 0) return 0; // No teammates
            float friendsEngaging = Mathf.Max(NumFriendliesEngaging(myMf.Team) - 1, 0);
            friendsEngaging = 1 - Mathf.Clamp(friendsEngaging / teammates, 0f, 1f);
            friendsEngaging = friendsEngaging == 0f ? -1f : friendsEngaging;
            return friendsEngaging; // Range is -1, 0 to 1. -1 if all teammates are engaging target, between 0-1 otherwise depending on number of teammates engaging
        }

        public float TargetPriThreat(MissileFire mf, MissileFire myMf)
        {
            if (mf == null || myMf == null) return 0;
            float firingAtMe = 0;
            if (mf.vessel == myMf.incomingThreatVessel)
            {
                if (myMf.missileIsIncoming || myMf.underFire || myMf.underAttack)
                    firingAtMe = 1f;
            }
            return firingAtMe; // Equals either 0 (not under attack) or 1 (under attack)
        }

        public float TargetPriAoD(MissileFire myMF)
        {
            if (myMF == null) return 0;
            var relativePosition = vessel.CoM - myMF.vessel.CoM;
            float theta = VectorUtils.Angle(myMF.vessel.srf_vel_direction, relativePosition);
            float cosTheta2 = Mathf.Cos(theta / 2f);
            return Mathf.Clamp(((cosTheta2 * cosTheta2 + 1f) * 100f / Mathf.Max(10f, relativePosition.magnitude)) / 2, 0, 1); // Ranges from 0 to 1, clamped at 1 for distances closer than 100m
        }

        public float TargetPriMass(MissileFire mf, MissileFire myMf) // Relative mass compared to our own mass
        {
            if (mf == null || myMf == null) return 0;
            if (mf.vessel != null)
            {
                float targetMass = mf.vessel.GetTotalMass();
                float myMass = myMf.vessel.GetTotalMass();
                return Mathf.Clamp(Mathf.Log10(targetMass / myMass) / 2f, -1, 1); // Ranges -1 to 1, -1 if we are 100 times as heavy as target, 1 target is 100 times as heavy as us
            }
            else
            {
                return 0;
            }
        }

        public float TargetPriDmg(MissileFire mf) // Relative HP of Target
        {
            if (mf == null) return 0;
            if (mf.vessel != null)
            {
                float TargetPriDmg = 1 - Mathf.Clamp(mf.currentHP / mf.totalHP, 0, 1); //ranges from 0-1, 0 is undamaged, 1 is cockpit falling out of the sky
                return TargetPriDmg;
            }
            else
            {
                return 0;
            }
        }

        public float TargetPriProtectTeammate(MissileFire mf, MissileFire myMf) // If target is attacking one of our teammates. 1 if true, 0 if false.
        {
            if (myMf == null) return 0;
            var targetMf = mf != null && mf.currentTarget != null ? mf.currentTarget.WeaponManager : null;
            if (targetMf == null) return 0;
            return (targetMf != myMf && targetMf.Team == myMf.Team) ? 1 : 0; // Not us, but on the same team.
        }

        public float TargetPriProtectVIP(MissileFire mf, MissileFire myMf) // If target is attacking our VIP(s)
        {
            if (mf == null || myMf == null) return 0;
            var targetMf = mf != null && mf.currentTarget != null ? mf.currentTarget.WeaponManager : null;
            if (mf.vessel == null || targetMf == null) return 0;
            bool attackingOurVIPs = targetMf.isVIP && myMf.Team == targetMf.Team;
            return attackingOurVIPs ? 1 : 0; // Ranges 0 to 1, 1 if target is attacking our VIP(s), 0 if it is not
        }

        public float TargetPriAttackVIP(MissileFire mf) // If target is enemy VIP
        {
            if (mf == null) return 0;
            if (mf.vessel != null)
            {
                bool isVIP = mf.isVIP;
                return ((isVIP == true) ? 1 : 0); // Ranges 0 to 1, 1 if target is an enemy VIP, 0 if it is not
            }
            else
            {
                return 0;
            }
        }
        // End functions used for prioritizing targets
        #endregion

        public int TotalEngaging()
        {
            int engaging = 0;
            using (var teamEngaging = friendliesEngaging.GetEnumerator())
                while (teamEngaging.MoveNext())
                    engaging += teamEngaging.Current.Value.Count(wm => wm != null);
            return engaging;
        }

        public void Engage(MissileFire mf)
        {
            if (mf == null)
                return;

            if (friendliesEngaging.TryGetValue(mf.Team, out var friendlies))
            {
                if (!friendlies.Contains(mf))
                    friendlies.Add(mf);
            }
            else
                friendliesEngaging.Add(mf.Team, [mf]);
        }

        public void Disengage(MissileFire mf)
        {
            if (mf == null)
                return;

            if (friendliesEngaging.TryGetValue(mf.Team, out var friendlies))
                friendlies.Remove(mf);
        }

        void AboutToBeDestroyed()
        {
            BDATargetManager.RemoveTarget(this);
            Destroy(this);
        }

        public bool IsCloser(TargetInfo otherTarget, MissileFire myMf)
        {
            float thisSqrDist = (position - myMf.vessel.CoM).sqrMagnitude;
            float otherSqrDist = (otherTarget.position - myMf.vessel.CoM).sqrMagnitude;
            return thisSqrDist < otherSqrDist;
        }

        public bool SafeOrbitalIntercept(MissileFire myMf)
        {
            // For orbital AI craft, avoid intercepting targets if we are descending and the maneuver will bring our own periapsis to an unsafe altitude

            if (!vessel) return true;
            var orbitalAI = myMf.vessel.ActiveController().OrbitalAI;
            if (orbitalAI == null || !orbitalAI.pilotEnabled)
                return true;

            Orbit o = myMf.vessel.orbit;
            bool unsafeDescent = o.timeToPe > 0 && o.timeToPe < o.timeToAp && o.PeA < (1.2f * o.referenceBody.MinSafeAltitude());
            bool inRange = (vessel.CoM - myMf.vessel.CoM).sqrMagnitude < orbitalAI.interceptRanges.y * orbitalAI.interceptRanges.y;
            Vector3 relVel = vessel.Velocity() - myMf.vessel.Velocity();
            bool killVelocityNeeded = Vector3.Dot(vessel.CoM - myMf.vessel.CoM, relVel) < 0f &&
                Vector3.Dot(o.GetPrograde(Planetarium.GetUniversalTime()), relVel) < 0f; // Moving away from each other in prograde direction (kill vel direction is retrograde)

            return (inRange || !(unsafeDescent && killVelocityNeeded));
        }

        public void VesselModified(Vessel v)
        {
            if (v && v == this.vessel && isActiveAndEnabled)
            {
                if (!alreadyScheduledRCSUpdate)
                    StartCoroutine(UpdateRCSDelayed());
                targetPartListNeedsUpdating = true;
            }
        }

        public static Vector3 TargetCOMDispersion(Vessel v)
        {
            Vector3 TargetCOM_ = new Vector3(0, 0);
            ShipConstruct sc = new ShipConstruct("ship", "temp ship", v.parts[0]);

            Vector3 size = ShipConstruction.CalculateCraftSize(sc);

            float dispersionMax = size.y;

            //float dispersionMax = 100f;

            float dispersion = UnityEngine.Random.Range(0, dispersionMax);

            TargetCOM_ = v.CoM + new Vector3(0, dispersion);

            return TargetCOM_;
        }
    }
}

;
