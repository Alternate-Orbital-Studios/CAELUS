using UnityEngine;

namespace Caelus
{
    public class ModuleCaelusSequencer : PartModule
    {
        [KSPField] public string whenGone = "";
        [KSPField] public string engineID = "";
        [KSPField] public string action = "engine";
        [KSPField] public float delay = 0f;
        [KSPField] public string label = "";
        [KSPField(isPersistant = true)] public bool armed = false;
        [KSPField(isPersistant = true)] public bool done = false;
        [KSPField(isPersistant = true)] public double fireAt = -1;
        string[] keys;
        int lastCount = -1;
        Vessel lastVessel;

        public override void OnStart(StartState state)
        {
            var list = new System.Collections.Generic.List<string>();
            foreach (var k in whenGone.Split(',')) if (k.Trim().Length > 0) list.Add(k.Trim());
            keys = list.ToArray();
        }

        bool Present()
        {
            foreach (var p in vessel.parts)
            {
                if (p == part || p.partInfo == null) continue;
                foreach (var k in keys) if (p.partInfo.name.StartsWith(k)) return true;
            }
            return false;
        }

        void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || done || vessel == null || keys == null || keys.Length == 0) return;
            if (fireAt < 0)
            {
                if (vessel == lastVessel && vessel.parts.Count == lastCount) return;
                lastVessel = vessel; lastCount = vessel.parts.Count;
                if (Present()) { armed = true; return; }
                if (!armed) return;
                fireAt = Planetarium.GetUniversalTime() + delay;
                Debug.Log("[CaelusSequencer] " + Name() + ": " + whenGone + " separated, firing in " + delay.ToString("F1") + " s");
            }
            if (Planetarium.GetUniversalTime() < fireAt) return;
            done = true;
            Fire();
        }

        string Name() { return (label.Length > 0 ? label : action + " " + engineID) + " on " + part.partInfo.title; }

        void Fire()
        {
            if (action == "decouple")
            {
                foreach (var d in part.FindModulesImplementing<ModuleDecouple>())
                {
                    if (d.isDecoupled) { Debug.Log("[CaelusSequencer] " + Name() + ": already decoupled"); return; }
                    d.Decouple();
                    Debug.Log("[CaelusSequencer] " + Name() + ": decoupled");
                    return;
                }
                Debug.Log("[CaelusSequencer] " + Name() + ": no decoupler on the part");
                return;
            }
            foreach (var e in part.FindModulesImplementing<ModuleEngines>())
            {
                if (e.engineID != engineID) continue;
                if (e.EngineIgnited || e.flameout || e.getIgnitionState) { Debug.Log("[CaelusSequencer] " + Name() + ": already lit"); return; }
                e.Activate();
                Debug.Log("[CaelusSequencer] " + Name() + ": fired");
                return;
            }
            Debug.Log("[CaelusSequencer] " + Name() + ": no engine module " + engineID);
        }
    }
}
