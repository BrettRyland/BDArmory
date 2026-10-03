using BDArmory.Competition;
using BDArmory.Control;
using BDArmory.Extensions;
using BDArmory.Radar;
using BDArmory.Settings;
using BDArmory.Targeting;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.WeaponMounts;
using BDArmory.Weapons;
using BDArmory.Weapons.Missiles;
using Expansions.Serenity;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BDArmory.Damage
{
    public class ModuleDrainEC : BDAPartModule
    {
        public float incomingDamage = 0; //damage from EMP source
        public float EMPDamage = 0; //total EMP buildup accrued
        float EMPThreshold = 100; //craft get temporarily disabled
        float BrickThreshold = 1000; //craft get permanently bricked
        public bool softEMP = true; //can EMPdamage exceed EMPthreshold?
        private bool disabled = false; //prevent further EMP buildup while rebooting
        public bool bricked = false; //He's dead, jeb
        public bool isMissile = false;
        private float rebootTimer = 15;
        //if for whatever reason players are manually firing EMPs at targets with AI/WM disabled, don't enable them when vessel reboots
        private IBDAIControl activeAI = null;
        private List<MissileFire> activeWMs = [];
        private List<ModuleRoboticServoHinge> activeHinges = [];
        private List<ModuleRoboticRotationServo> activeServos = [];
        List<ModuleEngines> activeEngines = [];
        List<PartModule> activeFSEngines = [];
        public enum EMPbuildupTiers
        {
            None = 0,
            Sensors = 1,
            Engines = 2,
            Controls = 3,
            Weapons = 4,
            Electrics = 5,
            Command = 6
        }
        public static readonly EMPbuildupTiers EMPTierMax = Enum.GetValues(typeof(EMPbuildupTiers)).Cast<EMPbuildupTiers>().Max();
        EMPbuildupTiers currentEMPBuildup = EMPbuildupTiers.None;
        EMPbuildupTiers lastTierTriggered = EMPbuildupTiers.None;
        float EMPTierThreshold = 10;
        /// <summary>
        /// So. basic idea is EMP base threshold determined by seat count - more command seats, more flight comps, more redundancy.
        /// Probe cores look at SASServiceLevel, since that's a decent measure of how 'advanced' the probe is/what sort of electronics it'd have.
        /// EMP Damage is then modified based on part mass and armor/hull materials (incl. that of the command part). 
        /// </summary>
        void Start()
        {
            foreach (var moduleCommand in VesselModuleRegistry.GetModuleCommands(vessel))
            {
                if (moduleCommand.part.CrewCapacity > 0) EMPThreshold += moduleCommand.part.CrewCapacity * 100; //cockpits worth 100 per seat
                if (moduleCommand.minimumCrew == 0)
                {
                    var CPULevel = moduleCommand.part.FindModuleImplementing<ModuleSAS>();
                    EMPThreshold += 10;
                    if (CPULevel != null) EMPThreshold += CPULevel.SASServiceLevel * 20; //drones worth 10-70, depending on capability
                }
            }
            if (isMissile = vessel.IsMissile()) EMPThreshold = 5;
            BrickThreshold = EMPThreshold * 5;
            EMPTierThreshold = EMPThreshold / (int)EMPTierMax;
            //EMPThreshold = (100 * (seatCount - ((1 - (vessel.GetTotalMass() / seatCount)) / 2));     
        }

        void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (BDArmorySetup.GameIsPaused) return;
            if (BDArmorySettings.PAINTBALL_MODE && !isMissile)
            {
                EMPDamage = 0;
                incomingDamage = 0;
                if (disabled) EnableVessel(EMPbuildupTiers.None);
                return;
            }
            if (!bricked)
            {
                if (EMPDamage > 0 || incomingDamage > 0)
                {
                    UpdateEMPLevel();
                }
            }

        }

        void UpdateEMPLevel()
        {
            if ((!disabled || (disabled && !softEMP)) && incomingDamage > 0)
            {
                EMPDamage += incomingDamage; //only accumulate EMP damage if it's hard EMP or craft isn't disabled
                incomingDamage = 0; //reset incoming damage amount
                if (disabled && !softEMP)
                {
                    if (rebootTimer > 0)
                    {
                        rebootTimer += incomingDamage / 100; //if getting hit by new sources of hard EMP, add to reboot timer
                    }
                }
            }
            if (disabled)
            {
                //EMPDamage = Mathf.Clamp(EMPDamage - 5 * TimeWarp.fixedDeltaTime, 0, Mathf.Infinity); //speed EMP cooldown, if electrolaser'd takes about ~10 sec to reboot. may need to be reduced further
                //fatal if fast+low alt, but higher alt or good glide ratio is survivable
                if (rebootTimer > 0)
                {
                    rebootTimer -= 1 * TimeWarp.fixedDeltaTime;
                }
                else
                {
                    EMPDamage = 0;
                }
            }
            else
            {
                EMPDamage = Mathf.Clamp(EMPDamage - 5 * TimeWarp.fixedDeltaTime, 0, Mathf.Infinity); //have EMP buildup dissipate over time
            }
            if (isMissile && EMPDamage > 10)
            {
                foreach (Part p in vessel.parts)
                {
                    var MB = p.FindModuleImplementing<MissileBase>();
                    if (MB != null)
                    {
                        MB.guidanceActive = false;
                    }
                }
                bricked = true;
                return;
            }
            //if (EMPDamage > EMPThreshold && !bricked && !disabled) //does the damage exceed the soft cap, but not the hard cap?
            if (!bricked && !disabled && EMPDamage > 0) //does the damage exceed the soft cap, but not the hard cap?
            {
                currentEMPBuildup = (EMPbuildupTiers)Math.Min(Mathf.FloorToInt(EMPDamage / EMPTierThreshold), (int)EMPTierMax);
                //Debug.Log($"[BDArmory.ModuleDrainEC]: currentEMPBuildup Tier on {vessel.GetName()}: {currentEMPBuildup}. last Tier Triggered: {lastTierTriggered}");
                if (currentEMPBuildup > lastTierTriggered)
                {
                    if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log($"[BDArmory.ModuleDrainEC]: Increasing EMP tier from {lastTierTriggered} to {currentEMPBuildup} on {vessel.GetName()}");
                    DisableVessel(currentEMPBuildup);
                }
                if (currentEMPBuildup < lastTierTriggered)
                {
                    if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log($"[BDArmory.ModuleDrainEC]: Lowering EMP tier from {lastTierTriggered} to {currentEMPBuildup} on {vessel.GetName()}");
                    EnableVessel(currentEMPBuildup);
                }
            }

            if (EMPDamage > BrickThreshold && !bricked) //does the damage exceed the hard cap?
            {
                bricked = true; //if so brick the craft
                var message = vessel.vesselName + " is bricked!";
                if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log("[BDArmory.ModuleDrainEC]: " + message);
                BDACompetitionMode.Instance.competitionStatus.Add(message);
            }
            if (EMPDamage <= 0 && disabled && !bricked) //reset craft
            {
                var message = "Rebooting " + vessel.vesselName;
                if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log("[BDArmory.ModuleDrainEC]: " + message);
                BDACompetitionMode.Instance.competitionStatus.Add(message);
                EnableVessel(EMPbuildupTiers.None);
            }
        }
        private void DisableVessel(EMPbuildupTiers EMPbuildup)
        {
            if (EMPbuildup >= EMPbuildupTiers.Sensors && lastTierTriggered < EMPbuildupTiers.Sensors) //deactivate sensors
            {
                foreach (var radar in VesselModuleRegistry.GetModules<ModuleSensorBase>(vessel))
                {
                    if (radar.sensorEnabled)
                        radar.DisableSensor();
                }
                foreach (var camera in VesselModuleRegistry.GetModules<ModuleTargetingCamera>(vessel))
                {
                    if (camera.cameraEnabled)
                        camera.DisableCamera();
                }
                if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log($"[BDArmory.ModuleDrainEC]: Disabling Sensors on {vessel.GetName()}");
            }
            if (EMPbuildup >= EMPbuildupTiers.Engines && lastTierTriggered < EMPbuildupTiers.Engines) //deactivate Engines
            {
                foreach (var engine in VesselModuleRegistry.GetModuleEngines(vessel))
                {
                    engine.Shutdown();
                    engine.allowRestart = false;
                    activeEngines.Add(engine);
                }
                activeFSEngines = FireSpitter.GetActiveEngines(vessel);
                FireSpitter.SetActiveFSEngines(activeFSEngines, false);
                if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log($"[BDArmory.ModuleDrainEC]: Disabling Engines on {vessel.GetName()}");
            }
            if (EMPbuildup >= EMPbuildupTiers.Controls && lastTierTriggered < EMPbuildupTiers.Controls) //deactivate control surfaces and other hydraulics
            {
                foreach (var ctrl in VesselModuleRegistry.GetModules<ModuleControlSurface>(vessel))
                {
                    ctrl.authorityLimiter /= 10; //simpler than having to store all control surface values in a list somewhere
                    ctrl.ctrlSurfaceRange /= 10;
                }
                foreach (var turret in VesselModuleRegistry.GetModules<ModuleTurret>(vessel))
                {
                    turret.yawSpeedDPS /= 100;
                    turret.pitchSpeedDPS /= 100;
                }
                foreach (var hinge in VesselModuleRegistry.GetModules<ModuleRoboticServoHinge>(vessel))
                {
                    if (hinge.servoMotorIsEngaged)
                        hinge.servoMotorIsEngaged = false;
                    activeHinges.Add(hinge);
                }
                foreach (var servo in VesselModuleRegistry.GetModules<ModuleRoboticRotationServo>(vessel))
                {
                    if (servo.servoMotorIsEngaged)
                        servo.servoMotorIsEngaged = false;
                    activeServos.Add(servo);
                }
                if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log($"[BDArmory.ModuleDrainEC]: Disabling ControlSurfaces on {vessel.GetName()}");
            }
            if (EMPbuildup >= EMPbuildupTiers.Weapons && lastTierTriggered < EMPbuildupTiers.Weapons) //deactivate Weapons
            {
                foreach (var weapon in VesselModuleRegistry.GetModuleWeapons(vessel))
                {
                    weapon.weaponState = ModuleWeapon.WeaponStates.Locked; //prevent weapons from firing
                }
                foreach (var missile in VesselModuleRegistry.GetMissileBases(vessel))
                {
                    missile.engageRangeMax /= 10000; //prevent weapons from firing
                }
                if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log($"[BDArmory.ModuleDrainEC]: Disabling Weapons on {vessel.GetName()}");
            }
            if (EMPbuildup >= EMPbuildupTiers.Electrics && lastTierTriggered < EMPbuildupTiers.Electrics) //drain electrics.
            {
                foreach (Part p in vessel.parts)
                {
                    PartResource r = p.Resources.Where(pr => pr.resourceName == "ElectricCharge").FirstOrDefault();
                    if (r != null)
                    {
                        if (r.amount >= 0)
                        {
                            p.RequestResource("ElectricCharge", r.amount);
                            //Random battery Fire if 'Fires' Battledamage enabled?
                        }
                    }
                }
                if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log($"[BDArmory.ModuleDrainEC]: Shorting Electrics on {vessel.GetName()}");
            }
            if (EMPbuildup >= EMPbuildupTiers.Command && lastTierTriggered < EMPbuildupTiers.Command) //deactivate control
            {
                disabled = true;
                foreach (var command in VesselModuleRegistry.GetModuleCommands(vessel))
                {
                    command.minimumCrew *= 10; //disable vessel control
                }
                // Store the active AI if there was one.
                var AI = vessel.ActiveController().AI;
                activeAI = (AI != null && AI.pilotEnabled) ? AI : null;
                if (AI != null && AI.pilotEnabled) AI.DeactivatePilot(); //disable AI

                // Store the guardMode state of the WMs.
                activeWMs.Clear();
                foreach (var wm in VesselModuleRegistry.GetMissileFires(vessel).Where(wm => wm != null))
                {
                    if (wm.guardMode) activeWMs.Add(wm);
                    wm.guardMode = false; //disable guardmode
                    wm.debilitated = true; //for weapon selection and targeting;
                }
                rebootTimer = BDArmorySettings.WEAPON_FX_DURATION;
                var message = "Disabling " + vessel.vesselName + " for " + rebootTimer + "s due to EMP damage";
                if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log("[BDArmory.ModuleDrainEC]: " + message);
                BDACompetitionMode.Instance.competitionStatus.Add(message);

                var empFX = Instantiate(GameDatabase.Instance.GetModel("BDArmory/FX/Electroshock"),
                vessel.rootPart.transform.position, Quaternion.identity);
                empFX.SetActive(true);
                empFX.transform.SetParent(vessel.rootPart.transform);
                empFX.AddComponent<EMPShock>();
            }
            lastTierTriggered = EMPbuildup;
        }
        private void EnableVessel(EMPbuildupTiers EMPbuildup)
        {
            if (EMPbuildup < EMPbuildupTiers.Sensors && lastTierTriggered >= EMPbuildupTiers.Sensors) //Reactivate sensors
            {
                foreach (var radar in VesselModuleRegistry.GetModules<ModuleSensorBase>(vessel))
                {
                    if (!radar.sensorEnabled)
                        radar.EnableSensor();
                }
                foreach (var camera in VesselModuleRegistry.GetModules<ModuleTargetingCamera>(vessel))
                {
                    if (!camera.cameraEnabled)
                        camera.EnableCamera();
                }
            }
            if (EMPbuildup < EMPbuildupTiers.Engines && lastTierTriggered >= EMPbuildupTiers.Engines) //reactivate Engines
            {
                foreach (var engine in activeEngines.Where(e => e != null))
                {
                    engine.allowRestart = true;
                    var mme = engine.part.FindModuleImplementing<MultiModeEngine>();
                    if (mme == null) engine.Activate();
                    else
                    {
                        if (mme.runningPrimary) mme.PrimaryEngine.Activate();
                        else mme.SecondaryEngine.Activate();
                    }
                }
                activeEngines.Clear();
                FireSpitter.SetActiveFSEngines(activeFSEngines, true);
            }
            if (EMPbuildup < EMPbuildupTiers.Controls && lastTierTriggered >= EMPbuildupTiers.Controls) //reactivate control surfaces
            {
                foreach (var ctrl in VesselModuleRegistry.GetModules<ModuleControlSurface>(vessel))
                {
                    ctrl.authorityLimiter *= 10;
                    ctrl.ctrlSurfaceRange *= 10;
                }
                foreach (var turret in VesselModuleRegistry.GetModules<ModuleTurret>(vessel))
                {
                    turret.yawSpeedDPS *= 100;
                    turret.pitchSpeedDPS *= 100;
                }
                foreach (var servo in activeServos) servo.servoMotorIsEngaged = true;
                foreach (var hinge in activeHinges) hinge.servoMotorIsEngaged = true;
            }
            if (EMPbuildup < EMPbuildupTiers.Weapons && lastTierTriggered >= EMPbuildupTiers.Weapons) //reactivate Weapons
            {
                foreach (var weapon in VesselModuleRegistry.GetModuleWeapons(vessel))
                {
                    if (weapon.isAPS)
                        weapon.EnableWeapon(); //reactivate APS 
                    else
                        weapon.DisableWeapon(); //reset WeaponState
                }
                foreach (var missile in VesselModuleRegistry.GetMissileBases(vessel))
                {
                    missile.engageRangeMax *= 10000;
                }
            }
            if (EMPbuildup < EMPbuildupTiers.Command && lastTierTriggered >= EMPbuildupTiers.Command) //reactivate control
            {
                foreach (var command in VesselModuleRegistry.GetModuleCommands(vessel))
                {
                    {
                        command.minimumCrew /= 10; //more elegant than a dict storing every crew part's cap to restore to original amount
                    }
                }
                // Previously enabled active AI is still attached, fire it up again.
                if (activeAI != null && activeAI.vessel == vessel)
                    activeAI.ActivatePilot();
                activeAI = null;

                // Restore the guardMode state of the WMs.
                foreach (var wm in VesselModuleRegistry.GetMissileFires(vessel).Where(wm => wm != null)) wm.debilitated = false;
                foreach (var wm in activeWMs) wm.guardMode = true;
                activeWMs.Clear();
            }
            disabled = false;
            lastTierTriggered = EMPbuildup;
        }
    }

    internal class EMPShock : MonoBehaviour
    {
        public void Start()
        {
            foreach (var pe in gameObject.GetComponentsInChildren<KSPParticleEmitter>())
            {
                EffectBehaviour.AddParticleEmitter(pe);
                pe.emit = true;
                StartCoroutine(TimerRoutine());
            }
        }
        IEnumerator TimerRoutine()
        {
            yield return new WaitForSecondsFixed(5);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            foreach (var pe in gameObject.GetComponentsInChildren<KSPParticleEmitter>())
            {
                EffectBehaviour.RemoveParticleEmitter(pe);
            }

        }
    }
}
