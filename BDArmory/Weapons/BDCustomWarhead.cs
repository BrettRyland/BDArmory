using KSP.Localization;
using System.Linq;
using UnityEngine;

using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.Bullets;
using static BDArmory.Bullets.PooledBullet;

namespace BDArmory.Weapons
{
    public class BDCustomWarhead : BDWarheadBase
    {
        [KSPField]
        public string warheadType = "def";
        public string warheadReportingName;
        public BulletInfo _warheadType;

        [KSPField]
        public string explModelPath = "BDArmory/Models/explosion/explosion";

        [KSPField]
        public string explSoundPath = "BDArmory/Sounds/explode1";

        [KSPField]
        public string smokeTexturePath = "";

        [KSPField]
        public string bulletTexturePath = "BDArmory/Textures/bullet";

        [KSPField]
        public float maxDeviation = -1f;

        [KSPField]
        public float bulletDmgMult = 1f;

        public void ParseWarheadType()
        {
            _warheadType = BulletInfo.bullets[warheadType];
            if (_warheadType.DisplayName != "Default Bullet")
                warheadReportingName = _warheadType.DisplayName;
            else
                warheadReportingName = _warheadType.name;
        }

        private void FireProjectile(float detRange = -1, float detTime = -1)
        {
            SourceInfo sourceInfo = new SourceInfo(vessel, Team.Name, part, transform.position);
            GraphicsInfo graphicsInfo = new GraphicsInfo(bulletTexturePath, _warheadType.projectileColorC, _warheadType.startColorC,
                _warheadType.caliber / 300, _warheadType.caliber / 750, 0, 1.75f, 2.65f, smokeTexturePath, explModelPath, explSoundPath);
            NukeInfo nukeInfo = new NukeInfo(); // Will inherit parent part's models on enable
            float currentSpeed = (float)vessel.Velocity().magnitude;

            if (_warheadType.tntMass > 0 || _warheadType.beehive)
            {
                detRange = detRange < 0 ? detonationRange : detRange;
                detTime = detTime < 0 ? detonationRange / (currentSpeed + _warheadType.bulletVelocity) : detTime;
            }
            if (maxDeviation < 0) maxDeviation = _warheadType.subProjectileDispersion;
            FireBullet(_warheadType, _warheadType.projectileCount, sourceInfo, graphicsInfo, nukeInfo,
                        true, _warheadType.projectileTTL, TimeWarp.fixedDeltaTime, detRange, detTime,
                        false, null, null, false, 1f, bulletDmgMult,
                        true, currentSpeed, 0f, transform.up, true, maxDeviation);
        }

        protected override void WarheadSpecificSetup()
        {
            ParseWarheadType();
        }
        protected override void WarheadSpecificUISetup()
        {

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
                    direction = part.partTransform.forward; //both the missileReferenceTransform and smallWarhead part's forward direction is Z+, or transform.forward.
                                                            // could also do warheadType == "standard" ? default: part.partTransform.forward, as this simplifies the isAngleAllowed check in ExplosionFX, but at the cost of standard heads always being 360deg blasts (but we don't have limited angle blasts for missiels at present anyway, so not a bit deal RN)
                                                            //var sourceWeapon = part.FindModuleImplementing<EngageableWeapon>();
                    FireProjectile();

                    ////////////////////////////////////////////////////
                    if (BDArmorySettings.DEBUG_MISSILES)
                        Debug.Log($"[BDArmory.BDCustomWarhead]: {part} ({(uint)(part.GetInstanceID())}) from {SourceVesselName} (Team:{Team.Name}) detonating with a {warheadType} warhead");
                    part.explode();
                }
                else
                    if (BDArmorySettings.DEBUG_MISSILES)
                        Debug.Log($"[BDArmory.BDCustomWarhead]: {part} ({(uint)(part.GetInstanceID())}) from {SourceVesselName} explosive fuse failed!");
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
                    direction = part.partTransform.forward;
                    FireProjectile();
                    if (BDArmorySettings.DEBUG_MISSILES)
                        Debug.Log($"[BDArmory.BDCustomWarhead]: {part} ({(uint)(part.GetInstanceID())}) from {SourceVesselName} detonating with a {warheadType} warhead");
                    /////////////////////////

                    part.Destroy();
                    part.explode();
                }
                else
                    if (BDArmorySettings.DEBUG_MISSILES)
                        Debug.Log($"[BDArmory.BDCustomWarhead]: {part} ({(uint)(part.GetInstanceID())}) from {SourceVesselName} explosive fuse failed!");
            }
        }
    }
}
