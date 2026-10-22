using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using BDArmory.Damage;
using BDArmory.Utils;
using BDArmory.Settings;
using System.Linq;
using BDArmory.Extensions;

namespace BDArmory.Armor
{
    public class BDAdjustableArmor : BDAPartModule
    {
        // Version 0 was built prior to size changes affecting the parts position from surface attachment.
        // Version 1 includes size-based surface attachment movement.
        [KSPField]
        int version = 0;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_ArmorAdjustParts"),//Move Child Parts
            UI_Toggle(disabledText = "#LOC_BDArmory_false", enabledText = "#LOC_BDArmory_true")]//false--true
        public bool moveChildParts = true;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_ArmorWidth"),//Armor Width / Left Side Width
            UI_FloatSemiLogRange(minValue = 0.1f, maxValue = 16, scene = UI_Scene.Editor)]
        public float Width = 1;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_ArmorWidthR"),//Right Side Width
            UI_FloatSemiLogRange(minValue = 0.1f, maxValue = 8, scene = UI_Scene.Editor)]
        public float scaleneWidth = 1; // Note: for scalene triangles, these are actually double their real value, but it's easier to handle this way for localScale.

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_ArmorLength"),//Armor Length
            UI_FloatSemiLogRange(minValue = 0.1f, maxValue = 16, scene = UI_Scene.Editor)]
        public float Length = 1;

        [KSPField]
        public float maxScale = 16;

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_ArmorTriIso", active = true)]//Toggle Tri Type
        public void ToggleTriTypeOption() => ToggleTriTypeOptionHandler();
        void ToggleTriTypeOptionHandler(bool applySym = true, Toggle state = Toggle.Toggle)
        {
            switch (state)
            {
                case Toggle.Toggle:
                    scaleneTri = !scaleneTri;
                    break;
                case Toggle.NoChange:
                    break;
                case Toggle.Off:
                    scaleneTri = false;
                    break;
                case Toggle.On:
                    scaleneTri = true;
                    break;
            }

            Fields[nameof(scaleneWidth)].guiActiveEditor = scaleneTri;
            UI_FloatSemiLogRange AWidth = (UI_FloatSemiLogRange)Fields[nameof(Width)].uiControlEditor;
            AWidth.UpdateLimits(clamped ? 0.1f : BDArmorySettings.PROC_ARMOR_ALT_LIMITS.x, scaleneTri ? clamped ? maxScale / 2 : BDArmorySettings.PROC_ARMOR_ALT_LIMITS.y / 2 : clamped ? maxScale : BDArmorySettings.PROC_ARMOR_ALT_LIMITS.y);

            if (scaleneTri)
            {
                Fields[nameof(Width)].guiName = StringUtils.Localize("#LOC_BDArmory_ArmorWidthL");
                Events[nameof(ToggleTriTypeOption)].guiName = StringUtils.Localize("#LOC_BDArmory_ArmorTriSca");
            }
            else
            {
                Fields[nameof(Width)].guiName = StringUtils.Localize("#LOC_BDArmory_ArmorWidth");
                Events[nameof(ToggleTriTypeOption)].guiName = StringUtils.Localize("#LOC_BDArmory_ArmorTriIso");
            }
            if (state != Toggle.NoChange) // Avoids calling UpdateScale during OnStart before armor is non-null.
            {
                UpdateScale(Width, Length, scaleneWidth, true);
                // Adjust the surface attached children for the triangle type change. Non-surface attached children will get updated during UpdateStackNode.
                foreach (var child in part.children)
                {
                    if (child.attachMode != AttachModes.SRF_ATTACH) continue;
                    var srfAttachPoint = part.transform.InverseTransformPoint(child.transform.TransformPoint(child.srfAttachNode.position));
                    if (srfAttachPoint.x >= 0) continue; // Ignore the left side.
                    child.transform.position = part.transform.TransformPoint(new Vector3(
                        (scaleneTri ? scaleneWidth / prevWidth : Width / prevScaleneWidth) * srfAttachPoint.x,
                        srfAttachPoint.y,
                        srfAttachPoint.z
                    )); // This sets the child position to the updated srfAttachNode position.
                    child.transform.position -= child.transform.TransformVector(child.srfAttachNode.position); // This shifts it back to where it should be relative to the attach point.
                }
            }
            if (part.PartActionWindow != null)
                part.PartActionWindow.displayDirty = true; // Force a rebuild of the PAW, since we're changing the slider label.
            if (applySym)
            {
                using List<Part>.Enumerator sym = part.symmetryCounterparts.GetEnumerator();
                while (sym.MoveNext())
                {
                    if (sym.Current == null) continue;
                    sym.Current.FindModuleImplementing<BDAdjustableArmor>().ToggleTriTypeOptionHandler(false, state);
                }
            }
        }

        [KSPEvent(active = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_UnclampTuning_disabledText")]//Toggle scale limit
        public void ToggleScaleClamp() => ToggleScaleClampHandler();
        public void ToggleScaleClampHandler(bool applySym = true, Toggle state = Toggle.Toggle)
        {
            switch (state)
            {
                case Toggle.Toggle:
                    clamped = !clamped;
                    break;
                case Toggle.NoChange:
                    break;
                case Toggle.Off:
                    clamped = false;
                    break;
                case Toggle.On:
                    clamped = true;
                    break;
            }

            UI_FloatSemiLogRange AWidth = (UI_FloatSemiLogRange)Fields[nameof(Width)].uiControlEditor;
            AWidth.UpdateLimits(clamped ? 0.1f : BDArmorySettings.PROC_ARMOR_ALT_LIMITS.x, scaleneTri ? clamped ? maxScale / 2 : BDArmorySettings.PROC_ARMOR_ALT_LIMITS.y / 2 : clamped ? maxScale : BDArmorySettings.PROC_ARMOR_ALT_LIMITS.y);
            UI_FloatSemiLogRange ALength = (UI_FloatSemiLogRange)Fields[nameof(Length)].uiControlEditor;
            ALength.UpdateLimits(clamped ? 0.1f : BDArmorySettings.PROC_ARMOR_ALT_LIMITS.x, clamped ? maxScale : BDArmorySettings.PROC_ARMOR_ALT_LIMITS.y);
            UI_FloatSemiLogRange SWidth = (UI_FloatSemiLogRange)Fields[nameof(scaleneWidth)].uiControlEditor;
            SWidth.UpdateLimits(clamped ? 0.1f : BDArmorySettings.PROC_ARMOR_ALT_LIMITS.x, clamped ? maxScale / 2 : BDArmorySettings.PROC_ARMOR_ALT_LIMITS.y / 2);

            if (!clamped)
            {
                Events[nameof(ToggleScaleClamp)].guiName = StringUtils.Localize("#LOC_BDArmory_AI_UnclampTuning_enabledText");
            }
            else
            {
                Events[nameof(ToggleScaleClamp)].guiName = StringUtils.Localize("#LOC_BDArmory_AI_UnclampTuning_disabledText");
            }
            if (applySym)
            {
                using (List<Part>.Enumerator sym = part.symmetryCounterparts.GetEnumerator())
                    while (sym.MoveNext())
                    {
                        if (sym.Current == null) continue;
                        sym.Current.FindModuleImplementing<BDAdjustableArmor>().ToggleScaleClampHandler(false, state);
                    }
            }
        }

        [KSPField(isPersistant = true)]
        bool clamped = true;

        [KSPEvent(active = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_Armor_MatchParent")]
        public void MatchParent() => MatchParent(true);
        public void MatchParent(bool applySym)
        {
            var attachedNode = part.attachNodes.First(node => node.attachedPart == part.parent);
            var parentAttachNode = attachedNode.FindOpposingNode();
            var parentArmorModule = part.parent.GetComponent<BDAdjustableArmor>();
            if (parentArmorModule == null) return; // Shouldn't happen, but just in case. This is checked before calling MatchParent.

            // Match length
            float parentEdgeLength = parentArmorModule.isTriangularPanel switch
            {
                true => parentArmorModule.TriangleType switch
                {
                    "Right" => parentAttachNode.id switch
                    {
                        "bottom" => parentArmorModule.Length,
                        "right" => parentArmorModule.Width,
                        "side" => (parentArmorModule.Length * parentArmorModule.Length + parentArmorModule.Width * parentArmorModule.Width).Sqrt(),
                        _ => -1
                    },
                    "Scalene" => parentAttachNode.id switch
                    {
                        "right" => parentArmorModule.scaleneTri ? (parentArmorModule.Width + parentArmorModule.scaleneWidth) / 2f : parentArmorModule.Width,
                        "top" => (parentArmorModule.Length * parentArmorModule.Length + parentArmorModule.Width * parentArmorModule.Width / 4f).Sqrt(),
                        "bottom" => parentArmorModule.scaleneTri switch
                        {
                            true => (parentArmorModule.Length * parentArmorModule.Length + parentArmorModule.scaleneWidth * parentArmorModule.scaleneWidth / 4f).Sqrt(),
                            false => (parentArmorModule.Length * parentArmorModule.Length + parentArmorModule.Width * parentArmorModule.Width / 4f).Sqrt()
                        },
                        _ => -1
                    },
                    _ => -1
                },
                false => parentAttachNode.id switch
                {
                    "top" or "bottom" => parentArmorModule.Length,
                    "left" or "right" => parentArmorModule.Width,
                    _ => -1
                }
            };
            if (BDArmorySettings.DEBUG_ARMOR) Debug.Log($"[BDArmory.BDAdjustableArmor]: Node {attachedNode.id} on {part.partInfo.name} attached to {parentAttachNode.id} on {part.parent.partInfo.name} of length {parentEdgeLength}");
            if (parentEdgeLength < 0)
            {
                Debug.LogError($"[BDArmory.BDAdjustableArmor]: Failed to find parent edge length on {part.parent.partInfo.name}.{parentAttachNode.id}");
                return;
            }
            if (isTriangularPanel)
            {
                if (TriangleType == "Right")
                {
                    if (attachedNode.id == "bottom")
                    {
                        Length = parentEdgeLength;
                    }
                    else if (attachedNode.id == "right")
                    {
                        Width = parentEdgeLength;
                    }
                    else
                    {
                        float scale = parentEdgeLength / (Width * Width + Length * Length).Sqrt();
                        Width *= scale;
                        Length *= scale;
                    }
                }
                else if (scaleneTri)
                {
                    if (attachedNode.id == "right")
                    {
                        Width = parentEdgeLength;
                        scaleneWidth = Width;
                    }
                    else
                    {
                        float width = (attachedNode.id == "top" ? Width : scaleneWidth) / 2f;
                        float scale = parentEdgeLength / (width * width + Length * Length).Sqrt();
                        Width *= scale;
                        scaleneWidth *= scale;
                        Length *= scale;
                    }
                }
                else
                {
                    if (attachedNode.id == "right")
                    {
                        Width = parentEdgeLength;
                    }
                    else
                    {
                        float scale = parentEdgeLength / (Width * Width / 4f + Length * Length).Sqrt();
                        Width *= scale;
                        Length *= scale;
                    }
                }
            }
            else
            {
                if (attachedNode.id == "top" || attachedNode.id == "bottom")
                {
                    Length = parentEdgeLength;
                }
                else
                {
                    Width = parentEdgeLength;
                }
            }
            UpdateScale(Width, Length, scaleneWidth, true);

            // Reorient to align edges
            Vector3 offset = part.transform.TransformPoint(attachedNode.position) - part.parent.transform.TransformPoint(parentAttachNode.position); // Preserve manual offset.
            Vector3 parentNodeAxis = part.parent.transform.TransformVector(parentAttachNode.orientation);
            Vector3 parentNormal = part.parent.transform.TransformVector(Vector3.up);
            Vector3 parentEdgeAxis = Vector3.Cross(parentNodeAxis, parentNormal);
            Vector3 nodeAxis = part.transform.TransformVector(-attachedNode.orientation);
            Vector3 targetNormal = Vector3.Cross(parentEdgeAxis, nodeAxis);
            Vector3 partNormal = part.transform.TransformVector(Vector3.up);
            part.transform.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(partNormal, targetNormal, nodeAxis), nodeAxis) * part.transform.rotation;
            partNormal = part.transform.TransformVector(Vector3.up);
            Vector3 partEdgeAxis = Vector3.Cross(nodeAxis, partNormal);
            part.transform.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(partEdgeAxis, parentEdgeAxis, partNormal), partNormal) * part.transform.rotation;
            part.transform.position = part.parent.transform.TransformPoint(parentAttachNode.position) - part.transform.TransformVector(attachedNode.position) + offset; // Realign position.

            if (applySym)
            {
                foreach (var sym in part.symmetryCounterparts)
                {
                    var armor = sym.GetComponent<BDAdjustableArmor>();
                    if (sym == null) continue;
                    armor.MatchParent(false);
                }
            }
        }

        //public bool isCurvedPanel = false;
        private float armorthickness = 1;
        private float Oldthickness = 0;

        [KSPField]
        public bool isTriangularPanel = false; // FIXME The panel type should probably become an enum if more panel types get added.

        [KSPField(isPersistant = true)]
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


        HitpointTracker armor;

        float origBreakingForce;
        float origBreakingTorque;
        float prevLength, prevWidth, prevScaleneWidth;

        public override void OnStart(StartState state)
        {
            armorTransforms = part.FindModelTransforms(ArmorTransformName);
            ThicknessTransform = part.FindModelTransform(ThicknessTransformName);
            origBreakingForce = part.breakingForce;
            origBreakingTorque = part.breakingTorque;
            prevLength = Length;
            prevWidth = Width;
            prevScaleneWidth = scaleneWidth;
            if (isTriangularPanel && TriangleType != "Right")
            {
                Events[nameof(ToggleTriTypeOption)].guiActiveEditor = true;
                scaleneTransforms = part.FindModelTransforms(ScaleneTransformName);
                UI_FloatSemiLogRange SWidth = (UI_FloatSemiLogRange)Fields[nameof(scaleneWidth)].uiControlEditor;
                SWidth.onFieldChanged = AdjustSWidth;
                // SWidth.UpdateLimits(0.1f, maxScale / 2);
            }
            if (HighLogic.LoadedSceneIsEditor)
            {
                StartCoroutine(DelayedUpdateStackNode());
                GameEvents.onEditorShipModified.Add(OnEditorShipModifiedEvent);
                ToggleTriTypeOptionHandler(state: Toggle.NoChange); // Initialise the UI for the Triangle Type toggle
                ToggleScaleClampHandler(state: Toggle.NoChange); // Initialise the UI for the Clamped toggle
                GameEvents.onEditorPartPlaced.Add(OnPartPlaced);
                SetMatchParentVisibility();
            }
            UpdateThickness();
            UI_FloatSemiLogRange AWidth = (UI_FloatSemiLogRange)Fields[nameof(Width)].uiControlEditor;
            AWidth.onFieldChanged = AdjustWidth;
            // AWidth.UpdateLimits(0.1f, maxScale);
            UI_FloatSemiLogRange ALength = (UI_FloatSemiLogRange)Fields[nameof(Length)].uiControlEditor;
            ALength.onFieldChanged = AdjustLength;
            // ALength.UpdateLimits(0.1f, maxScale);

            armor = GetComponent<HitpointTracker>();
            UpdateScale(Width, Length, scaleneWidth, false);
        }

        IEnumerator DelayedUpdateStackNode()
        {
            yield return null;
            UpdateStackNode(false);
            version = 1; // Upgrade the part to version 1 next time it's saved.
        }

        private void OnDestroy()
        {
            GameEvents.onEditorShipModified.Remove(OnEditorShipModifiedEvent);
            GameEvents.onEditorPartPlaced.Remove(OnPartPlaced);
        }

        public void AdjustWidth(BaseField field, object oldValue)
        {
            Width = Mathf.Clamp(Width, clamped ? 0.1f : BDArmorySettings.PROC_ARMOR_ALT_LIMITS.x, scaleneTri ? clamped ? maxScale / 2 : BDArmorySettings.PROC_ARMOR_ALT_LIMITS.y / 2 : clamped ? maxScale : BDArmorySettings.PROC_ARMOR_ALT_LIMITS.y);
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
                    sym.Current.FindModuleImplementing<BDAdjustableArmor>().UpdateScale(Width, Length, scaleneWidth); //needs to be changed to use updatewidth() - FIXME later, future SI
                }
            updateArmorStats();
            UpdateStackNode(true);
        }
        public void AdjustSWidth(BaseField field, object oldValue)
        {
            scaleneWidth = Mathf.Clamp(scaleneWidth, clamped ? 0.1f : BDArmorySettings.PROC_ARMOR_ALT_LIMITS.x, clamped ? maxScale / 2 : BDArmorySettings.PROC_ARMOR_ALT_LIMITS.y / 2);
            for (int i = 0; i < scaleneTransforms.Length; i++)
            {
                scaleneTransforms[i].localScale = new Vector3(scaleneWidth, Length, armorthickness);
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
        public void AdjustLength(BaseField field, object oldValue)
        {
            Length = Mathf.Clamp(Length, clamped ? 0.1f : BDArmorySettings.PROC_ARMOR_ALT_LIMITS.x, clamped ? maxScale : BDArmorySettings.PROC_ARMOR_ALT_LIMITS.y);
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
                armorTransforms[i].localScale = new Vector3(Width, Length, Mathf.Clamp(armor.Armor / 10, 0.1f, 1500));
            }
            if (isTriangularPanel && TriangleType != "Right")
            {
                for (int i = 0; i < scaleneTransforms.Length; i++)
                {
                    scaleneTransforms[i].localScale = new Vector3(scaleneTri ? scaleneWidth : Width, Length, Mathf.Clamp(armor.Armor / 10, 0.1f, 1500));
                }
            }
            updateArmorStats();
            if (updateNodes) UpdateStackNode(true);
        }
        IEnumerator updateDrag()
        {
            yield return null;
            DragCube DragCube = DragCubeSystem.Instance.RenderProceduralDragCube(part);
            part.DragCubes.ClearCubes();
            part.DragCubes.Cubes.Add(DragCube);
            part.DragCubes.ResetCubeWeights();
            part.DragCubes.ForceUpdate(true, true, false);
            part.DragCubes.SetDragWeights();
            if (HighLogic.LoadedSceneIsEditor) GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
        }
        public void UpdateStackNode(bool translateChidren)
        {
            Dictionary<uint, Vector3> childSrfAttachPoints = []; // For translating surface attached children.
            foreach (var child in part.children)
            {
                if (child.attachMode != AttachModes.SRF_ATTACH) continue;
                childSrfAttachPoints[child.persistentId] = part.transform.InverseTransformPoint(child.transform.TransformPoint(child.srfAttachNode.position));
            }
            using List<AttachNode>.Enumerator stackNode = part.attachNodes.GetEnumerator();
            while (stackNode.MoveNext())
            {
                if (stackNode.Current == null) continue;
                Vector3 prevPos = stackNode.Current.position;
                if (stackNode.Current.nodeType != AttachNode.NodeType.Stack) continue; // Note: there don't seem to be any other attach node types in this list.

                if (stackNode.Current.id == "top" || stackNode.Current.id == "bottom")
                {
                    Vector3 prevAngle = stackNode.Current.orientation;
                    if (stackNode.Current.id == "top")
                    {
                        stackNode.Current.size = Mathf.CeilToInt(Width / 2);
                        stackNode.Current.breakingForce = Width * origBreakingForce;
                        stackNode.Current.breakingTorque = Width * origBreakingTorque;
                        stackNode.Current.position.x = stackNode.Current.originalPosition.x * Width;
                        if (isTriangularPanel) stackNode.Current.orientation = new Vector3(1, 0, -(Width / 2 / Length));
                        if (translateChidren) MoveParts(stackNode.Current, stackNode.Current.position - prevPos, stackNode.Current.orientation - prevAngle);
                    }
                    else
                    {
                        stackNode.Current.size = Mathf.CeilToInt(scaleneTri ? scaleneWidth / 2 : Width / 2);
                        stackNode.Current.breakingForce = (scaleneTri ? scaleneWidth : Width) * origBreakingForce;
                        stackNode.Current.breakingTorque = (scaleneTri ? scaleneWidth : Width) * origBreakingTorque;
                        stackNode.Current.position.x = stackNode.Current.originalPosition.x * (scaleneTri ? scaleneWidth : Width);
                        if (isTriangularPanel && TriangleType != "Right")
                        {
                            stackNode.Current.orientation = new Vector3(-1, 0, -((scaleneTri ? scaleneWidth : Width) / 2 / Length));
                        }
                        if (translateChidren) MoveParts(stackNode.Current, stackNode.Current.position - prevPos, stackNode.Current.orientation - prevAngle); //look into making triangle side nodes rotate attachnode based on new angle? AttachNode.Orientation?                            
                    }
                }
                else if (stackNode.Current.id == "left" || stackNode.Current.id == "right")
                {
                    stackNode.Current.size = Mathf.CeilToInt(Length / 2);
                    stackNode.Current.breakingForce = Length * origBreakingForce;
                    stackNode.Current.breakingTorque = Length * origBreakingTorque;
                    stackNode.Current.position.z = stackNode.Current.originalPosition.z * Length;
                    if (translateChidren) MoveParts(stackNode.Current, stackNode.Current.position - prevPos, Vector3.zero);
                }
                else if (stackNode.Current.id == "side")
                {
                    stackNode.Current.size = Mathf.CeilToInt(((Width / 2) + (Length / 2)) / 2);
                    stackNode.Current.orientation = new Vector3(1, 0, -(Width / Length));
                }
            }
            // Also update the surface attach node.
            if (version > 0) // This also needs to be done during the DelayedUpdateStackNode in the editor for some reason. Older (v0) craft break if we do this.
            {
                // These all have the surface attach node on the z-axis.
                Vector3 prevPos = part.srfAttachNode.position;
                part.srfAttachNode.position.z = part.srfAttachNode.originalPosition.z * Length;
                MoveParts(part.srfAttachNode, part.srfAttachNode.position - prevPos, Vector3.zero);
            }
            // Update any surface attached children.
            if (translateChidren)
            {
                foreach (var child in part.children)
                {
                    if (child.attachMode != AttachModes.SRF_ATTACH) continue;
                    child.transform.position = part.transform.TransformPoint(
                        new Vector3(
                            (isTriangularPanel && scaleneTri && childSrfAttachPoints[child.persistentId].x < 0 ? scaleneWidth / prevScaleneWidth : Width / prevWidth) * childSrfAttachPoints[child.persistentId].x,
                            childSrfAttachPoints[child.persistentId].y,
                            Length / prevLength * childSrfAttachPoints[child.persistentId].z)
                        ); // This sets the child position to the updated srfAttachNode position.
                    child.transform.position -= child.transform.TransformVector(child.srfAttachNode.position); // This shifts it back to where it should be relative to the attach point.
                }
            }
            prevLength = Length; // Previous values since the nodes were adjusted.
            prevWidth = Width;
            prevScaleneWidth = scaleneWidth;
        }
        public void MoveParts(AttachNode node, Vector3 delta, Vector3 angleDelta)
        {
            if (!moveChildParts) return;
            if (node.attachedPart is Part pushTarget)
            {
                if (pushTarget == null) return;
                Vector3 worldDelta = part.transform.TransformVector(delta);
                if (BDArmorySettings.DEBUG_ARMOR) Debug.Log($"[BDArmory.BDAdjustableArmor]: Moving {(pushTarget == part.parent ? part : pushTarget)} on node {node.id} by {worldDelta}");
                if (pushTarget == part.parent) // push ourselves
                {
                    part.transform.position -= worldDelta;
                }
                else // push them
                    pushTarget.transform.position += worldDelta;
                //Vector3 worldAngle = part.transform.TransformVector(angleDelta);
                //pushTarget.transform.rotation += worldAngle;
            }
        }
        public float calcMaxArmor()
        {
            return Mathf.Min(Mathf.Min(Width, Length) * 1000f, 1500f);
        }
        public void updateArmorStats()
        {
            armor.armorVolume = (scaleneTri ? scaleneWidth + Width : Width) * Length;
            if (isTriangularPanel)
            {
                armor.armorVolume /= 2;
            }
            armor.maxSupportedArmor = calcMaxArmor();
            armor.ArmorSetup(null, null);
            StartCoroutine(updateDrag());
        }
        void UpdateThickness()
        {
            if (armor != null && armorTransforms != null)
            {
                float tempMaxThickness = calcMaxArmor();
                if (armor.Armor > tempMaxThickness)
                {
                    armor.Armor = tempMaxThickness;
                    updateArmorStats();
                }
                armorthickness = Mathf.Clamp(armor.Armor / 10, 0.1f, 1500);

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
                    if (Oldthickness > 0)
                    {
                        foreach (var child in part.children)
                        {
                            if (child.attachMode != AttachModes.SRF_ATTACH) continue; // Only surface attached children are affected.
                            var srfAttachPoint = part.transform.InverseTransformPoint(child.transform.TransformPoint(child.srfAttachNode.position));
                            child.transform.position = part.transform.TransformPoint(new Vector3(
                                srfAttachPoint.x,
                                armorthickness / Oldthickness * srfAttachPoint.y,
                                srfAttachPoint.z
                            )); // This sets the child position to the updated srfAttachNode position.
                            child.transform.position -= child.transform.TransformVector(child.srfAttachNode.position); // This shifts it back to where it should be relative to the attach point.
                        }
                    }
                }
                Oldthickness = armorthickness;
            }
            else
            {
                //if (armor == null) Debug.Log("[BDAdjustableArmor] No HitpointTracker found! aborting UpdateThickness()!");
                //if (armorTransforms == null) Debug.Log("[BDAdjustableArmor] No ArmorTransform found! aborting UpdateThickness()!");
                return;
            }
        }
        private void OnEditorShipModifiedEvent(ShipConstruct data)
        {
            UpdateThickness();
        }

        public void OnPartPlaced(Part p)
        {
            if (part != p) return;
            SetMatchParentVisibility();
        }
        public void SetMatchParentVisibility()
        {
            Events[nameof(MatchParent)].guiActiveEditor = part.parent != null && part.attachMode == AttachModes.STACK && part.parent.GetComponent<BDAdjustableArmor>() != null;
        }
    }
}