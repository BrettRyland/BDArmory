using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.CompilerServices;

using BDArmory.Settings;
using BDArmory.Utils;
using BDArmory.Weapons.Missiles;

namespace BDArmory.Extensions
{
    public static class VesselExtensions
    {
        public static HashSet<Vessel.Situations> InOrbitSituations = new HashSet<Vessel.Situations> { Vessel.Situations.ORBITING, Vessel.Situations.SUB_ORBITAL, Vessel.Situations.ESCAPING };

        public static bool InOrbit(this Vessel v)
        {
            if (v == null) return false;
            return InOrbitSituations.Contains(v.situation);
        }

        public static bool InVacuum(this Vessel v)
        {
            return v.atmDensity <= 0.001f;
        }

        public static bool InNearVacuum(this Vessel v)
        {
            return v.atmDensity <= 0.05f;
        }

        public static bool IsUnderwater(this Vessel v)
        {
            if (!v) return false;
            return v.altitude < -20; //some boats sit slightly underwater, this is only for submersibles
        }

        /// <summary>
        /// Check for a vessel being a missile.
        /// It's considered a missile if the root part is a missile, or it has a MMG that has fired.
        /// </summary>
        public static bool IsMissile(this Vessel v)
        {
            if (v == null) return false;
            if (v.rootPart.IsMissile()) return true;
            var mmg = VesselModuleRegistry.GetModule<BDModularGuidance>(v);
            if (mmg == null) return false;
            return mmg.HasFired;
        }

        /// <summary>
        /// Get the vessel's velocity accounting for whether it's in orbit and optionally whether it's above 100km (which is another hard-coded KSP limit).
        /// </summary>
        /// <param name="v"></param>
        /// <param name="altitudeCheck"></param>
        /// <returns></returns>
        public static Vector3d Velocity(this Vessel v, bool altitudeCheck = true)
        {
            try
            {
                if (v == null) return Vector3d.zero;
                if (v.InOrbit() && (!altitudeCheck || v.altitude > 1e5f)) return v.obt_velocity;
                else return v.srf_velocity;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BDArmory.VesselExtensions]: Exception thrown in Velocity: " + e.Message + "\n" + e.StackTrace);
                //return v.srf_velocity;
                return new Vector3d(0, 0, 0);
            }
        }

        /// <summary>
        /// Set the world velocity of a vessel, taking into account Krakensbane, which Vessel.SetWorldVelocity doesn't.
        /// </summary>
        /// <param name="v"></param>
        /// <param name="velocity"></param>
        public static void SetVelocity(this Vessel v, in Vector3 velocity)
        {
            v.SetWorldVelocity(BDKrakensbane.IsActive ? velocity - BDKrakensbane.FrameVelocityV3f : velocity);
        }

        public static double GetFutureAltitude(this Vessel vessel, float predictionTime = 10) => GetRadarAltitudeAtPos(AIUtils.PredictPosition(vessel, predictionTime));

        public static Vector3 GetFuturePosition(this Vessel vessel, float predictionTime = 10) => AIUtils.PredictPosition(vessel, predictionTime);

        public static float GetRadarAltitudeAtPos(in Vector3 position)
        {
            double latitudeAtPos = FlightGlobals.currentMainBody.GetLatitude(position);
            double longitudeAtPos = FlightGlobals.currentMainBody.GetLongitude(position);

            float radarAlt = Mathf.Clamp(
                (float)(FlightGlobals.currentMainBody.GetAltitude(position) -
                        FlightGlobals.currentMainBody.TerrainAltitude(latitudeAtPos, longitudeAtPos)), 0,
                (float)FlightGlobals.currentMainBody.GetAltitude(position));
            return radarAlt;
        }

        /// <summary>
        /// Get a vessel's "radius".
        /// Note:
        ///   - Use bounds whenever you want a more precise radius from a given perspective. It is more computationally expensive though.
        ///   - Use average:true for situations such as targeting/proximity checks when not using bounds to better handle non-spherical vessels.
        ///   - Use average:false (the default) for situations where the maximum radius is important, e.g., collision avoidance.
        /// </summary>
        /// <param name="vessel"></param>
        /// <param name="fireTransform"></param>
        /// <param name="bounds"></param>
        /// <param name="average">If not using bounds, return the average of the dimensions instead of the max.</param>
        /// <returns></returns>
        public static float GetRadius(this Vessel vessel, in Vector3 fireTransform = default, in Vector3 bounds = default, bool average = false)
        {
            if (fireTransform == Vector3.zero || bounds == Vector3.zero)
            {
                // Get vessel size.
                Vector3 size = vessel.vesselSize;

                if (average)
                    // Get the average dimension (without using bounds) for a more appropriate estimate of the vessel's radius for targeting/proximity checks.
                    return (size.x + size.y + size.z) / 6f;
                else
                    // Get largest dimension as this is mostly used for terrain/vessel avoidance. More precise "radii" should probably pass the fireTransform and bounds parameters.
                    return Mathf.Max(Mathf.Max(size.x, size.y), size.z) / 2f;
            }
            else
            {
                // Check the 4 diagonals of the box and take the max.
                var radius = BDAMath.Sqrt(Mathf.Max(
                    (vessel.vesselTransform.up * bounds.y + vessel.vesselTransform.right * bounds.x + vessel.vesselTransform.forward * bounds.z).ProjectOnPlane(fireTransform).sqrMagnitude,
                    (-vessel.vesselTransform.up * bounds.y + vessel.vesselTransform.right * bounds.x + vessel.vesselTransform.forward * bounds.z).ProjectOnPlane(fireTransform).sqrMagnitude,
                    (vessel.vesselTransform.up * bounds.y - vessel.vesselTransform.right * bounds.x + vessel.vesselTransform.forward * bounds.z).ProjectOnPlane(fireTransform).sqrMagnitude,
                    (vessel.vesselTransform.up * bounds.y + vessel.vesselTransform.right * bounds.x - vessel.vesselTransform.forward * bounds.z).ProjectOnPlane(fireTransform).sqrMagnitude
                )) / 2f;
#if DEBUG
                if (radius < bounds.x / 2f && radius < bounds.y / 2f && radius < bounds.z / 2f) Debug.LogWarning($"DEBUG Radius {radius} of {vessel.vesselName} is less than half its minimum bounds {bounds}");
#endif
                return Mathf.Min(radius, (Mathf.Max(Mathf.Max(vessel.vesselSize.x, vessel.vesselSize.y), vessel.vesselSize.z) / 2f) * 1.7321f); // clamp bounds to vesselsize in case of Bounds erroneously reporting vessel sizes that are impossibly large
            }
        }

        static HashSet<string> badBoundsParts = null;
        static void GetBadBoundsParts()
        {
            badBoundsParts = new HashSet<string>();
            foreach (var part in PartLoader.LoadedPartsList)
            {
                var weapon = part.partPrefab.FindModuleImplementing<Weapons.ModuleWeapon>();
                if (weapon != null)
                {
                    if (BDArmorySettings.DEBUG_OTHER && !string.IsNullOrEmpty(weapon.name)) // For some reason, the weapons are showing up a second time with an empty name.
                        Debug.Log($"[BDArmory.VesselExtensions]: Adding {weapon.name} to the bounds exclusion list.");
                    badBoundsParts.Add(weapon.name); // Exclude all weapons as they can become unreasonably large if they have line renderers attached to them.
                }
            }
        }

#if DEBUG
        static HashSet<string> badBoundsReported = new HashSet<string>(); // Only report vessels with bad bounds once.
#endif
        /// <summary>
        /// Get a vessel's bounds.
        /// </summary>
        /// <param name="vessel">The vessel to get the bounds of.</param>
        /// <param name="useBounds">Use the renderer bounds and calculate min/max manually instead of using KSP's internal functions.</param>
        /// <returns></returns>
        public static Vector3 GetBounds(this Vessel vessel, bool useBounds = true)
        {
            if (vessel is null || vessel.packed || !vessel.loaded) return Vector3.zero;
            if (badBoundsParts == null) GetBadBoundsParts();
            var vesselRot = vessel.transform.rotation;
            vessel.SetRotation(Quaternion.identity);

            Vector3 size = Vector3.zero;
            Vector3 min = default, max = default;
            if (!useBounds)
            {
                size = ShipConstruction.CalculateCraftSize(vessel.Parts, vessel.rootPart); //x: Width, y: Length, z: Height
            }
            else
            {
                var partBound = GetRendererPartBounds(vessel.rootPart);
                if (partBound.min.sqrMagnitude > 1e6 || partBound.max.sqrMagnitude > 1e6) // Fall back to the first collider bounds if renderer bounds are nonsensical. This is usually temporary.
                    partBound = vessel.rootPart.GetColliderBounds().FirstOrDefault();
                min = partBound.min; max = partBound.max;
                using (var part = vessel.Parts.GetEnumerator())
                    while (part.MoveNext())
                    {
                        if (badBoundsParts.Contains(part.Current.name)) continue; // Skip parts that are known to give bad bounds (e.g., lasers when firing).
                        partBound = GetRendererPartBounds(part.Current);
                        if (partBound.min.sqrMagnitude > 1e6 || partBound.max.sqrMagnitude > 1e6)
                            partBound = part.Current.GetColliderBounds().FirstOrDefault(); // Fall back to the first collider bounds if renderer bounds are nonsensical. This is usually temporary.
                        min.x = Mathf.Min(min.x, partBound.min.x);
                        min.y = Mathf.Min(min.y, partBound.min.y);
                        min.z = Mathf.Min(min.z, partBound.min.z);
                        max.x = Mathf.Max(max.x, partBound.max.x);
                        max.y = Mathf.Max(max.y, partBound.max.y);
                        max.z = Mathf.Max(max.z, partBound.max.z);
                    }
                size = max - min; //x: Width, y: Length, z: Height
            }
#if DEBUG
            if (!badBoundsReported.Contains(vessel.vesselName))
            {
                var GetBoundString = (Part p) => { var bwop = GetRendererPartBounds(p); return $"{bwop.size}@{bwop.center}"; };
                if (size.x > 1000 || size.y > 1000 || size.z > 1000) Debug.LogWarning($"[BDArmory.VesselExtensions]: Bounds on {vessel.vesselName} are bad: {size} (max: {max}, min: {min}, useBounds: {useBounds}). Parts: {string.Join("; ", vessel.Parts.Select(p => $"{p.name}, collider bounds: {string.Join(", ", p.GetColliderBounds().Select(b => $"{b.size}@{b.center}"))}, bounds w/o particles: {GetBoundString(p)}"))}. Root: {vessel.rootPart.name}, bounds {string.Join(", ", vessel.rootPart.GetColliderBounds().Select(b => $"{b.size}@{b.center}"))}.");
                badBoundsReported.Add(vessel.vesselName);
            }
#endif
            vessel.SetRotation(vesselRot);
            return size;
        }

        /// <summary>
        /// Work-around for pre-1.11 versions of KSP not having GameObject.GetRendererBoundsWithoutParticles().
        /// </summary>
        /// <param name="part"></param>
        /// <returns></returns>
        static Bounds GetRendererPartBounds(Part part)
        {
            if ((Versioning.version_major == 1 && Versioning.version_minor > 10) || Versioning.version_major > 1) // Introduced in 1.11
                return GetRendererPartBounds_1_11(part);
            else
                return part.gameObject.GetRendererBounds();
        }

        static Bounds GetRendererPartBounds_1_11(Part part)
        {
            return part.gameObject.GetRendererBoundsWithoutParticles();
        }

        public static Bounds GetRendererBounds(this Vessel vessel, bool useCached = true, bool checkForLaunchClamps = false, bool ignoreLineRenderers = true, bool ignoreParticleRenderers = true)
        {
            Bounds result = default;
            bool flag = false;
            Quaternion vesselRot = vessel.transform.rotation;
            Quaternion vesselRefRot = vessel.ReferenceTransform.rotation;
            if (vesselRot == vesselRefRot)
            {
                vessel.SetRotation(Quaternion.identity);
            }
            else
            {
                vessel.SetRotation(Quaternion.Inverse(vesselRefRot) * vesselRot);
            }
            foreach (var part in vessel.Parts)
            {
                if (checkForLaunchClamps && part.Modules.GetModule<LaunchClamp>() != null) continue;
                foreach (var renderer in useCached ? part.FindModelRenderersCached().AsEnumerable() : part.GetComponentsInChildren<Renderer>().AsEnumerable())
                {
                    if (ignoreLineRenderers && renderer is LineRenderer) continue;
                    if (ignoreParticleRenderers && renderer is ParticleSystemRenderer) continue;

                    if (flag)
                    {
                        result.Encapsulate(renderer.bounds);
                    }
                    else
                    {
                        result = renderer.bounds;
                        flag = true;
                    }
                }
            }
            result.center -= vessel.ReferenceTransform.position;
            vessel.SetRotation(vesselRot);
            return result;
        }

        public static Bounds GetColliderBounds(this Vessel vessel, bool checkForLaunchClamps = false)
        {
            Bounds result = default;
            bool flag = false;
            Quaternion vesselRot = vessel.transform.rotation;
            Quaternion vesselRefRot = vessel.ReferenceTransform.rotation;
            if (vesselRot == vesselRefRot)
            {
                vessel.SetRotation(Quaternion.identity);
            }
            else
            {
                vessel.SetRotation(Quaternion.Inverse(vesselRefRot) * vesselRot);
            }
            foreach (var part in vessel.Parts)
            {
                if (checkForLaunchClamps && part.Modules.GetModule<LaunchClamp>() != null) continue;
                foreach (var collider in part.FindModelComponents<Collider>())
                {
                    if (flag)
                    {
                        result.Encapsulate(collider.bounds);
                    }
                    else
                    {
                        result = collider.bounds;
                        flag = true;
                    }
                }
            }
            result.center -= vessel.ReferenceTransform.position;
            vessel.SetRotation(vesselRot);
            return result;
        }

        /// <summary>
        /// Convert a bounds (local to a vessel's reference transform) into bounds relative to the viewer.
        /// E.g.,
        ///   Transform viewer = FlightCamera.fetch.transform;
        ///   Bounds localBounds = vessel.GetColliderBounds();
        ///   Bounds bounds = vessel.ViewBoundsFrom(localBounds, viewer);
        ///   Vector3 worldSpacePosition = viewer.TransformPoint(bounds.center);
        /// </summary>
        /// <param name="vessel"></param>
        /// <param name="bounds"></param>
        /// <param name="viewer"></param>
        /// <returns></returns>
        public static Bounds ViewBoundsFrom(this Vessel vessel, Bounds bounds, Transform viewer)
        {
            var t = vessel.ReferenceTransform;
            var r = Quaternion.Inverse(viewer.rotation) * t.rotation;
            Vector3[] corners = [
                r * bounds.extents,
                r * new Vector3(bounds.extents.x, bounds.extents.y, -bounds.extents.z),
                r * new Vector3(bounds.extents.x, -bounds.extents.y, bounds.extents.z),
                r * new Vector3(-bounds.extents.x, bounds.extents.y, bounds.extents.z),
            ];
            return new Bounds(
                viewer.InverseTransformDirection(t.TransformDirection(bounds.center) + t.position - viewer.position),
                new Vector3(
                    2f * corners.Max(c => Mathf.Abs(c.x)),
                    2f * corners.Max(c => Mathf.Abs(c.y)),
                    2f * corners.Max(c => Mathf.Abs(c.z))
                )
            );
        }

        /// <summary>
        /// Work-around for pre-1.11 versions of KSP not having Vessel.FindVesselModuleImplementing<T>().
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="vessel"></param>
        /// <returns></returns>
        public static T FindVesselModuleImplementingBDA<T>(this Vessel vessel) where T : class
        {
            if ((Versioning.version_major == 1 && Versioning.version_minor > 10) || Versioning.version_major > 1) // Introduced in 1.11
                return vessel.FindVesselModuleImplementing_1_11<T>();
            else
            {
                foreach (var module in vessel.vesselModules)
                    if (module is T)
                        return module as T;
                return null;
            }
        }
        static T FindVesselModuleImplementing_1_11<T>(this Vessel vessel) where T : class
        {
            return vessel.FindVesselModuleImplementing<T>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ActiveController ActiveController(this Vessel vessel) => Utils.ActiveController.GetActiveController(vessel);

        /// <summary>
        /// Strip the vessel type from the end of a vessel's name (as long as it's not an ignored type).
        /// KSP automatically adds this whenever a new vessel is made.
        /// </summary>
        /// <param name="vessel"></param>
        public static void StripTypeFromName(this Vessel vessel)
        {
            if (vessel == null || string.IsNullOrEmpty(vessel.vesselName)) return;
            if (!VesselModuleRegistry.IgnoredVesselTypes.Contains(vessel.vesselType) && vessel.vesselName.EndsWith($" {vessel.vesselType}"))
            {
                vessel.vesselName = vessel.vesselName.Remove(vessel.vesselName.Length - $" {vessel.vesselType}".Length);
            }
        }
    }
}
