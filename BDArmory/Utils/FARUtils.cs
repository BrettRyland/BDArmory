using System;
using System.Reflection;
using UnityEngine;
using BDArmory.Settings;

namespace BDArmory.Utils
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class FerramAerospace : MonoBehaviour
    {
        public static FerramAerospace Instance;
        public static bool hasFAR = false;
        private static bool hasCheckedForFAR = false;
        public static bool hasFARWing = false;
        public static bool hasFARControllableSurface = false;
        private static bool hasCheckedForFARWing = false;
        private static bool hasCheckedForFARControllableSurface = false;

        public static Assembly FARAssembly;
        public static Type FARWingModule;
        public static Type FARControllableSurfaceModule;


        void Awake()
        {
            if (Instance != null) return; // Don't replace existing instance.
            Instance = new FerramAerospace();
        }

        void Start()
        {
            CheckForFAR();
            if (hasFAR)
            {
                CheckForFARWing();
                CheckForFARControllableSurface();
            }
        }

        public static bool CheckForFAR()
        {
            if (hasCheckedForFAR) return hasFAR;
            hasCheckedForFAR = true;
            foreach (var assy in AssemblyLoader.loadedAssemblies)
            {
                if (assy.assembly.FullName.StartsWith("FerramAerospaceResearch"))
                {
                    FARAssembly = assy.assembly;
                    hasFAR = true;
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FARUtils]: Found FAR Assembly: {FARAssembly.FullName}");
                }
            }
            return hasFAR;
        }

        public static bool CheckForFARWing()
        {
            if (!hasFAR) return false;
            if (hasCheckedForFARWing) return hasFARWing;
            hasCheckedForFARWing = true;
            foreach (var type in FARAssembly.GetTypes())
            {
                if (type.Name == "FARWingAerodynamicModel")
                {
                    FARWingModule = type;
                    hasFARWing = true;
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FARUtils]: Found FAR wing module type.");
                }
            }
            return hasFARWing;
        }

        public static bool CheckForFARControllableSurface()
        {
            if (!hasFAR) return false;
            if (hasCheckedForFARControllableSurface) return hasFARControllableSurface;
            hasCheckedForFARControllableSurface = true;
            foreach (var type in FARAssembly.GetTypes())
            {
                if (type.Name == "FARControllableSurface")
                {
                    FARControllableSurfaceModule = type;
                    hasFARControllableSurface = true;
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FARUtils]: Found FAR controllable surface module type.");
                }
            }
            return hasFARControllableSurface;
        }

        public static float GetFARMassMult(Part part)
        {
            if (!hasFARWing) return 1;

            foreach (var module in part.Modules)
            {
                if (module.GetType() == FARWingModule)
                {
                    var massMultiplier = (float)FARWingModule.GetField("massMultiplier", BindingFlags.Public | BindingFlags.Instance).GetValue(module);
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FARUtils]: Found wing Mass multiplier of {massMultiplier} for {part.name}.");
                    return massMultiplier;
                }
                if (module.GetType() == FARControllableSurfaceModule)
                {
                    var massMultiplier = (float)FARControllableSurfaceModule.GetField("massMultiplier", BindingFlags.Public | BindingFlags.Instance).GetValue(module);
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FARUtils]: Found ctrl. srf. Mass multiplier of {massMultiplier} for {part.name}.");
                    return massMultiplier;
                }
            }
            return 1;
        }
        public static float GetFARcurrWingMass(Part part)
        {
            if (!hasFARWing) return -1;
            foreach (var module in part.Modules)
            {
                if (module.GetType() == FARWingModule)
                {
                    var wingMass = (float)FARWingModule.GetField("curWingMass", BindingFlags.Public | BindingFlags.Instance).GetValue(module);
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FARUtils]: Found wing Mass of {wingMass} for {part.name}.");
                    return wingMass;
                }
                if (module.GetType() == FARControllableSurfaceModule)
                {
                    var wingMass = (float)FARControllableSurfaceModule.GetField("curWingMass", BindingFlags.Public | BindingFlags.Instance).GetValue(module);
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FARUtils]: Found ctrl. srf. Mass multiplier of {wingMass} for {part.name}.");
                    return wingMass;
                }
            }
            return -1;
        }
        public static float GetFARWingSweep(Part part)
        {
            if (!hasFARWing) return 0;

            foreach (var module in part.Modules)
            {
                if (module.GetType() == FARWingModule)
                {
                    var sweep = FARWingModule.GetField("MidChordSweep", BindingFlags.Public | BindingFlags.Instance).GetValue(module); //leading + trailing angle / 2
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FARUtils]: Found mid chord sweep of {sweep} for {part.name}.");
                    if (float.TryParse(sweep.ToString(), out float f))
                    {
                        return f;
                    }
                }
                if (module.GetType() == FARControllableSurfaceModule)
                {
                    var sweep = FARControllableSurfaceModule.GetField("MidChordSweep", BindingFlags.Public | BindingFlags.Instance).GetValue(module);
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FARUtils]: Found ctrl. srf. mid chord sweep of {sweep} for {part.name}.");
                    if (float.TryParse(sweep.ToString(), out float f))
                    {
                        return f;
                    }
                }
            }
            return 0;
        }
    }

    public class ProceduralWing : MonoBehaviour
    {
        public static ProceduralWing Instance;
        public static bool hasB9ProcWing = false;
        private static bool hasCheckedForB9PW = false;
        public static bool hasPwingModule = false;
        private static bool hasCheckedForPwingModule = false;

        public static Assembly PWAssembly;
        public static string PWAssyVersion = "unknown";
        public static Type PWType;


        void Awake()
        {
            if (Instance != null) return; // Don't replace existing instance.
            Instance = new ProceduralWing();
        }

        void Start()
        {
            CheckForB9ProcWing();
            if (hasB9ProcWing) CheckForPWModule();
        }

        public static bool CheckForB9ProcWing()
        {
            if (hasCheckedForB9PW) return hasB9ProcWing;
            hasCheckedForB9PW = true;
            foreach (var assy in AssemblyLoader.loadedAssemblies)
            {
                if (assy.assembly.FullName.Contains("B9") && assy.assembly.FullName.Contains("PWings")) // Not finding 'if (assy.assembly.FullName.StartsWith("B9-PWings-Fork"))'?
                {
                    PWAssembly = assy.assembly;
                    hasB9ProcWing = true;
                    PWAssyVersion = assy.assembly.GetName().Version.ToString();
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FARUtils]: Found Pwing Assembly: {PWAssembly.FullName}");
                }
            }

            return hasB9ProcWing;
        }

        public static bool CheckForPWModule()
        {
            if (!hasB9ProcWing) return false;
            if (hasCheckedForPwingModule) return hasPwingModule;
            hasCheckedForPwingModule = true;
            foreach (var type in PWAssembly.GetTypes())
            {
                //Debug.Log($"[BDArmory.FARUtils]: Found module " + type.Name);

                if (type.Name == "WingProcedural")
                {
                    PWType = type;
                    hasPwingModule = true;
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FARUtils]: Found Pwing module.");
                }
            }
            return hasPwingModule;
        }

        public static float GetPWingVolume(Part part)
        {
            if (!hasPwingModule)
            {
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FARUtils]: hasPwing check failed!");
                return -1;
            }

            foreach (var module in part.Modules)
            {
                if (module.GetType() == PWType || module.GetType().IsSubclassOf(PWType))
                {
                    if (module.GetType() == PWType)
                    {
                        bool WingctrlSrf = (bool)PWType.GetField("isWingAsCtrlSrf", BindingFlags.Public | BindingFlags.Instance).GetValue(module);
                        bool ctrlSrf = (bool)PWType.GetField("isCtrlSrf", BindingFlags.Public | BindingFlags.Instance).GetValue(module);
                        float length = (float)PWType.GetField("sharedBaseLength", BindingFlags.Public | BindingFlags.Instance).GetValue(module);
                        bool isAeroSrf = (bool)PWType.GetField("aeroIsLiftingSurface", BindingFlags.Public | BindingFlags.Instance).GetValue(module);
                        //bool isAeroSrf = (float)PWType.GetField("stockLiftCoefficient", BindingFlags.Public | BindingFlags.Instance).GetValue(module) > 0f;
                        float width = ((float)PWType.GetField("sharedBaseWidthRoot", BindingFlags.Public | BindingFlags.Instance).GetValue(module) + (float)PWType.GetField("sharedBaseWidthTip", BindingFlags.Public | BindingFlags.Instance).GetValue(module));
                        int edgeLeadingType = Mathf.RoundToInt((float)PWType.GetField("sharedEdgeTypeLeading", BindingFlags.Public | BindingFlags.Instance).GetValue(module));
                        int edgeTrailingType = Mathf.RoundToInt((float)PWType.GetField("sharedEdgeTypeTrailing", BindingFlags.Public | BindingFlags.Instance).GetValue(module));
                        float edgeWidth = ((edgeLeadingType >= 2 ? 
                            ((float)PWType.GetField("sharedEdgeWidthLeadingTip", BindingFlags.Public | BindingFlags.Instance).GetValue(module) +
                        (float)PWType.GetField("sharedEdgeWidthLeadingRoot", BindingFlags.Public | BindingFlags.Instance).GetValue(module)) : 0)     
                        + (edgeTrailingType >= 2 ? ((float)PWType.GetField("sharedEdgeWidthTrailingTip", BindingFlags.Public | BindingFlags.Instance).GetValue(module) +
                        (float)PWType.GetField("sharedEdgeWidthTrailingRoot", BindingFlags.Public | BindingFlags.Instance).GetValue(module)) : 0));
                        float thickness = 0.18f;
                        float adjustedThickness = 0.18f;
                        if (BDArmorySettings.PWING_THICKNESS_AFFECT_MASS_HP)
                        {
                            thickness = ((float)PWType.GetField("sharedBaseThicknessRoot", BindingFlags.Public | BindingFlags.Instance).GetValue(module) + (float)PWType.GetField("sharedBaseThicknessTip", BindingFlags.Public | BindingFlags.Instance).GetValue(module)) / 2;
                            if (thickness >= 0.18f)
                            //adjustedThickness = (Mathf.Max(0.18f, (Mathf.Log(1.05f + thickness) * 0.78f))); //stock parts use exponential mass scalar, not linear - 25kg/s0, 125kg/s1, 500kg/s2, 4t/s3 for 1m long tanks
                            {
                                adjustedThickness = Mathf.Pow(thickness / 2.5f, 2.75f); //this follows stock mass scaling, up to about 3.75m parts
                                if (adjustedThickness >= 0.15f) adjustedThickness += 0.45f;
                                else adjustedThickness += (adjustedThickness * 1.66f) + 0.188f; 
                            }
                            else
                                adjustedThickness = Mathf.Max(thickness, 0.05f);                //2.5x2.5x0.938 tank: .5t; pWing: .36t - needs adjustedThickness of 1.35 instead of 0.96
                        }                                                                       //3.75x3.75x1.875 tank: 4t; pwing: 1.37t - needs adjustment of 3.6, not 1.19
                        //float thickness = 0.36f;

                        float liftCoeff = (length * ((width + edgeWidth) / 2)) / 3.52f;
                        float aeroVolume = (0.786f * length * ((width + edgeWidth) / 2) * adjustedThickness); //original .7 was based on errorneous 2x4 wingboard dimensions; stock reference wing area is 1.875x3.75m
                        if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FARUtils]: Found volume of {aeroVolume} for {part.name}.");

                        //if (PWAssyVersion != "0.44.0.0") //PWings now have edge colliders, unnecessary
                        if ((!BDArmorySettings.PWING_EDGE_LIFT) && !ctrlSrf) //if part !controlsurface, remove lift/mass from edges to bring inline with stock boards
                        {
							aeroVolume = (0.786f * length * (width / 2) * adjustedThickness); //original .7 was based on errorneous 2x4 wingboard dimensions; stock reference wing area is 1.875x3.75m
							liftCoeff = (length * (width / 2f)) / 3.52f;
                        }
                            if (!FerramAerospace.CheckForFAR()) part.Modules.GetModule<ModuleLiftingSurface>().deflectionLiftCoeff = (float)Math.Round(liftCoeff, 2);
                        if (!ctrlSrf && !WingctrlSrf)
                            PWType.GetField("aeroUIMass", BindingFlags.Public | BindingFlags.Instance).SetValue(module, ((float)liftCoeff / 10f) * (adjustedThickness * 5.6f)); //Adjust PWing GUI mass readout
                        else
                            PWType.GetField("aeroUIMass", BindingFlags.Public | BindingFlags.Instance).SetValue(module, ((float)liftCoeff / 5f) * (adjustedThickness * 5.6f)); //this modifies the IPartMassModifier, so the mass will also change along with the GUI
                        if (BDArmorySettings.RUNWAY_PROJECT)
                        {
                            liftCoeff = Mathf.Clamp((float)liftCoeff, 0, BDArmorySettings.MAX_PWING_LIFT); //if Runway Project, check lift is within limit and clamp if not
                            if (!WingctrlSrf) PWType.GetField("sharedBaseOffsetRoot", BindingFlags.Public | BindingFlags.Instance).SetValue(module, 0); //Adjust PWing GUI mass readout
                        }
                        if (BDArmorySettings.PWING_THICKNESS_AFFECT_MASS_HP)
                        {
                            liftCoeff *= (1 - ((adjustedThickness - 0.188f) / ((width + (BDArmorySettings.PWING_EDGE_LIFT ? edgeWidth : 0)) / 2))); //adjust lift coeff based on Thickness to Chord Ratio. Loggins lift nerf for spherical/near-spherical wing crossections
							if (liftCoeff < 0) liftCoeff = 0;
                            if (HighLogic.LoadedSceneIsFlight) if (Mathf.Abs(Vector3.Dot(part.vessel.ReferenceTransform.up, part.transform.right)) > 0.85) liftCoeff /= 2; //something something a wing rotated 90deg would only be generating body lift due to airfoil now perpendicular to airflow. Loggins pWing body nerf
                            if (HighLogic.LoadedSceneIsEditor) if (Mathf.Abs(Vector3.Dot(EditorLogic.fetch.vesselRotation * Vector3d.up, part.transform.right)) > 0.85) liftCoeff /= 2;
                        }
                        PWType.GetField("stockLiftCoefficient", BindingFlags.Public | BindingFlags.Instance).SetValue(module, isAeroSrf ? (float)liftCoeff : 0f); //adjust PWing GUI lift readout
                        if (part.name.Contains("B9.Aero.Wing.Procedural.Panel") || !isAeroSrf) //if Josue's noLift PWings PR never gets folded in, here's an alternative using an MM'ed PWing structural panel part
                        {
                            PWType.GetField("stockLiftCoefficient", BindingFlags.Public | BindingFlags.Instance).SetValue(module, 0f); //adjust PWing GUI lift readout
                            PWType.GetField("aeroUIMass", BindingFlags.Public | BindingFlags.Instance).SetValue(module, (((length * ((width + edgeWidth) / 2)) / 3.52f) / 12.5f) * (Mathf.Max(0.3f, adjustedThickness * 5.6f))); //Struct panels lighter than wings, clamp mass for panels thinner than 0.05m
                            if (!FerramAerospace.CheckForFAR()) part.FindModuleImplementing<ModuleLiftingSurface>().deflectionLiftCoeff = 0;
                            else
                            {
                                PWType.GetField("sharedArmorRatio", BindingFlags.Public | BindingFlags.Instance).SetValue(module, 100);
                            }
                            //PWType.GetField("aeroUIMass", BindingFlags.Public | BindingFlags.Instance).SetValue(module, (((length * (width / 2f)) / 3.52f) / 12.5f) * (thickness / 0.18f)); //version that has mass based on panel thickness
                        }
                        else
                        {
                            if (BDArmorySettings.PWING_THICKNESS_AFFECT_MASS_HP && FerramAerospace.CheckForFAR()) //PWings disables massMod if FAR, so need to re-add the additional mass from thickness
                            {
                                float massToAdd = 0;
                                massToAdd = ((float)liftCoeff / ((!ctrlSrf && !WingctrlSrf) ? 10 : 5)) * (adjustedThickness * 2.8f) - 
                                    ((float)liftCoeff / ((!ctrlSrf && !WingctrlSrf) ? 10 : 5)) * (0.36f * 3);
                                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FARUtils]: massToAdd {massToAdd} for {part.name}.");

                                massToAdd += part.partInfo.partPrefab.mass; //this gets subtracted out in the WingProcedural GetModuleMass, so need to add it here to get proper mass addition
                                if (massToAdd > 0)
                                {
                                    PWType.GetField("aeroUIMass", BindingFlags.Public | BindingFlags.Instance).SetValue(module, massToAdd); 
                                    PWType.GetField("sharedArmorRatio", BindingFlags.Public | BindingFlags.Instance).SetValue(module, 100);
                                }
                            }
                        }
                        return aeroVolume;
                    }
                }
            }
            if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FARUtils]: Pwing module not found!");
            return -1;
        }

        public static float ResetPWing(Part part)
        {
            if (!hasPwingModule)
            {
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FARUtils]: hasPwing check failed!");
                return 0;
            }
            if (FerramAerospace.CheckForFAR())
            {
                return 0;
            }
            foreach (var module in part.Modules)
            {
                if (module.GetType() == PWType || module.GetType().IsSubclassOf(PWType))
                {
                    if (module.GetType() == PWType)
                    {
                        bool ctrlSrf = (bool)PWType.GetField("isCtrlSrf", BindingFlags.Public | BindingFlags.Instance).GetValue(module);
                        bool WingctrlSrf = (bool)PWType.GetField("isWingAsCtrlSrf", BindingFlags.Public | BindingFlags.Instance).GetValue(module);

                        if (ctrlSrf) return 0; //control surfaces don't have any lift modification to begin with
                        double originalLift = (double)PWType.GetField("aeroStatSurfaceArea", BindingFlags.Public | BindingFlags.Instance).GetValue(module);
                        originalLift /= 3.52f;

                        bool isLiftingSurface = (float)PWType.GetField("stockLiftCoefficient", BindingFlags.Public | BindingFlags.Instance).GetValue(module) > 0f;

                        PWType.GetField("stockLiftCoefficient", BindingFlags.Public | BindingFlags.Instance).SetValue(module, isLiftingSurface ? (float)originalLift : 0f); //restore lift value/ correct GUI readout
                        part.Modules.GetModule<ModuleLiftingSurface>().deflectionLiftCoeff = (float)Math.Round((float)originalLift, 2);

                        if (!WingctrlSrf)
                            PWType.GetField("aeroUIMass", BindingFlags.Public | BindingFlags.Instance).SetValue(module, (float)originalLift / 10f);
                        else
                            PWType.GetField("aeroUIMass", BindingFlags.Public | BindingFlags.Instance).SetValue(module, (float)originalLift / 5f);
                    }
                }
            }

            if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FARUtils]: Pwing module not found!");
            return 0;
        }

        public static float GetPWingArea(Part part)
        {
            if (!hasPwingModule) return -1;

            foreach (var module in part.Modules)
            {
                if (module.GetType() == PWType)
                {
                    var length = (float)PWType.GetField("sharedBaseLength", BindingFlags.Public | BindingFlags.Instance).GetValue(module);
                    var width = ((float)PWType.GetField("sharedBaseWidthRoot", BindingFlags.Public | BindingFlags.Instance).GetValue(module) + (float)PWType.GetField("sharedBaseWidthTip", BindingFlags.Public | BindingFlags.Instance).GetValue(module)) / 2;
                    var thickness = ((float)PWType.GetField("sharedBaseThicknessRoot", BindingFlags.Public | BindingFlags.Instance).GetValue(module) + (float)PWType.GetField("sharedBaseThicknessTip", BindingFlags.Public | BindingFlags.Instance).GetValue(module)) / 2;

                    int edgeLeadingType = Mathf.RoundToInt((float)PWType.GetField("sharedEdgeTypeLeading", BindingFlags.Public | BindingFlags.Instance).GetValue(module));
                    int edgeTrailingType = Mathf.RoundToInt((float)PWType.GetField("sharedEdgeTypeTrailing", BindingFlags.Public | BindingFlags.Instance).GetValue(module));

                    if (BDArmorySettings.PWING_EDGE_LIFT) width += ((edgeLeadingType >= 2 ? (((float)PWType.GetField("sharedEdgeWidthLeadingTip", BindingFlags.Public | BindingFlags.Instance).GetValue(module) +
                    (float)PWType.GetField("sharedEdgeWidthLeadingRoot", BindingFlags.Public | BindingFlags.Instance).GetValue(module)) / 2) : 0)
                    + (edgeTrailingType >= 2 ? (((float)PWType.GetField("sharedEdgeWidthTrailingTip", BindingFlags.Public | BindingFlags.Instance).GetValue(module) +
                    (float)PWType.GetField("sharedEdgeWidthTrailingRoot", BindingFlags.Public | BindingFlags.Instance).GetValue(module)) / 2) : 0));

                    float area = (2 * (length * width)) + (2 * (width * thickness)) + (2 * (length * thickness));
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FARUtils]: Found wing area of {area}: {length} * {width} * {thickness} * 2 for {part.name}.");
                    if (thickness <= 0.25f) area /= 2; //for ~stock thickness wings, halve area to prevent to prevent double armor. Thicker wings/Wings ued as structural elements that can conceivably have other stuff inside them, treat as standard part for armor volume
                    return area;
                }
            }
            return -1;
        }
        public static float getPwingThickness(Part part)
        {
            if (!hasPwingModule) return 20;
            foreach (var module in part.Modules)
            {
                if (module.GetType() == PWType)
                {
                    float thickness = ((float)PWType.GetField("sharedBaseThicknessRoot", BindingFlags.Public | BindingFlags.Instance).GetValue(module) + (float)PWType.GetField("sharedBaseThicknessTip", BindingFlags.Public | BindingFlags.Instance).GetValue(module)) / 2;
                    return Mathf.Max(thickness, 0.2f) * 100;
                }
            }
            return 20;
        }
    }
}
