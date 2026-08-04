using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Linq;
using System;
using UnityEngine;

using BDArmory.Control;
using BDArmory.Settings;
using BDArmory.Targeting;
using BDArmory.UI;

namespace BDArmory.Utils
{
    public static class GUIUtils
    {
        public static Texture2D pixel;

        public static Camera GetMainCamera()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                return FlightCamera.fetch.mainCamera;
            }
            else
            {
                return Camera.main;
            }
        }

        public static void DrawTextureOnWorldPosFoV(Vector3 worldPos, Texture texture, float scaledFoV, float wobble)
        {
            var cam = GetMainCamera();
            if (cam == null) return;
            var guiMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            Vector3 screenPos = cam.WorldToViewportPoint(worldPos);
            if (screenPos.z < 0) return; //dont draw if point is behind camera
            if (screenPos.x != Mathf.Clamp01(screenPos.x)) return; //dont draw if off screen
            if (screenPos.y != Mathf.Clamp01(screenPos.y)) return;

            // Divide scaled FoV by cam FoV to get the % of the screen, and then multiply by screen width
            scaledFoV *= Screen.width / cam.fieldOfView;

            float xPos = screenPos.x * Screen.width - (0.5f * scaledFoV);
            float yPos = (1 - screenPos.y) * Screen.height - (0.5f * scaledFoV);
            if (wobble > 0)
            {
                xPos += UnityEngine.Random.Range(-wobble / 2, wobble / 2);
                yPos += UnityEngine.Random.Range(-wobble / 2, wobble / 2);
            }
            Rect iconRect = new Rect(xPos, yPos, scaledFoV, scaledFoV);

            GUI.DrawTexture(iconRect, texture);
            GUI.matrix = guiMatrix;
        }

        public static void DrawTextureOnWorldPos(Vector3 worldPos, Texture texture, Vector2 size, float wobble)
        {
            var cam = GetMainCamera();
            if (cam == null) return;
            var guiMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            Vector3 screenPos = cam.WorldToViewportPoint(worldPos);
            if (screenPos.z < 0) return; //dont draw if point is behind camera
            if (screenPos.x != Mathf.Clamp01(screenPos.x)) return; //dont draw if off screen
            if (screenPos.y != Mathf.Clamp01(screenPos.y)) return;
            float xPos = screenPos.x * Screen.width - (0.5f * size.x);
            float yPos = (1 - screenPos.y) * Screen.height - (0.5f * size.y);
            if (wobble > 0)
            {
                xPos += UnityEngine.Random.Range(-wobble / 2, wobble / 2);
                yPos += UnityEngine.Random.Range(-wobble / 2, wobble / 2);
            }
            Rect iconRect = new Rect(xPos, yPos, size.x, size.y);

            GUI.DrawTexture(iconRect, texture);
            GUI.matrix = guiMatrix;
        }

        public static void DrawTextureOnScreenPos(Vector3 screenPos, Texture texture, Vector2 size, float wobble)
        {
            var cam = GetMainCamera();
            if (cam == null) return;
            var guiMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            if (screenPos.z < 0) return; //dont draw if point is behind camera
            if (screenPos.x != Mathf.Clamp01(screenPos.x)) return; //dont draw if off screen
            if (screenPos.y != Mathf.Clamp01(screenPos.y)) return;
            float xPos = screenPos.x * Screen.width - (0.5f * size.x);
            float yPos = (1 - screenPos.y) * Screen.height - (0.5f * size.y);
            if (wobble > 0)
            {
                xPos += UnityEngine.Random.Range(-wobble / 2, wobble / 2);
                yPos += UnityEngine.Random.Range(-wobble / 2, wobble / 2);
            }
            Rect iconRect = new Rect(xPos, yPos, size.x, size.y);

            GUI.DrawTexture(iconRect, texture);
            GUI.matrix = guiMatrix;
        }

        public static void DrawLabelOnWorldPos(Vector3 worldPos, string label, Vector2 size)
        {
            var cam = GetMainCamera();
            if (cam == null) return;
            var guiMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            Vector3 screenPos = cam.WorldToViewportPoint(worldPos);
            if (screenPos.z < 0) return; //dont draw if point is behind camera
            if (screenPos.x != Mathf.Clamp01(screenPos.x)) return; //dont draw if off screen
            if (screenPos.y != Mathf.Clamp01(screenPos.y)) return;
            float xPos = screenPos.x * Screen.width - (0.5f * size.x);
            float yPos = (1 - screenPos.y) * Screen.height - (0.5f * size.y);
            Rect iconRect = new Rect(xPos, yPos, size.x, size.y);

            GUI.Label(iconRect, label);
            GUI.matrix = guiMatrix;
        }

        public static bool WorldToGUIPos(Vector3 worldPos, out Vector2 guiPos)
        {
            var cam = GetMainCamera();
            if (cam == null)
            {
                guiPos = Vector2.zero;
                return false;
            }
            Vector3 screenPos = cam.WorldToViewportPoint(worldPos);
            bool offScreen = false;
            if (screenPos.z < 0) offScreen = true; //dont draw if point is behind camera
            if (screenPos.x != Mathf.Clamp01(screenPos.x)) offScreen = true; //dont draw if off screen
            if (screenPos.y != Mathf.Clamp01(screenPos.y)) offScreen = true;
            if (!offScreen)
            {
                float xPos = screenPos.x * Screen.width;
                float yPos = (1 - screenPos.y) * Screen.height;
                guiPos = new Vector2(xPos, yPos);
                return true;
            }
            else
            {
                guiPos = Vector2.zero;
                return false;
            }
        }

        public static Vector3 WorldToViewportPoint(Vector3 worldPos)
        {
            var cam = GetMainCamera();
            if (cam == null)
            {
                return Vector2.zero;
            }
            return cam.WorldToViewportPoint(worldPos);
        }

        public static void DrawLineBetweenWorldPositions(Vector3 worldPosA, Vector3 worldPosB, float width, Color color)
        {
            Camera cam = GetMainCamera();

            if (cam == null) return;

            var guiMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;

            bool aBehind = false;

            Plane clipPlane = new Plane(cam.transform.forward, cam.transform.position + cam.transform.forward * 0.05f);

            if (Vector3.Dot(cam.transform.forward, worldPosA - cam.transform.position) < 0)
            {
                Ray ray = new Ray(worldPosB, worldPosA - worldPosB);
                float dist;
                if (clipPlane.Raycast(ray, out dist))
                {
                    worldPosA = ray.GetPoint(dist);
                }
                aBehind = true;
            }
            if (Vector3.Dot(cam.transform.forward, worldPosB - cam.transform.position) < 0)
            {
                if (aBehind) return;

                Ray ray = new Ray(worldPosA, worldPosB - worldPosA);
                float dist;
                if (clipPlane.Raycast(ray, out dist))
                {
                    worldPosB = ray.GetPoint(dist);
                }
            }

            Vector3 screenPosA = cam.WorldToViewportPoint(worldPosA);
            screenPosA.x = screenPosA.x * Screen.width;
            screenPosA.y = (1 - screenPosA.y) * Screen.height;
            Vector3 screenPosB = cam.WorldToViewportPoint(worldPosB);
            screenPosB.x = screenPosB.x * Screen.width;
            screenPosB.y = (1 - screenPosB.y) * Screen.height;

            screenPosA.z = screenPosB.z = 0;

            float angle = Vector2.Angle(Vector3.up, screenPosB - screenPosA);
            if (screenPosB.x < screenPosA.x)
            {
                angle = -angle;
            }

            Vector2 vector = screenPosB - screenPosA;
            float length = vector.magnitude;

            Rect upRect = new Rect(screenPosA.x - (width / 2), screenPosA.y - length, width, length);

            GUIUtility.RotateAroundPivot(-angle + 180, screenPosA);
            DrawRectangle(upRect, color);
            GUI.matrix = guiMatrix;
        }

        public static void DrawRectangle(Rect rect, Color color)
        {
            if (pixel == null)
            {
                pixel = new Texture2D(1, 1);
            }

            Color originalColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, pixel);
            GUI.color = originalColor;
        }

        public static void MarkPosition(Transform transform, Color color) => MarkPosition(transform.position, transform, color);

        public static void MarkPosition(Vector3 position, Transform transform, Color color, float size = 3, float thickness = 2)
        {
            DrawLineBetweenWorldPositions(position + transform.right * size, position - transform.right * size, thickness, color);
            DrawLineBetweenWorldPositions(position + transform.up * size, position - transform.up * size, thickness, color);
            DrawLineBetweenWorldPositions(position + transform.forward * size, position - transform.forward * size, thickness, color);
        }

        public static void UseMouseEventInRect(Rect rect)
        {
            if (Event.current == null) return;
            if (MouseIsInRect(rect) && ((Event.current.isMouse && Event.current.type == EventType.MouseDown) || Event.current.isScrollWheel)) // Don't consume MouseUp events as multiple windows should use these.
            {
                Event.current.Use();
            }
        }

        /// <summary>
        /// Lock the model if our own window is shown and has cursor focus to prevent click-through.
        /// Code adapted from FAR Editor GUI
        /// Only valid in an editor.
        /// Use forceUnlock to unlock the lockID when hiding a window or when the behaviour is destroyed to avoid leaving orphaned locks.
        /// </summary>
        public static void PreventClickThrough(Rect rect, string lockID, bool forceUnlock = false)
        {
            EditorLogic EdLogInstance = EditorLogic.fetch;
            if (!EdLogInstance) return;
            if (forceUnlock)
            {
                EdLogInstance.Unlock(lockID);
                return;
            }
            if (MouseIsInRect(rect))
            {
                if (!CameraMouseLook.GetMouseLook())
                    EdLogInstance.Lock(false, false, false, lockID);
                else
                    EdLogInstance.Unlock(lockID);
            }
            else
            {
                EdLogInstance.Unlock(lockID);
            }
        }

        public static Rect CleanRectVals(Rect rect)
        {
            // Remove decimal places so Mac does not complain.
            rect.x = (int)rect.x;
            rect.y = (int)rect.y;
            rect.width = (int)rect.width;
            rect.height = (int)rect.height;
            return rect;
        }

        /// <summary>
        /// Reposition the window, taking care to keep the window on the screen and attached to screen edges (if strict boundaries).
        /// </summary>
        /// <param name="windowRect">The window rect.</param>
        /// <param name="previousWindowHeight">The previous height of the window, for auto-sizing windows.</param>
        internal static void RepositionWindow(ref Rect windowRect, float previousWindowHeight = 0)
        {
            var scaledWindowSize = BDArmorySettings.UI_SCALE_ACTUAL * windowRect.size;
            if (BDArmorySettings.STRICT_WINDOW_BOUNDARIES)
            {
                if ((windowRect.height < previousWindowHeight && Mathf.RoundToInt(windowRect.y + BDArmorySettings.UI_SCALE_ACTUAL * previousWindowHeight) >= Screen.height) || // Window shrunk while being at the bottom of screen.
                    (BDArmorySettings.PREVIOUS_UI_SCALE > BDArmorySettings.UI_SCALE_ACTUAL && Mathf.RoundToInt(windowRect.y + BDArmorySettings.PREVIOUS_UI_SCALE * windowRect.height) >= Screen.height))
                    windowRect.y = Screen.height - scaledWindowSize.y;
                if (BDArmorySettings.PREVIOUS_UI_SCALE > BDArmorySettings.UI_SCALE_ACTUAL && Mathf.RoundToInt(windowRect.x + BDArmorySettings.PREVIOUS_UI_SCALE * windowRect.width) >= Screen.width) // Window shrunk while being at the right of screen.
                    windowRect.x = Screen.width - scaledWindowSize.x;

                // This method uses Gui point system.
                if (windowRect.x < 0) windowRect.x = 0;
                if (windowRect.y < 0) windowRect.y = 0;

                if (windowRect.x + scaledWindowSize.x > Screen.width) // Don't go off the right of the screen.
                    windowRect.x = Screen.width - scaledWindowSize.x;
                if (scaledWindowSize.y > Screen.height) // Don't go off the top of the screen.
                    windowRect.y = 0;
                else if (windowRect.y + scaledWindowSize.y > Screen.height) // Don't go off the bottom of the screen.
                    windowRect.y = Screen.height - scaledWindowSize.y;
            }
            else // If the window is completely off-screen, bring it just onto the screen.
            {
                if (windowRect.width == 0) windowRect.width = 1;
                if (windowRect.height == 0) windowRect.height = 1;
                if (windowRect.x >= Screen.width) windowRect.x = Screen.width - 1;
                if (windowRect.y >= Screen.height) windowRect.y = Screen.height - 1;
                if (windowRect.x + scaledWindowSize.x < 1) windowRect.x = 1 - scaledWindowSize.x;
                if (windowRect.y + scaledWindowSize.y < 1) windowRect.y = 1 - scaledWindowSize.y;
            }
            GUIUtilsInstance.Reset(); // Reset once-per-frame checks.
        }

        internal static Rect GuiToScreenRect(Rect rect)
        {
            // Must run during OnGui to work...
            Rect newRect = new Rect
            {
                position = GUIUtility.GUIToScreenPoint(rect.position),
                width = rect.width,
                height = rect.height
            };
            return newRect;
        }

        public static Texture2D resizeTexture
        {
            get
            {
                if (_resizeTexture == null) _resizeTexture = GameDatabase.Instance.GetTexture(BDArmorySetup.textureDir + "resizeSquare", false);
                return _resizeTexture;
            }
        }
        static Texture2D _resizeTexture;

        public static Color ParseColor255(string color)
        {
            Color outputColor = new Color(0, 0, 0, 1);

            string[] strings = color.Split(","[0]);
            for (int i = 0; i < 4; i++)
            {
                outputColor[i] = Mathf.Clamp01(float.Parse(strings[i]) / 255);
            }

            return outputColor;
        }

        public static AnimationState[] SetUpAnimation(string animationName, Part part, bool animatePhysics = true) //Thanks Majiir!
        {
            List<AnimationState> states = new List<AnimationState>();
            using (IEnumerator<UnityEngine.Animation> animation = part.FindModelAnimators(animationName).AsEnumerable().GetEnumerator())
                while (animation.MoveNext())
                {
                    if (animation.Current == null) continue;
                    animation.Current.animatePhysics = animatePhysics;
                    AnimationState animationState = animation.Current[animationName];
                    animationState.speed = 0; // FIXME Shouldn't this be 1?
                    animationState.enabled = true;
                    animationState.wrapMode = WrapMode.ClampForever;
                    animation.Current.Blend(animationName);
                    states.Add(animationState);
                }
            return states.ToArray();
        }

        public static AnimationState SetUpSingleAnimation(string animationName, Part part, bool animatePhysics = true)
        {
            using (IEnumerator<UnityEngine.Animation> animation = part.FindModelAnimators(animationName).AsEnumerable().GetEnumerator())
                while (animation.MoveNext())
                {
                    if (animation.Current == null) continue;
                    animation.Current.animatePhysics = animatePhysics;
                    AnimationState animationState = animation.Current[animationName];
                    animationState.speed = 0; // FIXME Shouldn't this be 1?
                    animationState.enabled = true;
                    animationState.wrapMode = WrapMode.ClampForever;
                    animation.Current.Blend(animationName);
                    return animationState;
                }
            return null;
        }

        public static bool CheckMouseIsOnGui()
        {
            if (!BDArmorySetup.GAME_UI_ENABLED) return false;

            if (!BDInputSettingsFields.WEAP_FIRE_KEY.inputString.Contains("mouse")) return false;

            if (ModIntegration.MouseAimFlight.IsMouseAimActive) return false;

            return GUIUtilsInstance.fetch.MouseIsOnGUI;
        }

        static bool _CheckMouseIsOnGui()
        {
            Vector3 inverseMousePos = new Vector3(Input.mousePosition.x, Screen.height - Input.mousePosition.y, 0);
            Rect topGui = new Rect(0, 0, Screen.width, 65);

            if (MouseIsInRect(topGui, inverseMousePos)) return true;
            if (BDArmorySetup.windowBDAToolBarEnabled && MouseIsInRect(BDArmorySetup.WindowRectToolbar, inverseMousePos))
                return true;
            if (ModuleTargetingCamera.windowIsOpen && MouseIsInRect(BDArmorySetup.WindowRectTargetingCam, inverseMousePos))
                return true;
            MissileFire wm = BDArmorySetup.Instance.OnGUIWM;
            if (wm != null)
            {
                if (wm.vesselRadarData && wm.vesselRadarData.guiEnabled)
                {
                    if (MouseIsInRect(BDArmorySetup.WindowRectRadar, inverseMousePos)) return true;
                    if (wm.vesselRadarData.linkWindowOpen && MouseIsInRect(wm.vesselRadarData.linkWindowRect, inverseMousePos))
                        return true;
                }
                if (wm.rwr && wm.rwr.rwrEnabled && wm.rwr.displayRWR && MouseIsInRect(BDArmorySetup.WindowRectRwr, inverseMousePos))
                    return true;
            }
            if (extraGUIRects != null)
            {
                foreach (var guiRect in extraGUIRects.Values)
                {
                    if (!guiRect.visible) continue;
                    if (MouseIsInRect(guiRect.rect, inverseMousePos)) return true;
                }
            }

            return false;
        }

        public static void ResizeGuiWindow(Rect windowrect, Vector2 mousePos)
        {
            GUIUtilsInstance.Reset();
        }

        /// <summary>
        /// Only do GUI.DragWindow on the left mouse button.
        /// </summary>
        public static void DragWindow()
        {
            if (Event.current.button != 0) return;
            GUI.DragWindow();
        }

        public class ExtraGUIRect
        {
            public ExtraGUIRect(Rect rect) { this.rect = rect; }
            public bool visible = false;
            public Rect rect;
        }
        public static Dictionary<int, ExtraGUIRect> extraGUIRects;

        public static int RegisterGUIRect(Rect rect)
        {
            if (extraGUIRects == null)
            {
                extraGUIRects = new Dictionary<int, ExtraGUIRect>();
            }

            int index = extraGUIRects.Count;
            extraGUIRects.Add(index, new ExtraGUIRect(rect));
            GUIUtilsInstance.Reset();
            return index;
        }

        public static void UpdateGUIRect(Rect rect, int index)
        {
            if (extraGUIRects == null || !extraGUIRects.ContainsKey(index)) return;
            extraGUIRects[index].rect = rect;
            GUIUtilsInstance.Reset();
        }

        public static void SetGUIRectVisible(int index, bool visible)
        {
            if (extraGUIRects == null || !extraGUIRects.ContainsKey(index)) return;
            extraGUIRects[index].visible = visible;
            GUIUtilsInstance.Reset();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool MouseIsInRect(Rect rect)
        {
            Vector2 inverseMousePos = new(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return MouseIsInRect(rect, inverseMousePos);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool MouseIsInRect(Rect rect, Vector2 inverseMousePos)
        {
            Rect scaledRect = new(rect.position, BDArmorySettings.UI_SCALE_ACTUAL * rect.size);
            return scaledRect.Contains(inverseMousePos);
        }

        #region PAW
        // Notes:
        // The previous "RefreshAssociatedWindows(Part part)" function was just calling "part.PartActionWindow.UpdateWindow()", which is done every frame by KSP anyway, making this redundant.
        // The other variant was setting "part.PartActionWindow.displayDirty = true", which causes a full rebuild of the PAW and breaks continuous interaction with sliders, so avoid it unless absolutely necessary.

        /// <summary>
        /// Refresh the UI for the given resource in the PAW.
        /// </summary>
        /// <param name="part">The part.</param>
        /// <param name="resource">The resource to refresh.</param>
        /// <param name="all">Stop after the first found matching resource or look for multiple matching resources (not sure if a PAW can have more than 1).</param>
        public static void RefreshPAWResource(Part part, PartResource resource, bool all = false)
        {
            var PAW = part.PartActionWindow;
            if (PAW == null) return;
            foreach (var uiPartActionItem in PAW.ListItems)
            {
                if (uiPartActionItem is not UIPartActionResourceEditor) continue;
                var resourceUI = uiPartActionItem as UIPartActionResourceEditor;
                if (resourceUI.Resource != resource) continue;
                resourceUI.resourceMax.text = KSPUtil.LocalizeNumber(resource.maxAmount, "F1");
                resourceUI.resourceAmnt.text = KSPUtil.LocalizeNumber(resource.amount, "F1");
                if (resourceUI.Window.NumericSliders)
                {
                    resourceUI.inputField.text = KSPUtil.LocalizeNumber(resource.amount, "#.0##");
                }
                else
                {
                    // Note: we set the value without triggering callbacks as that is what KSP does to round the slider to a nearest position, which we don't want.
                    resourceUI.slider.SetValueWithoutNotify(Mathf.Lerp(resourceUI.slider.minValue, resourceUI.slider.maxValue, (float)(resource.amount / resource.maxAmount)));
                }
                if (!all) break;
            }
        }

        /// <summary>
        /// Helper method to avoid having to specify the PartModule type.
        /// </summary>
        /// <typeparam name="T">The subclass Type of the PartModule.</typeparam>
        /// <param name="partModule">The PartModule for the field.</param>
        /// <param name="field">The field being updated.</param>
        /// <param name="obj">The old value.</param>
        public static void UpdateChooseOptionPAW<T>(this T _, BaseField field, object obj = null) where T : PartModule => UpdateChooseOptionPAW<T>(field, obj);

        /// <summary>
        /// Update a UI_ChooseOption PAW slider when the field value is changed.
        /// KSP updates most sliders automatically, but not these apparently.
        /// </summary>
        /// <typeparam name="T">The type of PartModule.</typeparam>
        /// <param name="field">The field to update. If field is null, then all UI_ChooseOption fields are updated if obj is a PartModule.</param>
        /// <param name="obj">The value of the control.</param>
        /// <param name="updateSymmetric">Update the symmetric parts too.</param>
        public static void UpdateChooseOptionPAW<T>(BaseField field, object obj = null, bool updateSymmetric = true) where T : PartModule
        {
            if (field is null)
            {
                if (obj is not PartModule pm) return;
                foreach (var f in pm.Fields)
                {
                    if ((HighLogic.LoadedSceneIsFlight ? f.uiControlFlight : f.uiControlEditor) is UI_ChooseOption)
                        UpdateChooseOptionPAW<T>(f, null);
                }
                return;
            }
            if (field.host is not PartModule partModule) return; // Not a field of a PartModule
            if ((HighLogic.LoadedSceneIsFlight ? field.uiControlFlight : field.uiControlEditor) is not UI_ChooseOption uiControl) return; // Wrong UI_Control type
            if (uiControl.partActionItem is not UIPartActionChooseOption pawChooseOption) return; // Not shown
            var value = (string)field.GetValue(partModule);
            int newIndex = uiControl.options.IndexOf(value);
            if (newIndex == -1)
            {
                Debug.LogWarning($"[BDArmory.GUIUtils]: Invalid value {value} for {field.guiName} ({field.name}) on {partModule.part}");
                return;
            }

            // Debug.Log($"DEBUG Updating ChooseOptionPAW of {field.guiName} ({field.name}) on {partModule.part.persistentId} of type {typeof(T).Name} to {value}, index {pawChooseOption.slider.value}->{newIndex}");
            bool changed = pawChooseOption.slider.value != newIndex;
            pawChooseOption.slider.value = newIndex; // Set the value even if it hasn't changed to trigger the slider callback.

            // When set externally (e.g., from symmetry or the AI GUI) UI_ChooseOption sets the field value (but not the slider) without triggering onFieldChanged!
            // Thus we have to invoke it here in order for it to trigger the onFieldChanged handlers.
            // Invoking it here may lead to an extra loop or two of this function, but shouldn't recurse further unless handlers on symmetric parts are behaving asymmetricallyn.
            // It shouldn't trigger onFieldChanged more than once per part module.
            if (changed && !updateSymmetric && uiControl.onFieldChanged != null) uiControl.onFieldChanged.Invoke(field, obj);
            if (updateSymmetric) foreach (Part sym in partModule.part.symmetryCounterparts)
            {
                // Debug.Log($"DEBUG Updating symmetric part {sym.persistentId} of {partModule.part.persistentId}");
                Type fieldHostType = field.host.GetType();
                foreach (T pm in sym.GetComponents<T>())
                {
                    if (pm.GetType() != fieldHostType) continue; // Wrong PartModule.
                    List<string> debugString = [];
                    foreach (var f in pm.Fields) debugString.Add(f.name);
                    UpdateChooseOptionPAW<T>(pm.Fields[field.name], obj, false); // We need to use the field on the symmetric PartModule, otherwise the wrong UI_Control is grabbed.
                    break;
                }
            }
        }

        /// <summary>
        /// Helper method to avoid having to specify the PartModule type.
        /// </summary>
        /// <typeparam name="T">The subclass Type of the PartModule.</typeparam>
        /// <param name="partModule">The PartModule for the field.</param>
        /// <param name="field">The field being updated.</param>
        /// <param name="obj">The old value.</param>
        public static void UpdateToggle<T>(this T _, BaseField field, object obj = null) where T : PartModule => UpdateToggle<T>(field, obj);

        /// <summary>
        /// Setting a UI_Toggle value externally (e.g., from symmetry or the AI GUI) doesn't trigger the onFieldChanged or onToggle handlers.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="field"></param>
        /// <param name="partModule"></param>
        /// <param name="updateSymmetric"></param>
        public static void UpdateToggle<T>(BaseField field, object obj, bool updateSymmetric = true) where T : PartModule
        {
            if (field is null || field.host is not PartModule partModule) return;
            if ((HighLogic.LoadedSceneIsFlight ? field.uiControlFlight : field.uiControlEditor) is not UI_Toggle uiControl) return;
            if (uiControl.partActionItem is not UIPartActionToggle pawToggle) return;
            var value = (bool)field.GetValue(partModule);
            if (pawToggle.toggle.state != value)
            {
                // Debug.Log($"DEBUG Updating Toggle of {field.guiName} ({field.name}) on {partModule.part.persistentId} of type {typeof(T).Name}, {pawToggle.toggle.state}->{value}");
                pawToggle.toggle.SetState(value);
                pawToggle.toggle.onToggle.Invoke();
                if (value) pawToggle.toggle.onToggleOn.Invoke();
                else pawToggle.toggle.onToggleOff.Invoke();
                if (!updateSymmetric && uiControl.onFieldChanged != null) uiControl.onFieldChanged.Invoke(field, obj);
            }
            if (updateSymmetric) foreach (Part sym in partModule.part.symmetryCounterparts)
            {
                Type fieldHostType = field.host.GetType();
                foreach (T pm in sym.GetComponents<T>())
                {
                    if (pm.GetType() != fieldHostType) continue;
                    UpdateToggle<T>(pm.Fields[field.name], obj, false);
                    break;
                }
            }
        }

        /// <summary>
        /// Add the default symmetry-propagating handler for other UI_ChooseOption fields.
        /// 
        /// Note: UI_ChooseOption only affects symmetry parts by default in the editor.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="partModule"></param>
        public static void SetDefaultChooseOptionHandlers<T>(this T partModule) where T : PartModule
        {
            foreach (var field in partModule.Fields)
            {
                if ((HighLogic.LoadedSceneIsFlight ? field.uiControlFlight : field.uiControlEditor) is not UI_ChooseOption uiControl) continue;
                // if (uiControl.onFieldChanged == null) Debug.Log($"DEBUG Setting onFieldChanged handler for UI_ChooseOption {field.guiName}");
                if (uiControl.affectSymCounterparts switch
                {
                    UI_Scene.All => true,
                    UI_Scene.Editor when HighLogic.LoadedSceneIsEditor => true,
                    UI_Scene.Flight when HighLogic.LoadedSceneIsFlight => true,
                    _ => false
                })
                    uiControl.onFieldChanged ??= DefaultChooseOptionHandler<T>;
            }
        }
        static void DefaultChooseOptionHandler<T>(BaseField field, object obj) where T : PartModule
        {
            if (field is null || obj is null) return;
            if (field.host is not PartModule pm) return;
            // Debug.Log($"DEBUG Handling onFieldChanged for {field.guiName} on {field.host} ({pm.part.persistentId}) with obj: {obj}");
            UpdateChooseOptionPAW<T>(field, obj);
            if (pm is BDGenericAIBase && BDArmoryAIGUI.Instance != null && BDArmoryAIGUI.Instance.ActiveAI == pm)
            {
                BDArmoryAIGUI.Instance.SetChooseOptionSliders();
            }
        }
        /// <summary>
        /// Helper method to avoid having to specify the PartModule type.
        /// </summary>
        /// <typeparam name="T">The subclass Type of the PartModule.</typeparam>
        /// <param name="partModule">The PartModule for the field.</param>
        /// <param name="field">The field being updated.</param>
        /// <param name="obj">The old value.</param>
        public static void DefaultChooseOptionHandler<T>(this T _, BaseField field, object obj = null) where T : PartModule => DefaultChooseOptionHandler<T>(field, obj);

        /// <summary>
        /// Set any unhandled UI_Toggle to sync with symmetric PAWs.
        /// Custom handlers should make sure to end by calling GUIUtils.UpdateToggle(field, obj);
        /// 
        /// Note: UI_Toggle only affects symmetry parts by default in the editor.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="partModule"></param>
        public static void SetDefaultToggleHanders<T>(this T partModule) where T : PartModule
        {
            foreach (var field in partModule.Fields)
            {
                if ((HighLogic.LoadedSceneIsFlight ? field.uiControlFlight : field.uiControlEditor) is not UI_Toggle uiControl) continue;
                // Debug.Log($"DEBUG Setting onFieldChanged handler for UI_Toggle {field.guiName}");
                if (uiControl.affectSymCounterparts switch
                {
                    UI_Scene.All => true,
                    UI_Scene.Editor when HighLogic.LoadedSceneIsEditor => true,
                    UI_Scene.Flight when HighLogic.LoadedSceneIsFlight => true,
                    _ => false
                })
                    uiControl.onFieldChanged ??= DefaultToggleHandler<T>;
            }
        }
        /// <summary>
        /// Handle a PartModule toggle being toggled.
        /// </summary>
        /// <typeparam name="T">The type of PartModule.</typeparam>
        /// <param name="field">The field for the toggle.</param>
        /// <param name="obj">The old value.</param>
        static void DefaultToggleHandler<T>(BaseField field, object obj) where T : PartModule
        {
            if (field.host is not PartModule pm) return;
            // Debug.Log($"DEBUG Handling onFieldChanged for {field.guiName} on {field.host} ({pm.part.persistentId}) with obj: {obj}");
            UpdateToggle<T>(field, obj);
        }
        /// <summary>
        /// Helper method to avoid having to specify the PartModule type.
        /// </summary>
        /// <typeparam name="T">The subclass Type of the PartModule.</typeparam>
        /// <param name="partModule">The PartModule for the field.</param>
        /// <param name="field">The field being updated.</param>
        /// <param name="obj">The old value.</param>
        public static void DefaultToggleHandler<T>(this T _, BaseField field, object obj = null) where T : PartModule => DefaultToggleHandler<T>(field, obj);
        #endregion PAW       

        /// <summary>
        /// Disable zooming with the scroll wheel if the mouse is over a registered GUI window.
        /// </summary>
        public static void SetScrollZoom()
        {
            if (CheckMouseIsOnGui()) BeginDisableScrollZoom();
            else EndDisableScrollZoom();
        }
        static bool scrollZoomEnabled = true;
        static float originalScrollRate = 1;
        static bool _originalScrollRateSet = false;
        public static void BeginDisableScrollZoom()
        {
            if (!scrollZoomEnabled || !BDArmorySettings.SCROLL_ZOOM_PREVENTION) return;
            if (!_originalScrollRateSet)
            {
                originalScrollRate = GameSettings.AXIS_MOUSEWHEEL.primary.scale; // Get the original scroll rate once.
                if (originalScrollRate == 0)
                {
                    Debug.LogWarning($"[BDArmory.GUIUtils]: Original scroll rate was 0, resetting it to 1.");
                    originalScrollRate = 1; // Sometimes it's getting set to 0 for some reason. Default it back to 1.
                }
                _originalScrollRateSet = true;
            }
            GameSettings.AXIS_MOUSEWHEEL.primary.scale = 0;
            scrollZoomEnabled = false;
        }
        public static void EndDisableScrollZoom()
        {
            if (scrollZoomEnabled) return;
            if (_originalScrollRateSet)
                GameSettings.AXIS_MOUSEWHEEL.primary.scale = originalScrollRate;
            scrollZoomEnabled = true;
        }
        /// <summary>
        /// Reset the scroll rate to 1.
        /// </summary>
        public static void ResetScrollRate()
        {
            EndDisableScrollZoom();
            originalScrollRate = 1;
            GameSettings.AXIS_MOUSEWHEEL.primary.scale = originalScrollRate;
            _originalScrollRateSet = true;
            scrollZoomEnabled = true;
        }

        /// <summary>
        /// GUILayout TextField with a grey placeholder string.
        /// </summary>
        /// <param name="text">The current text.</param>
        /// <param name="placeholder">A placeholder text for when 'text' is empty.</param>
        /// <param name="fieldName">An internal name for the field so it can be reference with, for example, GUI.FocusControl.</param>
        /// <param name="rect">If specified, then GUI.TextField is used with the specified Rect, otherwise a GUILayout is used.</param>
        /// <returns>The current text.</returns>
        public static string TextField(string text, string placeholder, string fieldName = null, Rect rect = default)
        {
            bool isGUILayout = rect == default;
            if (fieldName != null) GUI.SetNextControlName(fieldName);
            var newText = isGUILayout ? GUILayout.TextField(text) : GUI.TextField(rect, text);
            if (string.IsNullOrEmpty(text))
            {
                var guiColor = GUI.color;
                GUI.color = Color.grey;
                GUI.Label(isGUILayout ? GUILayoutUtility.GetLastRect() : rect, placeholder);
                GUI.color = guiColor;
            }
            return newText;
        }

        /// <summary>
        /// Wrapper for HorizontalSlider for UI_FloatSemiLogRange fields.
        /// </summary>
        /// <param name="rect"></param>
        /// <param name="value"></param>
        /// <param name="minValue"></param>
        /// <param name="maxValue"></param>
        /// <param name="sigFig"></param>
        /// <param name="withZero"></param>
        /// <param name="reducedPrecisionAtMin"></param>
        /// <param name="cache">A cache of tuples to avoid needlessly recalculating semi-log values. Can initially be null.</param>
        /// <returns></returns>
        public static float HorizontalSemiLogSlider(Rect rect, float value, float minValue, float maxValue, float sigFig, bool withZero, bool reducedPrecisionAtMin, ref (float, float)[] cache)
        {
            if (cache == null || cache.Length != 4)
            {
                cache = [
                    (value, UI_FloatSemiLogRange.ToSliderValue(value, minValue, sigFig, withZero, reducedPrecisionAtMin)), // Current slider value
                    (minValue, UI_FloatSemiLogRange.ToSliderValue(withZero ? 0 : minValue, minValue, sigFig, withZero, reducedPrecisionAtMin)), // Min slider value
                    (maxValue, UI_FloatSemiLogRange.ToSliderValue(maxValue, minValue, sigFig, withZero, reducedPrecisionAtMin)), // Max slider value
                    (sigFig, Mathf.Pow(10f, 1 - sigFig)) // Slider rounding
                ];
            }
            else
            {
                if (value != cache[0].Item1) cache[0] = (value, UI_FloatSemiLogRange.ToSliderValue(value, minValue, sigFig, withZero, reducedPrecisionAtMin));
                if (minValue != cache[1].Item1) cache[1] = (minValue, UI_FloatSemiLogRange.ToSliderValue(withZero ? 0 : minValue, minValue, sigFig, withZero, reducedPrecisionAtMin));
                if (maxValue != cache[2].Item1) cache[2] = (maxValue, UI_FloatSemiLogRange.ToSliderValue(maxValue, minValue, sigFig, withZero, reducedPrecisionAtMin));
                if (sigFig != cache[3].Item1) cache[3] = (sigFig, Mathf.Pow(10f, 1 - sigFig));
            }
            float sliderValue = cache[0].Item2;
            float sliderRounding = cache[3].Item2;
            if (sliderValue != (sliderValue = BDAMath.RoundToUnit(GUI.HorizontalSlider(rect, sliderValue, cache[1].Item2, cache[2].Item2), sliderRounding)))
            {
                cache[0] = (value, sliderValue);
                return UI_FloatSemiLogRange.FromSliderValue(sliderValue, minValue, sigFig, withZero, reducedPrecisionAtMin);
            }
            else return value;
        }

        /// <summary>
        /// Wrapper for HorizontalSlider for UI_FloatLogRange fields.
        /// </summary>
        /// <param name="rect"></param>
        /// <param name="value"></param>
        /// <param name="minValue"></param>
        /// <param name="maxValue"></param>
        /// <param name="sigFig"></param>
        /// <param name="withZero"></param>
        /// <param name="reducedPrecisionAtMin"></param>
        /// <param name="cache">A cache of tuples to avoid needlessly recalculating log values. Can initially be null.</param>
        /// <returns></returns>
        public static float HorizontalFloatLogSlider(Rect rect, float value, float minValue, float maxValue, int steps, ref (float, float)[] cache)
        {
            if (cache == null || cache.Length != 3)
            {
                cache = [
                    (value, UI_FloatLogRange.ToSliderValue(value, minValue, maxValue, steps)),
                    (minValue, UI_FloatLogRange.ToSliderValue(0, minValue, maxValue, steps)),
                    (maxValue, UI_FloatLogRange.ToSliderValue(maxValue, minValue, maxValue, steps))
                ];
            }
            else
            {
                if (value != cache[0].Item1) cache[0] = (value, UI_FloatLogRange.ToSliderValue(value, minValue, maxValue, steps));
                if (minValue != cache[1].Item1) cache[1] = (minValue, UI_FloatLogRange.ToSliderValue(0, minValue, maxValue, steps));
                if (maxValue != cache[2].Item1) cache[2] = (maxValue, UI_FloatLogRange.ToSliderValue(maxValue, minValue, maxValue, steps));
            }
            float sliderValue = cache[0].Item2;
            if (sliderValue != (sliderValue = GUI.HorizontalSlider(rect, sliderValue, cache[1].Item2, cache[2].Item2)))
            {
                cache[0] = (value, sliderValue);
                return UI_FloatLogRange.FromSliderValue(sliderValue, minValue, maxValue, steps);
            }
            else return value;
        }

        /// <summary>
        /// Wrapper for HorizontalSlider for UI_FloatPowerRange fields.
        /// </summary>
        /// <param name="rect"></param>
        /// <param name="value"></param>
        /// <param name="minValue"></param>
        /// <param name="maxValue"></param>
        /// <param name="power"></param>
        /// <param name="sigFig"></param>
        /// <param name="cache">A cache of tuples to avoid needlessly recalculating power values. Can initially be null.</param>
        /// <returns></returns>
        public static float HorizontalPowerSlider(Rect rect, float value, float minValue, float maxValue, float power, int sigFig, ref (float, float)[] cache)
        {
            if (cache == null || cache.Length != 3)
            {
                cache = [
                    (value, UI_FloatPowerRange.ToSliderValue(value, power)),
                    (minValue, UI_FloatPowerRange.ToSliderValue(minValue, power)),
                    (maxValue, UI_FloatPowerRange.ToSliderValue(maxValue, power))
                ];
            }
            else
            {
                if (value != cache[0].Item1) cache[0] = (value, UI_FloatPowerRange.ToSliderValue(value, power));
                if (minValue != cache[1].Item1) cache[1] = (minValue, UI_FloatPowerRange.ToSliderValue(minValue, power));
                if (maxValue != cache[2].Item1) cache[2] = (maxValue, UI_FloatPowerRange.ToSliderValue(maxValue, power));
            }
            float sliderValue = cache[0].Item2;
            if (sliderValue != (sliderValue = GUI.HorizontalSlider(rect, sliderValue, cache[1].Item2, cache[2].Item2)))
            {
                cache[0] = (value, sliderValue);
                return UI_FloatPowerRange.FromSliderValue(sliderValue, power, sigFig, maxValue);
            }
            else return value;
        }

        [KSPAddon(KSPAddon.Startup.EveryScene, false)]
        internal class GUIUtilsInstance : MonoBehaviour
        {
            public bool MouseIsOnGUI
            {
                get
                {
                    if (!_mouseIsOnGUICheckedThisFrame)
                    {
                        _mouseIsOnGUI = _CheckMouseIsOnGui();
                        _mouseIsOnGUICheckedThisFrame = true;
                    }
                    return _mouseIsOnGUI;
                }
            }
            bool _mouseIsOnGUI = false;
            bool _mouseIsOnGUICheckedThisFrame = false;

            public static GUIUtilsInstance fetch;
            void Awake()
            {
                if (fetch != null) Destroy(this);
                fetch = this;
            }

            void Update()
            {
                _mouseIsOnGUICheckedThisFrame = false;
            }

            void LateUpdate()
            {
                SetScrollZoom();
            }

            public static void Reset()
            {
                if (fetch == null) return;
                fetch.Update();
            }

            void Destroy()
            {
                EndDisableScrollZoom();
            }
        }
    }
}
