using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Text;
using BDArmory.Utils;

namespace BDArmory.Weapons.Missiles
{
    public class ModuleMissileMagazine : PartModule, IPartMassModifier, IPartCostModifier
    {
        public float GetModuleMass(float baseMass, ModifierStagingSituation situation) => Mathf.Max(ammoCount, 0) * missileMass;

        public ModifierChangeWhen GetModuleMassChangeWhen() => ModifierChangeWhen.FIXED;
        public float GetModuleCost(float baseCost, ModifierStagingSituation situation) => Mathf.Max(ammoCount, 0) * missileCost;
        public ModifierChangeWhen GetModuleCostChangeWhen() => ModifierChangeWhen.FIXED;

        private float missileMass = 0;
        private float missileCost = 0;

        [KSPField(isPersistant = true, guiActive = true, guiName = "#LOC_BDArmory_WeaponName", guiActiveEditor = false), UI_Label(affectSymCounterparts = UI_Scene.All, scene = UI_Scene.All)]//Weapon Name 
        public string loadedMissileName = "";

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_OrdinanceAvailable"),//Ordinance Available
UI_FloatRange(minValue = 1f, maxValue = 4, stepIncrement = 1f, scene = UI_Scene.All)]
        public float ammoCount = 1;

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "#LOC_BDArmory_OrdinanceAvailable"),//Ordinance Available
UI_ProgressBar(affectSymCounterparts = UI_Scene.None, controlEnabled = false, scene = UI_Scene.Flight, maxValue = 100, minValue = 0, requireFullControl = false)]
        public float ammoRemaining = 1;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = false, guiName = "#autoLOC_8003393"), UI_FloatRange(minValue = 1, maxValue = 10, stepIncrement = 1, scene = UI_Scene.All, affectSymCounterparts = UI_Scene.All)]//Priority
        public float priority = 1;

        [KSPField(isPersistant = true)]
        public string MissileName;

        [KSPField] public string RailNode = "rail"; //name of attachnode for VLS MMLs to set missile loadout

        [KSPField] public bool AccountForAmmo = true;
        [KSPField] public float maxAmmo = 20;

        [KSPField(isPersistant = true)]
        public Vector2 missileScale = Vector2.zero;

        [KSPField]
        public string scaleTransformName;
        Transform ScaleTransform;

        [KSPField] public bool isRectangularMagazine = true;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_ArmorWidth"),// Length
UI_FloatRange(minValue = 1f, maxValue = 4, stepIncrement = 1f, scene = UI_Scene.All, affectSymCounterparts = UI_Scene.All)]
        public float rowCount = 1;

        FloatCurve cylinderScale = null;

        public void Start()
        {
            if (cylinderScale == null && !isRectangularMagazine)
            {
                cylinderScale = new FloatCurve(); //diameter of a circle to fix x uniform smaller circles within its area
                cylinderScale.Add(1, 1f);
                cylinderScale.Add(2, 2f);
                cylinderScale.Add(3, 2.15f);
                cylinderScale.Add(4, 2.414f);
                cylinderScale.Add(5, 2.7f);
                cylinderScale.Add(6, 3f);
                cylinderScale.Add(7, 3f);
                cylinderScale.Add(8, 3.3f);
                cylinderScale.Add(9, 3.6f);
                cylinderScale.Add(10, 3.8f);
                cylinderScale.Add(11, 3.92f);
                cylinderScale.Add(12, 4f);
                cylinderScale.Add(13, 4.236f);
                cylinderScale.Add(14, 4.33f);
                cylinderScale.Add(15, 4.52f);
                cylinderScale.Add(16, 4.615f);
                cylinderScale.Add(17, 4.792f);
                cylinderScale.Add(18, 4.864f);
                cylinderScale.Add(19, 4.864f);
                cylinderScale.Add(20, 5.122f);
            }
            if (HighLogic.LoadedSceneIsEditor)
            {
                if (missileScale == Vector2.zero) missileScale = new Vector2(3, 0.25f);
                GameEvents.onEditorShipModified.Add(ShipModified);
                if (!isRectangularMagazine)
                {
                    Fields["rowCount"].guiActiveEditor = false;
                }
                UI_FloatRange Ammo = (UI_FloatRange)Fields["ammoCount"].uiControlEditor;
                Ammo.maxValue = maxAmmo;
            }
            if (HighLogic.LoadedSceneIsFlight)
            {
                UI_ProgressBar ordinance = (UI_ProgressBar)Fields["ammoRemaining"].uiControlFlight;
                ordinance.maxValue = ammoCount;
                ammoRemaining = ammoCount;
            }
            GUIUtils.RefreshAssociatedWindows(part);
            StartCoroutine(DelayedStart());
        }

        IEnumerator DelayedStart()
        {
            yield return new WaitForFixedUpdate();

			if (AccountForAmmo)
			{
				if (!String.IsNullOrEmpty(MissileName))
				{
					using (var parts = PartLoader.LoadedPartsList.GetEnumerator())
						while (parts.MoveNext())
						{
							if (parts.Current == null) continue;
							if (parts.Current.partConfig == null || parts.Current.partPrefab == null)
								continue;
							if (parts.Current.partPrefab.partInfo.name != MissileName) continue;
							missileMass = parts.Current.partPrefab.mass;
							missileCost = parts.Current.partPrefab.partInfo.cost;
							break;
						}
				}
			}
			else
			{
				missileMass = 0;
				missileCost = 0;
			}
            if (string.IsNullOrEmpty(scaleTransformName))
            {
                Fields["Scale"].guiActiveEditor = false;
            }
            else
            {
                ScaleTransform = part.FindModelTransform(scaleTransformName);
                UI_FloatRange scale = (UI_FloatRange)Fields["ammoCount"].uiControlEditor;
                scale.onFieldChanged = UpdateScale;
                UI_FloatRange rows = (UI_FloatRange)Fields["rowCount"].uiControlEditor;
                rows.maxValue = Mathf.CeilToInt(BDAMath.Sqrt(maxAmmo));
                rows.onFieldChanged = UpdateScale;
            }
            UpdateScaling(missileScale);
        }

        public void UpdateScale(BaseField field, object obj)
        {
            if (ScaleTransform != null)
            {
                if (isRectangularMagazine)
                    ScaleTransform.localScale = new Vector3(missileScale.x + 0.05f, missileScale.y * 1.5f * rowCount, missileScale.y * 1.5f * Mathf.CeilToInt(ammoCount / rowCount)); //missile length, missileWidth
                else
                    ScaleTransform.localScale = new Vector3(((missileScale.x + 0.05f) * Mathf.CeilToInt(ammoCount / 20)), missileScale.y * 1.5f * cylinderScale.Evaluate(ammoCount), missileScale.y * 1.5f * cylinderScale.Evaluate(ammoCount));
                //default model scaling is 1x1x1m. Cylinders max diameter at 20 missiles, increase length if mag capacity more
                using (List<Part>.Enumerator sym = part.symmetryCounterparts.GetEnumerator())
                    while (sym.MoveNext())
                    {
                        if (sym.Current == null) continue;
                        var mmm = sym.Current.FindModuleImplementing<ModuleMissileMagazine>();
                        if (mmm == null) continue;
                        mmm.missileScale = missileScale;
                        mmm.rowCount = rowCount;
                        mmm.UpdateScaling(missileScale);
                    }
            }
        }

        public void UpdateScaling(Vector2 scale)
        {
            //Debug.Log($"[MMM debug] Calling missile mag UpdateScaling, scale({scale.x}, {scale.y})");
            if (ScaleTransform != null)
            {
                if (isRectangularMagazine)
                    ScaleTransform.localScale = new Vector3(scale.x + 0.05f, scale.y * 1.5f * rowCount, scale.y * 1.5f * Mathf.CeilToInt(ammoCount / rowCount));
                else
                    ScaleTransform.localScale = new Vector3(((scale.x + 0.05f) * Mathf.CeilToInt(ammoCount / 20)), scale.y * 1.5f * cylinderScale.Evaluate(ammoCount), scale.y * 1.5f * cylinderScale.Evaluate(ammoCount));
            }
        }
        private void OnDestroy()
        {
            GameEvents.onEditorShipModified.Remove(ShipModified);
        }

        public void ShipModified(ShipConstruct data)
        {
            if (part.children.Count > 0)
            {
                using (List<AttachNode>.Enumerator stackNode = part.attachNodes.GetEnumerator())
                    while (stackNode.MoveNext())
                    {
                        if (stackNode.Current == null) continue;
                        if (stackNode.Current?.nodeType != AttachNode.NodeType.Stack) continue;
                        if (stackNode.Current.id != RailNode) continue;
                        {
                            if (stackNode.Current.attachedPart is Part missile)
                            {
                                if (missile == null) return;

                                if (missile.FindModuleImplementing<MissileLauncher>())
                                {
                                    MissileName = missile.name;
                                    missileScale = new Vector2(Mathf.Max(missile.collider.bounds.size.x, missile.collider.bounds.size.y, missile.collider.bounds.size.z), Mathf.Min(missile.collider.bounds.size.x, missile.collider.bounds.size.y, missile.collider.bounds.size.z));
                                    //Debug.Log($"[MissileMagazine] Missile bounds are {missile.collider.bounds.size.x.ToString("0.00")}, {missile.collider.bounds.size.y.ToString("0.00")}, {missile.collider.bounds.size.z.ToString("0.00")}");
                                    //this will grab missile body dia/length, something something folding fins. But given BDA missiles are IRL scale instead of ~0.7 kerbalscale, including fins would make the mags *really* large
                                    MissileLauncher MLConfig = missile.FindModuleImplementing<MissileLauncher>();
                                    Fields["loadedMissileName"].guiActive = true;
                                    Fields["loadedMissileName"].guiActiveEditor = true;
                                    loadedMissileName = MLConfig.GetShortName();
                                    GUIUtils.RefreshAssociatedWindows(part);
                                    missileMass = AccountForAmmo ? missile.partInfo.partPrefab.mass : 0;
                                    missileCost = AccountForAmmo ? missile.partInfo.cost : 0;
                                    EditorLogic.DeletePart(missile);
                                    using (List<Part>.Enumerator sym = part.symmetryCounterparts.GetEnumerator())
                                        while (sym.MoveNext())
                                        {
                                            if (sym.Current == null) continue;
                                            var mmm = sym.Current.FindModuleImplementing<ModuleMissileMagazine>();
                                            if (mmm == null) continue;
                                            mmm.MissileName = MissileName;
                                        }
                                    UpdateScale(null, null);
                                }
                            }
                        }
                    }
            }
        }
        public override string GetInfo()
        {
            StringBuilder output = new StringBuilder();

            output.Append(Environment.NewLine);
            output.AppendLine($"Missile Magazine");
            output.AppendLine($"Attach a missile to this to load magazine with selected ordinance");
            output.AppendLine($"- Maximum Ordinance: {maxAmmo}");
            output.AppendLine($"- Ammo has Mass/Cost: {AccountForAmmo}");
            return output.ToString();
        }
    }
}