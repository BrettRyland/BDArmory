using BDArmory.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace BDArmory.Weapons.Missiles
{
    class MissileDummy : MonoBehaviour
    {
        /// <summary>
        /// Create a Dummy missile model to attach to VLS or other multi-missile launchers prior to launch and the actual missile created and fired.
        /// </summary>
        Part parentPart;
        static bool hasOnVesselUnloaded = false;
        public float missileDia = 0.2f;
        public static ObjectPool CreateDummyPool(string modelPath)
        {
            if ((Versioning.version_major == 1 && Versioning.version_minor > 10) || Versioning.version_major > 1) // onVesselUnloaded event introduced in 1.11
                hasOnVesselUnloaded = true;
            GameObject template = GameDatabase.Instance.GetModel(modelPath);
            template.SetActive(false);
            template.AddComponent<MissileDummy>();
            
            return ObjectPool.CreateObjectPool(template, 10, true, true, 0, true);
        }

        public MissileDummy AttachAt(Part Parent, Transform missileTransform)
        {
            if (Parent is null) return null;
            parentPart = Parent;
            transform.SetParent(Parent.transform);
            transform.position = missileTransform.position;
            transform.rotation = missileTransform.rotation;
            transform.parent = missileTransform;
            parentPart.OnJustAboutToDie += OnParentDestroy;
            parentPart.OnJustAboutToBeDestroyed += OnParentDestroy;
            if (hasOnVesselUnloaded)
            {
                OnVesselUnloaded_1_11(false); // Remove any previous onVesselUnloaded event handler (due to forced reuse in the pool).
                OnVesselUnloaded_1_11(true); // Catch unloading events too.
            }
            gameObject.SetActive(true);
            return this;
        }

        void OnParentDestroy()
        {
            if (parentPart is not null)
            {
                parentPart.OnJustAboutToDie -= OnParentDestroy;
                parentPart.OnJustAboutToBeDestroyed -= OnParentDestroy;
                Deactivate();
            }
        }

        void OnVesselUnloaded(Vessel vessel)
        {
            if (parentPart is not null && (parentPart.vessel is null || parentPart.vessel == vessel))
            {
                OnParentDestroy();
            }
            else if (parentPart is null)
            {
                Deactivate();
            }
        }
        void OnVesselUnloaded_1_11(bool addRemove) // onVesselUnloaded event introduced in 1.11
        {
            if (addRemove)
                GameEvents.onVesselUnloaded.Add(OnVesselUnloaded);
            else
                GameEvents.onVesselUnloaded.Remove(OnVesselUnloaded);
        }
        public void Deactivate()
        {
            if (gameObject is not null && gameObject.activeSelf) // Deactivate even if a parent is already inactive.
            {
                parentPart = null;
                transform.parent = null;
                gameObject.SetActive(false);
            }
        }
        public void OnDestroy() // This shouldn't be happening except on exiting KSP, but sometimes they get destroyed instead of disabled!
        {
            if (hasOnVesselUnloaded) // onVesselUnloaded event introduced in 1.11
                OnVesselUnloaded_1_11(false);
        }
    }
}
