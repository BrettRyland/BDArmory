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
        // Version 0 was built prior to size changes affecting the parts position from surface attachment and fixing the mirroring of the rectangular panel.
        // Version 1 includes size-based surface attachment movement.
        [KSPField(isPersistant = true, guiActiveEditor = false, guiActive = false), UI_Label(scene = UI_Scene.Editor)]
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
        Vector3 srfAttachPosOnLoad = default;

        public override void OnStart(StartState state)
        {
            srfAttachPosOnLoad = part.srfAttachNode.position; // Note: On load in flight, this is the prefab default position, but in an editor, this is the scaled position.
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
            UpdateThickness();
            UI_FloatSemiLogRange AWidth = (UI_FloatSemiLogRange)Fields[nameof(Width)].uiControlEditor;
            AWidth.onFieldChanged = AdjustWidth;
            // AWidth.UpdateLimits(0.1f, maxScale);
            UI_FloatSemiLogRange ALength = (UI_FloatSemiLogRange)Fields[nameof(Length)].uiControlEditor;
            ALength.onFieldChanged = AdjustLength;
            // ALength.UpdateLimits(0.1f, maxScale);

            armor = GetComponent<HitpointTracker>();
            UpdateScale(Width, Length, scaleneWidth, false);

            if (HighLogic.LoadedSceneIsEditor)
            {
                Fields[nameof(version)].guiActiveEditor = BDArmorySettings.DEBUG_ARMOR;
                StartCoroutine(DelayedUpdateStackNode());
                GameEvents.onEditorShipModified.Add(OnEditorShipModifiedEvent);
                ToggleTriTypeOptionHandler(state: Toggle.NoChange); // Initialise the UI for the Triangle Type toggle
                ToggleScaleClampHandler(state: Toggle.NoChange); // Initialise the UI for the Clamped toggle
                GameEvents.onEditorPartPlaced.Add(OnPartPlaced);
                SetMatchParentVisibility();
            }
        }

        IEnumerator DelayedUpdateStackNode()
        {
            // For some reason, the stack and srf nodes are resetting to the prefab values after OnStart is finished, despite being the right values during OnStart.
            yield return null;
            UpdateStackNode(true);
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
            UpdateStackNode();
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
            UpdateStackNode();
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
            UpdateStackNode();
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
            if (updateNodes) UpdateStackNode();
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
        public void UpdateStackNode(bool duringStart = false)
        {
            // Get the node info from the prefab (which has scale 1,1,1) and use that instead of relying on original position being reset.
            Dictionary<string, Vector3> prefabNodePositions = part.partInfo.partPrefab.attachNodes.ToDictionary(n => n.id, n => n.originalPosition);
            prefabNodePositions.Add(part.srfAttachNode.id, part.partInfo.partPrefab.srfAttachNode.originalPosition);
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
                bool isParentNode = part.parent != null && stackNode.Current.attachedPart == part.parent;

                if (stackNode.Current.id == "top" || stackNode.Current.id == "bottom")
                {
                    Vector3 prevAngle = stackNode.Current.orientation;
                    if (stackNode.Current.id == "top")
                    {
                        stackNode.Current.size = Mathf.CeilToInt(Width / 2);
                        stackNode.Current.breakingForce = Width * origBreakingForce;
                        stackNode.Current.breakingTorque = Width * origBreakingTorque;
                        stackNode.Current.position.x = prefabNodePositions[stackNode.Current.id].x * Width;
                        if (isTriangularPanel) stackNode.Current.orientation = new Vector3(1, 0, -(Width / 2 / Length));
                        if (!duringStart && (isParentNode || moveChildParts)) MoveParts(stackNode.Current, stackNode.Current.position - prevPos, stackNode.Current.orientation - prevAngle);
                    }
                    else
                    {
                        stackNode.Current.size = Mathf.CeilToInt(scaleneTri ? scaleneWidth / 2 : Width / 2);
                        stackNode.Current.breakingForce = (scaleneTri ? scaleneWidth : Width) * origBreakingForce;
                        stackNode.Current.breakingTorque = (scaleneTri ? scaleneWidth : Width) * origBreakingTorque;
                        stackNode.Current.position.x = prefabNodePositions[stackNode.Current.id].x * (scaleneTri ? scaleneWidth : Width);
                        if (isTriangularPanel && TriangleType != "Right")
                        {
                            stackNode.Current.orientation = new Vector3(-1, 0, -((scaleneTri ? scaleneWidth : Width) / 2 / Length));
                        }
                        if (!duringStart && (isParentNode || moveChildParts)) MoveParts(stackNode.Current, stackNode.Current.position - prevPos, stackNode.Current.orientation - prevAngle); //look into making triangle side nodes rotate attachnode based on new angle? AttachNode.Orientation?                            
                    }
                }
                else if (stackNode.Current.id == "left" || stackNode.Current.id == "right")
                {
                    stackNode.Current.size = Mathf.CeilToInt(Length / 2);
                    stackNode.Current.breakingForce = Length * origBreakingForce;
                    stackNode.Current.breakingTorque = Length * origBreakingTorque;
                    stackNode.Current.position.z = prefabNodePositions[stackNode.Current.id].z * Length;
                    if (!duringStart && (isParentNode || moveChildParts)) MoveParts(stackNode.Current, stackNode.Current.position - prevPos, Vector3.zero);
                }
                else if (stackNode.Current.id == "side")
                {
                    stackNode.Current.size = Mathf.CeilToInt(((Width / 2) + (Length / 2)) / 2);
                    stackNode.Current.orientation = new Vector3(1, 0, -(Width / Length));
                }
            }

            // Also update the surface attach node of this part to its parent.
            // This also needs to be done during the DelayedUpdateStackNode in the editor as all the nodes revert to their prefab positions for some reason.
            // The Approximately check catches parts that were saved with a shifted srfAttachNode position, but not an updated version (due to DLLs being posted for testing).
            if (version > 0 || !Mathf.Approximately(srfAttachPosOnLoad.z, prefabNodePositions[part.srfAttachNode.id].z))
            {
                Vector3 prevPos = part.srfAttachNode.position;
                part.srfAttachNode.position.z = prefabNodePositions[part.srfAttachNode.id].z * Length; // These all have the surface attach node on the z-axis.
                MoveParts(part.srfAttachNode, part.srfAttachNode.position - prevPos, Vector3.zero);
                version = 1; // Upgrade the part version to what it should be.
            }
            else
            {
                // Older panels (v0) didn't have their srfAttachNode adjusted when they were placed.
                // An option is to move their attPos0 (local attach point) instead, but this results in the parts loading in at weird positions before snapping into place, which looks bad.
                // Furthermore, "fixing" the rectangular panel (due to the old version having the wrong symmetry mode) is difficult due to having to reassign attachments.
                // So for now, they're being left at v0 until they're picked up and placed again.

                // var worldDelta = part.transform.TransformVector(part.srfAttachNode.position - prevPos);
                // part.attPos0 += worldDelta;
                // Fix the symmetry mode of panelArmor.cfg
                // if (!isTriangularPanel && part.symMethod == SymmetryMethod.Mirror && part.symmetryCounterparts.Count > 0)
                // {
                //     FixRectangularPanelSymmetry(part.symmetryCounterparts.First());
                // }
            }

            // Update any surface attached children.
            if (!duringStart && moveChildParts)
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
                {
                    pushTarget.transform.position += worldDelta;
                }
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
                    if (BDArmorySettings.ARMOR_ADJUSTING_THICKNESS_MOVES_CHILDREN && moveChildParts && Oldthickness > 0)
                    {
                        float offset = 0.5f * 0.01f * (armorthickness - Oldthickness); // armor.Armor is in mm.
                        foreach (var child in part.children)
                        {
                            if (child.attachMode != AttachModes.SRF_ATTACH) continue; // Only surface attached children are affected.
                            var srfAttachPoint = part.transform.InverseTransformPoint(child.transform.TransformPoint(child.srfAttachNode.position));
                            child.transform.position = part.transform.TransformPoint(new Vector3(
                                srfAttachPoint.x,
                                srfAttachPoint.y > 0 ? Mathf.Max(1e-4f, srfAttachPoint.y + offset) : Mathf.Min(-1e-4f, srfAttachPoint.y - offset), // Don't quite go to 0, so it stays on the same side if the thickness grows again.
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
            version = 1; // Upgrade version on placement.
            SetMatchParentVisibility();
            foreach (var symPart in part.symmetryCounterparts) // OnPartPlaced doesn't get triggered for the symmetric parts.
            {
                var symMod = symPart.GetComponent<BDAdjustableArmor>();
                symMod.version = 1;
                symMod.SetMatchParentVisibility();
            }
        }
        public void SetMatchParentVisibility()
        {
            Events[nameof(MatchParent)].guiActiveEditor = part.parent != null && part.attachMode == AttachModes.STACK && part.parent.GetComponent<BDAdjustableArmor>() != null;
        }

        /// <summary>
        /// Fix the symmetry issue for version 0 rectangular armor panels. 
        /// Rectangular panels didn't have the "applyMirrorRotationXAxis = false" property, so we being rotated in mirror symmetry mode instead of reflected.
        /// This causes a bunch of issues when trying to move child parts.
        /// version 1 panels don't have this problem.
        /// 
        /// Note: this doesn't fully work since I couldn't figure out how to swap the top and bottom stack nodes, but I'm leaving it here for future reference.
        /// </summary>
        /// <param name="symPart"></param>
        void FixRectangularPanelSymmetry(Part symPart)
        {
            // Position
            Vector3 prevPos = part.srfAttachNode.position;
            var worldDelta = part.transform.TransformVector(part.srfAttachNode.position - prevPos);
            part.attPos0 -= worldDelta;

            // Rotation
            Dictionary<Part, (Vector3, Quaternion)> childPosRot = symPart.children.ToDictionary(c => c, c => (c.transform.position, c.transform.rotation));
            symPart.attRotation0 *= new Quaternion(0, 0, 0, 1);
            symPart.transform.rotation *= new Quaternion(0, 0, 0, 1);
            foreach (var kvp in childPosRot)
            {
                kvp.Key.transform.SetPositionAndRotation(kvp.Value.Item1, kvp.Value.Item2);
            }

            // Swap children on top and bottom nodes. FIXME I can't see how to do this.

            var symMod = symPart.GetComponent<BDAdjustableArmor>();
            symMod.version = 1; // Flag the sym part as upgraded.
            symMod.SetMatchParentVisibility();
        }
    }
}