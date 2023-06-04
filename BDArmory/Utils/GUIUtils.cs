using System.Collections.Generic;
using System;
using UniLinq;
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

        public static void DrawTextureOnWorldPos(Vector3 worldPos, Texture texture, Vector2 size, float wobble)
        {
            var cam = GetMainCamera();
            if (cam == null) return;
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

        public static void DrawLineBetweenWorldPositions(Vector3 worldPosA, Vector3 worldPosB, float width, Color color)
        {
            Camera cam = GetMainCamera();

            if (cam == null) return;

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
            GUI.matrix = Matrix4x4.identity;
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
            if (GUIUtils.MouseIsInRect(rect) && ((Event.current.isMouse && (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp)) || Event.current.isScrollWheel))
            {
                Event.current.Use();
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

        internal static void RepositionWindow(ref Rect windowPosition)
        {
            if (BDArmorySettings.STRICT_WINDOW_BOUNDARIES)
            {
                // This method uses Gui point system.
                if (windowPosition.x < 0) windowPosition.x = 0;
                if (windowPosition.y < 0) windowPosition.y = 0;

                if (windowPosition.xMax > Screen.width) // Don't go off the right of the screen.
                    windowPosition.x = Screen.width - windowPosition.width;
                if (windowPosition.height > Screen.height) // Don't go off the top of the screen.
                    windowPosition.y = 0;
                else if (windowPosition.yMax > Screen.height) // Don't go off the bottom of the screen.
                    windowPosition.y = Screen.height - windowPosition.height;
            }
            else // If the window is completely off-screen, bring it just onto the screen.
            {
                if (windowPosition.width == 0) windowPosition.width = 1;
                if (windowPosition.height == 0) windowPosition.height = 1;
                if (windowPosition.x >= Screen.width) windowPosition.x = Screen.width - 1;
                if (windowPosition.y >= Screen.height) windowPosition.y = Screen.height - 1;
                if (windowPosition.x + windowPosition.width < 1) windowPosition.x = 1 - windowPosition.width;
                if (windowPosition.y + windowPosition.height < 1) windowPosition.y = 1 - windowPosition.height;
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
                outputColor[i] = Single.Parse(strings[i]) / 255;
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

            return GUIUtilsInstance.fetch.mouseIsOnGUI;
        }

        static bool _CheckMouseIsOnGui()
        {
            Vector3 inverseMousePos = new Vector3(Input.mousePosition.x, Screen.height - Input.mousePosition.y, 0);
            Rect topGui = new Rect(0, 0, Screen.width, 65);

            if (topGui.Contains(inverseMousePos)) return true;
            if (BDArmorySetup.windowBDAToolBarEnabled && BDArmorySetup.WindowRectToolbar.Contains(inverseMousePos))
                return true;
            if (ModuleTargetingCamera.windowIsOpen && BDArmorySetup.WindowRectTargetingCam.Contains(inverseMousePos))
                return true;
            if (BDArmorySetup.Instance.ActiveWeaponManager)
            {
                MissileFire wm = BDArmorySetup.Instance.ActiveWeaponManager;

                if (wm.vesselRadarData && wm.vesselRadarData.guiEnabled)
                {
                    if (BDArmorySetup.WindowRectRadar.Contains(inverseMousePos)) return true;
                    if (wm.vesselRadarData.linkWindowOpen && wm.vesselRadarData.linkWindowRect.Contains(inverseMousePos))
                        return true;
                }
                if (wm.rwr && wm.rwr.rwrEnabled && wm.rwr.displayRWR && BDArmorySetup.WindowRectRwr.Contains(inverseMousePos))
                    return true;
                if (wm.wingCommander && wm.wingCommander.showGUI)
                {
                    if (BDArmorySetup.WindowRectWingCommander.Contains(inverseMousePos)) return true;
                    if (wm.wingCommander.showAGWindow && wm.wingCommander.agWindowRect.Contains(inverseMousePos))
                        return true;
                }

            }
            if (extraGUIRects != null)
            {
                foreach (var guiRect in extraGUIRects.Values)
                {
                    if (!guiRect.visible) continue;
                    if (guiRect.rect.Contains(inverseMousePos)) return true;
                }
            }

            return false;
        }

        public static void ResizeGuiWindow(Rect windowrect, Vector2 mousePos)
        {
            GUIUtilsInstance.Reset();
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

        public static bool MouseIsInRect(Rect rect)
        {
            Vector3 inverseMousePos = new Vector3(Input.mousePosition.x, Screen.height - Input.mousePosition.y, 0);
            return rect.Contains(inverseMousePos);
        }

        //Thanks FlowerChild
        //refreshes part action window
        public static void RefreshAssociatedWindows(Part part)
        {
            if (part == null || part.PartActionWindow == null) return;
            part.PartActionWindow.UpdateWindow();
            // part.PartActionWindow.displayDirty = true;
            // IEnumerator<UIPartActionWindow> window = Object.FindObjectsOfType(typeof(UIPartActionWindow)).Cast<UIPartActionWindow>().GetEnumerator();
            // while (window.MoveNext())
            // {
            //     if (window.Current == null) continue;
            //     if (window.Current.part == part)
            //     {
            //         window.Current.displayDirty = true;
            //     }
            // }
            // window.Dispose();
        }


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
            if (String.IsNullOrEmpty(text))
            {
                var guiColor = GUI.color;
                GUI.color = Color.grey;
                GUI.Label(isGUILayout ? GUILayoutUtility.GetLastRect() : rect, placeholder);
                GUI.color = guiColor;
            }
            return newText;
        }

        [KSPAddon(KSPAddon.Startup.EveryScene, false)]
        internal class GUIUtilsInstance : MonoBehaviour
        {
            public bool mouseIsOnGUI
            {
                get
                {
                    if (!_mouseIsOnGUICheckedThisFrame)
                    {
                        _mouseIsOnGUI = GUIUtils._CheckMouseIsOnGui();
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
                GUIUtils.EndDisableScrollZoom();
            }
        }
    }
}
