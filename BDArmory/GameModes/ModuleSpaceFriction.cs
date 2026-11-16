using System.Linq;
using UnityEngine;

using BDArmory.Control;
using BDArmory.Settings;
using BDArmory.Utils;
using System.Collections.Generic;
using BDArmory.Extensions;
using System;

namespace BDArmory.GameModes
{
    public class ModuleSpaceFriction : BDAPartModule
    {
        /// <summary>
        /// Adds friction/drag to craft in null-atmo porportional to AI MaxSpeed setting to ensure craft does not exceed said speed
        /// Adds counter-gravity to prevent null-atmo ships from falling to the ground from gravity in the absence of wings and lift
        /// Provides additional friction/drag during corners to help spacecraft drift through turns instead of being stuck with straight-up joust charges
        /// TL;DR, provides the means for SciFi style space dogfights
        /// </summary>

        private double frictionCoeff = 1.0f; //how much force is applied to decelerate craft

        //[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Space Friction"), UI_Toggle(disabledText = "Disabled", enabledText = "Enabled", scene = UI_Scene.All, affectSymCounterparts = UI_Scene.All)]
        //public bool FrictionEnabled = false; //global value

        public bool repulsorActivated = false;

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "#LOC_BDArmory_Settings_Repulsor", active = true)]
        public void ToggleRepulsor()
        {
            repulsorActivated = !repulsorActivated;
            isLanded = part.vessel.LandedOrSplashed;
            targetAlt = 0.1f;
        }
        bool isLanded = true;

        [KSPField(isPersistant = true)]
        public bool AntiGravOverride = false; //per craft override to be set in the .craft file, for things like zeppelin battles where attacking planes shouldn't be under countergrav
        [KSPField(isPersistant = true)]
        public bool RepulsorOverride = false;
        public float maxVelocity = 300; //MaxSpeed setting in PilotAI

        [KSPField(isPersistant = true)]
        public float maxRepulsorMass = 10; //levitate up to 10t per repulsor

        [KSPField(isPersistant = true)]
        public float resourcePerSec = -1;

        [KSPField(isPersistant = true)]
        public string resourceName = "ElectricCharge";
        private int resourceID;

        public float frictMult; //engine thrust of craft

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_VesselMover_Help_AdjustAltitude"),
    UI_FloatRange(minValue = 1f, maxValue = 100, stepIncrement = 1f, scene = UI_Scene.All)]
        public float repulsorAlt = 10;

        float targetAlt = 0.1f;
        //public float driftMult = 2; //additional drag multipler for cornering/decellerating so things don't take the same amount of time to decelerate as they do to accelerate

        [KSPAction("#autoLOC_6001380")]
        public void AGToggleRepulsor(KSPActionParam param)
        {
            ToggleRepulsor();
        }

        List<ModuleWheelBase> repulsors;
        List<ModuleSpaceFriction> spaceFrictionModules;
        public static bool GameIsPaused
        {
            get { return PauseMenu.isOpen || Time.timeScale == 0; }
        }

        ModuleEngines foundEngine
        {
            get
            {
                if (field == null || field.vessel != vessel)
                    field = VesselModuleRegistry.GetModuleEngines(vessel).FirstOrDefault();
                return field;
            }
        }
        MissileFire WeaponManager
        {
            get
            {
                if (field == null || !field.IsPrimaryWM || field.vessel != vessel)
                    field = vessel && vessel.loaded ? vessel.ActiveController().WM : null;
                return field;
            }
        }
        IBDAIControl AI
        {
            get
            {
                if (field == null || !field.pilotEnabled || field.vessel != vessel) field = vessel.ActiveController().AI;
                return field;
            }
        }

        void Start()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (vessel.rootPart == part) //if we're an external non-root repulsor part, don't check for dupes in root.
            {
                foreach (var repMod in vessel.rootPart.FindModulesImplementing<ModuleSpaceFriction>())
                {
                    if (repMod != this)
                    {
                        // Not really sure how this is happening, but it is. It looks a bit like a race condition somewhere is allowing this module to be added twice.
                        Debug.LogWarning($"[BDArmory.GameModes.ModuleSpaceFriction]: Found a duplicate space friction module on root part of {vessel.vesselName}! Removing...");
                        Destroy(repMod);
                    }
                }
            }
            resourceID = PartResourceLibrary.Instance.GetDefinition(resourceName).id;
            if (!RepulsorOverride) //MSF added via Spawn utilities for Space Hacks
            {
                using (var engine = VesselModuleRegistry.GetModuleEngines(vessel).GetEnumerator())
                    while (engine.MoveNext())
                    {
                        if (engine.Current == null) continue;
                        if (engine.Current.independentThrottle) continue; //only grab primary thrust engines
                        frictMult += (engine.Current.maxThrust * (engine.Current.thrustPercentage / 100)); //FIXME - Look into grabbing max thrust from velCurve, if for whatever reason a rocket engine has one of these
                                                                                                           //have this called onvesselModified?
                    }
                frictMult /= 6; //doesn't need to be 100% of thrust at max speed, Ai will already self-limit; this also has the AI throttle down, which allows for slamming the throttle full for braking/coming about, instead of being stuck with lower TwR
                repulsors = VesselModuleRegistry.GetRepulsorModules(vessel);
                using (var r = repulsors.GetEnumerator())
                    while (r.MoveNext())
                    {
                        if (r.Current == null) continue;
                        r.Current.part.PhysicsSignificance = 1;
                    }
            }
            else
            {
                spaceFrictionModules = VesselModuleRegistry.GetModules<ModuleSpaceFriction>(vessel);
            }
        }

        public void FixedUpdate()
        {
            if ((!BDArmorySettings.SPACE_HACKS && (!AntiGravOverride && !RepulsorOverride)) || !HighLogic.LoadedSceneIsFlight || !FlightGlobals.ready || vessel.packed || GameIsPaused) return;

            IBDAIControl ai = AI;
            if (part.vessel.situation == Vessel.Situations.FLYING || part.vessel.situation == Vessel.Situations.SUB_ORBITAL)
            {
                if (BDArmorySettings.SF_FRICTION)
                {
                    if (part.vessel.speed > 10)
                    {
                        if (ai != null)
                        {
                            maxVelocity = ai.aiType switch
                            {
                                AIType.PilotAI => (ai as BDModulePilotAI).maxSpeed,
                                AIType.SurfaceAI => (ai as BDModuleSurfaceAI).MaxSpeed,
                                AIType.VTOLAI => (ai as BDModuleVTOLAI).MaxSpeed,
                                AIType.OrbitalAI => (ai as BDModuleOrbitalAI).ManeuverSpeed,
                                _ => 0
                            };
                        }

                        var speedFraction = (float)part.vessel.speed / maxVelocity;
                        if (speedFraction > 1) speedFraction = Math.Max(2, speedFraction);
                        frictionCoeff = speedFraction * speedFraction * speedFraction * frictMult; //at maxSpeed, have friction be 100% of vessel's engines thrust

                        frictionCoeff *= 1 + VectorUtils.Angle(part.vessel.srf_vel_direction, part.vessel.GetTransform().up) / 180 * BDArmorySettings.SF_DRAGMULT * 4; //greater AoA off prograde, greater drag
                        frictionCoeff /= vessel.Parts.Count;
                        //part.vessel.rootPart.rb.AddForceAtPosition((-part.vessel.srf_vel_direction * frictionCoeff), part.vessel.CoM, ForceMode.Acceleration);
                        using (var p = part.vessel.Parts.GetEnumerator())
                            while (p.MoveNext())
                            {
                                if (p.Current == null || p.Current.PhysicsSignificance == 1) continue;
                                p.Current.Rigidbody.AddForceAtPosition((-part.vessel.srf_vel_direction * frictionCoeff), part.vessel.CoM, ForceMode.Acceleration);
                            }
                    }
                }
                if (BDArmorySettings.SF_GRAVITY || AntiGravOverride) //have this disabled if no engines left?
                {
                    if (WeaponManager != null && foundEngine != null) //have engineless craft fall
                    {
                        using (var p = part.vessel.Parts.GetEnumerator())
                            while (p.MoveNext())
                            {
                                if (p.Current == null || p.Current.PhysicsSignificance == 1) continue; //attempting to apply rigidbody force to non-significant parts will NRE
                                p.Current.Rigidbody.AddForce(-FlightGlobals.getGeeForceAtPosition(p.Current.transform.position), ForceMode.Acceleration);
                            }
                    }
                    else //out of control/engineless craft get hurtled into the ground
                    {
                        using (var p = part.vessel.Parts.GetEnumerator())
                            while (p.MoveNext())
                            {
                                if (p.Current == null || p.Current.PhysicsSignificance == 1) continue;
                                p.Current.Rigidbody.AddForce(FlightGlobals.getGeeForceAtPosition(p.Current.transform.position), ForceMode.Acceleration);
                            }
                    }
                }
            }
            if (!(part.vessel.situation == Vessel.Situations.ORBITING || part.vessel.situation == Vessel.Situations.DOCKED || part.vessel.situation == Vessel.Situations.ESCAPING || part.vessel.situation == Vessel.Situations.PRELAUNCH))
            {
                if ((BDArmorySettings.SF_REPULSOR || RepulsorOverride) && repulsorActivated)
                {
                    if ((ai != null || RepulsorOverride) && foundEngine != null)
                    {
                        repulsorAlt = ai.aiType switch
                        {
                            AIType.PilotAI => (ai as BDModulePilotAI).defaultAltitude, // Use default alt instead of min alt to keep the vessel away from 'gain alt' behaviour.
                            AIType.SurfaceAI => (ai as BDModuleSurfaceAI).MaxSlopeAngle * 2,
                            AIType.VTOLAI => (ai as BDModuleVTOLAI).defaultAltitude,
                            _ => 0
                        };

                        Vector3d grav = FlightGlobals.getGeeForceAtPosition(vessel.CoM);
                        var vesselMass = part.vessel.GetTotalMass();
                        if (RepulsorOverride) //Asking this first, so SPACEHACKS repulsor mode will ignore it
                        {
                            if (isLanded)
                            {
                                targetAlt = Mathf.Lerp(targetAlt, repulsorAlt, 0.02f / 4);
                                if (targetAlt >= repulsorAlt) isLanded = false;
                            }
                            else
                                targetAlt = repulsorAlt;
                            float pointAltitude = BodyUtils.GetRadarAltitudeAtPos(part.transform.position);
                            if (pointAltitude <= 0 || pointAltitude > 2f * targetAlt) return;
                            if (!DrainResource()) return;
                            var factor = Mathf.Clamp(Mathf.Exp(BDArmorySettings.SF_REPULSOR_STRENGTH * (targetAlt - pointAltitude) / targetAlt - (float)vessel.verticalSpeed / targetAlt), 0f, 5f * BDArmorySettings.SF_REPULSOR_STRENGTH); // Decaying exponential balanced at the target altitude with velocity damping.
                            float repulsorForce = Math.Min(vesselMass * factor / spaceFrictionModules.Count, maxRepulsorMass); // Spread the force between the repulsors.
                            if (float.IsNaN(factor) || float.IsInfinity(factor)) // This should only happen if targetAlt is 0, which should never happen.
                                Debug.LogWarning($"[BDArmory.Spacehacks]: Repulsor Force is NaN or Infinity. TargetAlt: {targetAlt}, point Alt: {pointAltitude}, VesselMass: {vesselMass}");
                            else
                                part.Rigidbody.AddForce(-grav * repulsorForce, ForceMode.Force);
                        }
                        else
                        {
                            using (var repulsor = repulsors.GetEnumerator())
                                while (repulsor.MoveNext())
                                {
                                    if (repulsor.Current == null) continue;
                                    float pointAltitude = BodyUtils.GetRadarAltitudeAtPos(repulsor.Current.transform.position);
                                    if (pointAltitude <= 0 || pointAltitude > 2f * targetAlt) continue;
                                    var factor = Mathf.Clamp(Mathf.Exp(BDArmorySettings.SF_REPULSOR_STRENGTH * (targetAlt - pointAltitude) / targetAlt - (float)vessel.verticalSpeed / targetAlt), 0f, 5f * BDArmorySettings.SF_REPULSOR_STRENGTH); // Decaying exponential balanced at the target altitude with velocity damping.
                                    float repulsorForce = vesselMass * factor / repulsors.Count; // Spread the force between the repulsors.
                                    if (float.IsNaN(factor) || float.IsInfinity(factor)) // This should only happen if targetAlt is 0, which should never happen.
                                        Debug.LogWarning($"[BDArmory.Spacehacks]: Repulsor Force is NaN or Infinity. TargetAlt: {targetAlt}, point Alt: {pointAltitude}, VesselMass: {vesselMass}");
                                    else
                                        repulsor.Current.part.Rigidbody.AddForce(-grav * repulsorForce, ForceMode.Force);
                                }
                        }
                    }
                }
            }
        }
        bool DrainResource()
        {
            if (resourcePerSec <= 0)
            {
                return true;
            }

            double drainAmount = resourcePerSec * TimeWarp.fixedDeltaTime;
            double chargeAvailable = part.RequestResource(resourceID, drainAmount, ResourceFlowMode.ALL_VESSEL);
            if (chargeAvailable < drainAmount * 0.95f)
            {
                return false;
            }
            return true;
        }
        public static void AddSpaceFrictionToAllValidVessels()
        {
            foreach (var vessel in FlightGlobals.Vessels)
            {
                if (vessel.ActiveController().WM != null && vessel.rootPart.FindModuleImplementing<ModuleSpaceFriction>() == null)
                {
                    vessel.rootPart.AddModule("ModuleSpaceFriction");
                }
            }
        }
    }
}