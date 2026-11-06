using System.Collections.Generic;
using UnityEngine;
using KSP.UI.Screens;

using BDArmory.Control;
using BDArmory.Extensions;
using BDArmory.FX;
using BDArmory.Targeting;

namespace BDArmory.Utils
{
    public static class VesselUtils
    {
        // this stupid thing makes all the BD armory parts explode
        [KSPField]
        public static string explModelPath = "BDArmory/Models/explosion/explosion";
        [KSPField]
        public static string explSoundPath = "BDArmory/Sounds/explode1";

        public static void ForceDeadVessel(Vessel v)
        {
            Debug.Log("[BDArmory.Misc]: GM Killed Vessel " + v.GetName());
            foreach (var missileFire in VesselModuleRegistry.GetMissileFires(v))
            {
                PartExploderSystem.AddPartToExplode(missileFire.part);
                ExplosionFx.CreateExplosion(missileFire.part.transform.position, 1f, explModelPath, explSoundPath, ExplosionSourceType.Other, 0, missileFire.part, sourceVelocity: v.Velocity());
                TargetInfo tInfo;
                v.vesselType = VesselType.Debris;
                if (tInfo = v.gameObject.GetComponent<TargetInfo>())
                {
                    UI.BDATargetManager.RemoveTarget(tInfo); //prevent other craft from chasing GM killed craft (in case of maxAltitude or similar
                    UI.BDATargetManager.LoadedVessels.Remove(v);
                }
                UI.BDATargetManager.LoadedVessels.RemoveAll(ves => ves == null);
                UI.BDATargetManager.LoadedVessels.RemoveAll(ves => ves.loaded == false);
            }
        }


        // borrowed from SmartParts - activate the next stage on a vessel
        public static void fireNextNonEmptyStage(Vessel v)
        {
            // the parts to be fired
            List<Part> resultList = new List<Part>();

            int highestNextStage = getHighestNextStage(v.rootPart, v.currentStage);
            traverseChildren(v.rootPart, highestNextStage, ref resultList);

            foreach (Part stageItem in resultList)
            {
                //Log.Info("Activate:" + stageItem);
                stageItem.activate(highestNextStage, stageItem.vessel);
                stageItem.inverseStage = v.currentStage;
            }
            v.currentStage = highestNextStage;
            //If this is the currently active vessel, activate the next, now empty, stage. This is an ugly, ugly hack but it's the only way to clear out the empty stage.
            //Switching to a vessel that has been staged this way already clears out the empty stage, so this isn't required for those.
            if (v.isActiveVessel)
            {
                StageManager.ActivateNextStage();
            }
        }

        private static int getHighestNextStage(Part p, int currentStage)
        {

            int highestChildStage = 0;

            // if this is the root part and its a decoupler: ignore it. It was probably fired before.
            // This is dirty guesswork but everything else seems not to work. KSP staging is too messy.
            if (p.vessel.rootPart == p &&
                (p.name.IndexOf("ecoupl") != -1 || p.name.IndexOf("eparat") != -1))
            {
            }
            else if (p.inverseStage < currentStage)
            {
                highestChildStage = p.inverseStage;
            }


            // Check all children. If this part has no children, inversestage or current Stage will be returned
            int childStage = 0;
            foreach (Part child in p.children)
            {
                childStage = getHighestNextStage(child, currentStage);
                if (childStage > highestChildStage && childStage < currentStage)
                {
                    highestChildStage = childStage;
                }
            }
            return highestChildStage;
        }

        private static void traverseChildren(Part p, int nextStage, ref List<Part> resultList)
        {
            if (p.inverseStage >= nextStage)
            {
                resultList.Add(p);
            }
            foreach (Part child in p.children)
            {
                traverseChildren(child, nextStage, ref resultList);
            }
        }
    }
}