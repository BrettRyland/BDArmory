using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using BDArmory.Utils;
using BDArmory.Targeting;
using BDArmory.Radar;

namespace BDArmory.CounterMeasure
{
    public class VesselECMJInfo : MonoBehaviour
    {
        List<ModuleECMJammer> jammers;
        public Vessel vessel;
        private TargetInfo ti;
        bool jEnabled;
        bool cleaningRequired = false;

        public bool jammerEnabled
        {
            get { return jEnabled; }
        }

        float jStrength;

        public float jammerStrength
        {
            get { return jStrength; }
        }

        float lbs;

        public float lockBreakStrength
        {
            get { return lbs; }
        }

        float rcsr;

        public float rcsReductionFactor
        {
            get { return rcsr; }
        }
        void Start()
        {
            if (!Setup())
            {
                Destroy(this);
                return;
            }
            vessel.OnJustAboutToBeDestroyed += AboutToBeDestroyed;
            GameEvents.onVesselCreate.Add(OnVesselCreate);
            GameEvents.onPartJointBreak.Add(OnPartJointBreak);
            GameEvents.onPartDie.Add(OnPartDie);
        }

        bool Setup()
        {
            if (!vessel) vessel = GetComponent<Vessel>();
            if (!vessel)
            {
                Debug.Log("[BDArmory.VesselECMJInfo]: VesselECMJInfo was added to an object with no vessel component");
                return false;
            }
            if (jammers is null) jammers = new List<ModuleECMJammer>();
            return true;
        }

        void OnDestroy()
        {
            if (vessel) vessel.OnJustAboutToBeDestroyed -= AboutToBeDestroyed;
            GameEvents.onVesselCreate.Remove(OnVesselCreate);
            GameEvents.onPartJointBreak.Remove(OnPartJointBreak);
            GameEvents.onPartDie.Remove(OnPartDie);
        }

        void AboutToBeDestroyed()
        {
            Destroy(this);
        }

        void OnPartDie() => OnPartDie(null);
        void OnPartDie(Part p) => cleaningRequired = true;
        void OnVesselCreate(Vessel v) => cleaningRequired = true;
        void OnPartJointBreak(PartJoint j, float breakForce) => cleaningRequired = true;

        public void AddJammer(ModuleECMJammer jammer)
        {
            if (jammers is null && !Setup())
            {
                Destroy(this);
                return;
            }

            if (!jammers.Contains(jammer))
            {
                jammers.Add(jammer);
            }

            UpdateJammerStrength();
        }

        public void RemoveJammer(ModuleECMJammer jammer)
        {
            if (jammers is null && !Setup())
            {
                Destroy(this);
                return;
            }

            jammers.Remove(jammer);

            UpdateJammerStrength();
        }

        public void UpdateJammerStrength(TargetInfo tInfoIn = null)
        {
            if (jammers is null && !Setup())
            {
                Destroy(this);
                return;
            }
            jEnabled = jammers.Count > 0;

            if (!jammerEnabled)
            {
                jStrength = 0;
            }

            float totaljStrength = 0;
            float totalLBstrength = 0;
            float jSpamFactor = 1;
            float lbreakFactor = 1;

            float rcsrTotal = 1;
            float rcsrCount = 0;

            float rcsOverride = -1;

            List<ModuleECMJammer>.Enumerator jammer = jammers.GetEnumerator();
            while (jammer.MoveNext())
            {
                if (jammer.Current == null) continue;
                if (jammer.Current.signalSpam)
                {
                    totaljStrength += jSpamFactor * jammer.Current.jammerStrength;
                    jSpamFactor *= 0.75f;
                }
                if (jammer.Current.lockBreaker)
                {
                    totalLBstrength += lbreakFactor * jammer.Current.lockBreakerStrength;
                    lbreakFactor *= 0.65f;
                }
                if (jammer.Current.rcsReduction)
                {
                    rcsrTotal *= jammer.Current.rcsReductionFactor;
                    rcsrCount++;
                    if (rcsOverride < jammer.Current.rcsOverride) rcsOverride = jammer.Current.rcsOverride;
                }
            }
            jammer.Dispose();

            lbs = totalLBstrength;
            jStrength = totaljStrength;

            if (rcsrCount > 0)
            {
                rcsr = Math.Max((rcsrTotal * rcsrCount), 0.0f); //allow for 100% stealth (cloaking device) or stealth malus (radar reflectors)
            }
            else
            {
                rcsr = 1f;
            }

            if (tInfoIn == null)
                ti = RadarUtils.GetVesselRadarSignature(vessel, false);
            else
                ti = tInfoIn;

            if (rcsOverride > 0) ti.radarBaseSignature = rcsOverride;
            ti.radarRCSReducedSignature = ti.radarBaseSignature;
            ti.radarModifiedSignature = ti.radarBaseSignature;
            ti.radarLockbreakFactor = 1f;
            //1) read vessel ecminfo for jammers with RCS reduction effect and multiply factor
            ti.radarRCSReducedSignature *= rcsr;
            ti.radarModifiedSignature *= rcsr;
            //2) increase in detectability relative to jammerstrength and vessel rcs signature:
            // rcs_factor = jammerStrength / modifiedSig / 100 + 1.0f
            ti.radarModifiedSignature *= (((totaljStrength / ti.radarRCSReducedSignature) / 100f) + 1.0f);
            //3) garbling due to overly strong jamming signals relative to jammer's strength in relation to vessel rcs signature:
            // jammingDistance =  (jammerstrength / baseSig / 100 + 1.0) x js
            ti.radarJammingDistance = ((totaljStrength / ti.radarBaseSignature / 100f) + 1.0f) * totaljStrength;
            //4) lockbreaking strength relative to jammer's lockbreak strength in relation to vessel rcs signature:
            // lockbreak_factor = baseSig/modifiedSig x (1 - lockBreakStrength/baseSig/100)
            // Use clamp to prevent RCS reduction resulting in increased lockbreak factor, which negates value of RCS reduction)
            ti.radarLockbreakFactor = (ti.radarRCSReducedSignature == 0f) ? 0f :
                Math.Max(Mathf.Clamp01(ti.radarRCSReducedSignature / ti.radarModifiedSignature) * (1f - (totalLBstrength / ti.radarRCSReducedSignature / 100f)), 0f); // 0 is minimum lockbreak factor
        }
        void FixedUpdate()
        {
            if (UI.BDArmorySetup.GameIsPaused) return;
            //Debug.Log($"[ECMDebug]: jammer on {vessel.GetName()} active! Jammer strength: {jStrength}");
            if (jEnabled && jStrength > 0)
            {
                using (var loadedvessels = UI.BDATargetManager.LoadedVessels.GetEnumerator())
                    while (loadedvessels.MoveNext())
                    {
                        // ignore null, unloaded
                        if (loadedvessels.Current == null || !loadedvessels.Current.loaded || loadedvessels.Current == vessel) continue;
                        float distanceSqr = (loadedvessels.Current.CoM - vessel.CoM).sqrMagnitude;
                        if (distanceSqr < jStrength * jStrength * 100f)
                        {
                            RadarWarningReceiver.PingRWR(loadedvessels.Current, vessel.CoM, RadarWarningReceiver.RWRThreatTypes.Jamming, 0.2f, vessel);
                            //Debug.Log($"[ECMDebug]: jammer on {vessel.GetName()} active! Pinging RWR on {loadedvessels.Current.GetName()}");
                        }
                    }
            }
            if (cleaningRequired)
            {
                StartCoroutine(DelayedCleanJammerListRoutine());
                cleaningRequired = false; // Set it false here instead of in CleanJammerList to allow it to be triggered on consecutive frames.
            }
        }
        public void DelayedCleanJammerList()
        {
            cleaningRequired = true;
        }

        IEnumerator DelayedCleanJammerListRoutine()
        {
            var wait = new WaitForFixedUpdate();
            yield return wait;
            yield return wait;
            CleanJammerList();
        }

        void CleanJammerList()
        {
            vessel = GetComponent<Vessel>();

            if (!vessel)
            {
                Destroy(this);
            }
            jammers.RemoveAll(j => j == null);
            jammers.RemoveAll(j => j.vessel != vessel);

            using (var jam = VesselModuleRegistry.GetModules<ModuleECMJammer>(vessel).GetEnumerator())
                while (jam.MoveNext())
                {
                    if (jam.Current == null) continue;
                    if (jam.Current.jammerEnabled)
                    {
                        AddJammer(jam.Current);
                    }
                }
            UpdateJammerStrength();
        }
    }
}
