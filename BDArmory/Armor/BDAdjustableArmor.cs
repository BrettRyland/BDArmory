using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using BDArmory.Damage;
using KSP.Localization;
using BDArmory.Utils;

namespace BDArmory.Armor
{
    public class BDAdjustableArmor : PartModule
    {
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_ArmorAdjustParts"),//Move Child Parts
            UI_Toggle(disabledText = "#LOC_BDArmory_false", enabledText = "#LOC_BDArmory_true")]//false--true
        public bool moveChildParts = true;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_ArmorWidth"),//Armor Width
            UI_FloatRange(minValue = 0.5f, maxValue = 16, stepIncrement = 0.1f, scene = UI_Scene.Editor)]
        public float Width = 1;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_ArmorWidthR"),//Right Side Width
            UI_FloatRange(minValue = 0.1f, maxValue = 8, stepIncrement = 0.1f, scene = UI_Scene.Editor)]
        public float scaleneWidth = 1;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_ArmorLength"),//Armor Length
            UI_FloatRange(minValue = 0.5f, maxValue = 16, stepIncrement = 0.1f, scene = UI_Scene.Editor)]
        public float Length = 1;

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_ArmorTriIso", active = true)]//Toggle Tri Type
        public void ToggleTriTypeOption()
        {
            scaleneTri = !scaleneTri;

            Fields["scaleneWidth"].guiActiveEditor = scaleneTri;
            UI_FloatRange AWidth = (UI_FloatRange)Fields["Width"].uiControlEditor;
            AWidth.maxValue = scaleneTri ? 8 : 16;
            AWidth.minValue = scaleneTri ? 0.1f : 0.5f;

            if (scaleneTri)
            {
                Fields["Width"].guiName = Localizer.Format("#LOC_BDArmory_ArmorWidthL");
                Events["ToggleTriTypeOption"].guiName = Localizer.Format("#LOC_BDArmory_ArmorTriSca");
            }
            else
            {
                Fields["Width"].guiName = Localizer.Format("#LOC_BDArmory_ArmorWidth");
                Events["ToggleTriTypeOption"].guiName = Localizer.Format("#LOC_BDArmory_ArmorTriIso");
            }
            GUIUtils.RefreshAssociatedWindows(part);
            using (List<Part>.Enumerator sym = part.symmetryCounterparts.GetEnumerator())
                while (sym.MoveNext())
                {
                    if (sym.Current == null) continue;
                    sym.Current.FindModuleImplementing<BDAdjustableArmor>().ToggleTriTypeOption();
                }
        }


        //public bool isCurvedPanel = false;
        private float armorthickness = 1;
        private float Oldthickness = 1;

        [KSPField]
        public bool isTriangularPanel = false;
        bool scaleneTri = false;

        [KSPField]
        public string TriangleType = "none";

        [KSPField]
        public string ArmorTransformName = "ArmorTransform"; //transform of armor panel mesh/box collider
        Transform[] armorTransforms;

        [KSPField]
        public string ScaleneTransformName = "ScaleneTransform"; //transform of armor panel mesh/box collider
        Transform[] scaleneTransforms;

        [KSPField]
        public string ThicknessTransformName = "ThicknessTransform"; //name of armature to control thickness of curved panels
        Transform ThicknessTransform;

        [KSPField] public string stackNodePosition;

        Dictionary<string, Vector3> originalStackNodePosition;

        HitpointTracker armor;

        public override void OnStart(StartState state)
        {
            armorTransforms = part.FindModelTransforms(ArmorTransformName);
            ThicknessTransform = part.FindModelTransform(ThicknessTransformName);
            if (isTriangularPanel && TriangleType != "Right")
            {
                Events["ToggleTriTypeOption"].guiActiveEditor = true;
                scaleneTransforms = part.FindModelTransforms(ScaleneTransformName);
                UI_FloatRange SWidth = (UI_FloatRange)Fields["scaleneWidth"].uiControlEditor;
                SWidth.onFieldChanged = AdjustSWidth;
            }
            Fields["scaleneWidth"].guiActiveEditor = false;
            //figure out why the scalene tri toggle option is still visible in the flight scene
            if (HighLogic.LoadedSceneIsEditor)
            {
                ParseStackNodePosition();
                StartCoroutine(DelayedUpdateStackNode());
                GameEvents.onEditorShipModified.Add(OnEditorShipModifiedEvent);
            }
            UpdateThickness(true);
            UI_FloatRange AWidth = (UI_FloatRange)Fields["Width"].uiControlEditor;
            AWidth.onFieldChanged = AdjustWidth;
            UI_FloatRange ALength = (UI_FloatRange)Fields["Length"].uiControlEditor;
            ALength.onFieldChanged = AdjustLength;
            armor = GetComponent<HitpointTracker>();
            UpdateScale(Width, Length, scaleneWidth, false);
            GUIUtils.RefreshAssociatedWindows(part);
        }
        void ParseStackNodePosition()
        {
            originalStackNodePosition = new Dictionary<string, Vector3>();
            string[] nodes = stackNodePosition.Split(new char[] { ';' });
            for (int i = 0; i < nodes.Length; i++)
            {
                string[] split = nodes[i].Split(new char[] { ',' });
                string id = split[0];
                Vector3 position = new Vector3(float.Parse(split[1]), float.Parse(split[2]), float.Parse(split[3]));
                originalStackNodePosition.Add(id, position);
            }
        }

        IEnumerator DelayedUpdateStackNode()
        {
            yield return null;
            UpdateStackNode(false);
        }

        private void OnDestroy()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                GameEvents.onEditorShipModified.Remove(OnEditorShipModifiedEvent);
            }
        }

        public void AdjustWidth(BaseField field, object obj)
        {
            Width = Mathf.Clamp(Width, scaleneTri ? 0.1f : 0.5f, scaleneTri ? 8 : 16f);
            for (int i = 0; i < armorTransforms.Length; i++)
            {
                armorTransforms[i].localScale = new Vector3(Width, Length, armorthickness);
            }
            if (isTriangularPanel && TriangleType != "Right" && !scaleneTri)
            {
                for (int i = 0; i < scaleneTransforms.Length; i++)
                {
                    scaleneTransforms[i].localScale = new Vector3(Width, Length, armorthickness);
                }
            }
            using (List<Part>.Enumerator sym = part.symmetryCounterparts.GetEnumerator())
                while (sym.MoveNext())
                {
                    if (sym.Current == null) continue;
                    sym.Current.FindModuleImplementing<BDAdjustableArmor>().UpdateScale(Width, Length, scaleneWidth); //needs to be changed to use updatewitth() - FIXME later, future SI
                }
            updateArmorStats();
            UpdateStackNode(true);
        }
        public void AdjustSWidth(BaseField field, object obj)
        {
            scaleneWidth = Mathf.Clamp(scaleneWidth, 0.1f, 8);
            for (int i = 0; i < scaleneTransforms.Length; i++)
            {
                scaleneTransforms[i].localScale = new Vector3(scaleneWidth * 2, Length, armorthickness);
            }
            using (List<Part>.Enumerator sym = part.symmetryCounterparts.GetEnumerator())
                while (sym.MoveNext())
                {
                    if (sym.Current == null) continue;
                    sym.Current.FindModuleImplementing<BDAdjustableArmor>().UpdateScale(Width, Length, scaleneWidth); //needs to be changed to use updatewitth() - FIXME later, future SI
                }
            updateArmorStats();
            UpdateStackNode(true);
        }
        public void AdjustLength(BaseField field, object obj)
        {
            Length = Mathf.Clamp(Length, 0.5f, 16f);
            for (int i = 0; i < armorTransforms.Length; i++)
            {
                armorTransforms[i].localScale = new Vector3(Width, Length, armorthickness);
            }
            if (isTriangularPanel && TriangleType != "Right")
            {
                for (int i = 0; i < scaleneTransforms.Length; i++)
                {
                    scaleneTransforms[i].localScale = new Vector3(scaleneWidth, Length, armorthickness);
                }
            }
            using (List<Part>.Enumerator sym = part.symmetryCounterparts.GetEnumerator())
                while (sym.MoveNext())
                {
                    if (sym.Current == null) continue;
                    sym.Current.FindModuleImplementing<BDAdjustableArmor>().UpdateScale(Width, Length, scaleneWidth);
                }
            updateArmorStats();
            UpdateStackNode(true);
        }

        public void UpdateScale(float width, float length, float scalenewidth = 1, bool updateNodes = true)
        {
            Width = width;
            scaleneWidth = scalenewidth;
            Length = length;

            for (int i = 0; i < armorTransforms.Length; i++)
            {
                armorTransforms[i].localScale = new Vector3(Width, Length, Mathf.Clamp((armor.Armor / 10), 0.1f, 1500));
            }
            if (isTriangularPanel && TriangleType != "Right")
            {
                for (int i = 0; i < scaleneTransforms.Length; i++)
                {
                    scaleneTransforms[i].localScale = new Vector3(scaleneTri ? scaleneWidth : Width, Length, Mathf.Clamp((armor.Armor / 10), 0.1f, 1500));
                }
            }
            updateArmorStats();
            if (updateNodes) UpdateStackNode(true);
        }

        public void UpdateStackNode(bool translateChidren)
        {
            using (List<AttachNode>.Enumerator stackNode = part.attachNodes.GetEnumerator())
                while (stackNode.MoveNext())
                {
                    if (stackNode.Current?.nodeType != AttachNode.NodeType.Stack ||
                        !originalStackNodePosition.ContainsKey(stackNode.Current.id)) continue;

                    if (stackNode.Current.id == "top" || stackNode.Current.id == "bottom")
                    {
                        Vector3 prevPos = stackNode.Current.position;
                        Vector3 prevAngle = stackNode.Current.orientation;
                        int offsetScale = 2;
                        if (isTriangularPanel && TriangleType != "Right" && !scaleneTri)
                        {
                            offsetScale = 4;
                        }
                        if (stackNode.Current.id == "top")
                        {
                            stackNode.Current.size = Mathf.CeilToInt(Width / 2);
                            stackNode.Current.breakingForce = Width * 100;
                            stackNode.Current.breakingTorque = Width * 100;
                            stackNode.Current.position.x = originalStackNodePosition[stackNode.Current.id].x + (((Width - 1) / (scaleneTri ? 2 : 1)) / offsetScale); //if eqi tri this needs to be /4
                            if (isTriangularPanel) stackNode.Current.orientation = new Vector3(1, 0, -((Width / 2) / Length));
                            if (translateChidren) MoveParts(stackNode.Current, stackNode.Current.position - prevPos, stackNode.Current.orientation - prevAngle);
                        }
                        else
                        {
                            stackNode.Current.size = Mathf.CeilToInt(scaleneTri ? scaleneWidth / 2 : Width / 2);
                            stackNode.Current.breakingForce = scaleneTri ? scaleneWidth : Width * 100;
                            stackNode.Current.breakingTorque = scaleneTri ? scaleneWidth : Width * 100;
                            stackNode.Current.position.x = originalStackNodePosition[stackNode.Current.id].x - ((scaleneTri ? ((scaleneWidth - 1) / 2) : Width - 1) / offsetScale);// and a right tri hypotenuse node shouldn't move at all
                            if (isTriangularPanel && TriangleType != "Right")
                            {
                                stackNode.Current.orientation = new Vector3(-1, 0, -(((scaleneTri ? scaleneWidth : Width) / 2) / Length));
                            }
                            if (translateChidren) MoveParts(stackNode.Current, stackNode.Current.position - prevPos, stackNode.Current.orientation - prevAngle); //look into making triangle side nodes rotate attachnode based on new angle? AttachNode.Orientation?                            
                        }
                    }
                    if (stackNode.Current.id == "left" || stackNode.Current.id == "right")
                    {
                        stackNode.Current.size = Mathf.CeilToInt(Length / 2);
                        stackNode.Current.breakingForce = Length * 100;
                        stackNode.Current.breakingTorque = Length * 100;
                        Vector3 prevPos = stackNode.Current.position;
                        if (stackNode.Current.id == "right")
                        {
                            stackNode.Current.position.z = originalStackNodePosition[stackNode.Current.id].z + ((Length - 1) / 2);
                            if (translateChidren) MoveParts(stackNode.Current, stackNode.Current.position - prevPos, Vector3.zero);
                        }
                        else
                        {
                            stackNode.Current.position.z = originalStackNodePosition[stackNode.Current.id].z - ((Length - 1) / 2);
                            if (translateChidren) MoveParts(stackNode.Current, stackNode.Current.position - prevPos, Vector3.zero);
                        }
                    }
                    if (stackNode.Current.id == "side")
                    {
                        stackNode.Current.size = Mathf.CeilToInt(((Width / 2) + (Length / 2)) / 2);
                        stackNode.Current.orientation = new Vector3(1, 0, -(Width / Length));
                    }
                }
        }
        public void MoveParts(AttachNode node, Vector3 delta, Vector3 angleDelta)
        {
            if (!moveChildParts) return;
            if (node.attachedPart is Part pushTarget)
            {
                if (pushTarget == null) return;
                Vector3 worldDelta = part.transform.TransformVector(delta);
                pushTarget.transform.position += worldDelta;
                //Vector3 worldAngle = part.transform.TransformVector(angleDelta);
                //pushTarget.transform.rotation += worldAngle;
            }
        }
        public void updateArmorStats()
        {
            armor.armorVolume = ((scaleneTri ? scaleneWidth + Width : Width) * Length);
            if (isTriangularPanel)
            {
                armor.armorVolume /= 2;
            }
            armor.ArmorSetup(null, null);
        }
        void UpdateThickness(bool onLoad = false)
        {
            if (armor != null && armorTransforms != null)
            {
                armorthickness = Mathf.Clamp((armor.Armor / 10), 0.1f, 1500);

                if (armorthickness != Oldthickness)
                {
                    for (int i = 0; i < armorTransforms.Length; i++)
                    {
                        armorTransforms[i].localScale = new Vector3(Width, Length, armorthickness);
                    }
                    if (isTriangularPanel && TriangleType != "Right")
                    {
                        for (int i = 0; i < scaleneTransforms.Length; i++)
                        {
                            scaleneTransforms[i].localScale = new Vector3(scaleneTri ? scaleneWidth : Width, Length, armorthickness);
                        }
                    }
                }
            }
            else
            {
                //if (armor == null) Debug.Log("[BDAAdjustableArmor] No HitpointTracker found! aborting UpdateThickness()!");
                //if (armorTransforms == null) Debug.Log("[BDAAdjustableArmor] No ArmorTransform found! aborting UpdateThickness()!");
                return;
            }
            if (onLoad) return; //don't adjust part placement on load
            /*
            if (armorthickness != Oldthickness)
            {
                float ratio = (armorthickness - Oldthickness) / 100;

                Vector3 prevPos = new Vector3(0f, Oldthickness / 100, 0f);
                Vector3 delta = new Vector3(0f, armorthickness / 100, 0f);
                Vector3 worldDelta = part.transform.TransformVector(delta);
                List<Part>.Enumerator p = part.children.GetEnumerator();
                while (p.MoveNext())
                {
                    if (p.Current == null) continue;
                    if (p.Current.FindAttachNodeByPart(part) is AttachNode node && node.nodeType == AttachNode.NodeType.Surface)
                    {

                        p.Current.transform.position += worldDelta;
                    }
                }
                Oldthickness = armorthickness;
            }
            */
        }
        private void OnEditorShipModifiedEvent(ShipConstruct data)
        {
            UpdateThickness();
        }
    }
}