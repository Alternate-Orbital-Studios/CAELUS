using System.Collections.Generic;
using UnityEngine;

namespace CaelusSmoke
{
    public class ActuatorFollow
    {
        class Act
        {
            public Transform barrel, rod, gimbal, mount;
            public Vector3 pinG;
            public Vector3 dir0;
            public Quaternion barrel0, rod0;
        }
        readonly Dictionary<Part, List<Act>> parts = new Dictionary<Part, List<Act>>();
        readonly HashSet<Part> seen = new HashSet<Part>();
        float scanTimer;

        public void Update(float dt)
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if ((scanTimer -= dt) < 0f) { scanTimer = 2f; Scan(); }
            List<Part> gone = null;
            foreach (KeyValuePair<Part, List<Act>> kv in parts)
            {
                if (kv.Key == null) { (gone ?? (gone = new List<Part>())).Add(kv.Key); continue; }
                foreach (Act a in kv.Value) Drive(a);
            }
            if (gone != null) foreach (Part p in gone) parts.Remove(p);
        }

        void Scan()
        {
            seen.RemoveWhere(p => p == null);
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                if (v == null || !CaelusParts.Vessel(v)) continue;
                foreach (Part p in v.parts)
                {
                    if (p == null || seen.Contains(p) || !CaelusParts.Is(p)) continue;
                    seen.Add(p);
                    if (p.FindModuleImplementing<ModuleGimbal>() == null) continue;
                    List<Act> l = Build(p);
                    if (l.Count > 0) { parts[p] = l; Debug.Log("[CaelusSmoke] gimbal actuators: " + l.Count + " follow the engines on " + p.partInfo.name); }
                }
            }
        }

        static List<Act> Build(Part p)
        {
            var list = new List<Act>();
            Transform[] all = p.GetComponentsInChildren<Transform>(true);
            var byName = new Dictionary<string, Transform>();
            foreach (Transform t in all) if (t != null && !byName.ContainsKey(t.name)) byName[t.name] = t;
            const string B = "_RIGID_BARREL";
            foreach (Transform barrel in all)
            {
                if (barrel == null || !barrel.name.EndsWith(B)) continue;
                string stem = barrel.name.Substring(0, barrel.name.Length - B.Length);
                int k = stem.LastIndexOf("_ACTUATOR"); if (k <= 0) continue;
                string eng = stem.Substring(0, k) + "_";
                Transform rod; if (!byName.TryGetValue(stem + "_SLIDING_ROD", out rod) || rod == null) continue;
                Transform gimbal = null;
                foreach (Transform t in all) if (t != null && t.name.StartsWith(eng) && t.name.Contains("GIMBAL_AXIS")) { gimbal = t; break; }
                Transform mount = barrel.parent;
                if (gimbal == null || mount == null || rod.parent != mount) continue;

                Quaternion restLocal = gimbal.localRotation;
                foreach (ModuleGimbal mg in p.FindModulesImplementing<ModuleGimbal>())
                {
                    if (mg.gimbalTransforms == null || mg.initRots == null) continue;
                    int i = mg.gimbalTransforms.IndexOf(gimbal);
                    if (i >= 0 && i < mg.initRots.Count) { restLocal = mg.initRots[i]; break; }
                }
                Quaternion restWorld = (gimbal.parent != null ? gimbal.parent.rotation : Quaternion.identity) * restLocal;
                Vector3 pinW = rod.position;
                var a = new Act
                {
                    barrel = barrel, rod = rod, gimbal = gimbal, mount = mount,
                    pinG = Quaternion.Inverse(restWorld) * (pinW - gimbal.position),
                    dir0 = mount.InverseTransformDirection(pinW - barrel.position).normalized,
                    barrel0 = barrel.localRotation, rod0 = rod.localRotation
                };
                if (a.dir0.sqrMagnitude < 0.5f) continue;
                list.Add(a);
            }
            return list;
        }

        static void Drive(Act a)
        {
            if (a.barrel == null || a.rod == null || a.gimbal == null || a.mount == null) return;
            Vector3 pin = a.gimbal.position + a.gimbal.rotation * a.pinG;
            Vector3 d = pin - a.barrel.position;
            if (d.sqrMagnitude < 1e-6f || float.IsNaN(d.x)) return;
            Quaternion delta = Quaternion.FromToRotation(a.mount.TransformDirection(a.dir0), d.normalized);
            Quaternion m = a.mount.rotation;
            a.barrel.rotation = delta * (m * a.barrel0);
            a.rod.rotation = delta * (m * a.rod0);
            a.rod.position = pin;
        }
    }
}
