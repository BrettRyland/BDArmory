using BDArmory.Extensions;
using BDArmory.Settings;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BDArmory.Utils
{
    public class DecoupledObject : MonoBehaviour
    {
        Rigidbody rb;
        Part _part; // Original part this came off of
        //physicalObject pObj;
        double lastAtmDensity;
        /// <summary>
        /// Drag Area * C_D / mass
        /// </summary>
        public double dragConst = 0.001d;

        IEnumerator SelfDestructRoutine()
        {
            yield return new WaitForSecondsFixed(0.25f);
            Collider[] col = gameObject.GetComponentsInChildren<Collider>();
            for (int j = 0; j < col.Length; ++j)
            {
                if (col[j] == null) continue;
                col[j].enabled = true;
            }
            yield return new WaitForSecondsFixed(5);
            Deactivate();
        }

        /*public void DecoupleBooster(Part part, float ejectSpeed)
        {
            *//*gameObject.transform.parent = null;
            gameObject.SetLayerRecursive((int)LayerMasks.PhysicalObjects);
            gameObject.SetTagsRecursive("Drag_Hidden");
            List<Renderer> tempRenders = (gameObject.GetComponentsInChildren<Renderer>()).ToList();
            tempRenders.RemoveNonHighlightableRenderers();
            int i = 0;
            for (int count = tempRenders.Count; i < count; i++)
            {
                part.HighlightRenderer.Remove(tempRenders[i]);
            }
            part.partRendererBoundsIgnore.Add(transform.name);*//*
            //transform.parent = null;
            //rb = gameObject.AddComponent<Rigidbody>();
            //gameObject.AddComponent<KSPForceApplier>();

            pObj = physicalObject.ConvertToPhysicalObject(part, gameObject);
            gameObject.transform.SetParent(null, true);
            rb = pObj.rb;

            rb.velocity = part.Rigidbody.velocity + ejectSpeed * transform.forward;

            StartCoroutine(SelfDestructRoutine());
        }*/

        /*public static void Decouple(GameObject obj, Part part, float ejectSpeed)
        {
            GameObject booster = Instantiate(obj, obj.transform.position, obj.transform.rotation);
            obj.SetActive(false);
            booster.AddComponent<DecoupledBooster>().DecoupleBooster(part, ejectSpeed);
        }*/

        public void FixedUpdate()
        {
            // Gravity
            Vector3d force = (_part && _part.vessel) ? 
                _part.vessel.graviticAcceleration : // use part vessel's gravity
                (FlightGlobals.getGeeForceAtPosition(gameObject.transform.position, FlightGlobals.currentMainBody) * PhysicsGlobals.GraviticForceMultiplier); // Fallback on just a simple gravity calc
            rb.AddForce(force, ForceMode.Acceleration);
            if (_part && _part.vessel)
            {
                // Update density (if able)
                lastAtmDensity = _part.vessel.atmDensity;
            }
            (double airspeed, Vector3d velNorm) = (rb.velocity + BDKrakensbane.FrameVelocityV3f).MagNorm();
            // Calc drag
            rb.AddForce((dragConst * -0.5d * lastAtmDensity * airspeed * airspeed * BDArmorySettings.GLOBAL_DRAG_MULTIPLIER) * velNorm, ForceMode.Force);
        }

        public DecoupledObject Decouple(Part part, GameObject baseObject, float ejectSpeed)
        {
            gameObject.transform.position = baseObject.transform.position;
            gameObject.transform.rotation = baseObject.transform.rotation;
            if (!rb) rb = gameObject.GetComponent<Rigidbody>();
            if (!rb) rb = gameObject.AddComponent<Rigidbody>();

            _part = part;
            lastAtmDensity = part.vessel.atmDensity;

            rb.velocity = part.Rigidbody.velocity + ejectSpeed * transform.forward;

            Collider[] col = gameObject.GetComponentsInChildren<Collider>();
            for (int j = 0; j < col.Length; ++j)
            {
                if (col[j] == null) continue;
                col[j].enabled = false;
            }

            gameObject.SetActive(true);
            baseObject.SetActive(false);
            StartCoroutine(SelfDestructRoutine());
            return this;
        }

        public void Deactivate()
        {
            if (gameObject is not null && gameObject.activeSelf) // Deactivate even if a parent is already inactive.
            {
                _part = null;
                gameObject.SetActive(false);
            }
        }

        /*public static ObjectPool[] SetupDecoupledDummyPool(List<GameObject> objects)
        {
            ObjectPool[] poolArr = new ObjectPool[objects.Count];
            for (int i = 0; i < objects.Count; ++i)
            {
                if (objects[i] == null || objects[i].transform == null)
                {
                    poolArr[i] = null;
                    continue;
                }
                Transform tempParent = objects[i].transform.parent;
                objects[i].transform.SetParent(null, true);
                GameObject template = Instantiate(objects[i], null);
                template.AddComponent<Rigidbody>();
                template.AddComponent<DecoupledObject>();
                //template.SetTagsRecursive("Drag_Hidden");
                template.SetActive(false);
                *//*Collider[] col = template.GetComponentsInChildren<Collider>();
                for (int j = 0; j < col.Length; ++j)
                {
                    if (col[j] == null) continue;
                    col[j].enabled = false;
                }*//*
                poolArr[i] = ObjectPool.CreateObjectPool(template, 16, true, true);
                objects[i].transform.SetParent(tempParent, true);
            }
            return poolArr;
        }*/

        public static ObjectPool[] SetupDecoupledDummyPoolNonAlloc(List<Transform> objects)
        {
            ObjectPool[] poolArr = new ObjectPool[objects.Count];
            for (int i = 0; i < objects.Count; ++i)
            {
                if (objects[i] == null || objects[i].gameObject == null)
                {
                    poolArr[i] = null;
                    continue;
                }
                objects[i].SetParent(null, true);
                GameObject template = objects[i].gameObject;
                template.AddComponent<Rigidbody>();
                template.AddComponent<DecoupledObject>();
                template.SetTagsRecursive("Drag_Hidden");
                template.SetActive(false);
                Collider[] col = template.GetComponentsInChildren<Collider>();
                for (int j = 0; j < col.Length; ++j)
                {
                    if (col[j] == null) continue;
                    col[j].enabled = false;
                }
                poolArr[i] = ObjectPool.CreateObjectPool(template, 16, true, true);
            }
            return poolArr;
        }
    }
}
