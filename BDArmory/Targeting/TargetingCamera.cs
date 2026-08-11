using UnityEngine;

using BDArmory.Settings;
using BDArmory.Utils;
using BDArmory.ModIntegration;

namespace BDArmory.Targeting
{
    public class TargetingCamera : MonoBehaviour
    {
        public static TargetingCamera Instance;
        public static bool ReadyForUse;
        public RenderTexture targetCamRenderTexture;
        TGPCameraEffects camEffects;
        Light nvLight;
        public bool nvMode = false;
        public bool color = false;

        private Texture2D reticleTex;

        public Texture2D ReticleTexture
        {
            get
            {
                if (reticleTex != null)
                {
                    return reticleTex;
                }
                else
                {
                    reticleTex = GameDatabase.Instance.GetTexture("BDArmory/Textures/camReticle", false);
                    return reticleTex;
                }
            }
        }

        Camera[] cameras;
        public static Transform cameraTransform;
        public bool[] CamEnabled = [true, true, true, true];

        bool cameraEnabled;

        float currentFOV = 60;

        void Awake()
        {
            if (Instance)
            {
                Destroy(gameObject);
                return;
            }
            else
            {
                Instance = this;
            }
        }

        void Start()
        {
            GameEvents.onVesselChange.Add(VesselChange);
        }

        public void UpdateCamRotation(Transform tf)
        {
            if (cameras != null && cameras[0] != null)
            {
                tf.rotation = cameras[0].transform.rotation;
            }
        }

        public void SetFOV(float fov)
        {
            if (fov == currentFOV)
            {
                return;
            }

            if (cameras == null || cameras[0] == null)
            {
                if (cameraEnabled)
                {
                    DisableCamera();
                }
                return;
            }

            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] == null) continue;
                cameras[i].fieldOfView = fov;
            }
            currentFOV = fov;
        }

        void VesselChange(Vessel v)
        {
            if (!v.isActiveVessel)
            {
                return;
            }

            bool moduleFound = false;
            using (var mtc = VesselModuleRegistry.GetModules<ModuleTargetingCamera>(v).GetEnumerator())
                while (mtc.MoveNext())
                {
                    if (mtc.Current == null) continue;
                    if (BDArmorySettings.DEBUG_RADAR) Debug.Log("[BDArmory.TargetingCamera]: Vessel switched to vessel with targeting camera.  Refreshing camera state.");

                    if (mtc.Current.cameraEnabled)
                    {
                        mtc.Current.DelayedEnable();
                    }
                    else
                    {
                        mtc.Current.DisableCamera();
                    }
                    moduleFound = true;
                }

            if (!moduleFound)
            {
                DisableCamera();
                ModuleTargetingCamera.windowIsOpen = false;
            }
        }

        public void EnableCamera(Transform parentTransform)
        {
            if (cameraTransform)
            {
                cameraTransform.gameObject.SetActive(true);
            }

            SetupCamera(parentTransform);

            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] == null) continue;
                cameras[i].enabled = false;
            }

            cameraEnabled = true;

            ReadyForUse = true;
        }

        void RenderCameras()
        {
            if (cameras[3] != null && CamEnabled[3]) cameras[3].Render(); // Galaxy cam
            if (cameras[2] != null && CamEnabled[2]) cameras[2].Render(); // Sky cam

            Color origAmbientColor = RenderSettings.ambientLight;
            if (nvMode)
            {
                RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.5f, 1);
                nvLight.enabled = true;
            }
            if (cameras[1] != null && CamEnabled[1]) cameras[1].Render(); // Far cam
            if (cameras[0] != null && CamEnabled[0]) cameras[0].Render(); // Near cam

            nvLight.enabled = false;
            if (nvMode)
            {
                RenderSettings.ambientLight = origAmbientColor;
            }
        }

        void LateUpdate()
        {
            if (cameraEnabled && HighLogic.LoadedSceneIsFlight)
            {
                if (cameras == null || cameras[0] == null)
                {
                    DisableCamera();
                    return;
                }
                RenderCameras();
            }
        }

        public void DisableCamera()
        {
            if (cameraTransform)
            {
                cameraTransform.parent = null;
                cameraTransform.gameObject.SetActive(false);
            }

            if (cameras != null && cameras[0] != null)
            {
                for (int i = 0; i < cameras.Length; i++)
                {
                    if (cameras[i] == null) continue;
                    cameras[i].enabled = false;
                }
            }

            cameraEnabled = false;
        }

        void SetupCamera(Transform parentTransform)
        {
            if (!parentTransform)
            {
                Debug.Log("[BDArmory.TargetingCamera]: Targeting camera tried setup but parent transform is null");
                return;
            }

            if (cameraTransform == null)
            {
                cameraTransform = (new GameObject("targetCamObject")).transform;
            }

            //Debug.Log("[BDArmory.TargetingCamera]: Setting target camera parent");
            cameraTransform.parent = parentTransform;
            cameraTransform.localPosition = Vector3.zero;
            cameraTransform.localRotation = Quaternion.identity;

            if (targetCamRenderTexture == null)
            {
                int res = Mathf.RoundToInt(BDArmorySettings.TARGET_CAM_RESOLUTION);
                targetCamRenderTexture = new RenderTexture(res, res, (int)RenderTextureFormat.RGBAUShort)
                {
                    antiAliasing = 1
                };
                targetCamRenderTexture.Create();
            }

            if (cameras != null && cameras[0] != null)
            {
                return;
            }

            //cam setup
            cameras = new Camera[4];

            Camera fCamNear = FlightCamera.fetch.cameras[0];
            Camera fCamFar = FlightCamera.fetch.cameras[1];

            //flight cameras
            //nearCam
            GameObject cam1Obj = new GameObject();
            Camera nearCam = cam1Obj.AddComponent<Camera>();
            nearCam.CopyFrom(fCamNear);
            nearCam.name = "TGP Near Camera";
            nearCam.transform.parent = cameraTransform;
            nearCam.transform.localRotation = Quaternion.identity;
            nearCam.transform.localPosition = Vector3.zero;
            nearCam.transform.localScale = Vector3.one;
            nearCam.targetTexture = targetCamRenderTexture;
            cameras[0] = nearCam;

            TGPCameraEffects ge1 = cam1Obj.AddComponent<TGPCameraEffects>();
            ge1.textureRamp = GameDatabase.Instance.GetTexture("BDArmory/Textures/grayscaleRamp", false);
            ge1.rampOffset = 0;
            camEffects = ge1;

            //farCam
            GameObject cam2Obj = new GameObject();
            Camera farCam = cam2Obj.AddComponent<Camera>();
            farCam.CopyFrom(fCamFar);
            farCam.name = "TGP Far Camera";
            farCam.transform.parent = cameraTransform;
            farCam.transform.localRotation = Quaternion.identity;
            farCam.transform.localPosition = Vector3.zero;
            farCam.transform.localScale = Vector3.one;
            farCam.targetTexture = targetCamRenderTexture;
            cameras[1] = farCam;

            //skybox camera
            GameObject skyCamObj = new GameObject();
            Camera skyCam = skyCamObj.AddComponent<Camera>();
            Camera mainSkyCam = FindCamera("Camera ScaledSpace");
            skyCam.CopyFrom(mainSkyCam);
            skyCam.name = "TGP Sky Camera";
            skyCam.transform.parent = mainSkyCam.transform;
            skyCam.transform.localRotation = Quaternion.identity;
            skyCam.transform.localPosition = Vector3.zero;
            skyCam.transform.localScale = Vector3.one;
            skyCam.targetTexture = targetCamRenderTexture;
            cameras[cameras.Length - 2] = skyCam;
            skyCamObj.AddComponent<TGPCamRotator>();

            //galaxy camera
            GameObject galaxyCamObj = new GameObject();
            Camera galaxyCam = galaxyCamObj.AddComponent<Camera>();
            Camera mainGalaxyCam = FindCamera("GalaxyCamera");
            galaxyCam.CopyFrom(mainGalaxyCam);
            galaxyCam.name = "TGP Galaxy Camera";
            galaxyCam.transform.parent = mainGalaxyCam.transform;
            galaxyCam.transform.position = Vector3.zero;
            galaxyCam.transform.localRotation = Quaternion.identity;
            galaxyCam.transform.localScale = Vector3.one;
            galaxyCam.targetTexture = targetCamRenderTexture;
            cameras[cameras.Length - 1] = galaxyCam;
            galaxyCamObj.AddComponent<TGPCamRotator>();

            nvLight = new GameObject().AddComponent<Light>();
            nvLight.transform.parent = cameraTransform;
            nvLight.transform.localPosition = Vector3.zero;
            nvLight.transform.localRotation = Quaternion.identity;
            nvLight.type = LightType.Directional;
            nvLight.intensity = 2;
            nvLight.shadows = LightShadows.None;

            nvLight.cullingMask = 1 << 0;
            nvLight.enabled = false;

            if (!SystemInformation.isDirectX && Scatterer.IsInstalled)
            {
                Debug.LogWarning($"[BDArmory.TargetingCamera]: OpenGL and Scatterer detected, applying hack to work around https://github.com/LGhassen/Scatterer/issues/229");
                farCam.nearClipPlane = nearCam.nearClipPlane;
                CamEnabled[0] = false;
                TGPCameraEffects ge12 = farCam.gameObject.AddComponent<TGPCameraEffects>();
                ge12.textureRamp = GameDatabase.Instance.GetTexture("BDArmory/Textures/grayscaleRamp", false);
                ge12.rampOffset = 0;
                camEffects = ge12;
            }
        }

        private Camera FindCamera(string cameraName)
        {
            foreach (Camera cam in Camera.allCameras)
            {
                if (cam.name == cameraName)
                {
                    return cam;
                }
            }
            Debug.Log("[BDArmory.TargetingCamera]: Couldn't find " + cameraName);
            return null;
        }

        void OnDestroy()
        {
            ReadyForUse = false;
            GameEvents.onVesselChange.Remove(VesselChange);
            if (cameras != null)
            {
                foreach (var camera in cameras)
                {
                    if (camera != null && camera.gameObject != null)
                    { Destroy(camera.gameObject); }
                }
            }
            if (targetCamRenderTexture != null) targetCamRenderTexture.Release();
        }

        public static bool IsTGPCamera(Camera c)
        {
            return c.transform == cameraTransform;
        }
    }
}
