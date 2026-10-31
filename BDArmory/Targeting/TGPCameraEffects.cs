using UnityEngine;

using BDArmory.Settings;
using BDArmory.Shaders;

namespace BDArmory.Targeting
{
    public class TGPCameraEffects : MonoBehaviour
    {
        public static Material grayscaleMaterial;

        public Texture textureRamp;
        public float rampOffset;

        void Awake()
        {
            if (!grayscaleMaterial)
            {
                grayscaleMaterial = new Material(BDAShaderLoader.GrayscaleEffectShader);
                grayscaleMaterial.SetTexture("_RampTex", textureRamp);
                grayscaleMaterial.SetFloat("_RedPower", 4);
                grayscaleMaterial.SetFloat("_RedDelta", rampOffset);
            }
        }

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (!TargetingCamera.Instance.color || TargetingCamera.Instance.nvMode)
            {
                Graphics.Blit(source, destination, grayscaleMaterial); //apply grayscale
            }
            else
            {
                Graphics.Blit(source, destination);
            }
        }
    }
}
