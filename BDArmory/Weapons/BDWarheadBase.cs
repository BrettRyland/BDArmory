using KSP.Localization;
using System.Linq;
using UnityEngine;

using BDArmory.Control;
using BDArmory.Extensions;
using BDArmory.FX;
using BDArmory.Settings;
using BDArmory.Utils;
using BDArmory.Weapons.Missiles;
using BDArmory.Competition;

namespace BDArmory.Weapons
{
    public abstract class BDWarheadBase : PartModule
    {
        protected float distanceFromStart = 500;

        public Vessel sourcevessel
        {
            get { return _sourceVessel; }
            set { _sourceVessel = value; SourceVesselName = _sourceVessel != null ? _sourceVessel.vesselName : null; }
        }
        protected Vessel _sourceVessel;
        public string SourceVesselName { get; protected set; }

        public BDTeam Team { get; set; } = BDTeam.Get("Neutral");

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_ProximityTriggerDistance"), UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 1f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Proximity Fuze Radius
        public float detonationRange = -1f; // give ability to set proximity range
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_DetonateAtMinimumDistance"), UI_Toggle(disabledText = "#LOC_BDArmory_false", enabledText = "#LOC_BDArmory_true", scene = UI_Scene.All, affectSymCounterparts = UI_Scene.All)] // Detonate At Minumum Distance
        public bool detonateAtMinimumDistance = false;

        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_Status")]//Status
        public string guiStatusString = "ARMED";

        [KSPField]
        public float fuseFailureRate = 0f;                              // How often the explosive fuse will fail to detonate (0-1), evaluated once on detonation trigger

        //PartWindow buttons
        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "Disarm Warhead")]//Toggle
        public void Toggle()
        {
            Armed = !Armed;
            if (Armed)
            {
                guiStatusString = "ARMED";
                Events["Toggle"].guiName = StringUtils.Localize("Disarm Warhead");//"Enable Engage Options"
            }
            else
            {
                guiStatusString = "Safe";
                Events["Toggle"].guiName = StringUtils.Localize("Arm Warhead");//"Disable Engage Options"
            }
        }

        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "Targeting Logic")]//Status
        public string guiIFFString = "Ignore Allies";

        //PartWindow buttons
        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "Disable IFF")]//Toggle
        public void ToggleIFF()
        {
            IFF_On = !IFF_On;
            if (IFF_On)
            {
                guiIFFString = "Ignore Allies";
                Events["ToggleIFF"].guiName = StringUtils.Localize("Disable IFF");//"Enable Engage Options"
            }
            else
            {
                guiIFFString = "Indescriminate";
                Events["ToggleIFF"].guiName = StringUtils.Localize("Enable IFF");//"Disable Engage Options"
            }
        }

        public string IFFID = null;

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_DetonationDistanceOverride")]//Toggle
        public void ToggleProx()
        {
            manualOverride = !manualOverride;
            if (manualOverride)
            {
                Fields["detonationRange"].guiActiveEditor = true;
                Fields["detonationRange"].guiActive = true;
            }
            else
            {
                Fields["detonationRange"].guiActiveEditor = false;
                Fields["detonationRange"].guiActive = false;
            }
            GUIUtils.RefreshAssociatedWindows(part);
        }

        [KSPAction("Arm")]
        public void ArmAG(KSPActionParam param)
        {
            Armed = true;
            guiStatusString = "ARMED"; // Future me, this needs localization at some point
            Events["Toggle"].guiName = StringUtils.Localize("Disarm Warhead");//"Enable Engage Options"
        }

        [KSPAction("Detonate")]
        public void DetonateAG(KSPActionParam param)
        {
            Detonate();
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "#LOC_BDArmory_Detonate", active = true)]//Detonate
        public void DetonateEvent()
        {
            Detonate();
        }

        [KSPField(isPersistant = true)]
        public bool Armed = true;
        public bool Shaped { get; set; } = false;
        public bool isMissile = true;

        [KSPField(isPersistant = true)]
        public bool IFF_On = true;

        protected float updateTimer = 0;

        [KSPField(isPersistant = true)]
        public bool manualOverride = false;

        public bool hasDetonated;
        public bool fuseFailed = false;
        protected Collider[] proximityHitColliders = new Collider[100];

        public Vector3 direction;

        public override void OnStart(StartState state)
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                part.explosionPotential = 1.0f;
                part.OnJustAboutToBeDestroyed += DetonateIfPossible;
                part.force_activate();
                sourcevessel = vessel;
                if (part == null) return;
                var MF = VesselModuleRegistry.GetModule<MissileFire>(vessel, true);
                if (MF != null)
                {
                    sourcevessel = MF.vessel;
                }
            }
            if (part.FindModuleImplementing<MissileLauncher>() == null)
            {
                isMissile = false;
            }
            GuiSetup();
            /*
            if (BDArmorySettings.ADVANCED_EDIT)
            {
                //Fields["tntMass"].guiActiveEditor = true;

                //((UI_FloatRange)Fields["tntMass"].uiControlEditor).minValue = 0f;
                //((UI_FloatRange)Fields["tntMass"].uiControlEditor).maxValue = 3000f;
                //((UI_FloatRange)Fields["tntMass"].uiControlEditor).stepIncrement = 5f;
            }
            */
            WarheadSpecificSetup();
            if (HighLogic.LoadedSceneIsFlight)
                GameEvents.onGameSceneSwitchRequested.Add(HandleSceneChange);
        }

        protected void OnDestroy()
        {
            GameEvents.onGameSceneSwitchRequested.Remove(HandleSceneChange);
        }
        protected abstract void WarheadSpecificSetup();

        public void GuiSetup()
        {
            if (!isMissile)
            {
                Events["Toggle"].guiActiveEditor = true;
                Events["Toggle"].guiActive = true;
                Events["ToggleIFF"].guiActiveEditor = true;
                Events["ToggleIFF"].guiActive = true;
                Events["ToggleProx"].guiActiveEditor = true;
                Events["ToggleProx"].guiActive = true;
                Fields["guiStatusString"].guiActiveEditor = true;
                Fields["guiStatusString"].guiActive = true;
                Fields["guiIFFString"].guiActiveEditor = true;
                Fields["guiIFFString"].guiActive = true;
                if (Armed)
                {
                    guiStatusString = "ARMED";
                    Events["Toggle"].guiName = StringUtils.Localize("Disarm Warhead");
                }
                else
                {
                    guiStatusString = "Safe";
                    Events["Toggle"].guiName = StringUtils.Localize("Arm Warhead");
                }
                if (IFF_On)
                {
                    guiIFFString = "Ignore Allies";
                    Events["ToggleIFF"].guiName = StringUtils.Localize("Disable IFF");
                }
                else
                {
                    guiIFFString = "Indescriminate";
                    Events["ToggleIFF"].guiName = StringUtils.Localize("Enable IFF");
                }
                if (manualOverride)
                {
                    Fields["detonationRange"].guiActiveEditor = true;
                    Fields["detonationRange"].guiActive = true;
                }
                else
                {
                    Fields["detonationRange"].guiActiveEditor = false;
                    Fields["detonationRange"].guiActive = false;
                }
                WarheadSpecificUISetup();
            }
            else
            {
                Events["Toggle"].guiActiveEditor = false;
                Events["Toggle"].guiActive = false;
                Events["ToggleIFF"].guiActiveEditor = false;
                Events["ToggleIFF"].guiActive = false;
                Events["ToggleProx"].guiActiveEditor = false;
                Events["ToggleProx"].guiActive = false;
                Fields["guiStatusString"].guiActiveEditor = false;
                Fields["guiStatusString"].guiActive = false;
                Fields["guiIFFString"].guiActiveEditor = false;
                Fields["guiIFFString"].guiActive = false;
                Fields["detonationRange"].guiActiveEditor = false;
                Fields["detonationRange"].guiActive = false;
                Fields["detonateAtMinimumDistance"].guiActiveEditor = false;
                Fields["detonateAtMinimumDistance"].guiActive = false;
            }
            GUIUtils.RefreshAssociatedWindows(part);
        }
        protected abstract void WarheadSpecificUISetup();

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (!isMissile)
            {
                if (IFF_On)
                {
                    updateTimer -= Time.fixedDeltaTime;
                    if (updateTimer < 0)
                    {
                        GetTeamID(); //have this only called once a sec
                        updateTimer = 1.0f;    //next update in half a sec only
                    }
                }
                if (manualOverride) // don't call proximity code if a missile/MMG, use theirs
                {
                    if (Armed)
                    {
                        if (VesselModuleRegistry.GetModule<MissileFire>(vessel) == null)
                        {
                            if (sourcevessel != null && sourcevessel != part.vessel)
                            {
                                distanceFromStart = Vector3.Distance(part.vessel.transform.position, sourcevessel.transform.position);
                            }
                        }
                        if (Checkproximity(distanceFromStart))
                        {
                            Detonate();
                        }
                    }
                }
            }
        }

        private void GetTeamID()
        {
            var weaponManager = VesselModuleRegistry.GetModule<MissileFire>(sourcevessel);
            IFFID = weaponManager != null ? weaponManager.teamString : null;
        }

        public abstract void DetonateIfPossible();

        protected abstract void Detonate();

        public void HandleSceneChange(GameEvents.FromToAction<GameScenes, GameScenes> fromTo)
        {
            if (fromTo.from == GameScenes.FLIGHT)
            { hasDetonated = true; } // Don't trigger explosions on scene changes.
        }

        private bool Checkproximity(float distanceFromStart)
        {
            bool detonate = false;

            if (distanceFromStart < detonationRange)
            {
                return detonate = false;
            }

            var layerMask = (int)(LayerMasks.Parts | LayerMasks.Scenery | LayerMasks.Unknown19 | LayerMasks.Wheels);
            var hitCount = Physics.OverlapSphereNonAlloc(transform.position, detonationRange, proximityHitColliders, layerMask);
            if (hitCount == proximityHitColliders.Length)
            {
                proximityHitColliders = Physics.OverlapSphere(transform.position, detonationRange, layerMask);
                hitCount = proximityHitColliders.Length;
            }
            using (var hitsEnu = proximityHitColliders.Take(hitCount).GetEnumerator())
            {
                while (hitsEnu.MoveNext())
                {
                    if (hitsEnu.Current == null) continue;

                    Part partHit = hitsEnu.Current.GetComponentInParent<Part>();
                    if (partHit == null || partHit.vessel == null) continue;
                    if (ProjectileUtils.IsIgnoredPart(partHit)) continue; // Ignore ignored parts.
                    if (partHit.vessel == vessel || partHit.vessel == sourcevessel) continue;
                    if (partHit.vessel.vesselType == VesselType.Debris) continue;
                    if (!string.IsNullOrEmpty(SourceVesselName) && partHit.vessel.vesselName.Contains(SourceVesselName)) continue;
                    var weaponManager = VesselModuleRegistry.GetModule<MissileFire>(partHit.vessel);
                    if (IFF_On && (weaponManager == null || weaponManager.teamString == IFFID)) continue;
                    if (detonateAtMinimumDistance)
                    {
                        var distance = Vector3.Distance(partHit.transform.position + partHit.CoMOffset, transform.position);
                        var predictedDistance = Vector3.Distance(AIUtils.PredictPosition(partHit.transform.position + partHit.CoMOffset, partHit.vessel.Velocity(), partHit.vessel.acceleration, Time.fixedDeltaTime), AIUtils.PredictPosition(transform.position, vessel.Velocity(), vessel.acceleration, Time.fixedDeltaTime));
                        if (distance > predictedDistance && distance > Time.fixedDeltaTime * (float)vessel.srfSpeed) // If we're closing and not going to hit within the next update, then wait.
                        {
                            return detonate = false;
                        }
                    }
                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.BDWarheadBase]: Proxifuze triggered by {partHit.partName} from {partHit.vessel.vesselName}");
                    return detonate = true;
                }
            }
            return detonate;
        }
    }
}
