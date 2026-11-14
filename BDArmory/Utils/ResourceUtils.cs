using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using BDArmory.Settings;

namespace BDArmory.Utils
{
    public static class ResourceUtils
    {
        public static HashSet<string> FuelResources
        {
            get
            {
                if (_FuelResources == null)
                {
                    _FuelResources = new HashSet<string>();
                    foreach (var resource in PartResourceLibrary.Instance.resourceDefinitions)
                    {
                        if (resource.name.EndsWith("Fuel") || resource.name.EndsWith("Oxidizer") || resource.name.EndsWith("Air") || resource.name.EndsWith("Charge") || resource.name.EndsWith("Gas") || resource.name.EndsWith("Propellant")) // FIXME These ought to be configurable
                        { _FuelResources.Add(resource.name); }
                    }
                    Debug.Log("[BDArmory.ProjectileUtils]: Fuel resources: " + string.Join(", ", _FuelResources));
                }
                return _FuelResources;
            }
        }
        static HashSet<string> _FuelResources;
        public static HashSet<string> AmmoResources
        {
            get
            {
                if (_AmmoResources == null)
                {
                    _AmmoResources = new HashSet<string>();
                    foreach (var resource in PartResourceLibrary.Instance.resourceDefinitions)
                    {
                        if (resource.name.EndsWith("Ammo") || resource.name.EndsWith("Shell") || resource.name.EndsWith("Shells") || resource.name.EndsWith("Rocket") || resource.name.EndsWith("Rockets") || resource.name.EndsWith("Bolt") || resource.name.EndsWith("Mauser"))
                        { _AmmoResources.Add(resource.name); }
                    }
                    Debug.Log("[BDArmory.ProjectileUtils]: Ammo resources: " + string.Join(", ", _AmmoResources));
                }
                return _AmmoResources;
            }
        }
        static HashSet<string> _AmmoResources;
        public static HashSet<string> CMResources
        {
            get
            {
                if (_CMResources == null)
                {
                    _CMResources = new HashSet<string>();
                    foreach (var resource in PartResourceLibrary.Instance.resourceDefinitions)
                    {
                        if (resource.name.EndsWith("Flare") || resource.name.EndsWith("Smoke") || resource.name.EndsWith("Chaff"))
                        { _CMResources.Add(resource.name); }
                    }
                    Debug.Log("[BDArmory.ProjectileUtils]: Couter-measure resources: " + string.Join(", ", _CMResources));
                }
                return _CMResources;
            }
        }
        static HashSet<string> _CMResources;

        public static void StealResources(Part hitPart, Vessel sourceVessel, bool thiefWeapon = false)
        {
            // steal resources if enabled
            if (BDArmorySettings.RESOURCE_STEAL_ENABLED || thiefWeapon)
            {
                if (BDArmorySettings.RESOURCE_STEAL_FUEL_RATION > 0f) StealResource(hitPart.vessel, sourceVessel, FuelResources, BDArmorySettings.RESOURCE_STEAL_FUEL_RATION);
                if (BDArmorySettings.RESOURCE_STEAL_AMMO_RATION > 0f) StealResource(hitPart.vessel, sourceVessel, AmmoResources, BDArmorySettings.RESOURCE_STEAL_AMMO_RATION, true);
                if (BDArmorySettings.RESOURCE_STEAL_CM_RATION > 0f) StealResource(hitPart.vessel, sourceVessel, CMResources, BDArmorySettings.RESOURCE_STEAL_CM_RATION, true);
            }
        }

        private class PriorityQueue
        {
            private Dictionary<int, List<PartResource>> partResources = new Dictionary<int, List<PartResource>>();

            public PriorityQueue(HashSet<PartResource> elements)
            {
                foreach (PartResource r in elements)
                {
                    Add(r);
                }
            }

            public void Add(PartResource r)
            {
                int key = r.part.resourcePriorityOffset;
                if (partResources.ContainsKey(key))
                {
                    List<PartResource> existing = partResources[key];
                    existing.Add(r);
                    partResources[key] = existing;
                }
                else
                {
                    List<PartResource> newList = new List<PartResource>();
                    newList.Add(r);
                    partResources.Add(key, newList);
                }
            }

            public List<PartResource> Pop()
            {
                if (partResources.Count == 0)
                {
                    return new List<PartResource>();
                }
                int key = partResources.Keys.Max();
                List<PartResource> result = partResources[key];
                partResources.Remove(key);
                return result;
            }

            public bool HasNext()
            {
                return partResources.Count != 0;
            }
        }

        private static void StealResource(Vessel src, Vessel dst, HashSet<string> resourceNames, double ration, bool integerAmounts = false)
        {
            if (src == null || dst == null) return;

            // identify all parts on source vessel with resource
            Dictionary<string, HashSet<PartResource>> srcParts = new Dictionary<string, HashSet<PartResource>>();
            DeepFind(src.rootPart, resourceNames, srcParts, BDArmorySettings.RESOURCE_STEAL_RESPECT_FLOWSTATE_OUT);

            // identify all parts on destination vessel with resource
            Dictionary<string, HashSet<PartResource>> dstParts = new Dictionary<string, HashSet<PartResource>>();
            DeepFind(dst.rootPart, resourceNames, dstParts, BDArmorySettings.RESOURCE_STEAL_RESPECT_FLOWSTATE_IN);

            foreach (var resourceName in resourceNames)
            {
                if (!srcParts.ContainsKey(resourceName) || !dstParts.ContainsKey(resourceName))
                {
                    // if (BDArmorySettings.DEBUG_LABELS) Debug.Log(string.Format("[BDArmory.ProjectileUtils]: Steal resource {0} failed; no parts.", resourceName));
                    continue;
                }

                double remainingAmount = srcParts[resourceName].Sum(p => p.amount);
                if (integerAmounts)
                {
                    remainingAmount = Math.Floor(remainingAmount);
                    if (remainingAmount == 0) continue; // Nothing left to steal.
                }
                double amount = remainingAmount * ration;
                if (integerAmounts) { amount = Math.Ceiling(amount); } // Round up steal amount so that something is always stolen if there's something to steal.
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.ProjectileUtils]: " + dst.vesselName + " is trying to steal " + amount.ToString("F1") + " of " + resourceName + " from " + src.vesselName);

                // transfer resource from src->dst parts, honoring their priorities
                PriorityQueue sourceQueue = new PriorityQueue(srcParts[resourceName]);
                PriorityQueue destinationQueue = new PriorityQueue(dstParts[resourceName]);
                List<PartResource> sources = null, destinations = null;
                double tolerance = 1e-3;
                double amountTaken = 0;
                while (amount - amountTaken >= (integerAmounts ? 1d : tolerance))
                {
                    if (sources == null)
                    {
                        sources = sourceQueue.Pop();
                        if (sources.Count() == 0) break;
                    }
                    if (destinations == null)
                    {
                        destinations = destinationQueue.Pop();
                        if (destinations.Count() == 0) break;
                    }
                    var availability = sources.Where(e => e.maxAmount > 0 && e.amount >= tolerance / sources.Count).ToList(); // All source parts with something in.
                    var opportunity = destinations.Where(e => e.maxAmount > 0 && e.maxAmount - e.amount >= tolerance / destinations.Count).ToList(); // All destination parts with room to spare.
                    if (availability.Count() == 0) { sources = null; }
                    if (opportunity.Count() == 0) { destinations = null; }
                    if (sources == null || destinations == null) continue;
                    if (integerAmounts)
                    {
                        if (availability.Sum(e => e.amount) < 1d) { sources = null; }
                        if (opportunity.Sum(e => e.maxAmount - e.amount) < 1d) { destinations = null; }
                        if (sources == null || destinations == null) continue;
                    }
                    var minFractionAvailable = availability.Min(r => r.amount / r.maxAmount); // Minimum fraction of container size available for transfer.
                    var minFractionOpportunity = opportunity.Min(r => (r.maxAmount - r.amount) / r.maxAmount); // Minimum fraction of container size available to fill a part.
                    var totalTransferAvailable = availability.Sum(r => r.maxAmount * minFractionAvailable);
                    var totalTransferOpportunity = opportunity.Sum(r => r.maxAmount * minFractionOpportunity);
                    var totalTransfer = Math.Min(amount - amountTaken, Math.Min(totalTransferAvailable, totalTransferOpportunity)); // Total amount to transfer that either transfers the amount required, empties a container or fills a container.
                    if (integerAmounts)
                    {
                        // A small container must not prevent a whole-unit transfer from the group.
                        totalTransfer = Math.Floor(Math.Min(amount - amountTaken,
                            Math.Min(availability.Sum(r => r.amount), opportunity.Sum(r => r.maxAmount - r.amount))));
                    }
                    var totalContainerSizeAvailable = availability.Sum(r => r.maxAmount);
                    var totalContainerSizeOpportunity = opportunity.Sum(r => r.maxAmount);
                    var transferFractionAvailable = totalTransfer / totalContainerSizeAvailable;
                    var transferFractionOpportunity = totalTransfer / totalContainerSizeOpportunity;

                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.ProjectileUtils]: Transferring {totalTransfer:F1} of {resourceName} from {string.Join(", ", availability.Select(a => $"{a.part.name} ({a.amount:F1}/{a.maxAmount:F1})").ToList())} on {src.vesselName} to {string.Join(", ", opportunity.Select(o => $"{o.part.name} ({o.amount:F1}/{o.maxAmount:F1})").ToList())} on {dst.vesselName}");
                    // Transfer directly between parts doesn't seem to be working properly (it leaves the source, but doesn't arrive at the destination).
                    // Snapshot group totals before changing any containers.
                    var availableAmount = availability.Sum(r => r.amount);
                    var opportunityAmount = opportunity.Sum(r => r.maxAmount - r.amount);
                    var measuredOut = 0d;
                    var measuredIn = 0d;
                    foreach (var sourceResource in availability)
                    { measuredOut += sourceResource.part.TransferResource(sourceResource.info.id, -(integerAmounts ? totalTransfer * sourceResource.amount / availableAmount : transferFractionAvailable * sourceResource.maxAmount)); }
                    foreach (var destinationResource in opportunity)
                    { measuredIn += -destinationResource.part.TransferResource(destinationResource.info.id, integerAmounts ? totalTransfer * (destinationResource.maxAmount - destinationResource.amount) / opportunityAmount : transferFractionOpportunity * destinationResource.maxAmount); }
                    if (Math.Abs(measuredIn - measuredOut) > tolerance)
                    { Debug.LogWarning($"[BDArmory.ProjectileUtils]: Discrepancy in the amount of {resourceName} transferred from {string.Join(", ", availability.Select(r => r.part.name))} ({measuredOut:F3}) to {string.Join(", ", opportunity.Select(r => r.part.name))} ({measuredIn:F3})"); }

                    amountTaken += totalTransfer;
                    if (totalTransfer < tolerance)
                    {
                        Debug.LogWarning($"[BDArmory.ProjectileUtils]: totalTransfer was {totalTransfer} for resource {resourceName}, amount: {amount}, availability: {string.Join(", ", availability.Select(r => r.amount))}, opportunity: {string.Join(", ", opportunity.Select(r => r.maxAmount - r.amount))}");
                        if (availability.Sum(r => r.amount) < opportunity.Sum(r => r.maxAmount - r.amount)) { sources = null; } else { destinations = null; }
                    }
                }
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.ProjectileUtils]: Final amount of {resourceName} stolen: {amountTaken:F1}");
            }
        }

        private class ResourceAllocation
        {
            public PartResource sourceResource;
            public Part destPart;
            public double amount;
            public ResourceAllocation(PartResource r, Part p, double a)
            {
                this.sourceResource = r;
                this.destPart = p;
                this.amount = a;
            }
        }

        public static void DeepFind(Part p, HashSet<string> resourceNames, Dictionary<string, HashSet<PartResource>> accumulator, bool respectFlowState)
        {
            foreach (PartResource r in p.Resources)
            {
                if (resourceNames.Contains(r.resourceName))
                {
                    if (respectFlowState && !r.flowState) continue; // Ignore locked resources.
                    if (!accumulator.ContainsKey(r.resourceName))
                        accumulator[r.resourceName] = new HashSet<PartResource>();
                    accumulator[r.resourceName].Add(r);
                }
            }
            foreach (Part child in p.children)
            {
                DeepFind(child, resourceNames, accumulator, respectFlowState);
            }
        }
    }
}