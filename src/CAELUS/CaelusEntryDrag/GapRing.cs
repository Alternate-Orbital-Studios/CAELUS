using UnityEngine;

namespace Caelus
{
    public class ModuleCaelusGapRing : PartModule
    {
        [KSPField] public string ringTransform = "CLD_IU_GapRing";
        [KSPField] public string node = "bottom";
        [KSPField] public string flushWith = "";
        Transform ring; string[] keys; float next; int state = -1;

        public override void OnStart(StartState s)
        {
            ring = part.FindModelTransform(ringTransform);
            var l = new System.Collections.Generic.List<string>();
            foreach (var k in flushWith.Split(',')) if (k.Trim().Length > 0) l.Add(k.Trim());
            keys = l.ToArray(); Apply();
        }

        void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f; Apply();
        }

        void Apply()
        {
            if (ring == null || keys == null) return;
            AttachNode an = part.FindAttachNode(node); Part below = an != null ? an.attachedPart : null;
            bool flush = below == null || below.partInfo == null;
            if (!flush) foreach (var k in keys) if (below.partInfo.name.StartsWith(k)) { flush = true; break; }
            int want = flush ? 0 : 1;
            if (want == state) return;
            ring.gameObject.SetActive(want == 1); state = want;
        }
    }
}
