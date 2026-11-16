using UnityEngine;

using BDArmory.Services;

namespace BDArmory.Damage
{
    public enum DamageOperation
    {
        Set = 0,
        Add = 1
    }

    internal class ModuleDamageService : DamageService
    {
        public override void ReduceArmor_svc(Part p, float armorMass)
        {
            var damageModule = p.Modules.GetModule<HitpointTracker>();
            if (!damageModule) return;

            damageModule.ReduceArmor(armorMass);

            PublishEvent(new DamageEventArgs()
            {
                VesselId = p.vessel.GetInstanceID(),
                PartId = p.GetInstanceID(),
                Armor = armorMass,
                Operation = DamageOperation.Set
            });
        }

        public override void SetDamageToPart_svc(Part p, float PartDamage)
        {
            var damageModule = p.Modules.GetModule<HitpointTracker>();
            if (!damageModule) return;

            damageModule.SetDamage(PartDamage);

            PublishEvent(new DamageEventArgs()
            {
                VesselId = p.vessel.GetInstanceID(),
                PartId = p.GetInstanceID(),
                Damage = PartDamage,
                Operation = DamageOperation.Set
            });
        }

        public override void AddDamageToPart_svc(Part p, float PartDamage)
        {
            var damageModule = p.Modules.GetModule<HitpointTracker>();
            if (!damageModule) return;

            damageModule.AddDamage(PartDamage);

            PublishEvent(new DamageEventArgs()
            {
                VesselId = p.vessel.GetInstanceID(),
                PartId = p.GetInstanceID(),
                Damage = PartDamage,
                Operation = DamageOperation.Add
            });
        }
        public override void AddHealthToPart_svc(Part p, float PartDamage, bool overcharge)
        {
            var damageModule = p.Modules.GetModule<HitpointTracker>();
            if (!damageModule) return;

            damageModule.AddHealth(PartDamage, overcharge);

            PublishEvent(new DamageEventArgs()
            {
                VesselId = p.vessel.GetInstanceID(),
                PartId = p.GetInstanceID(),
                Damage = PartDamage,
                Operation = DamageOperation.Add
            });
        }
        public override void AddDamageToKerbal_svc(KerbalEVA kerbal, float damage)
        {
            var damageModule = kerbal.part.Modules.GetModule<HitpointTracker>();
            if (!damageModule) return;

            damageModule.AddDamageToKerbal(kerbal, damage);

            PublishEvent(new DamageEventArgs()
            {
                VesselId = kerbal.part.vessel.GetInstanceID(),
                PartId = kerbal.part.GetInstanceID(),
                Damage = damage,
                Operation = DamageOperation.Add
            });
        }

        public override float GetPartDamage_svc(Part p)
        {
            return p.Modules.GetModule<HitpointTracker>().Hitpoints;
        }

        public override float GetPartArmor_svc(Part p)
        {
            float armor_ = Math.Max(1, p.Modules.GetModule<HitpointTracker>().Armor);
            return armor_;
        }
        public override float GetPartRealArmor_svc(Part p)
        {
            float armor_ = Math.Max(1, p.Modules.GetModule<HitpointTracker>().GetRealArmor());
            return armor_;
        }
        public override float GetPartArmorQtyP_svc(Part p)
        {
            float armorQty_ = Math.Max(0, p.Modules.GetModule<HitpointTracker>().GetArmorQtyP());
            return armorQty_;
        }
        public override float GetPartMaxArmor_svc(Part p)
        {
            float armor_ = Math.Max(1, p.Modules.GetModule<HitpointTracker>().StartingArmor);
            return armor_;
        }
        public override float GetMaxPartDamage_svc(Part p)
        {
            return p.Modules.GetModule<HitpointTracker>().GetMaxHitpoints();
        }

        public override float GetMaxArmor_svc(Part p)
        {
            return p.Modules.GetModule<HitpointTracker>().GetMaxArmor();
        }
        public override float GetArmorDensity_svc(Part p)
        {
            return p.Modules.GetModule<HitpointTracker>().Density;
        }
        public override float GetArmorStrength_svc(Part p)
        {
            return p.Modules.GetModule<HitpointTracker>().Strength;
        }

        public override void DestroyPart_svc(Part p)
        {
            p.Modules.GetModule<HitpointTracker>().DestroyPart();
        }

        public override string GetExplodeMode_svc(Part p)
        {
            return p.Modules.GetModule<HitpointTracker>().ExplodeMode;
        }

        public override bool HasFireFX_svc(Part p)
        {
            if (p == null) return false;
            if (p.Modules.GetModule<HitpointTracker>() == null) return false;

            return p.Modules.GetModule<HitpointTracker>().GetFireFX();
        }

        public override float GetFireFXTimeOut(Part p)
        {
            return p.Modules.GetModule<HitpointTracker>().FireFXLifeTimeInSeconds;
        }
    }
}
