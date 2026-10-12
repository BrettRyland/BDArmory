using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using BDArmory.FX;
using BDArmory.Settings;
using BDArmory.Utils;

namespace BDArmory.Weapons.Missiles
{
    /// <summary>
    /// Add-on Module to MultiMissileLauncher
    /// tube covers (e.g., Mk 29 / SEARAM style) that pop off when the missile in their tube is launched
    /// Covers are transforms with the right TransformName ("eject", e.g., eject1, eject1.001, eject.002) and 
    /// are children of the launch transform (launcher1, launcher2, etc.).
    /// Covers are ejected along the local (+Z) axis and behave like ejected shells afterwards. 
    /// </summary>
    public class ModuleTubeCovers : PartModule
    {
        [KSPField] public float coverSpeedFactor = 1f;     // Cover speed as a fraction of the estimated missile speed. 0 = always use coverEjectSpeed.
        [KSPField] public float coverMaxEjectSpeed = 50f;  // Cap (m/s): small fast rigidbodies tunnel through colliders.
        [KSPField] public float coverEjectSpeed = 6f; // Speed (m/s) at which it is ejected.
        [KSPField] public float coverEjectDeviation = 1f; // Random velocity deviation (m/s) off the main axis of travel.
        [KSPField] public float coverLifeTime = 2f; // Seconds before an ejected cover disappears (shellEjectLifeTime on ballistics).
        [KSPField] public float coverEjectDelay = 0f; // Delay (s) between the launch and the covers being ejected.
        [KSPField] public float coverMass = 0.002f;

        class TubeCover
        {
            public Transform transform;
            public Transform parent;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
            public Collider[] colliders;
            public DetachedCover motion;
            public bool detached;
            public float ejectSpeed;
        }

        readonly Dictionary<Transform, List<TubeCover>> coversByTube = new Dictionary<Transform, List<TubeCover>>();
        const string coverPrefix = "eject";

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            GameEvents.onPartDie.Add(OnPartDie);
            // Find the covers now (this disables their colliders), without waiting for the launcher to call us.
            var launcher = part.FindModuleImplementing<MultiMissileLauncher>();
            var launchTransform = launcher != null ? part.FindModelTransform(launcher.launchTransformName) : null;
            if (launchTransform != null)
                for (int i = 0; i < launchTransform.childCount; ++i) GetCovers(launchTransform.GetChild(i));
        }

        void OnDestroy()
        {
            GameEvents.onPartDie.Remove(OnPartDie);
            DestroyDetachedCovers();
        }

        void OnPartDie(Part p)
        {
            if (p == part) DestroyDetachedCovers();
        }

        /// <summary>The covers under a tube transform, found on first use. Never null.</summary>
        List<TubeCover> GetCovers(Transform tube)
        {
            if (coversByTube.TryGetValue(tube, out var list)) return list;
            list = new List<TubeCover>();
            foreach (var t in tube.GetComponentsInChildren<Transform>(true))
            {
                if (t == tube || !IsCoverName(t.name)) continue;
                if (t.GetComponentInParent<MissileDummy>() != null) continue; // Part of a missile model, not the launcher.
                if (HasCoverAncestor(t, tube)) continue; // Moves with its parent cover.
                var cover = new TubeCover
                {
                    transform = t,
                    parent = t.parent,
                    localPosition = t.localPosition,
                    localRotation = t.localRotation,
                    localScale = t.localScale,
                    colliders = t.GetComponentsInChildren<Collider>(true),
                    motion = t.gameObject.GetComponent<DetachedCover>() ?? t.gameObject.AddComponent<DetachedCover>(),
                    detached = false,
                };
                foreach (var collider in cover.colliders) if (collider != null) collider.enabled = false; // Only active once ejected.
                list.Add(cover);
            }
            coversByTube[tube] = list;
            return list;
        }

        bool HasCoverAncestor(Transform t, Transform tube)
        {
            for (var p = t.parent; p != null && p != tube; p = p.parent)
                if (IsCoverName(p.name)) return true;
            return false;
        }
        static bool IsCoverName(string name) =>
            name.StartsWith(coverPrefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Pop off the covers of a tube, optionally after coverEjectDelay. Call before the tube transform is scaled to zero.
        /// </summary>
        public void Eject(Transform tube, float launchAccel = 0f, float launchSpeed = 0f)
        {
            if (tube == null) return;
            foreach (var cover in GetCovers(tube))
            {
                try // Cover handling is cosmetic: a failure here must never stop a missile from launching.
                {
                    if (cover.detached) continue; 
                    cover.ejectSpeed = EstimateEjectSpeed(cover, tube, launchAccel, launchSpeed); // Before any reparenting.
                    if (coverEjectDelay > 0)
                    {
                        // The tube is about to be scaled to zero (hiding its missile), which would hide covers parented to it too,
                        // so move delayed covers to the tube's parent, keeping their world pose, until they are ejected.
                        cover.transform.SetParent(tube.parent, true);
                        StartCoroutine(EjectDelayed(cover, coverEjectDelay));
                    }
                    else DoEject(cover);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[BDArmory.ModuleTubeCovers]: Failed to eject cover '{cover?.transform?.name}' on {part.name}: {e}");
                }
            }
        }
        float EstimateEjectSpeed(TubeCover cover, Transform tube, float accel, float v0)
        {
            if (coverSpeedFactor <= 0f) return coverEjectSpeed;
            float dist = Mathf.Max(0f, Vector3.Dot(cover.transform.position - tube.position, tube.forward));
            float missileSpeed = Mathf.Sqrt(v0 * v0 + 2f * accel * dist);
            return Mathf.Clamp(missileSpeed * coverSpeedFactor, coverEjectSpeed, Mathf.Max(coverEjectSpeed, coverMaxEjectSpeed));
        }

        IEnumerator EjectDelayed(TubeCover cover, float delay)
        {
            yield return new WaitForSecondsFixed(delay);
            try { DoEject(cover); }
            catch (Exception e) { Debug.LogError($"[BDArmory.ModuleTubeCovers]: Failed to eject cover '{cover?.transform?.name}' on {part.name}: {e}"); }
        }
        
        /// <summary>
        /// Perform the actual ejection of a single tube cover.
        /// </summary>
        /// <param name="cover">The TubeCover to eject. If null, already detached, or this part is invalid the method returns immediately.</param>
        void DoEject(TubeCover cover)
        {
            if (part == null || cover == null || cover.transform == null || cover.detached) return;
            cover.detached = true;

            if (!BDArmorySettings.EJECT_SHELLS || part.rb == null)
            {
                cover.transform.gameObject.SetActive(false); // Debris effects disabled: just remove the cover.
                return;
            }

            Vector3 worldScale = cover.transform.lossyScale;
            Vector3 direction = cover.transform.forward; // Covers are ejected along their own forward (+Z) axis, not the tube's.
            cover.transform.SetParent(null, true);
            cover.transform.localScale = worldScale;
            Vector3 ejectVelocity = direction * cover.ejectSpeed + UnityEngine.Random.insideUnitSphere * coverEjectDeviation;
            cover.motion.mass = coverMass;
            cover.motion.Launch(part.rb.velocity, ejectVelocity, coverLifeTime, cover.colliders, GetEjectIgnoreColliders());
        }

        /// <summary>
        /// Put covers back on tubes that have a missile loaded and remove covers from tubes that don't.
        /// Called when the launcher (re)populates its missile dummies, e.g., on load, after a reload or when the ordnance is changed in the editor.
        /// </summary>
        /// <param name="tubes">The launcher's tube transforms.</param>
        /// <param name="loadedOrdnance">The number of tubes (counting from the first) that have a missile.</param>
        public void UpdateCovers(Transform[] tubes, int loadedOrdnance)
        {
            if (tubes == null) return;
            for (int i = 0; i < tubes.Length; ++i)
            {
                if (tubes[i] == null) continue;
                foreach (var cover in GetCovers(tubes[i]))
                {
                    try // Cover handling is cosmetic: a failure here must never stop the dummies being reset or the tubes being reloaded.
                    {
                        if (cover == null || cover.transform == null) continue;
                        if (i < loadedOrdnance) RestoreCover(cover);
                        else if (!cover.detached)
                        {
                            // No missile in this tube (already fired, or never loaded): no cover.
                            cover.detached = true;
                            cover.transform.gameObject.SetActive(false);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[BDArmory.ModuleTubeCovers]: Failed to update cover '{cover?.transform?.name}' of tube {i} on {part.name}: {e}");
                    }
                }
            }
        }

        /// <summary>
        /// Perform the actual restoration of a single tube cover.
        /// </summary>
        /// <param name="cover">The TubeCover to restore. If already attached the method returns immediately.</param>
        void RestoreCover(TubeCover cover)
        {
            if (!cover.detached) return;
            cover.motion.Halt();
            cover.transform.SetParent(cover.parent, false);
            cover.transform.localPosition = cover.localPosition;
            cover.transform.localRotation = cover.localRotation;
            cover.transform.localScale = cover.localScale;
            foreach (var collider in cover.colliders) if (collider != null) collider.enabled = false; // Off while attached.
            cover.transform.gameObject.SetActive(true);
            cover.detached = false;
        }

        /// <summary>
        /// Colliders an ejected cover must not collide with: everything on the launching vessel, plus the colliders of this part's covers.
        /// </summary>
        Collider[] GetEjectIgnoreColliders()
        {
            return part != null
                ? part.GetComponentsInChildren<Collider>()
                : new Collider[0];
        }

        /// <summary>
        /// Clean up covers that are currently floating free of the part so they don't leak when the part is destroyed.
        /// </summary>
        void DestroyDetachedCovers()
        {
            foreach (var covers in coversByTube.Values)
                foreach (var cover in covers)
                {
                    if (cover == null || !cover.detached || cover.transform == null) continue;
                    if (cover.transform.parent == null) Destroy(cover.transform.gameObject);
                }
        }
    }
}
