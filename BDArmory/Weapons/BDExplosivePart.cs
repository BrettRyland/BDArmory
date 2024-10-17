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
    public class BDExplosivePart : BDWarheadBase
    {
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = false, guiName = "#LOC_BDArmory_TNTMass"),//TNT mass equivalent
        UI_Label(affectSymCounterparts = UI_Scene.All, controlEnabled = true, scene = UI_Scene.All)]
        public float tntMass = 1;

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "#LOC_BDArmory_BlastRadius"),//Blast Radius
         UI_Label(affectSymCounterparts = UI_Scene.All, controlEnabled = true, scene = UI_Scene.All)]
        public float blastRadius = 10;

        [KSPField]
        public string warheadType = "standard";
        public string warheadReportingName;
        public ExplosionFx.WarheadTypes _warheadType = ExplosionFx.WarheadTypes.Standard;

        [KSPField]
        public float caliber = 120;

        [KSPField]
        public float apMod = 1;

        [KSPField]
        public string explModelPath = "BDArmory/Models/explosion/explosion";

        [KSPField]
        public string explSoundPath = "BDArmory/Sounds/explode1";

        private double previousMass = -1;

        protected override void WarheadSpecificSetup()
        {
            CalculateBlast();
            ParseWarheadType();
        }

        protected override void WarheadSpecificUISetup()
        {
            SetInitialDetonationDistance();
        }

        public void Update()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                OnUpdateEditor();
            }
        }

        private void OnUpdateEditor()
        {
            CalculateBlast();
        }

        private void CalculateBlast()
        {
            if (part.Resources.Contains("HighExplosive"))
            {
                if (part.Resources["HighExplosive"].amount == previousMass) return;

                tntMass = (float)(part.Resources["HighExplosive"].amount * part.Resources["HighExplosive"].info.density * 1000) * 1.5f;
                part.explosionPotential = tntMass / 10f;
                previousMass = part.Resources["HighExplosive"].amount;
            }

            blastRadius = BlastPhysicsUtils.CalculateBlastRange(tntMass);
        }
        public void ParseWarheadType()
        {
            warheadType = warheadType.ToLower();
            switch (warheadType) //make sure this is a valid entry
            {
                case "continuousrod":
                    warheadReportingName = "Continuous Rod";
                    _warheadType = ExplosionFx.WarheadTypes.ContinuousRod;
                    break;
                case "shapedcharge":
                    warheadReportingName = "Shaped Charge";
                    _warheadType = ExplosionFx.WarheadTypes.ShapedCharge;
                    break;
                default:
                    warheadType = "standard";
                    warheadReportingName = "Standard";
                    _warheadType = ExplosionFx.WarheadTypes.Standard;
                    break;
            }
        }

        public override void DetonateIfPossible()
        {
            if (!hasDetonated && Armed)
            {
                hasDetonated = true;

                if (fuseFailureRate > 0f)
                    if (Random.Range(0f, 1f) < fuseFailureRate)
                        fuseFailed = true;

                if (!fuseFailed)
                {
                    direction = _warheadType == ExplosionFx.WarheadTypes.Standard ? default : part.partTransform.forward; //both the missileReferenceTransform and smallWarhead part's forward direction is Z+, or transform.forward.
                                                                                                  // could also do warheadType == "standard" ? default: part.partTransform.forward, as this simplifies the isAngleAllowed check in ExplosionFX, but at the cost of standard heads always being 360deg blasts (but we don't have limited angle blasts for missiels at present anyway, so not a bit deal RN)
                    var sourceWeapon = part.FindModuleImplementing<EngageableWeapon>();

                    ExplosionFx.CreateExplosion(part.transform.position, tntMass, explModelPath, explSoundPath, ExplosionSourceType.Missile, caliber, part, SourceVesselName, Team.Name, sourceWeapon != null ? sourceWeapon.GetShortName() : null, direction, -1, false, _warheadType == ExplosionFx.WarheadTypes.Standard ? part.mass : 0, -1, 1, _warheadType, null, apMod);
                    if (BDArmorySettings.DEBUG_MISSILES)
                        Debug.Log($"[BDArmory.BDExplosivePart]: {part} ({(uint)(part.GetInstanceID())}) from {SourceVesselName} (Team:{Team.Name}) detonating with a {_warheadType} warhead");
                    part.explode();
                }
                else
                    if (BDArmorySettings.DEBUG_MISSILES)
                        Debug.Log($"[BDArmory.BDExplosivePart]: {part} ({(uint)(part.GetInstanceID())}) from {SourceVesselName} explosive fuse failed!");
            }
        }

        protected override void Detonate()
        {
            if (!hasDetonated && Armed)
            {
                hasDetonated = true;

                if (fuseFailureRate > 0f)
                    if (Random.Range(0f, 1f) < fuseFailureRate)
                        fuseFailed = true;

                if (!fuseFailed)
                {
                    direction = _warheadType == ExplosionFx.WarheadTypes.Standard ? default : part.partTransform.forward;
                    var sourceWeapon = part.FindModuleImplementing<EngageableWeapon>();
                    if (BDArmorySettings.DEBUG_MISSILES)
                        Debug.Log($"[BDArmory.BDExplosivePart]: {part} ({(uint)(part.GetInstanceID())}) from {SourceVesselName} detonating with a {_warheadType} warhead");
                    ExplosionFx.CreateExplosion(part.transform.position, tntMass, explModelPath, explSoundPath, ExplosionSourceType.Missile, caliber, part, SourceVesselName, Team.Name, sourceWeapon != null ? sourceWeapon.GetShortName() : null, direction, -1, false, _warheadType == ExplosionFx.WarheadTypes.Standard ? part.mass : 0, -1, 1, _warheadType, null, apMod);

                    part.Destroy();
                    part.explode();
                }
                else
                    if (BDArmorySettings.DEBUG_MISSILES)
                        Debug.Log($"[BDArmory.BDExplosivePart]: {part} ({(uint)(part.GetInstanceID())}) from {SourceVesselName} explosive fuse failed!");
            }
        }

        public float GetBlastRadius()
        {
            CalculateBlast();
            return blastRadius;
        }
        protected void SetInitialDetonationDistance()
        {
            if (this.detonationRange == -1)
            {
                if (tntMass != 0)
                {
                    detonationRange = (BlastPhysicsUtils.CalculateBlastRange(tntMass) * 0.66f);
                }
            }
        }
    }
}
