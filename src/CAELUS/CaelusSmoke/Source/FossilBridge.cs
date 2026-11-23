using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace CaelusSmoke
{
    static class FossilBridge
    {
        class Held { public Component c; public bool force, on = true; public readonly List<KeyValuePair<ParticleSystem, bool>> em = new List<KeyValuePair<ParticleSystem, bool>>(); }
        static Type type; static FieldInfo fForce, fEmitters; static bool looked;
        static readonly Dictionary<Guid, Held> held = new Dictionary<Guid, Held>();
        const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        static bool Find()
        {
            if (!looked)
            {
                looked = true;
                foreach (AssemblyLoader.LoadedAssembly a in AssemblyLoader.loadedAssemblies)
                {
                    Type t = a.assembly.GetType("Contrails.FossilContrails", false);
                    if (t == null) continue;
                    type = t; fForce = t.GetField("forceDisable", BF); fEmitters = t.GetField("allEmitters", BF);
                    Debug.Log("[CaelusSmoke] Fossil Contrails found: paused on vessels with our dense trail (forceDisable " + (fForce != null) + ", emitters " + (fEmitters != null) + ")");
                    break;
                }
            }
            return type != null;
        }

        public static void Sync(HashSet<Vessel> ours)
        {
            if (!Find()) return;
            foreach (Vessel v in ours)
            {
                if (v == null || held.ContainsKey(v.id)) continue;
                Component c = v.GetComponent(type);
                if (c != null) held[v.id] = Pause(c);
            }
            List<Guid> back = null;
            foreach (KeyValuePair<Guid, Held> kv in held)
            {
                bool still = false;
                foreach (Vessel v in ours) if (v != null && v.id == kv.Key) { still = true; break; }
                if (!still) (back ?? (back = new List<Guid>())).Add(kv.Key);
            }
            if (back != null) foreach (Guid g in back) { Resume(held[g]); held.Remove(g); }
        }

        public static void ReleaseAll() { foreach (Held h in held.Values) Resume(h); held.Clear(); }

        static Held Pause(Component c)
        {
            var h = new Held { c = c };
            try
            {
                if (fForce != null) { h.force = (bool)fForce.GetValue(c); fForce.SetValue(c, true); }
                var mb = c as Behaviour; if (mb != null) { h.on = mb.enabled; mb.enabled = false; }
                var list = fEmitters != null ? fEmitters.GetValue(c) as List<ParticleSystem> : null;
                if (list != null) foreach (ParticleSystem ps in list)
                    {
                        if (ps == null) continue;
                        ParticleSystem.EmissionModule em = ps.emission; h.em.Add(new KeyValuePair<ParticleSystem, bool>(ps, em.enabled)); em.enabled = false;
                    }
            }
            catch (Exception ex) { Debug.LogWarning("[CaelusSmoke] Fossil Contrails pause failed: " + ex.Message); }
            return h;
        }

        static void Resume(Held h)
        {
            if (h.c == null) return;
            try
            {
                foreach (KeyValuePair<ParticleSystem, bool> kv in h.em) if (kv.Key != null) { ParticleSystem.EmissionModule em = kv.Key.emission; em.enabled = kv.Value; }
                if (fForce != null) fForce.SetValue(h.c, h.force);
                var mb = h.c as Behaviour; if (mb != null) mb.enabled = h.on;
            }
            catch (Exception ex) { Debug.LogWarning("[CaelusSmoke] Fossil Contrails resume failed: " + ex.Message); }
        }
    }
}
