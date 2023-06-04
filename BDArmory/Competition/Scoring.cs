using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;
using UnityEngine;

using BDArmory.Competition.RemoteOrchestration;
using BDArmory.Control;
using BDArmory.GameModes.Waypoints;
using BDArmory.Settings;
using BDArmory.Utils;
using BDArmory.VesselSpawning;

namespace BDArmory.Competition
{
    public enum DamageFrom { None, Guns, Rockets, Missiles, Ramming, Incompetence, Asteroids };
    public enum AliveState { Alive, CleanKill, HeadShot, KillSteal, AssistedKill, Dead };
    public enum GMKillReason { None, GM, OutOfAmmo, BigRedButton, LandedTooLong, Asteroids };

    public class CompetitionScores
    {
        #region Public fields
        public Dictionary<string, ScoringData> ScoreData = new Dictionary<string, ScoringData>();
        public Dictionary<string, ScoringData>.KeyCollection Players => ScoreData.Keys; // Convenience variable
        public int deathCount = 0;
        public List<string> deathOrder = new List<string>(); // The names of dead players ordered by their death.
        public string currentlyIT = "";
        #endregion

        #region Helper functions for registering hits, etc.
        /// <summary>
        /// Configure the scoring structure (wipes a previous one).
        /// If a piñata is involved, include it here too.
        /// </summary>
        /// <param name="vessels">List of vessels involved in the competition.</param>
        public void ConfigurePlayers(List<Vessel> vessels)
        {
            if (BDArmorySettings.DEBUG_OTHER) { foreach (var vessel in vessels) { Debug.Log("[BDArmory.BDACompetitionMode.Scores]: Adding Score Tracker For " + vessel.vesselName); } }
            ScoreData = vessels.ToDictionary(v => v.vesselName, v => new ScoringData());
            foreach (var vessel in vessels)
            {
                ScoreData[vessel.vesselName].team = VesselModuleRegistry.GetMissileFire(vessel, true).Team.Name;
            }
            deathCount = 0;
            deathOrder.Clear();
            currentlyIT = "";
        }
        /// <summary>
        /// Add a competitor after the competition has started.
        /// </summary>
        /// <param name="vessel"></param>
        public bool AddPlayer(Vessel vessel)
        {
            if (ScoreData.ContainsKey(vessel.vesselName)) return false; // They're already there.
            if (BDACompetitionMode.Instance.IsValidVessel(vessel) != BDACompetitionMode.InvalidVesselReason.None) return false; // Invalid vessel.
            ScoreData[vessel.vesselName] = new ScoringData();
            ScoreData[vessel.vesselName].team = VesselModuleRegistry.GetMissileFire(vessel, true).Team.Name;
            ScoreData[vessel.vesselName].lastFiredTime = Planetarium.GetUniversalTime();
            ScoreData[vessel.vesselName].previousPartCount = vessel.parts.Count();
            BDACompetitionMode.Instance.AddPlayerToRammingInformation(vessel);
            return true;
        }
        /// <summary>
        /// Remove a player from the competition.
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        public bool RemovePlayer(string player)
        {
            if (!Players.Contains(player)) return false;
            ScoreData.Remove(player);
            BDACompetitionMode.Instance.RemovePlayerFromRammingInformation(player);
            return true;
        }
        /// <summary>
        /// Register a shot fired.
        /// </summary>
        /// <param name="shooter">The shooting vessel</param>
        /// <returns>true if successfully registered, false otherwise</returns>
        public bool RegisterShot(string shooter)
        {
            if (!BDACompetitionMode.Instance.competitionIsActive) return false;
            if (shooter == null || !ScoreData.ContainsKey(shooter)) return false;
            if (ScoreData[shooter].aliveState != AliveState.Alive) return false; // Ignore shots fired after the vessel is dead.
            ++ScoreData[shooter].shotsFired;
            if (BDArmorySettings.RUNWAY_PROJECT)
            {
                if (BDArmorySettings.RUNWAY_PROJECT_ROUND == 41 && !BDACompetitionMode.Instance.s4r1FiringRateUpdatedFromShotThisFrame)
                {
                    BDArmorySettings.FIRE_RATE_OVERRIDE += Mathf.Round(VectorUtils.Gaussian() * BDArmorySettings.FIRE_RATE_OVERRIDE_SPREAD + (BDArmorySettings.FIRE_RATE_OVERRIDE_CENTER - BDArmorySettings.FIRE_RATE_OVERRIDE) * BDArmorySettings.FIRE_RATE_OVERRIDE_BIAS * BDArmorySettings.FIRE_RATE_OVERRIDE_BIAS);
                    BDArmorySettings.FIRE_RATE_OVERRIDE = Mathf.Max(BDArmorySettings.FIRE_RATE_OVERRIDE, 10f);
                    BDACompetitionMode.Instance.s4r1FiringRateUpdatedFromShotThisFrame = true;
                }
            }
            return true;
        }
        /// <summary>
        /// Register a bullet hit.
        /// </summary>
        /// <param name="attacker">The attacking vessel</param>
        /// <param name="victim">The victim vessel</param>
        /// <param name="weaponName">The name of the weapon that fired the projectile</param>
        /// <param name="distanceTraveled">The distance travelled by the projectile</param>
        /// <returns>true if successfully registered, false otherwise</returns>
        public bool RegisterBulletHit(string attacker, string victim, string weaponName = "", double distanceTraveled = 0)
        {
            if (!BDACompetitionMode.Instance.competitionIsActive) return false;
            if (attacker == null || victim == null || attacker == victim || !ScoreData.ContainsKey(attacker) || !ScoreData.ContainsKey(victim)) return false;
            if (ScoreData[victim].aliveState != AliveState.Alive) return false; // Ignore hits after the victim is dead.

            if (BDArmorySettings.DEBUG_OTHER)
                Debug.Log($"[BDArmory.BDACompetitionMode.Scores]: {attacker} scored a hit against {victim} with {weaponName} from a distance of {distanceTraveled}m.");

            var now = Planetarium.GetUniversalTime();

            // Attacker stats.
            ++ScoreData[attacker].hits;
            if (victim == BDArmorySettings.PINATA_NAME) ++ScoreData[attacker].PinataHits; //not registering hits? Try switching to victim.Contains(BDArmorySettings.PINATA_NAME)?
            // Victim stats.
            if (ScoreData[victim].lastPersonWhoDamagedMe != attacker)
            {
                ScoreData[victim].previousLastDamageTime = ScoreData[victim].lastDamageTime;
                ScoreData[victim].previousPersonWheDamagedMe = ScoreData[victim].lastPersonWhoDamagedMe;
            }
            if (ScoreData[victim].hitCounts.ContainsKey(attacker)) { ++ScoreData[victim].hitCounts[attacker]; }
            else { ScoreData[victim].hitCounts[attacker] = 1; }
            ScoreData[victim].lastDamageTime = now;
            ScoreData[victim].lastDamageWasFrom = DamageFrom.Guns;
            ScoreData[victim].lastPersonWhoDamagedMe = attacker;
            ScoreData[victim].everyoneWhoDamagedMe.Add(attacker);
            ScoreData[victim].damageTypesTaken.Add(DamageFrom.Guns);

            if (BDArmorySettings.REMOTE_LOGGING_ENABLED)
            { BDAScoreService.Instance.TrackHit(attacker, victim, weaponName, distanceTraveled); }

            if (BDArmorySettings.TAG_MODE && !string.IsNullOrEmpty(weaponName)) // Empty weapon name indicates fire or other effect that doesn't count for tag mode.
            {
                if (ScoreData[victim].tagIsIt || string.IsNullOrEmpty(currentlyIT))
                {
                    if (ScoreData[victim].tagIsIt)
                    {
                        UpdateITTimeAndScore(); // Register time the victim spent as IT.
                    }
                    RegisterIsIT(attacker); // Register the attacker as now being IT.
                }
            }

            if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 41 && !BDACompetitionMode.Instance.s4r1FiringRateUpdatedFromHitThisFrame)
            {
                BDArmorySettings.FIRE_RATE_OVERRIDE = Mathf.Round(Mathf.Min(BDArmorySettings.FIRE_RATE_OVERRIDE * BDArmorySettings.FIRE_RATE_OVERRIDE_HIT_MULTIPLIER, 1200f));
                BDACompetitionMode.Instance.s4r1FiringRateUpdatedFromHitThisFrame = true;
            }

            return true;
        }
        /// <summary>
        /// Register damage from bullets.
        /// </summary>
        /// <param name="attacker">Attacking vessel</param>
        /// <param name="victim">Victim vessel</param>
        /// <param name="damage">Amount of damage</param> 
        /// <returns>true if successfully registered, false otherwise</returns>
        public bool RegisterBulletDamage(string attacker, string victim, float damage)
        {
            if (!BDACompetitionMode.Instance.competitionIsActive) return false;
            if (damage <= 0 || attacker == null || victim == null || attacker == victim || !ScoreData.ContainsKey(attacker) || !ScoreData.ContainsKey(victim)) return false;
            if (ScoreData[victim].aliveState != AliveState.Alive) return false; // Ignore damage after the victim is dead.
            if (float.IsNaN(damage))
            {
                Debug.LogError($"DEBUG {attacker} did NaN damage to {victim}!");
                return false;
            }

            if (BDArmorySettings.DEBUG_OTHER)
                Debug.Log($"[BDArmory.BDACompetitionMode.Scores]: {attacker} did {damage} damage to {victim} with a gun.");

            if (ScoreData[victim].damageFromGuns.ContainsKey(attacker)) { ScoreData[victim].damageFromGuns[attacker] += damage; }
            else { ScoreData[victim].damageFromGuns[attacker] = damage; }

            if (BDArmorySettings.REMOTE_LOGGING_ENABLED)
            { BDAScoreService.Instance.TrackDamage(attacker, victim, damage); }
            return true;
        }
        /// <summary>
        /// Register a rocket fired.
        /// </summary>
        /// <param name="shooter">The shooting vessel</param>
        /// <returns>true if successfully registered, false otherwise</returns>
        public bool RegisterRocketFired(string shooter)
        {
            if (!BDACompetitionMode.Instance.competitionIsActive) return false;
            if (shooter == null || !ScoreData.ContainsKey(shooter)) return false;
            if (ScoreData[shooter].aliveState != AliveState.Alive) return false; // Ignore shots fired after the vessel is dead.
            ++ScoreData[shooter].rocketsFired;
            return true;
        }
        /// <summary>
        /// Register individual rocket strikes.
        /// Note: this includes both kinetic and explosive strikes, so a single rocket may count for two strikes.
        /// </summary>
        /// <param name="attacker"></param>
        /// <param name="victim"></param>
        /// <returns></returns>
        public bool RegisterRocketStrike(string attacker, string victim)
        {
            if (!BDACompetitionMode.Instance.competitionIsActive) return false;
            if (attacker == null || victim == null || attacker == victim || !ScoreData.ContainsKey(attacker) || !ScoreData.ContainsKey(victim)) return false;
            if (ScoreData[victim].aliveState != AliveState.Alive) return false; // Ignore hits after the victim is dead.

            if (BDArmorySettings.DEBUG_OTHER)
                Debug.Log($"[BDArmory.BDACompetitionMode.Scores]: {attacker} scored a rocket strike against {victim}.");

            ++ScoreData[attacker].rocketStrikes;
            if (ScoreData[victim].rocketStrikeCounts.ContainsKey(attacker)) { ++ScoreData[victim].rocketStrikeCounts[attacker]; }
            else { ScoreData[victim].rocketStrikeCounts[attacker] = 1; }

            if (BDArmorySettings.REMOTE_LOGGING_ENABLED)
            { BDAScoreService.Instance.TrackRocketStrike(attacker, victim); }
            return true;
        }
        /// <summary>
        /// Register the number of parts on the victim that were damaged by the attacker's rocket.
        /// </summary>
        /// <param name="attacker"></param>
        /// <param name="victim"></param>
        /// <param name="partsHit"></param>
        /// <returns></returns>
        public bool RegisterRocketHit(string attacker, string victim, int partsHit = 1)
        {
            if (!BDACompetitionMode.Instance.competitionIsActive) return false;
            if (partsHit <= 0 || attacker == null || victim == null || attacker == victim || !ScoreData.ContainsKey(attacker) || !ScoreData.ContainsKey(victim)) return false;
            if (ScoreData[victim].aliveState != AliveState.Alive) return false; // Ignore hits after the victim is dead.

            if (BDArmorySettings.DEBUG_OTHER)
                Debug.Log($"[BDArmory.BDACompetitionMode.Scores]: {attacker} damaged {partsHit} parts on {victim} with a rocket.");

            var now = Planetarium.GetUniversalTime();

            // Attacker stats.
            ScoreData[attacker].totalDamagedPartsDueToRockets += partsHit;

            if (victim == BDArmorySettings.PINATA_NAME) ++ScoreData[attacker].PinataHits;
            // Victim stats.
            if (ScoreData[victim].lastPersonWhoDamagedMe != attacker)
            {
                ScoreData[victim].previousLastDamageTime = ScoreData[victim].lastDamageTime;
                ScoreData[victim].previousPersonWheDamagedMe = ScoreData[victim].lastPersonWhoDamagedMe;
            }
            if (ScoreData[victim].rocketPartDamageCounts.ContainsKey(attacker)) { ScoreData[victim].rocketPartDamageCounts[attacker] += partsHit; }
            else { ScoreData[victim].rocketPartDamageCounts[attacker] = partsHit; }
            ScoreData[victim].lastDamageTime = now;
            ScoreData[victim].lastDamageWasFrom = DamageFrom.Rockets;
            ScoreData[victim].lastPersonWhoDamagedMe = attacker;
            ScoreData[victim].everyoneWhoDamagedMe.Add(attacker);
            ScoreData[victim].damageTypesTaken.Add(DamageFrom.Rockets);

            if (BDArmorySettings.REMOTE_LOGGING_ENABLED)
            { BDAScoreService.Instance.TrackRocketParts(attacker, victim, partsHit); }
            return true;
        }
        /// <summary>
        /// Register damage from rocket strikes.
        /// </summary>
        /// <param name="attacker"></param>
        /// <param name="victim"></param>
        /// <param name="damage"></param>
        /// <returns></returns>
        public bool RegisterRocketDamage(string attacker, string victim, float damage)
        {
            if (!BDACompetitionMode.Instance.competitionIsActive) return false;
            if (damage <= 0 || attacker == null || victim == null || attacker == victim || !ScoreData.ContainsKey(attacker) || !ScoreData.ContainsKey(victim)) return false;
            if (ScoreData[victim].aliveState != AliveState.Alive) return false; // Ignore damage after the victim is dead.

            if (BDArmorySettings.DEBUG_OTHER)
                Debug.Log($"[BDArmory.BDACompetitionMode.Scores]: {attacker} did {damage} damage to {victim} with a rocket.");

            if (ScoreData[victim].damageFromRockets.ContainsKey(attacker)) { ScoreData[victim].damageFromRockets[attacker] += damage; }
            else { ScoreData[victim].damageFromRockets[attacker] = damage; }

            if (BDArmorySettings.REMOTE_LOGGING_ENABLED)
            { BDAScoreService.Instance.TrackRocketDamage(attacker, victim, damage); }
            return true;
        }
        /// <summary>
        /// Register damage from Battle Damage.
        /// </summary>
        /// <param name="attacker"></param>
        /// <param name="victim"></param>
        /// <param name="damage"></param>
        /// <returns></returns>
        public bool RegisterBattleDamage(string attacker, Vessel victimVessel, float damage)
        {
            if (!BDACompetitionMode.Instance.competitionIsActive) return false;
            if (victimVessel == null) return false;
            var victim = victimVessel.vesselName;
            if (damage <= 0 || attacker == null || victim == null || !ScoreData.ContainsKey(attacker) || !ScoreData.ContainsKey(victim)) return false; // Note: we allow attacker=victim here to track self damage.
            if (ScoreData[victim].aliveState != AliveState.Alive) return false; // Ignore damage after the victim is dead.
            if (VesselModuleRegistry.GetModuleCount<MissileFire>(victimVessel) == 0) return false; // The victim is dead, but hasn't been registered as such yet. We want to check this here as it's common for BD to occur as the vessel is killed.

            if (ScoreData[victim].battleDamageFrom.ContainsKey(attacker)) { ScoreData[victim].battleDamageFrom[attacker] += damage; }
            else { ScoreData[victim].battleDamageFrom[attacker] = damage; }

            return true;
        }
        /// <summary>
        /// Register parts lost due to ram.
        /// </summary>
        /// <param name="attacker"></param>
        /// <param name="victim"></param>
        /// <param name="timeOfCollision">time the ram occured, which may be before the most recently registered damage from other sources</param>
        /// <param name="partsLost"></param>
        /// <returns>true if successfully registered, false otherwise</returns>
        public bool RegisterRam(string attacker, string victim, double timeOfCollision, int partsLost)
        {
            if (!BDACompetitionMode.Instance.competitionIsActive) return false;
            if (partsLost <= 0 || attacker == null || victim == null || attacker == victim || !ScoreData.ContainsKey(attacker) || !ScoreData.ContainsKey(victim)) return false;
            if (ScoreData[victim].aliveState != AliveState.Alive) return false; // Ignore rams after the victim is dead.

            if (BDArmorySettings.DEBUG_OTHER)
                Debug.Log($"[BDArmory.BDACompetitionMode.Scores]: {attacker} rammed {victim} at {timeOfCollision} and the victim lost {partsLost} parts.");

            // Attacker stats.
            ScoreData[attacker].totalDamagedPartsDueToRamming += partsLost;

            // Victim stats.
            if (ScoreData[victim].lastDamageTime < timeOfCollision && ScoreData[victim].lastPersonWhoDamagedMe != attacker)
            {
                ScoreData[victim].previousLastDamageTime = ScoreData[victim].lastDamageTime;
                ScoreData[victim].previousPersonWheDamagedMe = ScoreData[victim].lastPersonWhoDamagedMe;
            }
            else if (ScoreData[victim].previousLastDamageTime < timeOfCollision && !string.IsNullOrEmpty(ScoreData[victim].previousPersonWheDamagedMe) && ScoreData[victim].previousPersonWheDamagedMe != attacker) // Newer than the current previous last damage, but older than the most recent damage from someone else.
            {
                ScoreData[victim].previousLastDamageTime = timeOfCollision;
                ScoreData[victim].previousPersonWheDamagedMe = attacker;
            }
            if (ScoreData[victim].rammingPartLossCounts.ContainsKey(attacker)) { ScoreData[victim].rammingPartLossCounts[attacker] += partsLost; }
            else { ScoreData[victim].rammingPartLossCounts[attacker] = partsLost; }
            if (ScoreData[victim].lastDamageTime < timeOfCollision)
            {
                ScoreData[victim].lastDamageTime = timeOfCollision;
                ScoreData[victim].lastDamageWasFrom = DamageFrom.Ramming;
                ScoreData[victim].lastPersonWhoDamagedMe = attacker;
            }
            ScoreData[victim].everyoneWhoDamagedMe.Add(attacker);
            ScoreData[victim].damageTypesTaken.Add(DamageFrom.Ramming);

            if (BDArmorySettings.REMOTE_LOGGING_ENABLED)
            { BDAScoreService.Instance.TrackRammedParts(attacker, victim, partsLost); }
            return true;
        }
        /// <summary>
        /// Register individual missile strikes.
        /// </summary>
        /// <param name="attacker">The vessel that launched the missile.</param>
        /// <param name="victim">The struck vessel.</param>
        /// <returns>true if successfully registered, false otherwise</returns>
        public bool RegisterMissileStrike(string attacker, string victim)
        {
            if (!BDACompetitionMode.Instance.competitionIsActive) return false;
            if (attacker == null || victim == null || attacker == victim || !ScoreData.ContainsKey(attacker) || !ScoreData.ContainsKey(victim)) return false;
            if (ScoreData[victim].aliveState != AliveState.Alive) return false; // Ignore hits after the victim is dead.

            if (BDArmorySettings.DEBUG_OTHER)
                Debug.Log($"[BDArmory.BDACompetitionMode.Scores]: {attacker} scored a missile strike against {victim}.");

            if (ScoreData[victim].missileHitCounts.ContainsKey(attacker)) { ++ScoreData[victim].missileHitCounts[attacker]; }
            else { ScoreData[victim].missileHitCounts[attacker] = 1; }

            if (BDArmorySettings.REMOTE_LOGGING_ENABLED)
            { BDAScoreService.Instance.TrackMissileStrike(attacker, victim); }
            return true;
        }
        /// <summary>
        /// Register the number of parts on the victim that were damaged by the attacker's missile.
        /// </summary>
        /// <param name="attacker">The vessel that launched the missile</param>
        /// <param name="victim">The struck vessel</param>
        /// <param name="partsHit">The number of parts hit (can be 1 at a time)</param>
        /// <returns>true if successfully registered, false otherwise</returns>
        public bool RegisterMissileHit(string attacker, string victim, int partsHit = 1)
        {
            if (!BDACompetitionMode.Instance.competitionIsActive) return false;
            if (partsHit <= 0 || attacker == null || victim == null || attacker == victim || !ScoreData.ContainsKey(attacker) || !ScoreData.ContainsKey(victim)) return false;
            if (ScoreData[victim].aliveState != AliveState.Alive) return false; // Ignore hits after the victim is dead.

            if (BDArmorySettings.DEBUG_OTHER)
                Debug.Log($"[BDArmory.BDACompetitionMode.Scores]: {attacker} damaged {partsHit} parts on {victim} with a missile.");

            var now = Planetarium.GetUniversalTime();

            // Attacker stats.
            ScoreData[attacker].totalDamagedPartsDueToMissiles += partsHit;

            // Victim stats.
            if (ScoreData[victim].lastPersonWhoDamagedMe != attacker)
            {
                ScoreData[victim].previousLastDamageTime = ScoreData[victim].lastDamageTime;
                ScoreData[victim].previousPersonWheDamagedMe = ScoreData[victim].lastPersonWhoDamagedMe;
            }
            if (ScoreData[victim].missilePartDamageCounts.ContainsKey(attacker)) { ScoreData[victim].missilePartDamageCounts[attacker] += partsHit; }
            else { ScoreData[victim].missilePartDamageCounts[attacker] = partsHit; }
            ScoreData[victim].lastDamageTime = now;
            ScoreData[victim].lastDamageWasFrom = DamageFrom.Missiles;
            ScoreData[victim].lastPersonWhoDamagedMe = attacker;
            ScoreData[victim].everyoneWhoDamagedMe.Add(attacker);
            ScoreData[victim].damageTypesTaken.Add(DamageFrom.Missiles);

            if (BDArmorySettings.REMOTE_LOGGING_ENABLED)
            { BDAScoreService.Instance.TrackMissileParts(attacker, victim, partsHit); }
            return true;
        }
        /// <summary>
        /// Register damage from missile strikes.
        /// </summary>
        /// <param name="attacker">The vessel that launched the missile</param>
        /// <param name="victim">The struck vessel</param>
        /// <param name="damage">The amount of damage done</param>
        /// <returns>true if successfully registered, false otherwise</returns>
        public bool RegisterMissileDamage(string attacker, string victim, float damage)
        {
            if (!BDACompetitionMode.Instance.competitionIsActive) return false;
            if (damage <= 0 || attacker == null || victim == null || attacker == victim || !ScoreData.ContainsKey(attacker) || !ScoreData.ContainsKey(victim)) return false;
            if (ScoreData[victim].aliveState != AliveState.Alive) return false; // Ignore damage after the victim is dead.

            if (BDArmorySettings.DEBUG_OTHER)
                Debug.Log($"[BDArmory.BDACompetitionMode.Scores]: {attacker} did {damage} damage to {victim} with a missile.");

            if (ScoreData[victim].damageFromMissiles.ContainsKey(attacker)) { ScoreData[victim].damageFromMissiles[attacker] += damage; }
            else { ScoreData[victim].damageFromMissiles[attacker] = damage; }

            if (BDArmorySettings.REMOTE_LOGGING_ENABLED)
            { BDAScoreService.Instance.TrackMissileDamage(attacker, victim, damage); }
            return true;
        }
        /// <summary>
        /// Register a vessel dying.
        /// </summary>
        /// <param name="vesselName"></param>
        /// <returns>true if successfully registered, false otherwise</returns>
        public bool RegisterDeath(string vesselName, GMKillReason gmKillReason = GMKillReason.None, double timeOfDeath = -1)
        {
            if (!BDACompetitionMode.Instance.competitionIsActive) return false;
            if (vesselName == null || !ScoreData.ContainsKey(vesselName)) return false;
            if (ScoreData[vesselName].aliveState != AliveState.Alive) return false; // They're already dead!

            var now = timeOfDeath < 0 ? Planetarium.GetUniversalTime() : timeOfDeath;
            var deathTimes = ScoreData.Values.Select(s => s.deathTime).ToList();
            var fixDeathOrder = timeOfDeath > -1 && deathTimes.Count > 0 && timeOfDeath - BDACompetitionMode.Instance.competitionStartTime < deathTimes.Max();
            deathOrder.Add(vesselName);
            ScoreData[vesselName].deathOrder = deathCount++;
            ScoreData[vesselName].deathTime = now - BDACompetitionMode.Instance.competitionStartTime;
            ScoreData[vesselName].gmKillReason = gmKillReason;
            if (fixDeathOrder) // Fix the death order if needed.
            {
                deathOrder = ScoreData.Where(s => s.Value.deathTime > -1).OrderBy(s => s.Value.deathTime).Select(s => s.Key).ToList();
                for (int i = 0; i < deathOrder.Count; ++i)
                    ScoreData[deathOrder[i]].deathOrder = i;
            }

            if (BDArmorySettings.DEBUG_OTHER)
                Debug.Log($"[BDArmory.BDACompetitionMode.Scores]: {vesselName} died at {ScoreData[vesselName].deathTime} (position {ScoreData[vesselName].deathOrder}), GM reason: {gmKillReason}, last damage from: {ScoreData[vesselName].lastDamageWasFrom}");

            if (BDArmorySettings.REMOTE_LOGGING_ENABLED)
            { BDAScoreService.Instance.TrackDeath(vesselName); }

            if (BDArmorySettings.TAG_MODE)
            {
                if (ScoreData[vesselName].tagIsIt)
                {
                    UpdateITTimeAndScore(); // Update the final IT time for the vessel.
                    ScoreData[vesselName].tagIsIt = false; // Register the vessel as no longer IT.
                    if (gmKillReason == GMKillReason.None) // If it wasn't a GM kill, set the previous vessel that hit this one as IT.
                    { RegisterIsIT(ScoreData[vesselName].lastPersonWhoDamagedMe); }
                    else
                    { currentlyIT = ""; }
                    if (string.IsNullOrEmpty(currentlyIT)) // GM kill or couldn't find a someone else to be IT.
                    { BDACompetitionMode.Instance.TagResetTeams(); }
                }
                else if (ScoreData.ContainsKey(ScoreData[vesselName].lastPersonWhoDamagedMe) && ScoreData[ScoreData[vesselName].lastPersonWhoDamagedMe].tagIsIt) // Check to see if the IT vessel killed them.
                { ScoreData[ScoreData[vesselName].lastPersonWhoDamagedMe].tagKillsWhileIt++; }
            }

            if (ScoreData[vesselName].lastDamageWasFrom == DamageFrom.None || (ScoreData[vesselName].damageTypesTaken.Count == 1 && ScoreData[vesselName].damageTypesTaken.Contains(DamageFrom.Asteroids))) // Died without being hit by anyone => Incompetence
            {
                ScoreData[vesselName].aliveState = AliveState.Dead;
                if (gmKillReason == GMKillReason.None)
                { ScoreData[vesselName].lastDamageWasFrom = DamageFrom.Incompetence; }
                return true;
            }

            if (now - ScoreData[vesselName].lastDamageTime < BDArmorySettings.SCORING_HEADSHOT && ScoreData[vesselName].gmKillReason == GMKillReason.None) // Died shortly after being hit (and not by the GM)
            {
                if (ScoreData[vesselName].previousLastDamageTime < 0) // No-one else hit them => Clean kill
                { ScoreData[vesselName].aliveState = AliveState.CleanKill; }
                else if (now - ScoreData[vesselName].previousLastDamageTime > BDArmorySettings.SCORING_KILLSTEAL) // Last hit from someone else was a while ago => Head-shot
                { ScoreData[vesselName].aliveState = AliveState.HeadShot; }
                else // Last hit from someone else was recent => Kill Steal
                { ScoreData[vesselName].aliveState = AliveState.KillSteal; }

                if (BDArmorySettings.REMOTE_LOGGING_ENABLED)
                { BDAScoreService.Instance.TrackKill(ScoreData[vesselName].lastPersonWhoDamagedMe, vesselName); }
            }
            else // Survived for a while after being hit or GM kill => Assist
            {
                ScoreData[vesselName].aliveState = AliveState.AssistedKill;

                if (BDArmorySettings.REMOTE_LOGGING_ENABLED)
                { BDAScoreService.Instance.ComputeAssists(vesselName, "", now - BDACompetitionMode.Instance.competitionStartTime); }
            }

            if (BDArmorySettings.VESSEL_SPAWN_DUMP_LOG_EVERY_SPAWN && ContinuousSpawning.Instance.vesselsSpawningContinuously) ContinuousSpawning.Instance.DumpContinuousSpawningScores();

            return true;
        }
        /// <summary>
        /// Register the number of parts lost due to crashing into an asteroid.
        /// </summary>
        /// <param name="victim">The player that crashed</param>
        /// <param name="partsDestroyed">The number of parts they lost.</param>
        /// <returns>true if successfully registered, false otherwise.</returns>
        public bool RegisterAsteroidCollision(string victim, int partsDestroyed)
        {
            if (!BDACompetitionMode.Instance.competitionIsActive) return false;
            if (partsDestroyed <= 0 || victim == null || !ScoreData.ContainsKey(victim)) return false;

            var now = Planetarium.GetUniversalTime();

            var attacker = "Asteroids";
            if (ScoreData[victim].lastPersonWhoDamagedMe != attacker)
            {
                ScoreData[victim].previousLastDamageTime = ScoreData[victim].lastDamageTime;
                ScoreData[victim].previousPersonWheDamagedMe = ScoreData[victim].lastPersonWhoDamagedMe;
            }
            ScoreData[victim].partsLostToAsteroids += partsDestroyed;
            ScoreData[victim].lastDamageTime = now;
            ScoreData[victim].lastDamageWasFrom = DamageFrom.Asteroids;
            ScoreData[victim].lastPersonWhoDamagedMe = attacker;
            ScoreData[victim].everyoneWhoDamagedMe.Add(attacker);
            ScoreData[victim].damageTypesTaken.Add(DamageFrom.Asteroids);

            if (BDArmorySettings.REMOTE_LOGGING_ENABLED)
            { BDAScoreService.Instance.TrackPartsLostToAsteroids(victim, partsDestroyed); }
            return true;
        }

        #region Tag
        public bool RegisterIsIT(string vesselName)
        {
            if (string.IsNullOrEmpty(vesselName) || !ScoreData.ContainsKey(vesselName))
            {
                currentlyIT = "";
                return false;
            }

            var now = Planetarium.GetUniversalTime();
            var vessels = BDACompetitionMode.Instance.GetAllPilots().Select(pilot => pilot.vessel).Where(vessel => Players.Contains(vessel.vesselName)).ToDictionary(vessel => vessel.vesselName, vessel => vessel); // Get the vessels so we can trigger action groups on them. Also checks that the vessels are valid competitors.
            if (vessels.ContainsKey(vesselName)) // Set the player as IT if they're alive.
            {
                currentlyIT = vesselName;
                ScoreData[vesselName].tagIsIt = true;
                ScoreData[vesselName].tagTimesIt++;
                ScoreData[vesselName].tagLastUpdated = now;
                var mf = VesselModuleRegistry.GetMissileFire(vessels[vesselName]);
                mf.SetTeam(BDTeam.Get("IT"));
                mf.ForceScan();
                BDACompetitionMode.Instance.competitionStatus.Add(vesselName + " is IT!");
                vessels[vesselName].ActionGroups.ToggleGroup(BDACompetitionMode.KM_dictAG[8]); // Trigger AG8 on becoming "IT"
            }
            else { currentlyIT = ""; }
            foreach (var player in Players) // Make sure other players are not NOT IT.
            {
                if (player != vesselName && vessels.ContainsKey(player))
                {
                    if (ScoreData[player].team != "NO")
                    {
                        ScoreData[player].tagIsIt = false;
                        var mf = VesselModuleRegistry.GetMissileFire(vessels[player]);
                        mf.SetTeam(BDTeam.Get("NO"));
                        mf.ForceScan();
                        vessels[player].ActionGroups.ToggleGroup(BDACompetitionMode.KM_dictAG[9]); // Trigger AG9 on becoming "NOT IT"
                    }
                }
            }
            return true;
        }
        public bool UpdateITTimeAndScore()
        {
            if (!string.IsNullOrEmpty(currentlyIT))
            {
                if (BDACompetitionMode.Instance.previousNumberCompetitive < 2 || ScoreData[currentlyIT].landedState) return false; // Don't update if there are no competitors or we're landed.
                var now = Planetarium.GetUniversalTime();
                ScoreData[currentlyIT].tagTotalTime += now - ScoreData[currentlyIT].tagLastUpdated;
                ScoreData[currentlyIT].tagScore += (now - ScoreData[currentlyIT].tagLastUpdated) * BDACompetitionMode.Instance.previousNumberCompetitive * (BDACompetitionMode.Instance.previousNumberCompetitive - 1) / 5; // Rewards craft accruing time with more competitors
                ScoreData[currentlyIT].tagLastUpdated = now;
            }
            return true;
        }
        #endregion

        #region Waypoints
        public bool RegisterWaypointReached(string vesselName, int waypointCourseIndex, int waypointIndex, int lapNumber, int lapLimit, float distance)
        {
            if (!BDACompetitionMode.Instance.competitionIsActive) return false;
            if (vesselName == null || !ScoreData.ContainsKey(vesselName)) return false;

            ScoreData[vesselName].waypointsReached.Add(new ScoringData.WaypointReached(waypointIndex, distance, Planetarium.GetUniversalTime() - BDACompetitionMode.Instance.competitionStartTime));
            BDACompetitionMode.Instance.competitionStatus.Add($"{vesselName}: {WaypointCourses.CourseLocations[waypointCourseIndex].waypoints[waypointIndex].name} ({waypointIndex}{(lapLimit > 1 ? $", lap {lapNumber}" : "")}) reached: Time: {ScoreData[vesselName].waypointsReached.Last().timestamp - ScoreData[vesselName].waypointsReached.First().timestamp:F2}s, Deviation: {distance:F1}m");
            ScoreData[vesselName].totalWPTime = (float)(ScoreData[vesselName].waypointsReached.Last().timestamp - ScoreData[vesselName].waypointsReached.First().timestamp);
            ScoreData[vesselName].totalWPDeviation += distance;

            return true;
        }
        #endregion
        #endregion

        public void LogResults(string CompetitionID, string message = "", string tag = "")
        {
            var logStrings = new List<string>();
            logStrings.Add("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Dumping Results" + (message != "" ? " " + message : "") + " after " + (int)(Planetarium.GetUniversalTime() - BDACompetitionMode.Instance.competitionStartTime) + "s (of " + (BDArmorySettings.COMPETITION_DURATION * 60d) + "s) at " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss zzz"));

            // Find out who's still alive
            var alive = new HashSet<string>();
            var survivingTeams = new HashSet<string>();
            foreach (var vessel in FlightGlobals.Vessels)
            {
                if (vessel == null || !vessel.loaded || vessel.packed || VesselModuleRegistry.ignoredVesselTypes.Contains(vessel.vesselType))
                    continue;
                var mf = VesselModuleRegistry.GetModule<MissileFire>(vessel);
                var ai = VesselModuleRegistry.GetIBDAIControl(vessel);
                double HP = 0;
                double WreckFactor = 0;
                if (mf != null)
                {
                    HP = (mf.currentHP / mf.totalHP) * 100;
                    if (ScoreData.ContainsKey(vessel.vesselName))
                    {
                        ScoreData[vessel.vesselName].remainingHP = HP;
                        survivingTeams.Add(ScoreData[vessel.vesselName].team); //move this here so last man standing can claim the win, even if they later don't meet the 'survive' criteria
                    }
                    if (HP < 100)
                    {
                        WreckFactor += (100 - HP) / 100; //the less plane remaining, the greater the chance it's a wreck
                    }
                    if (ai == null)
                    {
                        WreckFactor += 0.5f; // It's brain-dead.
                    }
                    else if (vessel.LandedOrSplashed && (ai as BDModulePilotAI != null || ai as BDModuleVTOLAI != null))
                    {
                        WreckFactor += 0.5f; // It's a plane / helicopter that's now on the ground.
                    }
                    if (vessel.verticalSpeed < -30) //falling out of the sky? Could be an intact plane diving to default alt, could be a cockpit
                    {
                        WreckFactor += 0.5f;
                        var AI = ai as BDModulePilotAI;
                        if (AI == null || vessel.radarAltitude < AI.defaultAltitude) //craft is uncontrollably diving, not returning from high alt to cruising alt
                        {
                            WreckFactor += 0.5f;
                        }
                    }
                    if (VesselModuleRegistry.GetModuleCount<ModuleEngines>(vessel) > 0)
                    {
                        int engineOut = 0;
                        foreach (var engine in VesselModuleRegistry.GetModules<ModuleEngines>(vessel))
                        {
                            if (engine == null || engine.flameout || engine.finalThrust <= 0)
                                engineOut++;
                        }
                        WreckFactor += (engineOut / VesselModuleRegistry.GetModuleCount<ModuleEngines>(vessel)) / 2;
                    }
                    else
                    {
                        WreckFactor += 0.5f; //could be a glider, could be missing engines
                    }
                    if (WreckFactor < 1.1f) // 'wrecked' requires some combination of diving, no engines, and missing parts
                    {
                        alive.Add(vessel.vesselName);
                    }
                }
            }

            // General result. (Note: uses hand-coded JSON to make parsing easier in python.)     
            if (survivingTeams.Count == 0)
            {
                logStrings.Add("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: RESULT:Mutual Annihilation");
            }
            else if (survivingTeams.Count == 1)
            { // Win
                var winningTeam = survivingTeams.First();
                var winningTeamMembers = ScoreData.Where(s => s.Value.team == winningTeam).Select(s => s.Key);
                logStrings.Add("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: RESULT:Win:{\"team\": " + $"\"{winningTeam}\", \"members\": [" + string.Join(", ", winningTeamMembers.Select(m => $"\"{m.Replace("\"", "\\\"")}\"")) + "]}");
            }
            else
            { // Draw
                var drawTeams = survivingTeams.ToDictionary(t => t, t => ScoreData.Where(s => s.Value.team == t).Select(s => s.Key));
                logStrings.Add("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: RESULT:Draw:[" + string.Join(", ", drawTeams.Select(t => "{\"team\": " + $"\"{t.Key}\"" + ", \"members\": [" + string.Join(", ", t.Value.Select(m => $"\"{m.Replace("\"", "\\\"")}\"")) + "]}")) + "]");
            }
            { // Dead teams.
                var deadTeamNames = ScoreData.Where(s => !survivingTeams.Contains(s.Value.team)).Select(s => s.Value.team).ToHashSet();
                var deadTeams = deadTeamNames.ToDictionary(t => t, t => ScoreData.Where(s => s.Value.team == t).Select(s => s.Key));
                logStrings.Add("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: DEADTEAMS:[" + string.Join(", ", deadTeams.Select(t => "{\"team\": " + $"\"{t.Key}\"" + ", \"members\": [" + string.Join(", ", t.Value.Select(m => $"\"{m.Replace("\"", "\\\"")}\"")) + "]}")) + "]");
            }

            // Record ALIVE/DEAD status of each craft.
            foreach (var vesselName in alive) // List ALIVE craft first
            {
                logStrings.Add("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: ALIVE:" + vesselName);
            }
            foreach (var player in Players) // Then DEAD or MIA.
            {
                if (!alive.Contains(player))
                {
                    if (ScoreData[player].deathOrder > -1)
                    {
                        logStrings.Add("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: DEAD:" + ScoreData[player].deathOrder + ":" + ScoreData[player].deathTime.ToString("0.0") + ":" + player); // DEAD: <death order>:<death time>:<vessel name>
                    }
                    else
                    {
                        logStrings.Add("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: MIA:" + player);
                    }
                }
            }

            // Report survivors to Remote Orchestration
            if (BDArmorySettings.REMOTE_LOGGING_ENABLED)
            { BDAScoreService.Instance.TrackSurvivors(Players.Where(player => ScoreData[player].deathOrder == -1).ToList()); }

            // Who shot who.
            foreach (var player in Players)
                if (ScoreData[player].hitCounts.Count > 0)
                {
                    string whoShotMe = "[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: WHOSHOTWHOWITHGUNS:" + player;
                    foreach (var vesselName in ScoreData[player].hitCounts.Keys)
                        whoShotMe += ":" + ScoreData[player].hitCounts[vesselName] + ":" + vesselName;
                    logStrings.Add(whoShotMe);
                }

            // Damage from bullets
            foreach (var player in Players)
                if (ScoreData[player].damageFromGuns.Count > 0)
                {
                    string whoDamagedMeWithGuns = "[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: WHODAMAGEDWHOWITHGUNS:" + player;
                    foreach (var vesselName in ScoreData[player].damageFromGuns.Keys)
                        whoDamagedMeWithGuns += ":" + ScoreData[player].damageFromGuns[vesselName].ToString("0.0") + ":" + vesselName;
                    logStrings.Add(whoDamagedMeWithGuns);
                }

            // Who hit who with rockets.
            foreach (var player in Players)
                if (ScoreData[player].rocketStrikeCounts.Count > 0)
                {
                    string whoHitMeWithRockets = "[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: WHOHITWHOWITHROCKETS:" + player;
                    foreach (var vesselName in ScoreData[player].rocketStrikeCounts.Keys)
                        whoHitMeWithRockets += ":" + ScoreData[player].rocketStrikeCounts[vesselName] + ":" + vesselName;
                    logStrings.Add(whoHitMeWithRockets);
                }

            // Who hit parts by who with rockets.
            foreach (var player in Players)
                if (ScoreData[player].rocketPartDamageCounts.Count > 0)
                {
                    string partHitCountsFromRockets = "[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: WHOPARTSHITWHOWITHROCKETS:" + player;
                    foreach (var vesselName in ScoreData[player].rocketPartDamageCounts.Keys)
                        partHitCountsFromRockets += ":" + ScoreData[player].rocketPartDamageCounts[vesselName] + ":" + vesselName;
                    logStrings.Add(partHitCountsFromRockets);
                }

            // Damage from rockets
            foreach (var player in Players)
                if (ScoreData[player].damageFromRockets.Count > 0)
                {
                    string whoDamagedMeWithRockets = "[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: WHODAMAGEDWHOWITHROCKETS:" + player;
                    foreach (var vesselName in ScoreData[player].damageFromRockets.Keys)
                        whoDamagedMeWithRockets += ":" + ScoreData[player].damageFromRockets[vesselName].ToString("0.0") + ":" + vesselName;
                    logStrings.Add(whoDamagedMeWithRockets);
                }

            // Who hit who with missiles.
            foreach (var player in Players)
                if (ScoreData[player].missileHitCounts.Count > 0)
                {
                    string whoHitMeWithMissiles = "[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: WHOHITWHOWITHMISSILES:" + player;
                    foreach (var vesselName in ScoreData[player].missileHitCounts.Keys)
                        whoHitMeWithMissiles += ":" + ScoreData[player].missileHitCounts[vesselName] + ":" + vesselName;
                    logStrings.Add(whoHitMeWithMissiles);
                }

            // Who hit parts by who with missiles.
            foreach (var player in Players)
                if (ScoreData[player].missilePartDamageCounts.Count > 0)
                {
                    string partHitCountsFromMissiles = "[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: WHOPARTSHITWHOWITHMISSILES:" + player;
                    foreach (var vesselName in ScoreData[player].missilePartDamageCounts.Keys)
                        partHitCountsFromMissiles += ":" + ScoreData[player].missilePartDamageCounts[vesselName] + ":" + vesselName;
                    logStrings.Add(partHitCountsFromMissiles);
                }

            // Damage from missiles
            foreach (var player in Players)
                if (ScoreData[player].damageFromMissiles.Count > 0)
                {
                    string whoDamagedMeWithMissiles = "[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: WHODAMAGEDWHOWITHMISSILES:" + player;
                    foreach (var vesselName in ScoreData[player].damageFromMissiles.Keys)
                        whoDamagedMeWithMissiles += ":" + ScoreData[player].damageFromMissiles[vesselName].ToString("0.0") + ":" + vesselName;
                    logStrings.Add(whoDamagedMeWithMissiles);
                }

            // Who rammed who.
            foreach (var player in Players)
                if (ScoreData[player].rammingPartLossCounts.Count > 0)
                {
                    string whoRammedMe = "[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: WHORAMMEDWHO:" + player;
                    foreach (var vesselName in ScoreData[player].rammingPartLossCounts.Keys)
                        whoRammedMe += ":" + ScoreData[player].rammingPartLossCounts[vesselName] + ":" + vesselName;
                    logStrings.Add(whoRammedMe);
                }

            // Battle Damage
            foreach (var player in Players)
                if (ScoreData[player].battleDamageFrom.Count > 0)
                {
                    string whoDamagedMeWithBattleDamages = "[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: WHODAMAGEDWHOWITHBATTLEDAMAGE:" + player;
                    foreach (var vesselName in ScoreData[player].battleDamageFrom.Keys)
                        whoDamagedMeWithBattleDamages += ":" + ScoreData[player].battleDamageFrom[vesselName].ToString("0.0") + ":" + vesselName;
                    logStrings.Add(whoDamagedMeWithBattleDamages);
                }

            // GM kill reasons
            foreach (var player in Players)
                if (ScoreData[player].gmKillReason != GMKillReason.None)
                    logStrings.Add($"[BDArmory.BDACompetitionMode:{CompetitionID}]: GMKILL:{player}:{ScoreData[player].gmKillReason}");

            // Clean kills/rams/etc.
            var specialKills = new HashSet<AliveState> { AliveState.CleanKill, AliveState.HeadShot, AliveState.KillSteal };
            foreach (var player in Players)
            {
                if (specialKills.Contains(ScoreData[player].aliveState) && ScoreData[player].gmKillReason == GMKillReason.None)
                {
                    logStrings.Add("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: " + ScoreData[player].aliveState.ToString().ToUpper() + ScoreData[player].lastDamageWasFrom.ToString().ToUpper() + ":" + player + ":" + ScoreData[player].lastPersonWhoDamagedMe);
                }
            }

            // Asteroids
            foreach (var player in Players)
                if (ScoreData[player].partsLostToAsteroids > 0)
                    logStrings.Add($"[BDArmory.BDACompetitionMode:{CompetitionID}]: PARTSLOSTTOASTEROIDS:{player}:{ScoreData[player].partsLostToAsteroids}");

            // remaining health
            foreach (var key in Players)
            {
                logStrings.Add("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: HPLEFT:" + key + ":" + ScoreData[key].remainingHP);
            }

            // Accuracy
            foreach (var player in Players)
            {
                logStrings.Add("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: ACCURACY:" + player + ":" + ScoreData[player].hits + "/" + ScoreData[player].shotsFired + ":" + ScoreData[player].rocketStrikes + "/" + ScoreData[player].rocketsFired);
            }

            // Time "IT" and kills while "IT" logging
            if (BDArmorySettings.TAG_MODE)
            {
                foreach (var player in Players)
                    logStrings.Add("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: TAGSCORE:" + player + ":" + ScoreData[player].tagScore.ToString("0.0"));

                foreach (var player in Players)
                    logStrings.Add("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: TIMEIT:" + player + ":" + ScoreData[player].tagTotalTime.ToString("0.0"));

                foreach (var player in Players)
                    if (ScoreData[player].tagKillsWhileIt > 0)
                        logStrings.Add("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: KILLSWHILEIT:" + player + ":" + ScoreData[player].tagKillsWhileIt);

                foreach (var player in Players)
                    if (ScoreData[player].tagTimesIt > 0)
                        logStrings.Add("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: TIMESIT:" + player + ":" + ScoreData[player].tagTimesIt);
            }

            // Waypoints
            foreach (var player in Players)
            {
                if (ScoreData[player].waypointsReached.Count > 0)
                    logStrings.Add("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: WAYPOINTS:" + player + ":" + string.Join(";", ScoreData[player].waypointsReached.Select(wp => wp.waypointIndex + ":" + wp.deviation.ToString("F2") + ":" + wp.timestamp.ToString("F2"))));
            }

            // Dump the log results to a file
            var folder = Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "BDArmory", "Logs"));
            if (BDATournament.Instance.tournamentStatus == TournamentStatus.Running)
            {
                folder = Path.Combine(folder, "Tournament " + BDATournament.Instance.tournamentID, "Round " + BDATournament.Instance.currentRound);
                tag = "Heat " + BDATournament.Instance.currentHeat;
            }
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);
            var fileName = Path.Combine(folder, CompetitionID.ToString() + (tag != "" ? "-" + tag : "") + ".log");
            Debug.Log($"[BDArmory.BDACompetitionMode]: Dumping competition results to {fileName}");
            File.WriteAllLines(fileName, logStrings);
        }
    }

    public class ScoringData
    {
        public AliveState aliveState = AliveState.Alive; // State of the vessel.
        public string team; // The vessel's team.

        #region Guns
        public int hits; // Number of hits this vessel landed.
        public int PinataHits; // Number of hits this vessel landed on the piñata (included in Hits).
        public int shotsFired = 0; // Number of shots fired by this vessel.
        public Dictionary<string, int> hitCounts = new Dictionary<string, int>(); // Hits taken from guns fired by other vessels.
        public Dictionary<string, float> damageFromGuns = new Dictionary<string, float>(); // Damage taken from guns fired by other vessels.
        #endregion

        #region Rockets
        public int totalDamagedPartsDueToRockets = 0; // Number of other vessels' parts damaged by this vessel due to rocket strikes.
        public int rocketStrikes = 0; // Number of rockets fired by the vessel that hit someone.
        public int rocketsFired = 0; // Number of rockets fired by this vessel.
        public Dictionary<string, float> damageFromRockets = new Dictionary<string, float>(); // Damage taken from rocket hits from other vessels.
        public Dictionary<string, int> rocketPartDamageCounts = new Dictionary<string, int>(); // Number of parts damaged by rocket hits from other vessels.
        public Dictionary<string, int> rocketStrikeCounts = new Dictionary<string, int>(); // Number of rocket strikes from other vessels.
        #endregion

        #region Ramming
        public int totalDamagedPartsDueToRamming = 0; // Number of other vessels' parts destroyed by this vessel due to ramming.
        public Dictionary<string, int> rammingPartLossCounts = new Dictionary<string, int>(); // Number of parts lost due to ramming by other vessels.
        #endregion

        #region Missiles
        public int totalDamagedPartsDueToMissiles = 0; // Number of other vessels' parts damaged by this vessel due to missile strikes.
        public Dictionary<string, float> damageFromMissiles = new Dictionary<string, float>(); // Damage taken from missile strikes from other vessels.
        public Dictionary<string, int> missilePartDamageCounts = new Dictionary<string, int>(); // Number of parts damaged by missile strikes from other vessels.
        public Dictionary<string, int> missileHitCounts = new Dictionary<string, int>(); // Number of missile strikes from other vessels.
        #endregion

        #region Special
        public int partsLostToAsteroids = 0; // Number of parts lost due to crashing into asteroids.
        #endregion

        #region Battle Damage
        public Dictionary<string, float> battleDamageFrom = new Dictionary<string, float>(); // Battle damage taken from others.
        #endregion

        #region GM
        public double lastFiredTime; // Time that this vessel last fired a gun.
        public bool landedState; // Whether the vessel is landed or not.
        public double lastLandedTime; // Time that this vessel was landed last.
        public double landedKillTimer; // Counter tracking time this vessel is landed (for the kill timer).
        public double AltitudeKillTimer; // Counter tracking time this vessel is outside GM altitude restrictions (for kill timer).
        public double AverageSpeed; // Average speed of this vessel recently (for the killer GM).
        public double AverageAltitude; // Average altitude of this vessel recently (for the killer GM).
        public int averageCount; // Count for the averaging stats.
        public GMKillReason gmKillReason = GMKillReason.None; // Reason the GM killed this vessel.
        #endregion

        #region Tag
        public bool tagIsIt = false; // Whether this vessel is IT or not.
        public int tagKillsWhileIt = 0; // The number of kills gained while being IT.
        public int tagTimesIt = 0; // The number of times this vessel was IT.
        public double tagTotalTime = 0; // The total this vessel spent being IT.
        public double tagScore = 0; // Abstract score for tag mode.
        public double tagLastUpdated = 0; // Time the tag time was last updated.
        #endregion

        #region Waypoint
        public struct WaypointReached
        {
            public WaypointReached(int waypointIndex, float deviation, double timestamp) { this.waypointIndex = waypointIndex; this.deviation = deviation; this.timestamp = timestamp; }
            public int waypointIndex; // Number of waypoints this vessel reached.
            public float deviation; // Deviation from waypoint.
            public double timestamp; // Timestamp of reaching waypoint.
        }
        public List<WaypointReached> waypointsReached = new List<WaypointReached>();
        public float totalWPDeviation = 0; // Convenience tracker for the Vessel Switcher
        public float totalWPTime = 0; // Convenience tracker for the Vessel Switcher
        #endregion

        #region Misc
        public int previousPartCount; // Number of parts this vessel had last time we checked (for tracking when a vessel has lost parts).
        public double lastLostPartTime = 0; // Time of losing last part (up to granularity of the updateTickLength).
        public double remainingHP; // HP of vessel
        public double lastDamageTime = -1;
        public DamageFrom lastDamageWasFrom = DamageFrom.None;
        public string lastPersonWhoDamagedMe = "";
        public double previousLastDamageTime = -1;
        public string previousPersonWheDamagedMe = "";
        public int deathOrder = -1;
        public double deathTime = -1;
        public HashSet<DamageFrom> damageTypesTaken = new HashSet<DamageFrom>();
        public HashSet<string> everyoneWhoDamagedMe = new HashSet<string>(); // Every other vessel that damaged this vessel.
        #endregion
    }
}
