using System.Collections.Generic;
using UnityEngine;

using BDArmory.Damage;
using BDArmory.Initialization;
using BDArmory.Settings;
using Expansions.Serenity;
using System.Linq;

namespace BDArmory.Extensions
{
    public enum ExplosionSourceType { Other, Missile, Bullet, Rocket, BattleDamage };
    public static class PartExtensions
    {
        public static void AddDamage(this Part p, float damage) //Fires, lasers, ammo detonations
        {
            if (BDArmorySettings.PAINTBALL_MODE)
            {
                var ti = p.vessel.gameObject.GetComponent<Targeting.TargetInfo>();
                if (!(ti != null && ti.isMissile)) return; // Don't add damage when paintball mode is enabled, except against fired missiles
            }
            damage *= (BDArmorySettings.DMG_MULTIPLIER / 100);
            if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.ZOMBIE_MODE)
            {
                if (p.vessel.rootPart != null)
                {
                    if (p != p.vessel.rootPart)
                    {
                        damage *= BDArmorySettings.ZOMBIE_DMG_MULT;
                    }
                }
            }
            //////////////////////////////////////////////////////////
            // Basic Add Hitpoints for compatibility (only used by lasers & fires)
            //////////////////////////////////////////////////////////

            if (p.GetComponent<KerbalEVA>() != null)
            {
                ApplyHitPoints(p.GetComponent<KerbalEVA>(), damage);
            }
            else
            {
                Dependencies.Get<DamageService>().AddDamageToPart_svc(p, damage);
                if (BDArmorySettings.DEBUG_ARMOR || BDArmorySettings.DEBUG_DAMAGE)
                    Debug.Log($"[BDArmory.PartExtensions]: Standard Hitpoints Applied to {p.name}" + (p.vessel != null ? $" on {p.vessel.vesselName}" : "") + $" : {damage}");
            }
        }

        public static void AddInstagibDamage(this Part p)
        {
            if (p.GetComponent<KerbalEVA>() != null)
            {
                p.Destroy();
            }
            else
            {
                if (p.vessel.rootPart != null)
                {
                    p.vessel.rootPart.Destroy();
                }
                if (BDArmorySettings.DEBUG_ARMOR || BDArmorySettings.DEBUG_DAMAGE)
                    Debug.Log("[BDArmory.PartExtensions]: Instagib!");
            }
        }

        public static float AddExplosiveDamage(this Part p,
                                               float explosiveDamage,
                                               float caliber,
                                               ExplosionSourceType sourceType,
                                               float multiplier = 1) //bullet/rocket/missile explosive damage
        {
            if (BDArmorySettings.PAINTBALL_MODE)
            {
                var ti = p.vessel.gameObject.GetComponent<Targeting.TargetInfo>();
                if (!(ti != null && ti.isMissile)) return 0f; // Don't add damage when paintball mode is enabled, except against fired missiles
            }
            /*
            if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.ZOMBIE_MODE)
            {
                if (p.vessel.rootPart != null)
                {
                    //if (p != p.vessel.rootPart) return 0f;
                }
            }
            */
            if (Utils.ProjectileUtils.IsArmorPart(p)) return 0; //Armor panels don't have HP, so don't score/deal HP damage
            float damage_ = 0f;
            //////////////////////////////////////////////////////////
            // Explosive Hitpoints
            //////////////////////////////////////////////////////////

            damage_ = explosiveDamage * ExplosiveDamageModifier(sourceType, multiplier);

            //////////////////////////////////////////////////////////
            //   Armor Reduction factors
            //////////////////////////////////////////////////////////

            if (p.HasArmor())
            {
                float armorMass_ = p.GetArmorThickness();
                float armorDensity_ = p.GetArmorDensity();
                float armorStrength_ = p.GetArmorSrength();
                float damageReduction = DamageReduction(armorMass_, armorDensity_, armorStrength_, damage_, sourceType, caliber);

                damage_ = damageReduction;
            }
            //////////////////////////////////////////////////////////
            //   Apply Hitpoints
            //////////////////////////////////////////////////////////

            if (p.GetComponent<KerbalEVA>() != null)
            {
                ApplyHitPoints(p.GetComponent<KerbalEVA>(), (float)damage_);
            }
            else
            {
                if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.ZOMBIE_MODE)
                {
                    if (p.vessel.rootPart != null)
                    {
                        if (p != p.vessel.rootPart)
                        {
                            damage_ *= BDArmorySettings.ZOMBIE_DMG_MULT;
                        }
                    }
                }
                ApplyHitPoints(p, damage_);
            }
            return damage_;
        }

        /// <summary>
        /// Get the appropriate modifier for explosive damage of the given type and multiplier.
        /// </summary>
        /// <param name="sourceType"></param>
        /// <param name="multiplier"></param>
        /// <returns></returns>
        public static float ExplosiveDamageModifier(ExplosionSourceType sourceType, float multiplier = 1f)
        {
            return BDArmorySettings.DMG_MULTIPLIER / 100f *
            (sourceType switch
            {
                ExplosionSourceType.Bullet => BDArmorySettings.EXP_DMG_MOD_BALLISTIC_NEW * multiplier,
                ExplosionSourceType.Rocket => BDArmorySettings.EXP_DMG_MOD_ROCKET * multiplier,
                ExplosionSourceType.Missile => BDArmorySettings.EXP_DMG_MOD_MISSILE * multiplier,
                ExplosionSourceType.BattleDamage => BDArmorySettings.EXP_DMG_MOD_BATTLE_DAMAGE,
                _ => 1f
            });
        }

        //bullet/rocket kinetic damage
        public static float AddBallisticDamage(this Part p,
                                               float mass,
                                               float caliber,
                                               float multiplier,
                                               float penetrationfactor,
                                               float bulletDmgMult,
                                               float impactVelocity,
                                               ExplosionSourceType sourceType,
                                               bool explosionProjectile = false) // "explosionProjectile" specifically is intended for fragments and projectiles that come from explosions
        {
            if (BDArmorySettings.PAINTBALL_MODE)
            {
                var ti = p.vessel.gameObject.GetComponent<Targeting.TargetInfo>();
                if (!(ti != null && ti.isMissile)) return 0f; // Don't add damage when paintball mode is enabled, except against fired missiles
            }
            /*
            if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.ZOMBIE_MODE)
            {
                if (p.vessel.rootPart != null)
                {
                    if (p != p.vessel.rootPart) return 0f;
                }
            }
            */
            if (Utils.ProjectileUtils.IsArmorPart(p)) return 0; //Armor panels don't have HP, so don't score/deal HP damage
            //////////////////////////////////////////////////////////
            // Basic Kinetic Formula
            //////////////////////////////////////////////////////////
            //Hitpoints mult for scaling in settings
            //1e-4 constant for adjusting MegaJoules for gameplay

            float damage_;
            if (!explosionProjectile)
            {
                switch (sourceType)
                {
                    case ExplosionSourceType.Rocket:
                        damage_ = (0.5f * (mass * impactVelocity * impactVelocity))
                                * (BDArmorySettings.DMG_MULTIPLIER / 100) * bulletDmgMult * multiplier
                                * 1e-4f * BDArmorySettings.BALLISTIC_DMG_FACTOR;
                        break;
                    case ExplosionSourceType.BattleDamage:
                        damage_ = (0.5f * (mass * impactVelocity * impactVelocity))
                                * (BDArmorySettings.DMG_MULTIPLIER / 100) * bulletDmgMult * multiplier
                                * 1e-4f * BDArmorySettings.EXP_DMG_MOD_BATTLE_DAMAGE;
                        break;
                    case ExplosionSourceType.Bullet:
                        damage_ = (0.5f * (mass * impactVelocity * impactVelocity))
                                * (BDArmorySettings.DMG_MULTIPLIER / 100) * bulletDmgMult * multiplier
                                * 1e-4f * BDArmorySettings.BALLISTIC_DMG_FACTOR;
                        break;
                    default: // Other?    
                        damage_ = (0.5f * (mass * impactVelocity * impactVelocity))
                                * (BDArmorySettings.DMG_MULTIPLIER / 100) * bulletDmgMult * multiplier
                                * 1e-4f * BDArmorySettings.BALLISTIC_DMG_FACTOR;
                        break;
                }
            }
            else
            {
                damage_ = (0.5f * (mass * impactVelocity * impactVelocity))
                        * ExplosiveDamageModifier(sourceType, bulletDmgMult * multiplier)
                        * 1e-4f;
            }

            //////////////////////////////////////////////////////////
            //   Armor Reduction factors
            //////////////////////////////////////////////////////////

            if (p.HasArmor())
            {
                float armorMass_ = p.GetArmorThickness();
                float armorDensity_ = p.GetArmorDensity();
                float armorStrength_ = p.GetArmorSrength();
                float damageReduction = DamageReduction(armorMass_, armorDensity_, armorStrength_, damage_, ExplosionSourceType.Bullet, caliber, penetrationfactor);

                damage_ = damageReduction;
            }
            //////////////////////////////////////////////////////////
            //   Apply Hitpoints
            //////////////////////////////////////////////////////////

            if (p.GetComponent<KerbalEVA>() != null)
            {
                ApplyHitPoints(p.GetComponent<KerbalEVA>(), (float)damage_);
            }
            else
            {
                if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.ZOMBIE_MODE)
                {
                    if (p.vessel.rootPart != null)
                    {
                        if (p != p.vessel.rootPart)
                        {
                            damage_ *= BDArmorySettings.ZOMBIE_DMG_MULT;
                        }
                    }
                }
                ApplyHitPoints(p, damage_, caliber, mass, multiplier, impactVelocity, penetrationfactor);
            }
            return damage_;
        }

        /// <summary>
        /// Ballistic Hitpoint Damage
        /// </summary>
        public static void ApplyHitPoints(Part p, float damage_, float caliber, float mass, float multiplier, float impactVelocity, float penetrationfactor)
        {
            //////////////////////////////////////////////////////////
            // Apply HitPoints Ballistic
            //////////////////////////////////////////////////////////
            Dependencies.Get<DamageService>().AddDamageToPart_svc(p, damage_);
            if (BDArmorySettings.DEBUG_ARMOR || BDArmorySettings.DEBUG_DAMAGE)
            {
                Debug.Log("[BDArmory.PartExtensions]: mass: " + mass + " caliber: " + caliber + " multiplier: " + multiplier + " velocity: " + impactVelocity + " penetrationfactor: " + penetrationfactor);
            }
        }
        public static void AddHealth(this Part p, float healing, bool overcharge = false)
        {
            if (p.GetComponent<KerbalEVA>() != null)
            {
                ApplyHitPoints(p.GetComponent<KerbalEVA>(), healing);
            }
            else
            {
                Dependencies.Get<DamageService>().AddHealthToPart_svc(p, healing, overcharge);
                if (BDArmorySettings.DEBUG_ARMOR || BDArmorySettings.DEBUG_DAMAGE)
                    Debug.Log($"[BDArmory.PartExtensions]: Standard Hitpoints Restored to {p.name}" + (p.vessel != null ? $" on {p.vessel.vesselName}" : "") + $" : {healing}");
            }
        }
        /// <summary>
        /// Explosive Hitpoint Damage
        /// </summary>
        public static void ApplyHitPoints(Part p, float damage)
        {
            //////////////////////////////////////////////////////////
            // Apply Hitpoints / Explosive
            //////////////////////////////////////////////////////////

            Dependencies.Get<DamageService>().AddDamageToPart_svc(p, damage);
            if (BDArmorySettings.DEBUG_ARMOR || BDArmorySettings.DEBUG_DAMAGE)
                Debug.Log("[BDArmory.PartExtensions]: Explosive Hitpoints Applied to " + p.name + ": " + damage);
        }

        /// <summary>
        /// Kerbal Hitpoint Damage
        /// </summary>
        public static void ApplyHitPoints(KerbalEVA kerbal, float damage)
        {
            //////////////////////////////////////////////////////////
            // Apply Hitpoints / Kerbal
            //////////////////////////////////////////////////////////

            Dependencies.Get<DamageService>().AddDamageToKerbal_svc(kerbal, damage);
            if (BDArmorySettings.DEBUG_ARMOR || BDArmorySettings.DEBUG_DAMAGE)
                Debug.Log("[BDArmory.PartExtensions]: Hitpoints Applied to " + kerbal.name + ": " + damage);
        }

        public static void AddForceToPart(Rigidbody rb, Vector3 force, Vector3 position, ForceMode mode)
        {
            //////////////////////////////////////////////////////////
            // Add The force to part
            //////////////////////////////////////////////////////////

            if (rb == null || rb.mass == 0) return;
            rb.AddForceAtPosition(force, position, mode);
            Debug.Log("[BDArmory.PartExtensions]: Force Applied : " + force.magnitude);
        }

        public static void Destroy(this Part p)
        {
            Dependencies.Get<DamageService>().SetDamageToPart_svc(p, -1);
        }

        public static bool HasArmor(this Part p)
        {
            return Mathf.FloorToInt(p.GetArmorThickness()) > 0f;
        }

        public static bool GetFireFX(this Part p)
        {
            return Dependencies.Get<DamageService>().HasFireFX_svc(p);
        }

        public static float GetFireFXTimeOut(this Part p)
        {
            return Dependencies.Get<DamageService>().GetFireFXTimeOut(p);
        }

        public static float Damage(this Part p)
        {
            return Dependencies.Get<DamageService>().GetPartDamage_svc(p);
        }

        public static float MaxDamage(this Part p)
        {
            return Dependencies.Get<DamageService>().GetMaxPartDamage_svc(p);
        }

        public static void ReduceArmor(this Part p, double massToReduce)
        {
            if (!p.HasArmor()) return;
            //massToReduce = Math.Max(0.10, Math.Round(massToReduce, 2));
            Dependencies.Get<DamageService>().ReduceArmor_svc(p, (float)massToReduce);

            if (BDArmorySettings.DEBUG_ARMOR)
            {
                //Debug.Log("[BDArmory.PartExtensions]: Armor volume Removed : " + massToReduce);
            }
        }

        public static float GetArmorThickness(this Part p)
        {
            if (p == null) return 0f;
            float armorthickness = Dependencies.Get<DamageService>().GetPartArmor_svc(p);
            if (float.IsNaN(armorthickness))
            {
                if (BDArmorySettings.DEBUG_ARMOR) Debug.Log("[BDArmory.PartExtensions]: GetArmorThickness; thickness is NaN");
                return 0f;
            }
            else
            {
                //if (BDArmorySettings.DEBUG_ARMOR) Debug.Log("[BDArmory.PartExtensions]: GetArmorThickness; thickness is: " + armorthickness);
                return armorthickness;
            }
            //return Dependencies.Get<DamageService>().GetPartArmor_svc(p);
        }

        public static float GetRealArmorThickness(this Part p)
        {
            if (p == null) return 0f;
            float realArmor = Dependencies.Get<DamageService>().GetPartRealArmor_svc(p);
            if (float.IsNaN(realArmor))
            {
                if (BDArmorySettings.DEBUG_ARMOR) Debug.Log("[BDArmory.PartExtensions]: GetRealArmorThickness; realArmor is NaN");
                return 0f;
            }
            else
            {
                return realArmor;
            }
        }

        public static float GetRealArmorPercentage(this Part p)
        {
            if (p == null) return 0f;
            float armorQtyP = Dependencies.Get<DamageService>().GetPartArmorQtyP_svc(p);
            if (float.IsNaN(armorQtyP))
            {
                if (BDArmorySettings.DEBUG_ARMOR) Debug.Log("[BDArmory.PartExtensions]: GetRealArmorPercentage; armorQtyP is NaN");
                return 0f;
            }
            else
            {
                return armorQtyP;
            }
        }

        public static float GetArmorMaxThickness(this Part p)
        {
            if (p == null) return 0f;
            return Dependencies.Get<DamageService>().GetPartMaxArmor_svc(p);
        }
        public static float GetArmorDensity(this Part p)
        {
            if (p == null) return 0f;
            return Dependencies.Get<DamageService>().GetArmorDensity_svc(p);
        }
        public static float GetArmorSrength(this Part p)
        {
            if (p == null) return 0f;
            return Dependencies.Get<DamageService>().GetArmorStrength_svc(p);
        }

        public static float GetArmorPercentage(this Part p)
        {
            if (p == null) return 0;
            float armor_ = Dependencies.Get<DamageService>().GetPartArmor_svc(p);
            float maxArmor_ = Dependencies.Get<DamageService>().GetMaxArmor_svc(p);

            return armor_ / maxArmor_;
        }

        public static float GetDamagePercentage(this Part p)
        {
            if (p == null) return 0;

            float damage_ = p.Damage();
            float maxDamage_ = p.MaxDamage();

            return damage_ / maxDamage_;
        }
       
        public static bool IsMissile(this Part part)
        {
            if (part == null || part.Modules == null) return false;
            if (part.Modules.Contains("BDModularGuidance")) return true;
            if (part.Modules.Contains("MissileBase") || part.Modules.Contains("MissileLauncher"))
            {
                if (!part.Modules.Contains("MultiMissileLauncher")) return true;
                IEnumerator<PartModule> partModules = part.Modules.GetEnumerator();
                while (partModules.MoveNext())
                {
                    if (partModules.Current.moduleName == "MultiMissileLauncher")
                    {
                        return ((Weapons.Missiles.MultiMissileLauncher)partModules.Current).isClusterMissile;
                    }
                }
                //return ((part.Modules.Contains("MissileBase") || part.Modules.Contains("MissileLauncher") ||
                //      part.Modules.Contains("BDModularGuidance"))
            }
            return false;
        }
        public static bool IsWeapon(this Part part)
        {
            return part.Modules.Contains("ModuleWeapon");
        }
        public static bool IsFunctional(this Part part)
        {
            return (part.isEngine() || part.isAntenna(out ModuleDeployableAntenna antenna) || part.isBaseServo(out BaseServo serv) || part.isDecoupler(out ModuleDecouple decoupler) || part.isGenerator(out ModuleGenerator gen)
                || part.isRadiator(out ModuleDeployableRadiator rad) || part.isRobotic() || part.isRoboticHinge() || part.isRoboticPiston() || part.isRoboticRotationServo() || part.isRoboticRotor()
                || part.Modules.Contains("ModuleGrappleNode") || part.Modules.Contains("ModuleResourceConverter") || part.Modules.Contains("ModuleCoreHeat"));
        }
        public static float GetArea(this Part part, bool isprefab = false, Part prefab = null)
        {
            var size = part.GetSize();
            float sfcAreaCalc = 2f * (size.x * size.y) + 2f * (size.y * size.z) + 2f * (size.x * size.z);

            return sfcAreaCalc;
        }

        public static float GetAverageBoundSize(this Part part)
        {
            var size = part.GetSize();

            return (size.x + size.y + size.z) / 3f;
        }

        public static float GetVolume(this Part part)
        {
            var size = part.GetSize();
            var volume = size.x * size.y * size.z;
            return volume;
        }

        public static Vector3 GetSize(this Part part)
        {
            var meshFilter = part.GetComponentInChildren<MeshFilter>();
            if (meshFilter == null)
            {
                Debug.LogWarning($"[BDArmory.PartExtension]: {part.name} has no MeshFilter! Returning zero size.");
                return Vector3.zero;
            }
            var size = meshFilter.mesh.bounds.size;

            // if (part.name.Contains("B9.Aero.Wing.Procedural")) // Covered by SuicidalInsanity's patch.
            // {
            //     size = size * 0.1f;
            // }

            float scaleMultiplier = part.GetTweakScaleMultiplier();
            return size * scaleMultiplier;
        }

        private static bool tweakScaleChecked = false;
        private static bool tweakScaleInstalled
        {
            get
            {
                if (!tweakScaleChecked) CheckTweakScaleInstalled();
                return field;
            }
            set;
        }
        private static void CheckTweakScaleInstalled()
        {
            foreach (var assy in AssemblyLoader.loadedAssemblies)
                if (assy.assembly.FullName.Contains("TweakScale"))
                    tweakScaleInstalled = true;
            tweakScaleChecked = true;
        }
        public static float GetTweakScaleMultiplier(this Part part)
        {
            float scaleMultiplier = 1f;
            if (tweakScaleInstalled && part.Modules.Contains("TweakScale"))
            {
                var tweakScaleModule = part.Modules["TweakScale"];
                var currentScale = tweakScaleModule.Fields["currentScale"].GetValue<float>(tweakScaleModule);
                var defaultScale = tweakScaleModule.Fields["defaultScale"].GetValue<float>(tweakScaleModule);
                scaleMultiplier = currentScale / defaultScale;
            }
            return scaleMultiplier;
        }
        public static float GetTweakScaleMassMultiplier(this Part part)
        {
            float massMultiplier = 1f;
            if (tweakScaleInstalled && part.Modules.Contains("TweakScale"))
            {
                var tweakScaleModule = part.Modules["TweakScale"];
                var massMultiplierField = tweakScaleModule.Fields["MassScale"];
                if (massMultiplierField == null)
                {
                    float linScale = part.GetTweakScaleMultiplier();
                    return linScale * linScale * linScale; // Hacked together support for TweakScale Rescaled
                    // This is gonna be inaccurate for *many* parts
                }
                massMultiplier = massMultiplierField.GetValue<float>(tweakScaleModule);
            }
            return massMultiplier;
        }
        public static float GetTweakScaleDryCost(this Part part)
        {
            float dryCost = 0;
            if (tweakScaleInstalled && part.Modules.Contains("TweakScale"))
            {
                var tweakScaleModule = part.Modules["TweakScale"];
                var dryCostField = tweakScaleModule.Fields["DryCost"];
                if (dryCostField == null)
                {
                    return -1;
                }
                dryCost = dryCostField.GetValue<float>(tweakScaleModule);
            }
            return dryCost;
        }

        public static bool IsAero(this Part part)
        {
            if (part.Modules.Contains("ModuleLiftingSurface") || part.Modules.Contains("FARWingAerodynamicModel"))
            {
                if (part.name.Contains("mk2") || part.name.Contains("Mk2") || part.name.Contains("M2X") || part.name.Contains("HeatShield")) // don't grab Mk2 parts or heatshields. Caps-sensitive
                {
                    return false;
                }
                if (part.partInfo.bulkheadProfiles.Contains("mk2")) return false;
                else return true;
            }
            else if (part.Modules.Contains("ModuleControlSurface") ||
                   part.Modules.Contains("FARControllableSurface"))
            {
                return true;
            }
            else return false;
        }
        public static bool IsMotor(this Part part)
        {
            if (part.GetComponent<ModuleEngines>() != null)
                return true;
            else return false;
        }
        public static string GetExplodeMode(this Part part)
        {
            return Dependencies.Get<DamageService>().GetExplodeMode_svc(part);
        }

        public static bool IgnoreDecal(this Part part)
        {
            if (
                part.Modules.Contains("FSplanePropellerSpinner") ||
                part.Modules.Contains("ModuleWheelBase") ||
                part.Modules.Contains("KSPWheelBase") ||
                part.gameObject.GetComponentUpwards<KerbalEVA>() ||
                part.Modules.Contains("ModuleReactiveArmor") ||
                part.Modules.Contains("ModuleDCKShields") ||
                part.Modules.Contains("ModuleShieldGenerator")
                )
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public static bool HasFuel(this Part part)
        {
            bool hasFuel = false;
            using (IEnumerator<PartResource> resources = part.Resources.GetEnumerator())
                while (resources.MoveNext())
                {
                    if (resources.Current == null) continue;
                    switch (resources.Current.resourceName)
                    {
                        case "LiquidFuel":
                            if (resources.Current.amount > 1d) hasFuel = true;
                            break;
                        case "Oxidizer":
                            if (resources.Current.amount > 1d) hasFuel = true;
                            break;
                        case "MonoPropellant":
                            if (resources.Current.amount > 1d) hasFuel = true;
                            break;
                    }
                }
            return hasFuel;
        }

        public static float DamageReduction(float armor, float density, float strength, float damage, ExplosionSourceType sourceType, float caliber = 0, float penetrationfactor = 0)
        {
            float _damageReduction;

            switch (sourceType)
            {
                case ExplosionSourceType.Missile:
                    //damage *= Mathf.Clamp(-0.0005f * armor + 1.025f, 0f, 0.5f); // Cap damage reduction at 50% (armor = 1050)					
                    if (BDArmorySettings.DEBUG_ARMOR)
                    {
                        Debug.Log("[BDArmory.PartExtensions]: Damage Before Reduction : "
                            + damage + "; Damage Reduction (%) : " + 1 + (((strength * (density / 1000)) * armor) / 1000000)
                            + "; Damage After Armor : " + (damage / (1 + (((strength * (density / 1000)) * armor) / 1000000))));
                    }
                    damage /= 1 + (((strength * (density / 1000)) * armor) / 1000000); //500mm of DU yields about 95% reduction, 500mm steel = 80% reduction, Aramid = 73% reduction, if explosion makes it past armor

                    break;

                case ExplosionSourceType.BattleDamage:
                    //identical to missile for now, since fuel/ammo explosions can be mitigated by armor mass			
                    if (BDArmorySettings.DEBUG_ARMOR)
                    {
                        Debug.Log("[BDArmory.PartExtensions]: Damage Before Reduction : "
                            + damage + "; Damage Reduction (%) : " + 1 + (((strength * (density / 1000)) * armor) / 1000000)
                            + "; Damage After Armor : " + (damage / (1 + (((strength * (density / 1000)) * armor) / 1000000))));
                    }
                    damage /= 1 + (((strength * (density / 1000)) * armor) / 1000000); //500mm of DU yields about 95% reduction, 500mm steel = 80% reduction, Aramid = 73% reduction

                    break;
                default:
                    if (!(penetrationfactor >= 1f))
                    {
                        //if (BDAMath.Between(armor, 100f, 200f))
                        //{
                        //    damage *= 0.300f;
                        //}
                        //else if (BDAMath.Between(armor, 200f, 400f))
                        //{
                        //    damage *= 0.250f;
                        //}
                        //else if (BDAMath.Between(armor, 400f, 500f))
                        //{
                        //    damage *= 0.200f;
                        //}

                        //y=(98.34817*x)/(97.85935+x)

                        _damageReduction = (113 * armor) / (154 + armor); //should look at this later, review?

                        if (BDArmorySettings.DEBUG_ARMOR)
                        {
                            Debug.Log("[BDArmory.PartExtensions]: Damage Before Reduction : " + damage
                                + "; Damage Reduction (%) : " + 100 * (1 - Mathf.Clamp01((113f - _damageReduction) / 100f))
                                + "; Damage After Armor : " + (damage * Mathf.Clamp01((113f - _damageReduction) / 100f)));
                        }

                        damage *= Mathf.Clamp01((113f - _damageReduction) / 100f);
                    }
                    break;
            }

            return damage;
        }

        public static bool isBattery(this Part part)
        {
            bool hasEC = false;
            using (IEnumerator<PartResource> resources = part.Resources.GetEnumerator())
                while (resources.MoveNext())
                {
                    if (resources.Current == null) continue;
                    switch (resources.Current.resourceName)
                    {
                        case "ElectricCharge":
                            if (resources.Current.amount > 1d) hasEC = true; //discount trace EC in alternators
                            break;
                    }
                }
            return hasEC;
        }
        public static Vector3 GetBoundsSize(Part part)
        {
            return PartGeometryUtil.MergeBounds(part.GetRendererBounds(), part.transform).size;
        }

        /// <summary>
        /// KSP version dependent query of whether the part is a kerbal on EVA.
        /// </summary>
        /// <param name="part">Part to check.</param>
        /// <returns>true if the part is a kerbal on EVA.</returns>
        public static bool IsKerbalEVA(this Part part)
        {
            if (part == null) return false;
            if ((Versioning.version_major == 1 && Versioning.version_minor > 10) || Versioning.version_major > 1) // Introduced in 1.11
            {
                return part.IsKerbalEVA_1_11();
            }
            else
            {
                return part.IsKerbalEVA_1_10();
            }
        }

        private static bool IsKerbalEVA_1_11(this Part part) // KSP has issues on older versions if this call is in the parent function.
        {
            return part.isKerbalEVA();
        }

        private static bool IsKerbalEVA_1_10(this Part part)
        {
            return part.FindModuleImplementing<KerbalEVA>() != null;
        }

        /// <summary>
        /// KSP version dependent query of whether the part is a kerbal seat.
        /// </summary>
        /// <param name="part">Part to check.</param>
        /// <returns>true if the part is a kerbal seat.</returns>
        public static bool IsKerbalSeat(this Part part)
        {
            if (part == null) return false;
            if ((Versioning.version_major == 1 && Versioning.version_minor > 10) || Versioning.version_major > 1) // Introduced in 1.11
            {
                return part.IsKerbalSeat_1_11();
            }
            else
            {
                return part.IsKerbalSeat_1_10();
            }
        }

        private static bool IsKerbalSeat_1_11(this Part part) // KSP has issues on older versions if this call is in the parent function.
        {
            return part.isKerbalSeat();
        }

        private static bool IsKerbalSeat_1_10(this Part part)
        {
            return part.FindModuleImplementing<KerbalSeat>() != null;
        }
    
        /// <summary>
        /// Check if a part is attached to the ship in the SPH.
        /// </summary>
        /// <param name="part"></param>
        /// <returns></returns>
        public static bool ConnectedToShip(this Part part)
        {
            if (!HighLogic.LoadedSceneIsEditor) return false;
            var ship = EditorLogic.fetch.ship;
            if (ship is null) return false;
            var shipRoot = ship.Parts.First();
            while (shipRoot.parent is not null) shipRoot = shipRoot.parent;
            while (part.parent is not null) part = part.parent;
            return part == shipRoot;
        }
    }
}