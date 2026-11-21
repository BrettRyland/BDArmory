using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using BDArmory.Control;
using BDArmory.Utils;
using BDArmory.Weapons.Missiles;
using BDArmory.Extensions;
using System;

namespace BDArmory.WeaponMounts
{
    public class BDDeployableRail : BDAPartModule
    {
        [KSPField]
        public string deployAnimName = "deployAnim";
        AnimationState deployState;

        public Coroutine deployRoutine;
        public Coroutine retractRoutine;

        Transform deployTransform;
        [KSPField]
        public string deployTransformName = "deployTransform";

        public bool IsDeployed => (deployed && !deploying) || (!deployed && deploying); // Deploying or being deployed. !IsDeployed = retracted or retracting.
        bool deployed = false;
        bool deploying = false;
        [KSPField] public float rotationDelay = 0.15f;

        [KSPField]
        public bool hideMissiles = false;

        public int missileCount;
        MissileLauncher[] missileChildren;
        Transform[] missileTransforms;
        Transform[] missileReferenceTransforms;

        public MissileLauncher nextMissile;

        Dictionary<Part, Vector3> comOffsets;

        bool rdyToFire;
        bool setupComplete = false;
        public bool readyToFire
        {
            get { return rdyToFire; }
        }
        MissileLauncher rdyMissile;
        public MissileLauncher readyMissile
        {
            get { return rdyMissile; }
        }

        MissileFire WeaponManager
        {
            get
            {
                if (field == null || !field.IsPrimaryWM || field.vessel != vessel)
                    field = vessel && vessel.loaded ? vessel.ActiveController().WM : null;
                return field;
            }
        }

        [KSPAction("Toggle deployment")]
        public void AGToggleRail(KSPActionParam param) => ToggleRail();

        [KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "toggle deployment")]//FIXME - localize later--
        public void ToggleRail()
        {
            UpdateMissileChildren();
            DeployRail(false);
        }
        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            setupComplete = false;
            deployTransform = part.FindModelTransform(deployTransformName);
            deployState = GUIUtils.SetUpSingleAnimation(deployAnimName, part);

            deployState.enabled = true;
            deployState.speed = 0;
            deployState.normalizedTime = 1;
            deployed = true;

            UpdateMissileChildren();

            if (HighLogic.LoadedSceneIsFlight)
            {
                part.force_activate();
                //DisableRail(); //In SPH, missiletransforms got, then retracting works fine. in flight, transforms got, and retracting moves it a littlebit, then deploying reveals an offset in where the transforms are. wth
                //DeployRail(true); //this works when called manually later, but not as part of initial spawn...
                //...but does need to occur before RCS shapshot takes place. hrm.
                StartCoroutine(OnStartDeploy());
            }
            //setupComplete = true;
        }

        public void DeployRail(bool externallycalled, Toggle state = Toggle.Toggle)
        {
            switch (state)
            {
                case Toggle.Toggle: // This allows interrupting deploy/retract.
                    StopRoutines(); // StopRoutines sets deployed taking into account deploying in progress.
                    if (!deployed)
                        deployRoutine = StartCoroutine(Deploy());
                    else
                        retractRoutine = StartCoroutine(Retract());
                    break;
                case Toggle.On:
                    if (IsDeployed) return; // Already deployed or deploying.
                    StopRoutines();
                    deployRoutine = StartCoroutine(Deploy());
                    break;
                case Toggle.Off:
                    if (!IsDeployed) return; // Already retracted or retracting.
                    StopRoutines();
                    retractRoutine = StartCoroutine(Retract());
                    break;
            }
            if (externallycalled) return;
            using List<Part>.Enumerator p = part.symmetryCounterparts.GetEnumerator();
            while (p.MoveNext())
            {
                if (p.Current == null || p.Current == part) continue;
                var rail = p.Current.FindModuleImplementing<BDDeployableRail>();
                rail.UpdateMissileChildren();
                rail.DeployRail(true, state);
            }
        }
        public IEnumerator OnStartDeploy()
        {
            yield return new WaitForSecondsFixed(1); //figure out what the wait interval needs to be. Too soon, and the offsets get messed up. maybe have the RCS snapshot delay instead?
            UpdateMissileChildren();
            DeployRail(true, Toggle.Off); // Close bays on flight startup
        }
        public IEnumerator Deploy()
        {
            deployState.enabled = true;
            deployState.speed = 1;
            deploying = true;
            for (int i = 0; i < missileChildren.Length; i++)
            {
                if (!missileTransforms[i] || !missileChildren[i] || missileChildren[i].HasFired) continue;
                Part missilePart = missileChildren[i].part;
                missilePart.ShieldedFromAirstream = false;
                if (hideMissiles) missilePart.SetOpacity(1);
            }
            while (deployState.normalizedTime < 1)
            {
                UpdateChildrenPos();
                yield return new WaitForFixedUpdate();
            }
            deployState.normalizedTime = 1;
            deployState.speed = 0;
            deployState.enabled = false;
            deployed = true;
            deploying = false;
            if (HighLogic.LoadedSceneIsFlight)
            {
                yield return new WaitForSecondsFixed(rotationDelay);
                rdyToFire = true;
            }
            if (HighLogic.LoadedSceneIsEditor)
            {
                //have attachnode toggle on when deployed, and off when stowed
                using List<AttachNode>.Enumerator node = part.attachNodes.GetEnumerator();
                while (node.MoveNext())
                {
                    if (node.Current.id.ToLower().Contains("rail"))
                    {
                        node.Current.nodeType = AttachNode.NodeType.Stack;
                        node.Current.radius = 1f;
                    }
                }
            }
        }
        public IEnumerator Retract()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                //have attachnode toggle on when deployed, and off when stowed
                using (List<AttachNode>.Enumerator node = part.attachNodes.GetEnumerator())
                    while (node.MoveNext())
                    {
                        if (node.Current.id.ToLower().Contains("rail"))
                        {
                            node.Current.nodeType = AttachNode.NodeType.Dock;
                            node.Current.radius = 0.001f;
                        }
                    }
            }
            rdyToFire = false;
            deployState.enabled = true;
            deployState.speed = -1;
            deploying = true;
            while (deployState.normalizedTime > 0)
            {
                UpdateChildrenPos();
                yield return new WaitForFixedUpdate();
            }
            deployState.normalizedTime = 0;
            deployState.speed = 0;
            deployState.enabled = false;
            deployed = false;
            deploying = false;
            setupComplete = true;
            for (int i = 0; i < missileChildren.Length; i++)
            {
                if (!missileTransforms[i] || !missileChildren[i]) continue;
                Part missilePart = missileChildren[i].part;
                missilePart.ShieldedFromAirstream = true;
                if (hideMissiles) missilePart.SetOpacity(0);
            }
        }
        public void StopRoutines()
        {
            if (deploying) deployed = deployRoutine != null; // If partway through deploying, consider it as fully deployed/retracted for toggle logic.
            deploying = false;

            if (retractRoutine != null)
            {
                StopCoroutine(retractRoutine);
                retractRoutine = null;
            }

            if (deployRoutine != null)
            {
                StopCoroutine(deployRoutine);
                deployRoutine = null;
            }
        }
        public void EnableRail()
        {
            if (!setupComplete) return;
            DeployRail(true, Toggle.On);
        }
        public void DisableRail()
        {
            if (!setupComplete) return;
            DeployRail(true, Toggle.Off);
        }

        public void UpdateChildrenPos()
        {
            /*
            using (List<Part>.Enumerator p = part.children.GetEnumerator())
                while (p.MoveNext())
                {
                    if (p.Current == null) continue;
                    Transform mTf = p.Current.FindModelTransform("missileTransform");
                    if (mTf == null) continue;
                    mTf.position = deployTransform.position;
                    mTf.rotation = deployTransform.rotation;
                }
            */
            if (missileCount == 0)
            {
                return;
            }

            for (int i = 0; i < missileChildren.Length; i++)
            {
                if (!missileTransforms[i] || !missileChildren[i] || missileChildren[i].vessel != this.vessel) continue;
                missileTransforms[i].position = missileReferenceTransforms[i].position; //wait, is this just moving the mesh, but the part stays where it is? Would explain the need for CoM offset
                missileTransforms[i].rotation = missileReferenceTransforms[i].rotation; //have this reset on spawn?
                //float scaleVector = Mathf.Lerp(scaleVector, 1, 0.02f / deployState.length); //have the missile scale (so big missiles can fit inside shallow bays without clipping through base of the bay? Future SI, play around with this
                //missileTransforms[i].localScale = new Vector3(scaleVector, scaleVector, 1);
                Part missilePart = missileChildren[i].part;
                Vector3 newCoMOffset =
                    missilePart.transform.InverseTransformPoint(
                        missileTransforms[i].TransformPoint(comOffsets[missilePart]));
                missilePart.CoMOffset = newCoMOffset;
            }
        }
        public void UpdateMissileChildren()
        {
            missileCount = 0;

            if (comOffsets == null)
            {
                comOffsets = new Dictionary<Part, Vector3>();
            }

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
            using (List<Part>.Enumerator child = part.children.GetEnumerator())
                while (child.MoveNext())
                {
                    if (child.Current == null) continue;
                    if (child.Current.parent != part) continue;

                    MissileLauncher ml = child.Current.FindModuleImplementing<MissileLauncher>();

                    if (!ml) continue;

                    Transform mTf = child.Current.FindModelTransform("missileTransform");
                    //mTf = child.Current.partTransform;
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
                        using (IEnumerator<Transform> t = tfchildren.AsEnumerable().GetEnumerator())
                            while (t.MoveNext())
                            {
                                if (t.Current == null) continue;
                                t.Current.parent = mTf;
                            }
                    }

                    if (!ml || !mTf) continue;
                    msl.Add(ml);
                    mtfl.Add(mTf);
                    Transform mRef = new GameObject().transform;
                    mRef.position = mTf.position;
                    mRef.rotation = mTf.rotation;
                    mRef.parent = deployTransform;
                    mrl.Add(mRef);

                    ml.MissileReferenceTransform = mTf;
                    ml.deployableRail = this;

                    ml.decoupleForward = false;
                    ml.dropTime = Math.Max(ml.dropTime, 0.2f);

                    if (!comOffsets.ContainsKey(ml.part))
                    {
                        comOffsets.Add(ml.part, ml.part.CoMOffset);
                    }
                    missileCount++;
                }

            missileChildren = msl.ToArray();
            missileCount = missileChildren.Length;
            missileTransforms = mtfl.ToArray();
            missileReferenceTransforms = mrl.ToArray(); //one of these, either the missile transform, or the deploytransform, is getting offset a bit
        }

        public void PrepMissileForFire(MissileLauncher ml)
        {
            int index = IndexOfMissile(ml);

            if (index >= 0)
            {
                PrepMissileForFire(index);
            }
        }

        void PrepMissileForFire(int index)
        {
            missileTransforms[index].localPosition = Vector3.zero;
            missileTransforms[index].localRotation = Quaternion.identity;
            missileChildren[index].part.partTransform.position = missileReferenceTransforms[index].position;
            missileChildren[index].part.partTransform.rotation = missileReferenceTransforms[index].rotation;

            missileChildren[index].part.CoMOffset = comOffsets[missileChildren[index].part];
        }

        public void FireMissile(int missileIndex, Vessel targetVessel, MissileFire.TargetData targetData = null) //this is causing it not to fire, determine how missileindex is assigned
        {
            if (!readyToFire) return;

            if (missileIndex < missileCount && missileChildren != null && missileChildren[missileIndex] != null)
            {
                PrepMissileForFire(missileIndex);

                var wm = WeaponManager;
                if (wm)
                {
                    wm.SendTargetDataToMissile(missileChildren[missileIndex], targetVessel, true, targetData, true);
                    wm.PreviousMissile = missileChildren[missileIndex];
                }

                missileChildren[missileIndex].FireMissile();

                rdyMissile = null;

                UpdateMissileChildren();

                if (wm) // If the primary WM changes, the list will automatically update.
                {
                    wm.UpdateList();
                }
            }
        }

        public void FireMissile(MissileLauncher ml, Vessel targetVessel, MissileFire.TargetData targetData = null)
        {
            if (!readyToFire)
            {
                return;
            }

            int index = IndexOfMissile(ml);
            if (index >= 0)
            {
                FireMissile(index, targetVessel, targetData);
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
                if ((missileChildren[i]) && missileChildren[i].part.name == ml.part.name)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
