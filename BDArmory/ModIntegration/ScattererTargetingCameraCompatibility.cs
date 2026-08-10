using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

using BDArmory.Targeting;

namespace BDArmory.ModIntegration
{
    /// <summary>
    /// Soft compatibility layer between BDArmory's manually-rendered TGP camera stack
    /// and Scatterer's camera-driven ocean/scattering hooks.
    ///
    /// Scatterer dynamically attaches OceanCommandBuffer and ScatteringCommandBuffer
    /// components to cameras that see its projected-grid/scattering renderers. BDA's TGP
    /// is an off-screen four-camera stack that renders manually into one RenderTexture.
    /// Allowing Scatterer's ocean path to treat those cameras as normal flight cameras can
    /// leave ocean depth/screen-copy state tied to the wrong camera and produce incorrect
    /// ocean occlusion or view-dependent artifacts in the TGP feed.
    ///
    /// For BDA TGP cameras only, this class temporarily suppresses Scatterer's ocean mesh
    /// during the render boundary and makes screen-space atmosphere use its no-ocean path.
    /// Atmospheric scattering remains available. All global Scatterer state is restored in
    /// Camera.onPostRender. Scatterer remains an optional dependency: there is no compile-
    /// time reference to scatterer.dll.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    internal sealed class ScattererTargetingCameraCompatibility : MonoBehaviour
    {
        private sealed class BooleanFieldState
        {
            public Component Component;
            public FieldInfo Field;
            public bool Value;
        }

        // Scatterer's assembly is lower-case, while its C# namespace is capitalized.
        private const string ScattererAssemblyName = "scatterer";
        private const string OceanNodeTypeName = "Scatterer.OceanNode";
        private const string ScreenSpaceScatteringTypeName = "Scatterer.ScreenSpaceScattering";
        private const int DiscoveryRefreshFrames = 120;

        private readonly List<Renderer> oceanRenderers = new List<Renderer>();
        private readonly List<Renderer> disabledOceanRenderers = new List<Renderer>();
        private readonly List<Component> screenSpaceScatteringComponents = new List<Component>();
        private readonly List<BooleanFieldState> modifiedScatteringFields = new List<BooleanFieldState>();

        private Type oceanNodeType;
        private Type screenSpaceScatteringType;
        private FieldInfo waterMeshRenderersField;
        private FieldInfo hasOceanField;
        private Camera isolatedCamera;
        private bool callbacksRegistered;
        private bool compatibilityErrorReported;
        private bool restoreErrorReported;
        private int lastDiscoveryFrame = -1;

        private void Awake()
        {
            try
            {
                Assembly scattererAssembly = FindScattererAssembly();
                if (scattererAssembly == null) return;

                ResolveScattererTypes(scattererAssembly);
                Camera.onPreCull += HandleCameraPreCull;
                Camera.onPostRender += HandleCameraPostRender;
                callbacksRegistered = true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[BDArmory] Scatterer TGP compatibility failed to initialise safely: " + exception);
            }
        }

        private void OnDestroy()
        {
            if (callbacksRegistered)
            {
                Camera.onPreCull -= HandleCameraPreCull;
                Camera.onPostRender -= HandleCameraPostRender;
                callbacksRegistered = false;
            }
            RestoreScattererState();
        }

        private void HandleCameraPreCull(Camera camera)
        {
            if (camera == null) return;

            try
            {
                if (!IsBdaTargetingCamera(camera))
                {
                    // Defensive recovery if a previous manual camera render aborted before
                    // its onPostRender callback.
                    if (isolatedCamera != null) RestoreScattererState();
                    return;
                }

                if (isolatedCamera != null) RestoreScattererState();

                // Remove any ocean command buffer left on this TGP camera by an earlier
                // frame/session. New ocean hooks are prevented below by disabling the
                // projected-grid renderers for this camera's render boundary.
                RemoveOceanCommandBuffers(camera);

                DiscoverScattererObjects();
                TemporarilyUseNoOceanAtmosphere();
                DisableOceanRenderers();
                isolatedCamera = camera;
            }
            catch (Exception exception)
            {
                RestoreScattererState();
                if (!compatibilityErrorReported)
                {
                    compatibilityErrorReported = true;
                    Debug.LogWarning("[BDArmory] Scatterer TGP isolation failed safely; state restored: " + exception);
                }
            }
        }

        private void HandleCameraPostRender(Camera camera)
        {
            if (camera == null || !ReferenceEquals(camera, isolatedCamera)) return;
            RestoreScattererState();
        }

        private static bool IsBdaTargetingCamera(Camera camera)
        {
            TargetingCamera targetingCamera = TargetingCamera.Instance;
            if (targetingCamera == null) return false;

            if (TargetingCamera.IsTGPCamera(camera)) return true;

            // Safe fallback if this file is cherry-picked without the IsTGPCamera fix.
            return targetingCamera.targetCamRenderTexture != null
                && ReferenceEquals(camera.targetTexture, targetingCamera.targetCamRenderTexture);
        }

        private void ResolveScattererTypes(Assembly scattererAssembly)
        {
            oceanNodeType = scattererAssembly.GetType(OceanNodeTypeName, false);
            screenSpaceScatteringType = scattererAssembly.GetType(ScreenSpaceScatteringTypeName, false);

            if (oceanNodeType != null)
            {
                waterMeshRenderersField = oceanNodeType.GetField(
                    "waterMeshRenderers",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            if (screenSpaceScatteringType != null)
            {
                hasOceanField = screenSpaceScatteringType.GetField(
                    "hasOcean",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
        }

        private void DiscoverScattererObjects()
        {
            int currentFrame = Time.frameCount;
            if (lastDiscoveryFrame >= 0
                && currentFrame - lastDiscoveryFrame < DiscoveryRefreshFrames
                && ScattererObjectCacheIsUsable()) return;

            lastDiscoveryFrame = currentFrame;
            oceanRenderers.Clear();
            screenSpaceScatteringComponents.Clear();

            if (oceanNodeType != null && waterMeshRenderersField != null)
            {
                UnityEngine.Object[] oceanNodes = UnityEngine.Object.FindObjectsOfType(oceanNodeType);
                for (int i = 0; i < oceanNodes.Length; ++i)
                {
                    Component oceanNode = oceanNodes[i] as Component;
                    if (oceanNode == null) continue;

                    Array rendererArray = waterMeshRenderersField.GetValue(oceanNode) as Array;
                    if (rendererArray == null) continue;

                    for (int j = 0; j < rendererArray.Length; ++j)
                    {
                        Renderer renderer = rendererArray.GetValue(j) as Renderer;
                        if (renderer != null && !oceanRenderers.Contains(renderer))
                            oceanRenderers.Add(renderer);
                    }
                }
            }

            if (screenSpaceScatteringType != null && hasOceanField != null)
            {
                UnityEngine.Object[] scatteringObjects = UnityEngine.Object.FindObjectsOfType(screenSpaceScatteringType);
                for (int i = 0; i < scatteringObjects.Length; ++i)
                {
                    Component component = scatteringObjects[i] as Component;
                    if (component != null) screenSpaceScatteringComponents.Add(component);
                }
            }
        }

        private bool ScattererObjectCacheIsUsable()
        {
            if (oceanRenderers.Count == 0 && screenSpaceScatteringComponents.Count == 0) return false;

            for (int i = 0; i < oceanRenderers.Count; ++i)
                if (oceanRenderers[i] == null) return false;

            for (int i = 0; i < screenSpaceScatteringComponents.Count; ++i)
                if (screenSpaceScatteringComponents[i] == null) return false;

            return true;
        }

        private void TemporarilyUseNoOceanAtmosphere()
        {
            modifiedScatteringFields.Clear();
            if (hasOceanField == null) return;

            for (int i = 0; i < screenSpaceScatteringComponents.Count; ++i)
            {
                Component component = screenSpaceScatteringComponents[i];
                if (component == null) continue;

                object value = hasOceanField.GetValue(component);
                if (!(value is bool) || !(bool)value) continue;

                modifiedScatteringFields.Add(new BooleanFieldState
                {
                    Component = component,
                    Field = hasOceanField,
                    Value = true
                });
                hasOceanField.SetValue(component, false);
            }
        }

        private void DisableOceanRenderers()
        {
            disabledOceanRenderers.Clear();
            for (int i = 0; i < oceanRenderers.Count; ++i)
            {
                Renderer renderer = oceanRenderers[i];
                if (renderer == null || !renderer.enabled) continue;

                renderer.enabled = false;
                disabledOceanRenderers.Add(renderer);
            }
        }

        private void RestoreScattererState()
        {
            for (int i = 0; i < modifiedScatteringFields.Count; ++i)
            {
                BooleanFieldState state = modifiedScatteringFields[i];
                if (state.Component == null || state.Field == null) continue;
                try { state.Field.SetValue(state.Component, state.Value); }
                catch (Exception exception) { ReportRestoreError("atmosphere state", exception); }
            }
            modifiedScatteringFields.Clear();

            for (int i = 0; i < disabledOceanRenderers.Count; ++i)
            {
                Renderer renderer = disabledOceanRenderers[i];
                if (renderer == null) continue;
                try { renderer.enabled = true; }
                catch (Exception exception) { ReportRestoreError("ocean renderer state", exception); }
            }
            disabledOceanRenderers.Clear();
            isolatedCamera = null;
        }

        private void ReportRestoreError(string stateName, Exception exception)
        {
            if (restoreErrorReported) return;
            restoreErrorReported = true;
            Debug.LogWarning("[BDArmory] Scatterer " + stateName + " could not be fully restored: " + exception);
        }

        private static void RemoveOceanCommandBuffers(Camera camera)
        {
            Array events = Enum.GetValues(typeof(CameraEvent));
            for (int i = 0; i < events.Length; ++i)
            {
                CameraEvent cameraEvent = (CameraEvent)events.GetValue(i);
                CommandBuffer[] buffers = camera.GetCommandBuffers(cameraEvent);
                for (int j = 0; j < buffers.Length; ++j)
                {
                    CommandBuffer buffer = buffers[j];
                    if (buffer != null
                        && !string.IsNullOrEmpty(buffer.name)
                        && buffer.name.IndexOf("Ocean MeshRenderer CommandBuffer", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        camera.RemoveCommandBuffer(cameraEvent, buffer);
                    }
                }
            }
        }

        private static Assembly FindScattererAssembly()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; ++i)
            {
                AssemblyName name = assemblies[i].GetName();
                if (name != null
                    && string.Equals(name.Name, ScattererAssemblyName, StringComparison.OrdinalIgnoreCase))
                    return assemblies[i];
            }
            return null;
        }
    }
}
