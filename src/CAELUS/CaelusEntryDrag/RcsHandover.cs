using UnityEngine;

namespace Caelus
{
    public class ModuleRcsHandover : PartModule
    {
        [KSPField] public string yieldTo = "Caelus.D06,EngineS3";
        [KSPField(isPersistant = true)] public bool heldOff = false;

        [KSPField(isPersistant = true)] public int heldMask = -1;
        float next;
        string[] keys;

        public override void OnStart(StartState state)
        {
            var list = new System.Collections.Generic.List<string>();
            foreach (var k in yieldTo.Split(',')) if (k.Trim().Length > 0) list.Add(k.Trim());
            keys = list.ToArray();
        }

        void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || keys == null || Time.time < next) return;
            next = Time.time + 0.5f;
            Part by = null;
            foreach (var p in vessel.parts)
            {
                if (p == part || p.partInfo == null) continue;
                foreach (var k in keys) if (p.partInfo.name.Contains(k)) { by = p; break; }
                if (by != null) break;
            }
            if (by != null && !heldOff)
            {
                var rcs = part.FindModulesImplementing<ModuleRCS>(); int mask = 0;
                for (int i = 0; i < rcs.Count && i < 31; i++) if (rcs[i].rcsEnabled) { rcs[i].rcsEnabled = false; mask |= 1 << i; }
                heldOff = true; heldMask = mask;
                if (mask != 0) Debug.Log("[CaelusEntryDrag] RcsHandover: " + part.partInfo.title + " RCS held off while " + by.partInfo.title + " is attached");
            }
            else if (by == null && heldOff)
            {
                var rcs = part.FindModulesImplementing<ModuleRCS>(); int n = 0;
                for (int i = 0; i < rcs.Count; i++)
                    if (heldMask < 0 || (i < 31 && (heldMask & (1 << i)) != 0)) { rcs[i].rcsEnabled = true; n++; }
                heldOff = false; heldMask = 0;
                if (n > 0) Debug.Log("[CaelusEntryDrag] RcsHandover: " + part.partInfo.title + " RCS released (no part matching '" + yieldTo + "' left)");
            }
        }
    }
}
