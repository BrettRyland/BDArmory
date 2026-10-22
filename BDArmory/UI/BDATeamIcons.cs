using System.Collections.Generic;
using UnityEngine;
using BDArmory.Competition;
using BDArmory.Settings;
using BDArmory.Utils;
using BDArmory.VesselSpawning;
using BDArmory.Weapons.Missiles;
using BDArmory.Control;
using BDArmory.Extensions;

namespace BDArmory.UI
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class BDATeamIcons : MonoBehaviour
    {
        public BDATeamIcons Instance;

        public Material IconMat;

        void Awake()
        {
            if (Instance)
            {
                Destroy(this);
            }
            else
                Instance = this;
        }
        GUIStyle IconUIStyle;
        GUIStyle DropshadowStyle;
        GUIStyle mIStyle;
        Color Teamcolor;
        Color Missilecolor;
        float Opacity;
        int textScale = 10;
        float oldIconScale;

        private void Start()
        {
            textScale = Mathf.Max(10, Mathf.CeilToInt(10 * BDTISettings.ICONSCALE));
            IconUIStyle = new GUIStyle
            {
                fontStyle = FontStyle.Bold,
                fontSize = textScale,
                normal = new GUIStyleState { textColor = XKCDColors.Red }//replace with BDATISetup defined value variable.
            };

            DropshadowStyle = new GUIStyle
            {
                fontStyle = FontStyle.Bold,
                fontSize = textScale,
                normal = new GUIStyleState { textColor = Color.black }
            };

            mIStyle = new GUIStyle
            {
                fontStyle = FontStyle.Normal,
                fontSize = textScale
            };

            Missilecolor = XKCDColors.Yellow;

            IconMat = new Material(Shader.Find("KSP/Particles/Alpha Blended"));

            UpdateStyles(true);
            TimingManager.LateUpdateAdd(TimingManager.TimingStage.BetterLateThanNever, UpdateUI);
        }

        void OnDestroy()
        {
            TimingManager.LateUpdateRemove(TimingManager.TimingStage.BetterLateThanNever, UpdateUI);
        }

        private void DrawOnScreenIcon(Vector3 worldPos, Texture texture, Vector2 size, Color Teamcolor, bool ShowPointer)
        {
            Teamcolor.a *= BDTISetup.iconOpacity;
            if (Event.current.type.Equals(EventType.Repaint))
            {
                bool offscreen = false;
                Vector3 screenPos = GUIUtils.GetMainCamera().WorldToViewportPoint(worldPos);
                if (screenPos.z < 0)
                {
                    offscreen = true;
                    screenPos.x *= -1;
                    screenPos.y *= -1;
                }
                if (screenPos.x != Mathf.Clamp01(screenPos.x))
                {
                    offscreen = true;
                }
                if (screenPos.y != Mathf.Clamp01(screenPos.y))
                {
                    offscreen = true;
                }
                float xPos = (screenPos.x * Screen.width) - (0.5f * size.x);
                float yPos = ((1 - screenPos.y) * Screen.height) - (0.5f * size.y);
                float xtPos = 1 * (Screen.width / 2);
                float ytPos = 1 * (Screen.height / 2);

                if (!offscreen)
                {
                    IconMat.SetColor("_TintColor", Teamcolor);
                    IconMat.mainTexture = texture;
                    Rect iconRect = new Rect(xPos, yPos, size.x, size.y);
                    Graphics.DrawTexture(iconRect, texture, IconMat);
                }
                else
                {
                    if (BDTISettings.POINTERS)
                    {
                        Vector2 head;
                        Vector2 tail;

                        head.x = xPos;
                        head.y = yPos;
                        tail.x = xtPos;
                        tail.y = ytPos;
                        float angle = Vector2.Angle(Vector3.up, tail - head);
                        if (tail.x < head.x)
                        {
                            angle = -angle;
                        }
                        if (ShowPointer && BDTISettings.POINTERS)
                        {
                            DrawPointer(calculateRadialCoords(head, tail, angle, 0.75f), angle, 4, Teamcolor);
                        }
                    }
                }

            }
        }
        private void DrawThreatIndicator(Vector3 vesselPos, Vector3 targetPos, Color Teamcolor)
        {
            Teamcolor.a *= BDTISetup.iconOpacity;
            if (Event.current.type.Equals(EventType.Repaint))
            {
                Vector3 screenPos = GUIUtils.GetMainCamera().WorldToViewportPoint(vesselPos);
                Vector3 screenTPos = GUIUtils.GetMainCamera().WorldToViewportPoint(targetPos);
                if (screenTPos.z > 0)
                {
                    float xPos = (screenPos.x * Screen.width);
                    float yPos = ((1 - screenPos.y) * Screen.height);
                    float xtPos = (screenTPos.x * Screen.width);
                    float ytPos = ((1 - screenTPos.y) * Screen.height);

                    Vector2 head;
                    Vector2 tail;

                    head.x = xPos;
                    head.y = yPos;
                    tail.x = xtPos;
                    tail.y = ytPos;
                    float angle = Vector2.Angle(Vector3.up, tail - head);
                    if (tail.x < head.x)
                    {
                        angle = -angle;
                    }
                    DrawPointer(tail, (angle - 180), 2, Teamcolor);
                }
            }
        }
        public Vector2 calculateRadialCoords(Vector2 RadialCoord, Vector2 Tail, float angle, float edgeDistance)
        {
            float theta = Mathf.Abs(angle);
            if (theta > 90)
            {
                theta -= 90;
            }
            theta = theta * Mathf.Deg2Rad; //needs to be in radians for Mathf. trig
            float Cos = Mathf.Cos(theta);
            float Sin = Mathf.Sin(theta);

            if (RadialCoord.y >= Tail.y)
            {
                if (RadialCoord.x >= Tail.x) // set up Quads 3-4
                {
                    RadialCoord.x = (Cos * (edgeDistance * Tail.x)) + Tail.x;
                }
                else
                {
                    RadialCoord.x = Tail.x - ((Cos * edgeDistance) * Tail.x);
                }
                RadialCoord.y = (Sin * (edgeDistance * Tail.y)) + Tail.y;
            }
            else
            {
                if (RadialCoord.x >= Tail.x) // set up Quads 1-2 
                {
                    RadialCoord.x = (Sin * (edgeDistance * Tail.x)) + Tail.x;
                }
                else
                {
                    RadialCoord.x = Tail.x - ((Sin * edgeDistance) * Tail.x);
                }
                RadialCoord.y = Tail.y - ((Cos * edgeDistance) * Tail.y);
            }
            return RadialCoord;
        }
        public static void DrawPointer(Vector2 Pointer, float angle, float width, Color color)
        {
            Camera cam = GUIUtils.GetMainCamera();

            if (cam == null) return;

            var guiMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            float length = 60;

            Rect upRect = new Rect(Pointer.x - (width / 2), Pointer.y - length, width, length);
            GUIUtility.RotateAroundPivot(-angle + 180, Pointer);
            GUIUtils.DrawRectangle(upRect, color);
            GUI.matrix = guiMatrix;
        }

        readonly List<(Vector3, Texture2D, Vector2, Color, bool)> onScreenIcons = []; // (position, texture, size, color, showPointer)
        readonly List<(Rect, string, Color, GUIStyle)> onScreenLabels = []; // (position, content, style, shadow style) (shadow is the rect offset by Vector2.one if not null)
        readonly List<(Vector3, Texture2D, Vector2, float)> texturesToDraw = []; // (position, texture, size, wobble)
        readonly List<(Vector3, Vector3, Color)> threatIndicators = []; // (vessel, target, color)
        readonly List<(Rect, Color)> healthBars = []; // (position, color)
        void UpdateUI()
        {
            onScreenIcons.Clear();
            onScreenLabels.Clear();
            texturesToDraw.Clear();
            threatIndicators.Clear();
            healthBars.Clear();
            if ((HighLogic.LoadedSceneIsFlight && BDArmorySetup.GAME_UI_ENABLED && !MapView.MapIsEnabled && BDTISettings.TEAMICONS) || HighLogic.LoadedSceneIsFlight && !BDArmorySetup.GAME_UI_ENABLED && !MapView.MapIsEnabled && BDTISettings.TEAMICONS && BDTISettings.PERSISTANT)
            {
                float size = 40;
                UpdateStyles();
                float minDistanceSqr = BDTISettings.DISTANCE_THRESHOLD * BDTISettings.DISTANCE_THRESHOLD;
                float maxDistanceSqr = BDTISettings.MAX_DISTANCE_THRESHOLD == 0 ? float.MaxValue : BDTISettings.MAX_DISTANCE_THRESHOLD * BDTISettings.MAX_DISTANCE_THRESHOLD;
                using var vessel = FlightGlobals.Vessels.GetEnumerator();
                while (vessel.MoveNext())
                {
                    if (vessel.Current == null || vessel.Current.packed || !vessel.Current.loaded) continue;
                    if (BDTISettings.MISSILES)
                    {
                        using var ml = VesselModuleRegistry.GetModules<MissileBase>(vessel.Current).GetEnumerator();
                        while (ml.MoveNext())
                        {
                            if (ml.Current == null) continue;
                            MissileLauncher launcher = ml.Current as MissileLauncher;
                            //if (ml.Current.MissileState != MissileBase.MissileStates.Idle && ml.Current.MissileState != MissileBase.MissileStates.Drop)

                            bool multilauncher = false;
                            if (launcher != null)
                            {
                                if (launcher.multiLauncher && !launcher.multiLauncher.isClusterMissile) multilauncher = true;
                            }
                            if (ml.Current.HasFired && !multilauncher && !ml.Current.HasMissed && !ml.Current.HasExploded) //culling post-thrust missiles makes AGMs get cleared almost immediately after launch
                            {
                                Vector3 sPos = FlightGlobals.ActiveVessel.vesselTransform.position;
                                Vector3 tPos = vessel.Current.vesselTransform.position;
                                float distSqr = (tPos - sPos).sqrMagnitude;
                                if (distSqr >= minDistanceSqr && distSqr <= maxDistanceSqr)
                                {
                                    onScreenIcons.Add((vessel.Current.CoM, BDTISetup.Instance.TextureIconMissile, new Vector2(20, 20), Missilecolor, true));
                                    if (GUIUtils.WorldToGUIPos(ml.Current.vessel.CoM, out Vector2 guiPos))
                                    {
                                        var dist = BDAMath.Sqrt(distSqr);
                                        onScreenLabels.Add((new(guiPos.x - 12, guiPos.y + 10, 100, 32), dist > 1e3f ? $"{1e-3f * dist:0.00}km" : $"{dist:0.0}m", Missilecolor, null));
                                        if (BDTISettings.MISSILE_TEXT)
                                        {
                                            Color iconUI = BDTISetup.Instance.ColorAssignments.ContainsKey(ml.Current.Team.Name) ? BDTISetup.Instance.ColorAssignments[ml.Current.Team.Name] : Color.gray;
                                            iconUI.a = Opacity * BDTISetup.textOpacity;
                                            onScreenLabels.Add((new(guiPos.x + 24 * BDTISettings.ICONSCALE, guiPos.y - 4, 100, 32), ml.Current.vessel.vesselName, iconUI, DropshadowStyle));
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (!vessel.Current.loaded || vessel.Current.packed || vessel.Current.isActiveVessel) continue;
                    if (BDTISettings.DEBRIS)
                    {
                        if (vessel.Current == null) continue;
                        if (vessel.Current.vesselType != VesselType.Debris) continue;
                        if (vessel.Current.LandedOrSplashed) continue;

                        Vector3 sPos = FlightGlobals.ActiveVessel.vesselTransform.position;
                        Vector3 tPos = vessel.Current.vesselTransform.position;
                        float distSqr = (tPos - sPos).sqrMagnitude;
                        if (distSqr >= minDistanceSqr && distSqr <= maxDistanceSqr)
                        {
                            texturesToDraw.Add((vessel.Current.CoM, BDTISetup.Instance.TextureIconDebris, new Vector2(20, 20), 0));
                        }
                    }
                }
                using var teamManagers = BDTISetup.Instance.weaponManagers.GetEnumerator();
                while (teamManagers.MoveNext())
                {
                    using var wm = teamManagers.Current.Value.GetEnumerator();
                    while (wm.MoveNext())
                    {
                        if (wm.Current == null) continue;
                        if (!BDTISetup.Instance.ColorAssignments.ContainsKey(wm.Current.Team.Name)) continue; // Ignore entries that haven't been updated yet.
                        Color teamcolor = BDTISetup.Instance.ColorAssignments[wm.Current.Team.Name];
                        teamcolor.a = Opacity;
                        Teamcolor = teamcolor;
                        teamcolor.a *= BDTISetup.textOpacity;
                        size = wm.Current.vessel.vesselType == VesselType.Debris ? 20 : 40;
                        if (wm.Current.vessel.isActiveVessel)
                        {
                            if (BDTISettings.THREATICON)
                            {
                                if (wm.Current.currentTarget == null) continue;
                                Vector3 sPos = FlightGlobals.ActiveVessel.CoM;
                                Vector3 tPos = wm.Current.currentTarget.Vessel.CoM;
                                float relPosSqr = (tPos - sPos).sqrMagnitude;
                                if (relPosSqr >= minDistanceSqr && relPosSqr <= maxDistanceSqr)
                                {
                                    threatIndicators.Add((wm.Current.vessel.CoM, wm.Current.currentTarget.Vessel.CoM, Teamcolor));
                                }
                            }
                            if (BDTISettings.SHOW_SELF)
                            {
                                onScreenIcons.Add((
                                    wm.Current.vessel.CoM,
                                    GetIconForVessel(wm.Current.vessel),
                                    new Vector2(size * BDTISettings.ICONSCALE, size * BDTISettings.ICONSCALE),
                                    Teamcolor,
                                    true
                                ));
                                if (BDTISettings.VESSELNAMES)
                                {
                                    if (GUIUtils.WorldToGUIPos(wm.Current.vessel.CoM, out Vector2 guiPos))
                                    {
                                        onScreenLabels.Add((new(guiPos.x + 24 * BDTISettings.ICONSCALE, guiPos.y - 4, 100, 32), wm.Current.vessel.vesselName, teamcolor, DropshadowStyle));
                                    }
                                }
                            }
                        }
                        else
                        {
                            Vector3 selfPos = FlightGlobals.ActiveVessel.CoM;
                            Vector3 targetPos = wm.Current.vessel.CoM;
                            Vector3 targetRelPos = targetPos - selfPos;
                            float distSqr = targetRelPos.sqrMagnitude;
                            if (distSqr >= minDistanceSqr && distSqr <= maxDistanceSqr) //TODO - look into having vessel icons be based on vessel visibility? (So don't draw icon for undetected stealth plane, etc?)
                            {
                                onScreenIcons.Add((
                                    wm.Current.vessel.CoM,
                                    GetIconForVessel(wm.Current.vessel),
                                    new Vector2(size * BDTISettings.ICONSCALE, size * BDTISettings.ICONSCALE),
                                    Teamcolor,
                                    true
                                ));
                                if (BDTISettings.THREATICON)
                                {
                                    if (wm.Current.currentTarget != null)
                                    {
                                        if (!wm.Current.currentTarget.Vessel.isActiveVessel)
                                        {
                                            threatIndicators.Add((wm.Current.vessel.CoM, wm.Current.currentTarget.Vessel.CoM, Teamcolor));
                                        }
                                    }
                                }
                                if (GUIUtils.WorldToGUIPos(wm.Current.vessel.CoM, out Vector2 guiPos))
                                {
                                    if (BDTISettings.VESSELNAMES)
                                    {
                                        string vName = wm.Current.vessel.vesselName;
                                        onScreenLabels.Add((new(guiPos.x + 24 * BDTISettings.ICONSCALE, guiPos.y - 4, 100, 32), vName, teamcolor, DropshadowStyle));
                                    }
                                    if (BDTISettings.TEAMNAMES)
                                    {
                                        onScreenLabels.Add((new(guiPos.x + 16 * BDTISettings.ICONSCALE, guiPos.y - 19 * BDTISettings.ICONSCALE, 100, 32), $"Team: {wm.Current.Team.Name}", teamcolor, DropshadowStyle));
                                    }

                                    if (BDTISettings.SCORE)
                                    {
                                        int Score = 0;

                                        if (BDACompetitionMode.Instance.Scores.ScoreData.TryGetValue(wm.Current.vessel.vesselName, out var scoreData))
                                            Score = scoreData.hits;
                                        if (ContinuousSpawning.Instance.vesselsSpawningContinuously)
                                        {
                                            if (ContinuousSpawning.Instance.continuousSpawningScores.TryGetValue(wm.Current.vessel.vesselName, out var ctsScoreData))
                                                Score += ctsScoreData.cumulativeHits;
                                        }

                                        onScreenLabels.Add((new(guiPos.x + 16 * BDTISettings.ICONSCALE, guiPos.y + 14 * BDTISettings.ICONSCALE, 100, 32), "Score: " + Score, teamcolor, DropshadowStyle));
                                    }
                                    float dist = BDAMath.Sqrt(distSqr);
                                    string UIdistStr = dist > 1000f ? $"{1e-3f * dist:0.00}km" : $"{dist:0.0}m";
                                    if (BDTISettings.HEALTHBAR)
                                    {
                                        float hpPercent = Mathf.Clamp01(wm.Current.currentHP / wm.Current.totalHP);
                                        if (hpPercent > 0)
                                        {
                                            Rect barRect = new(guiPos.x - 32 * BDTISettings.ICONSCALE, guiPos.y + 30 * BDTISettings.ICONSCALE, 64 * BDTISettings.ICONSCALE, 12);
                                            Rect healthRect = new(guiPos.x - 30 * BDTISettings.ICONSCALE, guiPos.y + 32 * BDTISettings.ICONSCALE, 60 * hpPercent * BDTISettings.ICONSCALE, 8);
                                            Color temp = XKCDColors.Grey;
                                            temp.a = Opacity * BDTISetup.iconOpacity;
                                            healthBars.Add((barRect, temp));
                                            temp = Color.HSVToRGB(85f * hpPercent / 255, 1f, 1f);
                                            temp.a = Opacity * BDTISetup.iconOpacity;
                                            healthBars.Add((healthRect, temp));

                                        }
                                        onScreenLabels.Add((new(guiPos.x - 12, guiPos.y + 45 * BDTISettings.ICONSCALE, 100, 32), UIdistStr, teamcolor, DropshadowStyle));
                                    }
                                    else
                                    {
                                        onScreenLabels.Add((new(guiPos.x - 12, guiPos.y + 20 * BDTISettings.ICONSCALE, 100, 32), UIdistStr, teamcolor, DropshadowStyle));
                                    }
                                    if (BDTISettings.TELEMETRY)
                                    {
                                        string selectedWeapon = "Using: " + wm.Current.selectedWeaponString;
                                        string AIstate = wm.Current.AI != null ? $"Pilot {wm.Current.AI.currentStatus}" : "No AI";

                                        onScreenLabels.Add((new(guiPos.x + 32 * BDTISettings.ICONSCALE, guiPos.y + 32, 200, 32), selectedWeapon, teamcolor, DropshadowStyle));
                                        onScreenLabels.Add((new(guiPos.x + 32 * BDTISettings.ICONSCALE, guiPos.y + 48, 200, 32), AIstate, teamcolor, DropshadowStyle));
                                        if (wm.Current.isFlaring || wm.Current.isChaffing || wm.Current.isECMJamming)
                                        {
                                            onScreenLabels.Add((new(guiPos.x + 32 * BDTISettings.ICONSCALE, guiPos.y + 64, 200, 32), "Deploying Counter-Measures", teamcolor, DropshadowStyle));
                                        }
                                        onScreenLabels.Add((new(guiPos.x - 96 * BDTISettings.ICONSCALE, guiPos.y + 64, 100, 32), $"Speed: {wm.Current.vessel.speed:0.0}m/s", teamcolor, DropshadowStyle));
                                        onScreenLabels.Add((new(guiPos.x - 96 * BDTISettings.ICONSCALE, guiPos.y + 80, 100, 32), $"Alt: {wm.Current.vessel.altitude:0.0}m", teamcolor, DropshadowStyle));
                                        onScreenLabels.Add((new(guiPos.x - 96 * BDTISettings.ICONSCALE, guiPos.y + 96, 100, 32), $"Throttle: {Mathf.CeilToInt(wm.Current.vessel.ctrlState.mainThrottle * 100)}%", teamcolor, DropshadowStyle));
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        void OnGUI()
        {
            if ((HighLogic.LoadedSceneIsFlight && BDArmorySetup.GAME_UI_ENABLED && !MapView.MapIsEnabled && BDTISettings.TEAMICONS) || HighLogic.LoadedSceneIsFlight && !BDArmorySetup.GAME_UI_ENABLED && !MapView.MapIsEnabled && BDTISettings.TEAMICONS && BDTISettings.PERSISTANT)
            {
                // Ordering: textures (debris), icons, health bars, threat indicators, text.
                foreach (var (position, texture, size, wobble) in texturesToDraw) GUIUtils.DrawTextureOnWorldPos(position, texture, size, wobble);
                foreach (var (position, icon, size, color, showPointer) in onScreenIcons) DrawOnScreenIcon(position, icon, size, color, showPointer);
                foreach (var (rect, color) in healthBars) GUIUtils.DrawRectangle(rect, color);
                foreach (var (from, to, color) in threatIndicators) DrawThreatIndicator(from, to, color);
                foreach (var (rect, content, color, shadowStyle) in onScreenLabels)
                {
                    if (shadowStyle != null) GUI.Label(new(rect.position + Vector2.one, rect.size), content, shadowStyle);
                    IconUIStyle.normal.textColor = color;
                    GUI.Label(rect, content, IconUIStyle);
                }
            }
        }

        void UpdateStyles(bool forceUpdate = false)
        {
            // Update opacity for DropshadowStyle, mIStyle, Missilecolor. IconUIStyle opacity
            // is updated in OnGUI().
            if (forceUpdate || Opacity != BDTISettings.OPACITY)
            {
                Opacity = BDTISettings.OPACITY;

                Teamcolor.a = Opacity;
                Color temp;
                temp = DropshadowStyle.normal.textColor;
                temp.a = Opacity * BDTISetup.textOpacity;
                DropshadowStyle.normal.textColor = temp;
                temp = mIStyle.normal.textColor;
                temp.a = Opacity * BDTISetup.textOpacity;
                mIStyle.normal.textColor = temp;
                Missilecolor.a = Opacity;
            }
            if (forceUpdate || BDTISettings.ICONSCALE != oldIconScale)
            {
                textScale = Mathf.Max(10, Mathf.CeilToInt(10 * BDTISettings.ICONSCALE)); //Would BD_UI_SCALE make more sense here?
                oldIconScale = BDTISettings.ICONSCALE;

                IconUIStyle.fontSize = textScale;
                DropshadowStyle.fontSize = textScale;
                mIStyle.fontSize = textScale;
            }
        }

        Texture2D GetIconForVessel(Vessel v)
        {
            Texture2D icon;
            if ((v.vesselType == VesselType.Ship && !v.Splashed) || v.vesselType == VesselType.Plane)
            {
                icon = BDTISetup.Instance.TextureIconPlane;
            }
            else if (v.vesselType == VesselType.Base || v.vesselType == VesselType.Lander)
            {
                icon = BDTISetup.Instance.TextureIconBase;
            }
            else if (v.vesselType == VesselType.Rover)
            {
                icon = BDTISetup.Instance.TextureIconRover;
            }
            else if (v.vesselType == VesselType.Probe)
            {
                icon = BDTISetup.Instance.TextureIconProbe;
            }
            else if (v.vesselType == VesselType.Ship && v.Splashed)
            {
                icon = BDTISetup.Instance.TextureIconShip;
                if (v.vesselType == VesselType.Ship && v.altitude < -10)
                {
                    icon = BDTISetup.Instance.TextureIconSub;
                }
            }
            else if (v.vesselType == VesselType.Debris)
            {
                icon = BDTISetup.Instance.TextureIconDebris;
                Color temp = XKCDColors.Grey;
                temp.a = Opacity;
                Teamcolor = temp;
                temp.a *= BDTISetup.textOpacity;
                IconUIStyle.normal.textColor = temp;
            }
            else
            {
                icon = BDTISetup.Instance.TextureIconGeneric;
            }
            return icon;
        }

        /// <summary>
        /// A public accessor for other mods to get access to the team color.
        /// </summary>
        /// <param name="wm"></param>
        /// <returns></returns>
        public static Color GetTeamColor(MissileFire wm) => BDTISetup.Instance.ColorAssignments.GetValueOrDefault(wm.Team.Name, Color.gray);
    }
}
