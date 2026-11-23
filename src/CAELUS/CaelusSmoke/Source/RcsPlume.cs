using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CaelusSmoke
{
    public class RcsPlumeSystem
    {
        readonly List<Part> goneParts = new List<Part>(), triesTmp = new List<Part>();
        const int Layers = RcsPlumeModel.Layers;

        class Jet { public ModuleRCS rcs; public int index; public readonly Transform[] cone = new Transform[Layers]; public readonly Renderer[] rend = new Renderer[Layers]; public float level, flash; }
        class OnPart { public Part part; public RcsPlumeBinding b; public readonly Material[] mats = new Material[Layers]; public readonly Mesh[] meshes = new Mesh[Layers]; public readonly RcsLayer[] lay = new RcsLayer[Layers]; public readonly List<Jet> jets = new List<Jet>(); }

        readonly Shader shader; readonly Texture3D noise;
        readonly List<RcsPlumeBinding> bindings = new List<RcsPlumeBinding>();
        readonly Dictionary<Part, OnPart> parts = new Dictionary<Part, OnPart>();
        readonly HashSet<Part> seen = new HashSet<Part>();
        readonly Dictionary<Part, int> tries = new Dictionary<Part, int>();
        float scanTimer, logTimer;

        static string Norm(string n) { return (n ?? "").Replace('_', '.'); }

        public RcsPlumeSystem(Shader shader, Texture3D noise)
        {
            this.shader = shader; this.noise = noise;
            foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("CAELUSFLAME_RCS"))
            {
                var b = new RcsPlumeBinding { part = Norm(n.GetValue("part")) };
                n.TryGetValue("length", ref b.length); n.TryGetValue("radius", ref b.radius); n.TryGetValue("angle", ref b.angle);
                n.TryGetValue("brightness", ref b.brightness); n.TryGetValue("falloff", ref b.falloff); n.TryGetValue("edgeSoft", ref b.edgeSoft);
                n.TryGetValue("tipFade", ref b.tipFade); n.TryGetValue("noise", ref b.noise); n.TryGetValue("flow", ref b.flow);
                n.TryGetValue("responseUp", ref b.responseUp); n.TryGetValue("responseDown", ref b.responseDown);
                n.TryGetValue("startTint", ref b.startTint); n.TryGetValue("endTint", ref b.endTint);
                n.TryGetValue("core", ref b.core); n.TryGetValue("coreLengthK", ref b.coreLengthK); n.TryGetValue("coreAngle", ref b.coreAngle);
                n.TryGetValue("coreBrightness", ref b.coreBrightness); n.TryGetValue("coreFalloff", ref b.coreFalloff); n.TryGetValue("coreTint", ref b.coreTint);
                n.TryGetValue("halo", ref b.halo); n.TryGetValue("haloLengthK", ref b.haloLengthK); n.TryGetValue("haloAngle", ref b.haloAngle);
                n.TryGetValue("haloBrightness", ref b.haloBrightness); n.TryGetValue("haloFalloff", ref b.haloFalloff); n.TryGetValue("haloEdgeSoft", ref b.haloEdgeSoft);
                n.TryGetValue("flash", ref b.flash); n.TryGetValue("softDepth", ref b.softDepth);
                if (b.part.Length > 0) bindings.Add(b);
            }
            Debug.Log("[CaelusFlame] RCS plumes: " + bindings.Count + " bindings");
        }

        public void Update(float dt)
        {
            if (shader == null || bindings.Count == 0) return;
            if ((scanTimer -= dt) < 0f)
            {
                scanTimer = 0.5f;
                foreach (Vessel v in FlightGlobals.VesselsLoaded)
                {
                    if (v == null) continue;
                    foreach (Part p in v.parts)
                    {
                        if (p == null || p.partInfo == null || seen.Contains(p)) continue;
                        string pn = Norm(p.partInfo.name);
                        bool done = true;
                        foreach (RcsPlumeBinding b in bindings) if (b.part == pn && !Attach(p, b)) done = false;

                        int t; tries.TryGetValue(p, out t);
                        if (!done && t < 20) { tries[p] = t + 1; continue; }
                        if (!done)
                        {
                            string why = "";
                            foreach (PartModule pm in p.Modules)
                            {
                                var rcs = pm as ModuleRCS; if (rcs == null) continue;
                                why += pm.moduleName + " enabled " + pm.isEnabled + " transform '" + rcs.thrusterTransformName + "' list " + (rcs.thrusterTransforms == null ? "null" : rcs.thrusterTransforms.Count.ToString())
                                    + ", in model " + p.FindModelTransforms(rcs.thrusterTransformName).Length + "; ";
                            }
                            Debug.Log("[CaelusFlame] RCS plumes: gave up on " + p.partInfo.name + " - " + (why.Length > 0 ? why : "no ModuleRCS"));
                        }
                        seen.Add(p); tries.Remove(p);
                    }
                }
                seen.RemoveWhere(p => p == null);
                triesTmp.Clear(); triesTmp.AddRange(tries.Keys); foreach (Part p in triesTmp) if (p == null) tries.Remove(p);
            }
            var gone = goneParts; gone.Clear();
            bool log = FlameSettings.debugLog && (logTimer -= dt) < 0f;
            foreach (KeyValuePair<Part, OnPart> kv in parts)
            {
                if (kv.Key == null) { Free(kv.Value); gone.Add(kv.Key); continue; }
                OnPart op = kv.Value;
                foreach (Jet j in op.jets)
                {
                    float f = 0f;
                    if (j.rcs != null && j.rcs.rcs_active && j.rcs.thrustForces != null && j.index < j.rcs.thrustForces.Length && j.rcs.thrusterPower > 0f)
                        f = Mathf.Clamp01(j.rcs.thrustForces[j.index] / j.rcs.thrusterPower);
                    if (f > 0.05f && j.level < 0.05f) j.flash = 1f;
                    j.flash = Mathf.MoveTowards(j.flash, 0f, 8f * dt);
                    j.level = Mathf.MoveTowards(j.level, f, (f > j.level ? op.b.responseUp : op.b.responseDown) * dt);
                    bool show = j.level > 0.02f && CaelusUser.rcs;
                    for (int k = 0; k < Layers; k++)
                    {
                        if (j.rend[k] == null) continue;

                        GameObject cg = j.rend[k].gameObject;
                        if (cg.activeSelf != show) cg.SetActive(show);
                        float L = op.b.length * op.lay[k].lenK;
                        if (show) j.cone[k].localScale = new Vector3(L, L, L * RcsPlumeModel.Grow(k, j.level));
                    }
                }

                float lv = 0f, fl = 0f; foreach (Jet j in op.jets) { lv = Mathf.Max(lv, j.level); fl = Mathf.Max(fl, j.flash); }
                for (int k = 0; k < Layers; k++)
                {
                    if (op.mats[k] == null) continue;
                    float br = RcsPlumeModel.Brightness(op.b, op.lay[k], k, lv, fl);
                    op.mats[k].SetFloat("_Brightness", br);
                    foreach (Jet j in op.jets) { Material m = j.rend[k] != null ? j.rend[k].sharedMaterial : null; if (m != null && m != op.mats[k]) m.SetFloat("_Brightness", br); }
                }
                if (log)
                {
                    int pushing = 0, lit = 0; float fmax = 0f; bool active = false; Jet first = null;
                    foreach (Jet j in op.jets)
                    {
                        if (j.rcs != null) active |= j.rcs.rcs_active;
                        float f = j.rcs != null && j.rcs.thrustForces != null && j.index < j.rcs.thrustForces.Length ? j.rcs.thrustForces[j.index] : 0f;
                        if (f > 0f) pushing++; fmax = Mathf.Max(fmax, f);
                        if (j.rend[0] != null && j.rend[0].gameObject.activeSelf) { lit++; if (first == null) first = j; }
                    }
                    if (pushing > 0 || lit > 0)
                    {
                        Material dm = first != null ? first.rend[0].sharedMaterial : null;
                        Debug.Log("[CaelusFlame] RCS firing " + op.part.partInfo.name + ": rcs_active " + active + ", jets pushing " + pushing + "/" + op.jets.Count + " (max force " + fmax.ToString("F2") + " of " + (op.jets[0].rcs != null ? op.jets[0].rcs.thrusterPower.ToString("F2") : "?") + "), lit " + lit
                            + ", level " + lv.ToString("F2") + ", flash " + fl.ToString("F2") + (dm != null ? ", drawn " + dm.name + " _Brightness " + dm.GetFloat("_Brightness").ToString("F2") + ", cone length " + first.cone[0].lossyScale.z.ToString("F2") + " m, layer " + first.cone[0].gameObject.layer : ""));
                    }
                }
            }
            foreach (Part p in gone) parts.Remove(p);
            if (log) logTimer = 1f;
        }

        bool Attach(Part p, RcsPlumeBinding b)
        {
            var op = new OnPart { part = p, b = b };
            Mesh[] meshes = op.meshes;
            for (int k = 0; k < Layers; k++)
            {
                op.lay[k] = RcsPlumeModel.Layer(b, k);
                if (!op.lay[k].on) continue;
                op.mats[k] = new Material(shader); RcsPlumeModel.SetMaterial(op.mats[k], op.lay[k], noise, b.softDepth);
                meshes[k] = RcsPlumeModel.Cone(op.lay[k].r0, op.lay[k].angle);
            }
            foreach (PartModule pm in p.Modules)
            {
                var rcs = pm as ModuleRCS;
                if (rcs == null || rcs.thrusterTransforms == null) continue;
                for (int i = 0; i < rcs.thrusterTransforms.Count; i++)
                {
                    Transform t = rcs.thrusterTransforms[i];
                    if (t == null) continue;
                    var jet = new Jet { rcs = rcs, index = i };
                    for (int k = 0; k < Layers; k++)
                    {
                        if (op.mats[k] == null) continue;
                        var go = new GameObject(op.lay[k].name);
                        go.transform.SetParent(t, false);
                        go.transform.localPosition = Vector3.zero;

                        go.transform.localRotation = rcs.useZaxis ? Quaternion.identity : Quaternion.FromToRotation(Vector3.forward, Vector3.up);
                        float L = b.length * op.lay[k].lenK;
                        go.transform.localScale = new Vector3(L, L, L);
                        go.layer = t.gameObject.layer;
                        var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = meshes[k];
                        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = op.mats[k]; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
                        go.SetActive(false);
                        jet.cone[k] = go.transform; jet.rend[k] = mr;
                    }
                    op.jets.Add(jet);
                }
            }
            if (op.jets.Count == 0) { Free(op); return false; }
            parts[p] = op;
            if (FlameSettings.debugLog) Debug.Log("[CaelusFlame] RCS plumes: " + op.jets.Count + " jets on " + p.partInfo.name + " (core " + b.core + ", halo " + b.halo + ")");
            return true;
        }

        static void Free(OnPart op)
        {
            foreach (Material m in op.mats) if (m != null) UnityEngine.Object.Destroy(m);
            foreach (Mesh m in op.meshes) if (m != null) UnityEngine.Object.Destroy(m);
        }

        public void Destroy()
        {
            foreach (OnPart op in parts.Values) Free(op);
            parts.Clear(); seen.Clear(); tries.Clear();
        }
    }
}
