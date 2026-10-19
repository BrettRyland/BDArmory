namespace BDArmory.Utils
{
    internal class LayerMask
    {
        public static int CreateLayerMask(bool aExclude, params int[] aLayers)
        {
            int v = 0;
            foreach (var L in aLayers)
                v |= 1 << L;
            if (aExclude)
                v = ~v;
            return v;
        }

        public static int ToLayer(int bitmask)
        {
            int result = bitmask > 0 ? 0 : 31;
            while (bitmask > 1)
            {
                bitmask = bitmask >> 1;
                result++;
            }
            return result;
        }
    }

    // LayerMasks for raycasts. Use as (int)(Parts|EVA|Scenery).
    public enum LayerMasks
    {
        Parts = 1 << 0,
        Water = 1 << 4,
        Scenery = 1 << 15,
        Kerbals = 1 << 16, // Internal kerbals
        EVA = 1 << 17,
        PhysicalObjects = 1 << 19, // Why are some raycasts using this layer? PhysicalObjects?
        RootPart = 1 << 21,
        AeroFXIgnore = 1 << 23, // Why are some raycasts using this layer? AeroFXIgnore?
        Wheels = 1 << 26, // WheelCollidersIgnore?
        // 1 << 27 WheelColliders?
    }; // Scenery includes terrain and buildings.
    // Commonly used values:
    // 163840 = (1 << 15) | (1 << 17)
    // 557057 = (1 << 0) | (1 << 15) | (1 << 19) = Parts|Scenery|???
    // 9076737 = (1 << 0) | (1 << 15) | (1 << 17) | (1 << 19) | (1 << 23) = Parts|Scenery|EVA|???|???
}

/*
Layer mask names (doesn't actually seem to be correct when testing raycasts):
   0: Default
   1: TransparentFX
   2: Ignore Raycast
   3: 
   4: Water
   5: UI
   6: 
   7: 
   8: PartsList_Icons
   9: Atmosphere
   10: Scaled Scenery
   11: UIDialog
   12: UIVectors
   13: UI_Mask
   14: Screens
   15: Local Scenery
   16: kerbals
   17: EVA
   18: SkySphere
   19: PhysicalObjects
   20: Internal Space
   21: Part Triggers
   22: KerbalInstructors
   23: AeroFXIgnore
   24: MapFX
   25: UIAdditional
   26: WheelCollidersIgnore
   27: WheelColliders
   28: TerrainColliders
   29: DragRender
   30: SurfaceFX
   31: Vectors

From:
    for (int i=0; i<32; ++i)
        Debug.Log("[DEBUG] " + i + ": " + LayerMask.LayerToName(i));
*/
