using BDArmory.Weapons.Missiles;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BDArmory.WeaponMounts
{
    public class BDAdjustableRail : PartModule
    {
        [KSPField(isPersistant = true)] public float railHeight;

        [KSPField(isPersistant = true)] public float railLength = 1;

        Transform railLengthTransform;
        Transform railHeightTransform;
        Transform launchTransform;

        [KSPField] public string stackNodePosition;

        Dictionary<string, Vector3> originalStackNodePosition;

        public override void OnStart(StartState state)
        {
            railLengthTransform = part.FindModelTransform("Rail");
            railHeightTransform = part.FindModelTransform("RailSleeve");

            var launcher = part.FindModuleImplementing<MultiMissileLauncher>();
            if (launcher)
            {
                var tempTransform = part.FindModelTransform(launcher.launchTransformName);
                if (tempTransform.IsChildOf(railLengthTransform))
                    launchTransform = tempTransform;
            }
            railLengthTransform.localScale = new Vector3(1, railLength, 1);
            railHeightTransform.localPosition = new Vector3(0, railHeight, 0);

            if (HighLogic.LoadedSceneIsEditor)
            {
                ParseStackNodePosition();
                StartCoroutine(DelayedUpdateStackNode());
            }
        }

        void ParseStackNodePosition()
        {
            originalStackNodePosition = new Dictionary<string, Vector3>();
            string[] nodes = stackNodePosition.Split(new char[] { ';' });
            for (int i = 0; i < nodes.Length; i++)
            {
                string[] split = nodes[i].Split(new char[] { ',' });
                string id = split[0];
                Vector3 position = new Vector3(float.Parse(split[1]), float.Parse(split[2]), float.Parse(split[3]));
                originalStackNodePosition.Add(id, position);
            }
        }

        IEnumerator DelayedUpdateStackNode()
        {
            yield return new WaitForFixedUpdate();
            UpdateStackNode(false);
        }

        [KSPEvent(guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_IncreaseHeight", active = true)]//Height ++
        public void IncreaseHeight()
        {
            railHeight = Mathf.Clamp(railHeight - 0.02f, -.16f, 0);
            railHeightTransform.localPosition = new Vector3(0, railHeight, 0);

            UpdateStackNode(true);

            List<Part>.Enumerator sym = part.symmetryCounterparts.GetEnumerator();
            while (sym.MoveNext())
            {
                if (sym.Current == null) continue;
                sym.Current.FindModuleImplementing<BDAdjustableRail>().UpdateHeight(railHeight);
            }
            sym.Dispose();
        }

        [KSPEvent(guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_DecreaseHeight", active = true)]//Height --
        public void DecreaseHeight()
        {
            railHeight = Mathf.Clamp(railHeight + 0.02f, -.16f, 0);
            railHeightTransform.localPosition = new Vector3(0, railHeight, 0);

            UpdateStackNode(true);

            List<Part>.Enumerator sym = part.symmetryCounterparts.GetEnumerator();
            while (sym.MoveNext())
            {
                if (sym.Current == null) continue;
                sym.Current.FindModuleImplementing<BDAdjustableRail>().UpdateHeight(railHeight);
            }
            sym.Dispose();
        }

        [KSPEvent(guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_IncreaseLength", active = true)]//Length ++
        public void IncreaseLength()
        {
            railLength = Mathf.Clamp(railLength + 0.2f, 0.4f, 2f);
            railLengthTransform.localScale = new Vector3(1, railLength, 1);
            if (launchTransform != null)
                launchTransform.localScale = new Vector3(1, 1 / railLength, 1);
            List<Part>.Enumerator sym = part.symmetryCounterparts.GetEnumerator();
            while (sym.MoveNext())
            {
                if (sym.Current == null) continue;
                sym.Current.FindModuleImplementing<BDAdjustableRail>().UpdateLength(railLength);
            }
            sym.Dispose();
        }

        [KSPEvent(guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_DecreaseLength", active = true)]//Length --
        public void DecreaseLength()
        {
            railLength = Mathf.Clamp(railLength - 0.2f, 0.4f, 2f);
            railLengthTransform.localScale = new Vector3(1, railLength, 1);
            if (launchTransform != null)
                launchTransform.localScale = new Vector3(1, 1 / railLength, 1);
            List<Part>.Enumerator sym = part.symmetryCounterparts.GetEnumerator();
            while (sym.MoveNext())
            {
                if (sym.Current == null) continue;
                sym.Current.FindModuleImplementing<BDAdjustableRail>().UpdateLength(railLength);
            }
            sym.Dispose();
        }

        public void UpdateHeight(float height)
        {
            railHeight = height;
            railHeightTransform.localPosition = new Vector3(0, railHeight, 0);

            UpdateStackNode(true);
        }

        public void UpdateLength(float length)
        {
            railLength = length;
            railLengthTransform.localScale = new Vector3(1, railLength, 1);
            if (launchTransform != null)
                launchTransform.localScale = new Vector3(1, 1 / railLength, 1);
        }

        public void UpdateStackNode(bool updateChild)
        {
            Vector3 delta = Vector3.zero;
            List<AttachNode>.Enumerator stackNode = part.attachNodes.GetEnumerator();
            while (stackNode.MoveNext())
            {
                if (stackNode.Current?.nodeType != AttachNode.NodeType.Stack ||
                    !originalStackNodePosition.ContainsKey(stackNode.Current.id)) continue;
                Vector3 prevPos = stackNode.Current.position;

                stackNode.Current.position.y = originalStackNodePosition[stackNode.Current.id].y + railHeight;
                delta = stackNode.Current.position - prevPos;
            }
            stackNode.Dispose();

            if (!updateChild) return;
            Vector3 worldDelta = part.transform.TransformVector(delta);
            List<Part>.Enumerator p = part.children.GetEnumerator();
            while (p.MoveNext())
            {
                if (p.Current == null) continue;
                p.Current.transform.position += worldDelta;
            }
            p.Dispose();
        }
    }
}
