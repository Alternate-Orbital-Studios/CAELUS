using System.Collections.Generic;
using UnityEngine;

namespace CaelusSmoke
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class Pyro : MonoBehaviour
    {
        readonly Dictionary<int, bool> state = new Dictionary<int, bool>();
        float next, lastBang = -10f;

        void FixedUpdate()
        {
            if (Time.time < next) return;
            next = Time.time + 0.05f;
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                if (v == null) continue;
                foreach (Part p in v.parts)
                {
                    if (p == null || !Ours(p)) continue;
                    foreach (PartModule m in p.Modules)
                    {
                        var d = m as ModuleDecouplerBase;
                        if (d == null) continue;
                        int id = d.GetInstanceID(); bool was;
                        if (state.TryGetValue(id, out was) && !was && d.isDecoupled)
                        {

                            bool again = Time.time - lastBang < 0.15f;
                            if (!again) { p.Effect("decouple", 1f); lastBang = Time.time; }
                            Debug.Log("[CaelusSmoke] pyro: " + p.partInfo.title + (again ? " (same instant, not repeated)" : ""));
                        }
                        state[id] = d.isDecoupled;
                    }
                }
            }
        }

        static bool Ours(Part p)
        {
            string n = p.partInfo != null ? p.partInfo.name : "";
            return n.StartsWith("Caelus") || n.StartsWith("CaelusCorona") || n.StartsWith("RomaAeterna");
        }
    }
}
