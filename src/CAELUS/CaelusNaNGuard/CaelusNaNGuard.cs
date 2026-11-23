using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Caelus
{
    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    public class CaelusNaNGuard : MonoBehaviour
    {
        static Type layerType; static FieldInfo stopField; static bool searched;
        readonly HashSet<int> seen = new HashSet<int>();
        float next;

        static void Find()
        {
            if (searched) return; searched = true;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] ts; try { ts = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { ts = e.Types; } catch { continue; }
                foreach (var t in ts)
                {
                    if (t == null || t.Name != "PostProcessLayer" || !typeof(Component).IsAssignableFrom(t)) continue;
                    var f = t.GetField("stopNaNPropagation", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (f == null || f.FieldType != typeof(bool)) continue;
                    layerType = t; stopField = f;
                    Debug.Log("[CaelusNaNGuard] using " + t.FullName + " from " + asm.GetName().Name);
                    return;
                }
            }
            Debug.Log("[CaelusNaNGuard] no PostProcessLayer type found - nothing to guard");
        }

        static bool IsCaelusPart(Part p)
        {
            if (p == null || p.partInfo == null || p.partInfo.name == null) return false;
            string n = p.partInfo.name;
            return n.StartsWith("Caelus", StringComparison.Ordinal) || n.StartsWith("CaelusCorona", StringComparison.Ordinal) || n.StartsWith("RomaAeterna", StringComparison.Ordinal);
        }
        static bool AnyCaelus(List<Part> ps)
        {
            if (ps == null) return false;
            for (int i = 0; i < ps.Count; i++) if (IsCaelusPart(ps[i])) return true;
            return false;
        }
        static bool CaelusInScene()
        {
            if (HighLogic.LoadedSceneIsFlight) { var v = FlightGlobals.ActiveVessel; return v != null && AnyCaelus(v.parts); }
            if (HighLogic.LoadedSceneIsEditor) return EditorLogic.fetch != null && EditorLogic.fetch.ship != null && AnyCaelus(EditorLogic.fetch.ship.parts);
            return false;
        }

        readonly Dictionary<Component, bool> changed = new Dictionary<Component, bool>();

        void GiveBack(string why)
        {
            if (changed.Count == 0) return;
            int n = 0;
            foreach (var kv in changed) if (kv.Key != null) { try { stopField.SetValue(kv.Key, kv.Value); n++; } catch { } }
            changed.Clear(); seen.Clear();
            Debug.Log("[CaelusNaNGuard] " + why + " - stopNaNPropagation restored on " + n + " layer(s)");
        }

        void Update()
        {
            if (Time.realtimeSinceStartup < next) return;
            next = Time.realtimeSinceStartup + 2f;
            Find();
            if (layerType == null) return;

            if (!CaelusInScene()) { GiveBack("no CAELUS vessel active"); return; }
            foreach (var o in Resources.FindObjectsOfTypeAll(layerType))
            {
                var c = o as Component; if (c == null || c.gameObject.scene.name == null) continue;
                bool was = (bool)stopField.GetValue(c);
                if (!was) { if (!changed.ContainsKey(c)) changed[c] = was; stopField.SetValue(c, true); }
                if (seen.Add(c.GetInstanceID()))
                    Debug.Log("[CaelusNaNGuard] " + HighLogic.LoadedScene + " camera '" + c.gameObject.name + "': stopNaNPropagation was " + was + (was ? "" : " -> set true (CAELUS vessel)"));
            }
        }

        void OnDestroy() { if (stopField != null) GiveBack("scene closed"); }
    }
}
