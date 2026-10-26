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
       
        // Dummy's get prefixed "{parentPartInstanceID}_{slotIndex}" so TURD ignores any dummy objects
        public string UniqueId { get; private set; } = "";

        // Original names of every transform in the dummy's hierarchy (root included), captured the first time the dummy is attached.
        // Names are always rebuilt from these, so pooled dummies never accumulate prefixes on reuse.
        Transform[] namedTransforms = null;
        string[] originalNames = null;
        public static ObjectPool CreateDummyPool(string modelPath)
        {
            if ((Versioning.version_major == 1 && Versioning.version_minor > 10) || Versioning.version_major > 1) // onVesselUnloaded event introduced in 1.11
                hasOnVesselUnloaded = true;
            GameObject template = GameDatabase.Instance.GetModel(modelPath);
            template.SetActive(false);
            template.AddComponent<MissileDummy>();
            
            return ObjectPool.CreateObjectPool(template, 10, true, true, 0, true);
        }

        public MissileDummy AttachAt(Part Parent, Transform missileTransform, int slotIndex = -1)
        {
            if (Parent is null) return null;
            parentPart = Parent;
            AssignUniqueId(Parent, slotIndex);
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

        /// <summary>
        /// Prefix every GameObject in this dummy's hierarchy with the launcher's unique id.
        /// Nothing in BDA uses dummy objects by name so it's fine.
        /// </summary>
        void AssignUniqueId(Part parent, int slotIndex)
        {
            if (namedTransforms == null)
            {
                namedTransforms = GetComponentsInChildren<Transform>(true);
                originalNames = new string[namedTransforms.Length];
                for (int i = 0; i < namedTransforms.Length; ++i) originalNames[i] = namedTransforms[i].name;
            }
            UniqueId = $"{parent.GetInstanceID()}_{(slotIndex >= 0 ? slotIndex : gameObject.GetInstanceID())}";
            ApplyNamePrefix($"dummy{UniqueId}_");
        }

        void ApplyNamePrefix(string prefix)
        {
            if (namedTransforms == null) return;
            for (int i = 0; i < namedTransforms.Length; ++i)
                if (namedTransforms[i] != null) namedTransforms[i].name = prefix + originalNames[i];
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
                UniqueId = "";
                ApplyNamePrefix("");
                transform.parent = null;
                transform.localScale = Vector3.one;
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
