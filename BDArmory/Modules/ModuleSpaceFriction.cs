using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BDArmory.Core;
using UnityEngine;

namespace BDArmory.Modules
{
    public class ModuleSpaceFriction : PartModule
    {
    /// <summary>
    /// Adds friction/drag to craft in null-atmo porportional to AI MaxSpeed setting to ensure craft does not exceed said speed
    /// Adds counter-gravity to prevent null-atmo ships from falling to the ground from gravity in the absence of wings and lift
    /// Provides additional friction/drag during corners to help spacecraft drift through turns instead of being stuck with straight-up joust charges
    /// TL;DR, provides the means for SciFi style space dogfights
    /// </summary>

        private double frictionCoeff = 1.0f; //how much force is applied to decellerate craft

        //[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Space Friction"), UI_Toggle(disabledText = "Disabled", enabledText = "Enabled", scene = UI_Scene.All, affectSymCounterparts = UI_Scene.All)]
        //public bool FrictionEnabled = false; //global value

        //[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "CounterGrav"), UI_Toggle(disabledText = "Disabled", enabledText = "Enabled", scene = UI_Scene.All, affectSymCounterparts = UI_Scene.All)]
        //public bool AntiGravEnabled = false; //global value

        [KSPField(isPersistant = true)]
        public bool AntiGravOverride = false; //per craft override to be set in the .craft file, for things like zeppelin battles where attacking planes shouldn't be under countergrav

        public float maxVelocity = 300; //MaxSpeed setting in PilotAI

        public float frictMult; //engine thrust of craft

        float vesselAlt = 25;
        //public float driftMult = 2; //additional drag multipler for cornering/decellerating so things don't take the same amount of time to decelerate as they do to accelerate

        private bool BDAcraft = false; //check to see if craft is active BDA craft, if not, unaffected by countergrav so debris crashes, etc
        public static bool GameIsPaused
        {
            get { return PauseMenu.isOpen || Time.timeScale == 0; }
        }
        BDModulePilotAI AI;
        public BDModulePilotAI pilot
        {
            get
            {
                if (AI) return AI;
                AI = VesselModuleRegistry.GetBDModulePilotAI(vessel, true); // FIXME should this be IBDAIControl?
                return AI;
            }
        }
        BDModuleSurfaceAI SAI;
        public BDModuleSurfaceAI driver
        {
            get
            {
                if (SAI) return SAI;
                SAI = VesselModuleRegistry.GetBDModuleSurfaceAI(vessel, true); 
                return SAI;
            }
        }
        ModuleEngines Engine;
        public ModuleEngines foundEngine
        {
            get
            {
                if (Engine) return Engine;
                Engine = VesselModuleRegistry.GetModuleEngines(vessel).FirstOrDefault();
                return Engine;
            }
        }
        void Start()
        {
            /*
            if (HighLogic.LoadedSceneIsEditor)
            {
                if (pilot != null)
                {
                    Fields["FrictionEnabled"].guiActive = false;
                    Fields["FrictionEnabled"].guiActiveEditor = false;
                    Fields["AntiGravEnabled"].guiActive = false;
                    Fields["AntiGravEnabled"].guiActiveEditor = false;
                }
            }*/
            if (HighLogic.LoadedSceneIsFlight)
            {
                using (var engine = VesselModuleRegistry.GetModules<ModuleEngines>(vessel).GetEnumerator())
                    while (engine.MoveNext())
                    {
                        if (engine.Current == null) continue;
                            frictMult += (engine.Current.maxThrust * (engine.Current.thrustPercentage/100)); 
                        //have this called onvesselModified?
                    }
                if (pilot != null || driver != null)
                {
                    BDAcraft = true;
                    Debug.Log("[SpaceFriction] AI is: " + pilot + "; SAI is" + driver);
                }

                //add some additional code to pilot Ai so if in null atmo, can do broadside approach (steal from surfaceAI)?
                //not sure how to implement it, since if this kicks on at runtime, and doesn't ahve user-accesible toggles...
                //alt plan would be build a new Space AI class that has the frict module at its core, and is basically 90% pilotAi with some surface Ai stuff added
                //if that route taken, would need to find everything that looks for AIs and add in the space AI so it's properly reconnized
                //if (BDArmorySettings.DRAW_DEBUG_LABELS) 
                //Debug.Log("[Spacehacks] frictMult for " + part.vessel.GetName() + " is " + frictMult.ToString("0.00"));
            }
        }

        public void FixedUpdate()
        {
            if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && !this.vessel.packed && !GameIsPaused)
            {
                if (this.part.vessel.situation == Vessel.Situations.FLYING || this.part.vessel.situation == Vessel.Situations.SUB_ORBITAL)
                {
                    if (BDArmorySettings.SF_FRICTION)
                    {
                        if (this.part.vessel.speed > 10)
                        {
                            if (AI != null)
                            {
                                maxVelocity = AI.maxSpeed;
                            }
                            else if (SAI != null)
                            {
                                maxVelocity = SAI.MaxSpeed;
                            }
                            frictionCoeff = Mathf.Pow(((float)part.vessel.speed / maxVelocity), 3) * frictMult; //at maxSpeed, have friction be 100% of vessel's engines thrust

                            frictionCoeff *= (1 + (Vector3.Angle(this.part.vessel.srf_vel_direction, this.part.vessel.GetTransform().up) / 180) * BDArmorySettings.SF_DRAGMULT); //greater AoA off prograde, greater drag

                            part.vessel.rootPart.rb.AddForceAtPosition((-part.vessel.srf_vel_direction * frictionCoeff), part.vessel.CoM, ForceMode.Acceleration);
                        }
                    }
                    if (BDArmorySettings.SF_GRAVITY || AntiGravOverride) //have this disabled if no engines left?
                    {
                        if ((BDAcraft && (pilot != null || driver != null || foundEngine != null)) || //have pilotless/engineless craft fall
                            (!BDAcraft))
                        {
                            for (int i = 0; i < part.vessel.Parts.Count; i++)
                            {
                                if (part.vessel.parts[i].PhysicsSignificance != 1) //attempting to apply rigidbody force to non-significant parts will NRE
                                {
                                    part.vessel.Parts[i].Rigidbody.AddForce(-FlightGlobals.getGeeForceAtPosition(part.vessel.Parts[i].transform.position), ForceMode.Acceleration);
                                }
                            }
                        }
                    }
                }
                if (this.part.vessel.situation != Vessel.Situations.ORBITING || this.part.vessel.situation != Vessel.Situations.DOCKED || this.part.vessel.situation != Vessel.Situations.ESCAPING || this.part.vessel.situation != Vessel.Situations.PRELAUNCH)
                {
                    if (BDArmorySettings.SF_REPULSOR)
                    {
                        if ((BDAcraft && (pilot != null || driver != null)) || //have pilotless craft fall
                            (!BDAcraft))
                        {
                            vesselAlt = 10;
                            if (AI != null)
                            {
                                vesselAlt = AI.minAltitude;
                            }
                            else if (SAI != null)
                            {
                                vesselAlt = SAI.MaxSlopeAngle * 2;
                            }
                            float accelMult = 1f;
                            if (vessel.verticalSpeed > 1) //vessel ascending
                            {
                                accelMult = Mathf.Clamp(Mathf.Abs((float)vessel.verticalSpeed), 1f, 100);
                            }
                            if (vessel.radarAltitude < Mathf.Max((vesselAlt / 10), 5))
                            {
                                accelMult = Mathf.Clamp((float)vessel.radarAltitude / vesselAlt, 0.3f, 1);
                            }
                            if (vessel.radarAltitude < vesselAlt)
                            {
                                for (int i = 0; i < part.vessel.Parts.Count; i++)
                                {
                                    if (part.vessel.parts[i].PhysicsSignificance != 1) //attempting to apply rigidbody force to non-significant parts will NRE
                                    {
                                        part.vessel.Parts[i].Rigidbody.AddForce((-FlightGlobals.getGeeForceAtPosition(part.vessel.Parts[i].transform.position) * ((vesselAlt / part.vessel.radarAltitude)) / accelMult), ForceMode.Acceleration);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
