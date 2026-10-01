using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using BDArmory.Control;
using BDArmory.Guidances;
using BDArmory.Radar;
using BDArmory.Settings;
using BDArmory.Utils;
using BDArmory.Weapons.Missiles;
using BDArmory.Targeting;
using BDArmory.Extensions;

namespace BDArmory.WeaponMounts
{
    public class MissileTurret : BDAPartModule
    {
        [KSPField] public string finalTransformName;
        public Transform finalTransform;

        [KSPField] public int turretID = 0;

        public ModuleTurret turret;

        public MissileLauncher missilepod;

        public MissileBase activeMissile;

        [KSPField(guiActive = true, guiName = "#LOC_BDArmory_TurretEnabled")] public bool turretEnabled;//Turret Enabled

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_MissileTurretFireFOV"),
            UI_FloatRange(minValue = 1, maxValue = 180, stepIncrement = 1, scene = UI_Scene.All)]
        public float fireFOV = 5; // Fire when pointing within 5° of target.

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_AutoReturn"),//Auto-Return
         UI_Toggle(scene = UI_Scene.Editor)]
        public bool autoReturn = true;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TurretLoft"),
         UI_Toggle(scene = UI_Scene.All)]
        public bool turretLoft = false; // Turret fires at a lofted trajectory

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TurretLoftFac"),
            UI_FloatRange(minValue = 0, maxValue = 1, stepIncrement = 0.05f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.Editor)]
        public float turretLoftFac = 0.5f; // Factor for optimumVelocity for lofting, lower means a more lofted trajectory

        bool hasReturned = true;

        [KSPField] public float railLength = 3;

        Coroutine returnRoutine;

        int missileCount;
        MissileLauncher[] missileChildren;
        Transform[] missileTransforms;
        Transform[] missileReferenceTransforms;

        Dictionary<string, Vector3> comOffsets;

        public bool slaved;
        public bool slavedGuard
        {
            get
            {
                if (!_slavedGuard) return false;

                if (!_slavedGuardMissile || _slavedGuardMissile.vessel != vessel)
                {
                    _slavedGuard = false;
                    return false;
                }

                return true;
            }
        }

        public bool manuallyControlled = false;

        public Vector3 slavedTargetPosition;

        bool pausingAfterShot;
        float timeFired;
        [KSPField] public float firePauseTime = 0.25f;

        ModuleRadar attachedRadar;
        bool hasAttachedRadar;
        [KSPField] public bool disableRadarYaw = false;
        [KSPField] public bool disableRadarPitch = false;

        [KSPField] public bool mouseControllable = true;

        [KSPField] public bool deployBlocksReload = false; // Turret must stow/"undeploy" itself before reloading
        [KSPField] public bool deployBlocksYaw = false; // Turret must deploy before yawing, turret must return to yaw standby position to stow/"undeploy".
        [KSPField] public bool deployBlocksPitch = false; // Turret must deploy before pitching, turret must return to pitch standby position to stow/"undeploy".
        public bool isReloading = false;
        float _reloadUntil = 0;
        [KSPField] public bool startsDeployed = false; //Turret starts in deployed position and only uses deploy anim for relaoding. TODO: proper reload anim support for turrets independent of deployAnim

        //animation
        [KSPField] public string deployAnimationName;
        AnimationState deployAnimState;
        public bool hasDeployAnimation;

        public bool isDeployed()
        {
            return hasDeployAnimation && deployAnimState.normalizedTime > 0;
        }
        [KSPField] public float deployAnimationSpeed = 1;
        bool editorDeployed;
        Coroutine deployAnimRoutine;

        //special
        [KSPField] public bool activeMissileOnly = false;

        MissileFire WeaponManager
        {
            get
            {
                if (field == null || !field.IsPrimaryWM || field.vessel != vessel)
                    field = vessel && vessel.loaded ? vessel.ActiveController().WM : null;
                return field;
            }
        }

        public float DeployIfBlocking(bool yaw)
        {
            if (!hasDeployAnimation)
            {
                turret.SetDeployFlag(true, true);
                return 0;
            }

            // If not blocking, return 0 without doing anything
            if (!(yaw ? deployBlocksYaw : deployBlocksPitch))
            {
                if (yaw)
                {
                    turret.SetYawDeployFlag(true);
                }
                else
                {
                    turret.SetPitchDeployFlag(true);
                }
                return 0;
            }

            bool reloadBlock = deployBlocksReload && isReloading;

            // If no deploy animation or deployed and not reloading
            if (!(deployAnimState.normalizedTime < 1 || reloadBlock)) return 0;

            // If not blocked by reload
            if (!reloadBlock)
            {
                if (deployAnimRoutine != null)
                {
                    StopCoroutine(deployAnimRoutine);
                }

                deployAnimRoutine = StartCoroutine(DeployAnimation(true));

                hasReturned = false;

                return deployAnimState.RemainingTime();
            }
            else
            {
                // If blocked by reload, return time that reload completes at
                return _reloadUntil - Time.fixedDeltaTime;
            }
        }

        IEnumerator DeployAnimation(bool forward)
        {
            var wait = new WaitForFixedUpdate();
            yield return wait;

            if (forward)
            {
                while (deployAnimState.normalizedTime < 1)
                {
                    deployAnimState.speed = deployAnimationSpeed;
                    yield return wait;
                }

                deployAnimState.normalizedTime = 1;

                // Unblock turret
                turret.SetDeployFlag(true, true);
            }
            else
            {
                deployAnimState.speed = 0;

                yield return new WaitWhileFixed(() => pausingAfterShot);

                // Block turret prior to undeploy
                turret.SetDeployFlag(!deployBlocksYaw, !deployBlocksPitch);

                while (deployAnimState.normalizedTime > 0)
                {
                    deployAnimState.speed = -deployAnimationSpeed;
                    yield return wait;
                }

                deployAnimState.normalizedTime = 0;
            }

            deployAnimState.speed = 0;
        }

        public void EnableTurret(MissileBase currMissile, bool manualControl)
        {
            if (!HighLogic.LoadedSceneIsFlight)
            {
                return;
            }

            activeMissile = currMissile;
            if (!(isReloading && deployBlocksReload && hasDeployAnimation) && returnRoutine != null)
            {
                StopCoroutine(returnRoutine);
                returnRoutine = null;
            }

            turretEnabled = true;
            hasReturned = false;
            manuallyControlled = manuallyControlled |= manualControl;

            if (hasAttachedRadar)
            {
                attachedRadar.lockingYaw = !disableRadarYaw;
                attachedRadar.lockingPitch = !disableRadarPitch;
            }

            if (!autoReturn)
            {
                Events[nameof(ReturnTurret)].guiActive = false;
            }

            if ((hasDeployAnimation && !startsDeployed) && !(isReloading && deployBlocksReload))
            {
                if (deployAnimRoutine != null)
                {
                    StopCoroutine(deployAnimRoutine);
                }

                deployAnimRoutine = StartCoroutine(DeployAnimation(true));
            }
        }

        public void DisableTurret()
        {
            turretEnabled = false;
            activeMissile = null;
            manuallyControlled = false;

            if (autoReturn)
            {
                if (returnRoutine != null)
                {
                    StopCoroutine(returnRoutine);
                }
                returnRoutine = StartCoroutine(ReturnRoutine());
            }

            if (hasAttachedRadar)
            {
                attachedRadar.lockingYaw = !(hasDeployAnimation && deployBlocksYaw && disableRadarYaw);
                attachedRadar.lockingPitch = !(hasDeployAnimation && deployBlocksPitch && disableRadarPitch);
            }

            if (!autoReturn)
            {
                Events[nameof(ReturnTurret)].guiActive = true;
            }
        }

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_ReturnTurret")]//Return Turret
        public void ReturnTurret()
        {
            if (!turretEnabled || isReloading)
            {
                if (returnRoutine != null) StopCoroutine(returnRoutine);
                returnRoutine = StartCoroutine(ReturnRoutine());
            }
        }

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_ToggleAnimation")]//Toggle Animation
        public void EditorToggleAnimation()
        {
            editorDeployed = !editorDeployed;

            if (deployAnimRoutine != null)
            {
                StopCoroutine(deployAnimRoutine);
            }

            deployAnimRoutine = StartCoroutine(DeployAnimation(editorDeployed));
        }

        IEnumerator ReturnRoutine()
        {
            if (turretEnabled && !isReloading)
            {
                hasReturned = false;
                yield break;
            }

            hasReturned = true;

            bool retract = isDeployed() && ((!turretEnabled && !startsDeployed) || (isReloading && deployBlocksReload));

            if (retract && !(deployBlocksYaw || deployBlocksPitch))
            {
                if (deployAnimRoutine != null)
                {
                    StopCoroutine(deployAnimRoutine);
                }

                deployAnimRoutine = StartCoroutine(DeployAnimation(false));
            }

            yield return new WaitForSecondsFixed(0.25f);

            while (pausingAfterShot)
            {
                yield return new WaitForFixedUpdate();
            }

            // If the turret is enabled, then we're here because we're reloading, so we only need to return the turret's yaw/pitch if it's blocking the reload.
            bool pitch = !turretEnabled || (deployBlocksPitch && deployBlocksReload);
            bool yaw = !turretEnabled || (deployBlocksYaw && deployBlocksReload);

            while (turret != null && !turret.ReturnTurret(pitch, yaw, isReloading))
            {
                UpdateMissilePositions();
                yield return new WaitForFixedUpdate();
            }

            if (retract && (deployBlocksYaw || deployBlocksPitch))
            {
                if (deployAnimRoutine != null)
                {
                    StopCoroutine(deployAnimRoutine);
                }

                deployAnimRoutine = StartCoroutine(DeployAnimation(false));
            }
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            //setup anim
            if (!string.IsNullOrEmpty(deployAnimationName))
            {
                hasDeployAnimation = true;
                deployAnimState = GUIUtils.SetUpSingleAnimation(deployAnimationName, part);
                if (state == StartState.Editor && !startsDeployed)
                {
                    Events[nameof(EditorToggleAnimation)].guiActiveEditor = true;
                }
                if (startsDeployed)
                {
                    deployAnimState.normalizedTime = 1;
                }
            }

            if (HighLogic.LoadedSceneIsFlight)
            {
                part.force_activate();

                List<ModuleTurret>.Enumerator tur = part.FindModulesImplementing<ModuleTurret>().GetEnumerator();
                while (tur.MoveNext())
                {
                    if (tur.Current == null) continue;
                    if (tur.Current.turretID != turretID) continue;
                    turret = tur.Current;
                    turret.turretMissile = this;
                    break;
                }
                tur.Dispose();
                List<MissileLauncher>.Enumerator mml = part.FindModulesImplementing<MissileLauncher>().GetEnumerator();
                while (mml.MoveNext())
                {
                    if (mml.Current == null) continue;
                    missilepod = mml.Current;
                    break;
                }
                mml.Dispose();
                attachedRadar = part.FindModuleImplementing<ModuleRadar>();
                if (attachedRadar) hasAttachedRadar = true;

                if (hasAttachedRadar && hasDeployAnimation)
                {
                    attachedRadar.lockingYaw = !deployBlocksYaw;
                    attachedRadar.lockingPitch = !deployBlocksPitch;
                }

                finalTransform = part.FindModelTransform(finalTransformName);

                UpdateMissileChildren();

                if (!autoReturn)
                {
                    Events[nameof(ReturnTurret)].guiActive = true;
                }
            }
        }

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();
            if (turretEnabled)
            {
                if (!isReloading)
                {
                    if (hasDeployAnimation && deployBlocksReload && !(deployAnimState.normalizedTime > 0))
                    {
                        if (deployAnimRoutine != null)
                        {
                            StopCoroutine(deployAnimRoutine);
                        }

                        deployAnimRoutine = StartCoroutine(DeployAnimation(true));
                    }
                    hasReturned = false;
                }

                if ((missilepod == null && missileCount == 0) || (missilepod != null && missilepod.multiLauncher.missileSpawner.ammoCount < 1 && !BDArmorySettings.INFINITE_ORDINANCE))
                {
                    DisableTurret();
                    return;
                }

                Aim();
                UpdateMissilePositions();

                if (!vessel.IsControllable)
                {
                    DisableTurret();
                }
            }
            else
            {
                if (Quaternion.FromToRotation(finalTransform.forward, turret.baseTransform.forward) != 
                    Quaternion.identity)
                {
                    UpdateMissilePositions();
                }
                if (autoReturn && !hasReturned)
                {
                    DisableTurret();
                }
            }
            pausingAfterShot = (Time.time - timeFired < firePauseTime);
        }

        void Aim()
        {
            UpdateTarget();

            if (slaved)
            {
                SlavedAim();
            }
            else
            {
                var wm = WeaponManager;
                if (wm && wm.guardMode)
                {
                    return;
                }

                if (mouseControllable && vessel.isActiveVessel && manuallyControlled)
                {
                    MouseAim();
                }
            }
        }

        void UpdateTarget()
        {
            slaved = false;

            var wm = WeaponManager;
            if (wm && wm.CurrentMissile)
            {
                if (wm.guardMode)
                {
                    slaved = slavedGuard;
                    if (slaved) return; // Guard Mode provides the target
                }
                else
                {
                    _slavedGuard = false;
                }

                if (wm.slavingTurrets)
                {
                    slaved = true;
                    //slavedTargetPosition = MissileGuidance.GetAirToAirFireSolution(wm.CurrentMissile, wm.slavedPosition, wm.slavedVelocity);
                    slavedTargetPosition = MissileGuidance.GetAirToAirFireSolution(activeMissile, wm.slavedPosition, wm.slavedVelocity, turretLoft, turretLoftFac);
                    return;
                }
                else if (wm.mainTGP != null && ModuleTargetingCamera.windowIsOpen && wm.mainTGP.slaveTurrets)
                {
                    slaved = true;
                    //slavedTargetPosition = MissileGuidance.GetAirToAirFireSolution(wm.CurrentMissile, wm.slavedPosition, wm.slavedVelocity);
                    slavedTargetPosition = MissileGuidance.GetAirToAirFireSolution(activeMissile, wm.mainTGP.targetPointPosition, wm.mainTGP.lockedVessel ? wm.mainTGP.lockedVessel.Velocity() : Vector3.zero, turretLoft, turretLoftFac);
                    return;
                }
            }
        }

        bool _slavedGuard = false;
        MissileBase _slavedGuardMissile = null;

        public void SetSlavedGuard(bool slavedGuard, MissileBase ml)
        {
            _slavedGuard = slavedGuard;
            _slavedGuardMissile = slavedGuard ? ml : null;
        }

        public bool IsCurrentWMMissile()
        {
            MissileFire wm;
            if (!(wm = WeaponManager)) return false;

            if (!wm.CurrentMissile) return false;

            return slavedGuard || wm.CurrentMissile.GetPartName() == activeMissile.GetPartName();
        }

        public void SlavedAim()
        {
            if (pausingAfterShot) return;
            bool deployCond = hasDeployAnimation && (deployAnimState.normalizedTime < 1 || (deployBlocksReload && isReloading));

            turret.AimToTarget(slavedTargetPosition, !(deployCond && deployBlocksPitch), !(deployCond && deployBlocksYaw), IsCurrentWMMissile());
        }

        public void SetReloadBlock(float duration)
        {
            // Deploy must block reload and a deploy animation must exist before we block the turret
            if (!(deployBlocksReload && hasDeployAnimation)) return;

            _reloadUntil = Time.fixedDeltaTime + duration;

            // We only block if the respective toggle is also enabled
            if (deployBlocksPitch && turret.pitchAxisManager) turret.pitchAxisManager.SetTurretBlock(_reloadUntil);
            if (deployBlocksYaw && turret.yawAxisManager) turret.yawAxisManager.SetTurretBlock(_reloadUntil);
        }

        const int mouseAimLayerMask = (int)(LayerMasks.Parts | LayerMasks.Scenery | LayerMasks.EVA | LayerMasks.Unknown19 | LayerMasks.Unknown23 | LayerMasks.Wheels);
        void MouseAim()
        {
            if (pausingAfterShot) return;
            
            Vector3 targetPosition;
            float maxTargetingRange = 5000;

            //MouseControl
            Vector3 mouseAim = new Vector3(Input.mousePosition.x / Screen.width, Input.mousePosition.y / Screen.height, 0);
            Ray ray = FlightCamera.fetch.mainCamera.ViewportPointToRay(mouseAim);
            RaycastHit hit;
            //KerbalEVA hitEVA = null;
            //if (Physics.Raycast(ray, out hit, maxTargetingRange, 2228224))
            //{
            //    targetPosition = hit.point;
            //    hitEVA = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();

            //    if (hitEVA && hitEVA.part.vessel && hitEVA.part.vessel == vessel)
            //    {
            //        targetPosition = ray.direction * maxTargetingRange + FlightCamera.fetch.mainCamera.transform.position;
            //    }
            //}

            if (Physics.Raycast(ray, out hit, maxTargetingRange, mouseAimLayerMask))
            {
                targetPosition = hit.point;

                //aim through self vessel if occluding mouseray
                KerbalEVA eva = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                Part p = eva ? eva.part : hit.collider.gameObject.GetComponentInParent<Part>();
                if (p && p.vessel && p.vessel == vessel)
                {
                    targetPosition = ray.direction * maxTargetingRange + FlightCamera.fetch.mainCamera.transform.position;
                }
            }
            else
            {
                targetPosition = (ray.direction * (maxTargetingRange + (FlightCamera.fetch.Distance * 0.75f))) +
                                 FlightCamera.fetch.mainCamera.transform.position;
            }

            bool deployCond = hasDeployAnimation && (deployAnimState.normalizedTime < 1 || (deployBlocksReload && isReloading));
            turret.AimToTarget(targetPosition, !(deployCond && deployBlocksPitch), !(deployCond && deployBlocksYaw), IsCurrentWMMissile());
        }

        public void UpdateMissileChildren()
        {
            missileCount = 0;

            //setup com dictionary
            if (comOffsets == null)
            {
                comOffsets = new Dictionary<string, Vector3>();
            }

            //destroy the existing reference transform objects
            if (missileReferenceTransforms != null)
            {
                for (int i = 0; i < missileReferenceTransforms.Length; i++)
                {
                    if (missileReferenceTransforms[i])
                    {
                        Destroy(missileReferenceTransforms[i].gameObject);
                    }
                }
            }

            List<MissileLauncher> msl = new List<MissileLauncher>();
            List<Transform> mtfl = new List<Transform>();
            List<Transform> mrl = new List<Transform>();

            List<Part>.Enumerator child = part.children.GetEnumerator();
            while (child.MoveNext())
            {
                if (child.Current == null) continue;
                if (child.Current.parent != part) continue;

                MissileLauncher ml = child.Current.FindModuleImplementing<MissileLauncher>();

                if (!ml) continue;

                Transform mTf = child.Current.FindModelTransform("missileTransform");
                //fix incorrect hierarchy
                if (!mTf)
                {
                    Transform modelTransform = ml.part.partTransform.Find("model");

                    mTf = new GameObject("missileTransform").transform;
                    Transform[] tfchildren = new Transform[modelTransform.childCount];
                    for (int i = 0; i < modelTransform.childCount; i++)
                    {
                        tfchildren[i] = modelTransform.GetChild(i);
                    }
                    mTf.parent = modelTransform;
                    mTf.localPosition = Vector3.zero;
                    mTf.localRotation = Quaternion.identity;
                    mTf.localScale = Vector3.one;
                    IEnumerator<Transform> t = tfchildren.AsEnumerable().GetEnumerator();
                    while (t.MoveNext())
                    {
                        if (t.Current == null) continue;
                        if (BDArmorySettings.DEBUG_OTHER)
                            Debug.Log("[BDArmory.MissileTurret] : MissileTurret moving transform: " + t.Current.gameObject.name);
                        t.Current.parent = mTf;
                    }
                    t.Dispose();
                }

                if (!ml || !mTf) continue;
                msl.Add(ml);
                mtfl.Add(mTf);
                Transform mRef = new GameObject().transform;
                mRef.position = mTf.position;
                mRef.rotation = mTf.rotation;
                mRef.parent = finalTransform;
                mrl.Add(mRef);

                ml.MissileReferenceTransform = mTf;
                ml.missileTurret = this;

                if (!comOffsets.ContainsKey(ml.part.name))
                {
                    comOffsets.Add(ml.part.name, ml.part.CoMOffset);
                }

                missileCount++;
            }
            child.Dispose();

            missileChildren = msl.ToArray();
            missileTransforms = mtfl.ToArray();
            missileReferenceTransforms = mrl.ToArray();
        }

        void UpdateMissilePositions()
        {
            if (missileCount == 0)
            {
                return;
            }

            for (int i = 0; i < missileChildren.Length; i++)
            {
                if (missileTransforms[i] && missileChildren[i])// && !missileChildren[i].HasFired)
                {
                    missileTransforms[i].position = missileReferenceTransforms[i].position;
                    missileTransforms[i].rotation = missileReferenceTransforms[i].rotation;

                    Part missilePart = missileChildren[i].part;
                    if (missilePart != null)
                    {
                        Vector3 newCoMOffset =
                            missilePart.transform.InverseTransformPoint(
                                missileTransforms[i].TransformPoint(comOffsets[missilePart.name]));
                        missilePart.CoMOffset = newCoMOffset;
                    }
                }
            }
        }

        public void FireMissile(int index, Vessel targetVessel, MissileFire.TargetData targetData = null)
        {
            if (index < missileCount && missileChildren != null && missileChildren[index] != null)
            {
                PrepMissileForFire(index);

                var wm = WeaponManager;
                if (wm)
                {
                    wm.SendTargetDataToMissile(missileChildren[index], targetVessel, true, targetData, true);
                    wm.PreviousMissile = missileChildren[index];
                }
                missileChildren[index].FireMissile();
                StartCoroutine(MissileRailRoutine(missileChildren[index])); //turret is stil getting thrusted away despite this being behind a !relaodableRail conditional. investigate
                if (wm) // If the primary WM changes, the list will automatically update.
                {
                    wm.UpdateList();
                }

                if (!missileChildren[index].reloadableRail) UpdateMissileChildren();

                timeFired = Time.time;
            }
        }

        public void FireMissile(MissileLauncher ml, Vessel targetVessel, MissileFire.TargetData targetData = null)
        {
            int index = IndexOfMissile(ml);
            if (index >= 0)
            {
                Debug.Log("[BDArmory.MissileTurret] : Firing missile index: " + index);
                FireMissile(index, targetVessel, targetData);
            }
            else
            {
                Debug.Log("[BDArmory.MissileTurret] : Tried to fire a missile that doesn't exist or is not attached to the turret.");
            }
        }

        IEnumerator MissileRailRoutine(MissileLauncher ml)
        {
            var wait = new WaitForFixedUpdate();
            yield return wait;
            Ray ray = new Ray(ml.transform.position, ml.MissileReferenceTransform.forward);
            Transform turretTransform = turret.pitchTransform ? turret.pitchTransform : turret.yawTransform;
            Vector3 localOrigin = turretTransform.InverseTransformPoint(ray.origin);
            Vector3 localDirection = turretTransform.InverseTransformDirection(ray.direction);
            float forwardSpeed = ml.decoupleSpeed;
            while (ml && Vector3.SqrMagnitude(ml.transform.position - ray.origin) < railLength * railLength)
            {
                float thrust = ml.TimeIndex < ml.boostTime ? ml.thrust : ml.cruiseThrust;
                thrust = ml.TimeIndex < ml.boostTime + ml.cruiseTime ? thrust : 0;
                float accel = thrust / ml.part.mass;
                forwardSpeed += accel * Time.fixedDeltaTime;

                ray.origin = turretTransform.TransformPoint(localOrigin);
                ray.direction = turretTransform.TransformDirection(localDirection);

                Vector3 projPos = Vector3.Project(ml.vessel.transform.position - ray.origin, ray.direction) + ray.origin;
                Vector3 railVel = part.rb.GetPointVelocity(projPos);
                //Vector3 projVel = Vector3.Project(ml.vessel.Velocity-railVel, ray.direction);

                ml.vessel.SetPosition(projPos);
                //if (!ml.reloadableRail) ml.vessel.SetWorldVelocity(railVel + (forwardSpeed * ray.direction)); //this is still imparting veloctity on spawned missiles? Can function without, as long as missile turret is a static SAM site or similar
                //Why is this a thing? MissileLauncher is already imparting forward vel from jettison. If we really need to impart some vel, setWorldvel is absolutely not the method to use.
                //else ml.reloadableRail.SpawnedMissile.vessel.SetWorldVelocity(railVel + (forwardSpeed * ray.direction));
                yield return wait;

                ray.origin = turretTransform.TransformPoint(localOrigin);
                ray.direction = turretTransform.TransformDirection(localDirection);
            }
        }

        void PrepMissileForFire(int index)
        {
            Debug.Log("[BDArmory.MissileTurret] : Prepping missile for turret fire.");
            missileTransforms[index].localPosition = Vector3.zero;
            missileTransforms[index].localRotation = Quaternion.identity;
            missileChildren[index].part.partTransform.position = missileReferenceTransforms[index].position;
            missileChildren[index].part.partTransform.rotation = missileReferenceTransforms[index].rotation;

            missileChildren[index].part.CoMOffset = comOffsets[missileChildren[index].part.name];
        }

        public void PrepMissileForFire(MissileLauncher ml)
        {
            int index = IndexOfMissile(ml);

            if (index >= 0)
            {
                PrepMissileForFire(index);
            }
            else
            {
                Debug.Log("[BDArmory.MissileTurret] : Tried to prep a missile for firing that doesn't exist or is not attached to the turret.");
            }
        }

        private int IndexOfMissile(MissileLauncher ml)
        {
            if (missileCount == 0) return -1;

            for (int i = 0; i < missileCount; i++)
            {
                if (missileChildren[i] && missileChildren[i] == ml)
                {
                    return i;
                }
            }

            return -1;
        }

        public bool ContainsMissileOfType(MissileLauncher ml)
        {
            if (!ml) return false;
            if (missileCount == 0) return false;

            for (int i = 0; i < missileCount; i++)
            {
                if ((missileChildren[i]) && missileChildren[i].part.name == ml.part.name && !missileChildren[i].HasFired)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
