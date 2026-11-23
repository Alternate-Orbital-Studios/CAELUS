using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CaelusSmoke
{
    public class BellGlowBinding
    {
        public string part = "", model = "";
        public readonly List<string> parents = new List<string>(), engineIDs = new List<string>();
        public Color startTint = new Color(1f, 0.88f, 0.66f), endTint = new Color(1f, 0.58f, 0.26f);
        public float tintFalloff = 0.8f, falloff = 1.4f, noise = 0.08f, flow = 6f, responseUp = 1.5f, responseDown = 2.5f;
        public float uniform = 0f;
        public float gain = 1f;
        public float prepass = 0f, streaks = 0f, throat = 0f;
        public float[] brightness = { 0f, 0f, 0.05f, 0.66f, 1f, 1.2f };

        public bool Matches(ModuleEngines en)
        {
            if (engineIDs.Count == 0) return true;
            var fx = en as ModuleEnginesFX;
            return fx != null && engineIDs.Contains(fx.engineID);
        }
    }

    public class BellGlowSystem
    {
        readonly List<Part> goneParts = new List<Part>();
        class Glow
        {
            public Part part; public BellGlowBinding b; public Material mat;
            public readonly List<Renderer> rends = new List<Renderer>();
            public float level; public bool shown; public bool logged; public float litTime;
        }

        readonly Shader shader; readonly Texture3D noise;
        readonly List<BellGlowBinding> bindings = new List<BellGlowBinding>();
        readonly Dictionary<Part, List<Glow>> glows = new Dictionary<Part, List<Glow>>();
        readonly HashSet<Part> seen = new HashSet<Part>();
        static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
        float scanTimer;
        bool debugGreen;

        static string Norm(string n) { return (n ?? "").Replace('_', '.'); }

        public BellGlowSystem(Shader shader, Texture3D noise)
        {
            this.shader = shader; this.noise = noise;
            foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("CAELUSFLAME_BELLGLOW"))
            {
                var b = new BellGlowBinding { part = Norm(n.GetValue("part")), model = n.GetValue("model") ?? "" };
                foreach (string s in n.GetValues("parent")) b.parents.Add(s.Trim());
                foreach (string s in n.GetValues("engineID")) b.engineIDs.Add(s.Trim());
                n.TryGetValue("startTint", ref b.startTint); n.TryGetValue("endTint", ref b.endTint); n.TryGetValue("tintFalloff", ref b.tintFalloff);
                n.TryGetValue("falloff", ref b.falloff); n.TryGetValue("noise", ref b.noise); n.TryGetValue("flow", ref b.flow); n.TryGetValue("uniform", ref b.uniform); n.TryGetValue("gain", ref b.gain); n.TryGetValue("prepass", ref b.prepass); n.TryGetValue("streaks", ref b.streaks); n.TryGetValue("throat", ref b.throat);
                n.TryGetValue("responseUp", ref b.responseUp); n.TryGetValue("responseDown", ref b.responseDown);
                ConfigNode c = n.GetNode("brightness");
                if (c != null)
                {
                    var pts = new List<Vector2>();
                    foreach (string v in c.GetValues("key"))
                    {
                        string[] p = v.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                        float x, y;
                        if (p.Length >= 2 && float.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x)
                            && float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y)) pts.Add(new Vector2(x, y));
                    }
                    if (pts.Count > 0)
                    {
                        pts.Sort((a, d) => a.x.CompareTo(d.x));
                        b.brightness = new float[pts.Count * 2];
                        for (int i = 0; i < pts.Count; i++) { b.brightness[2 * i] = pts[i].x; b.brightness[2 * i + 1] = pts[i].y; }
                    }
                }
                if (b.part.Length > 0 && b.model.Length > 0 && b.parents.Count > 0) bindings.Add(b);
            }
            foreach (ConfigNode st in GameDatabase.Instance.GetConfigNodes("CAELUSFLAME_SETTINGS")) st.TryGetValue("bellDebug", ref debugGreen);
            Debug.Log("[CaelusFlame] bell glow: " + bindings.Count + " bindings, debug " + debugGreen + ", shader " + (shader != null ? shader.name : "MISSING"));
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
                        seen.Add(p);
                        string pn = Norm(p.partInfo.name);
                        foreach (BellGlowBinding b in bindings) if (b.part == pn) Attach(p, b);
                    }
                }
                seen.RemoveWhere(p => p == null);
            }
            var gone = goneParts; gone.Clear();
            foreach (KeyValuePair<Part, List<Glow>> kv in glows)
            {
                if (kv.Key == null)
                {
                    foreach (Glow g in kv.Value) if (g.mat != null) UnityEngine.Object.Destroy(g.mat);
                    gone.Add(kv.Key); continue;
                }
                foreach (Glow g in kv.Value) Drive(g, dt);
            }
            foreach (Part p in gone) glows.Remove(p);
        }

        void Attach(Part p, BellGlowBinding b)
        {
            GameObject prefab = GameDatabase.Instance.GetModelPrefab(b.model);
            if (prefab == null) { Debug.LogWarning("[CaelusFlame] bell glow: model not found " + b.model); return; }
            var g = new Glow { part = p, b = b, mat = new Material(shader) };
            g.mat.SetTexture("_Noise", noise);
            g.mat.SetColor("_StartTint", b.startTint); g.mat.SetColor("_EndTint", b.endTint);
            g.mat.SetFloat("_TintFalloff", b.tintFalloff); g.mat.SetFloat("_Falloff", b.falloff);
            g.mat.SetFloat("_NoiseAmp", b.noise); g.mat.SetFloat("_Flow", b.flow); g.mat.SetFloat("_Uniform", b.uniform); g.mat.SetFloat(BrightnessId, 0f); g.mat.SetFloat("_Prepass", b.prepass); g.mat.SetFloat("_Streaks", b.streaks); g.mat.SetFloat("_Throat", b.throat);
            int n = 0;
            foreach (string parent in b.parents)
                foreach (Transform t in p.FindModelTransforms(parent))
                {
                    GameObject go = UnityEngine.Object.Instantiate(prefab);
                    go.name = "CaelusBellGlow_" + b.model.Substring(b.model.LastIndexOf('/') + 1);
                    go.transform.SetParent(t, false);
                    go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
                    go.SetActive(true);
                    foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                    {
                        r.sharedMaterial = g.mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                        r.enabled = false;
                        r.gameObject.layer = t.gameObject.layer;
                        g.rends.Add(r);
                    }
                    foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.Destroy(c);
                    n++;
                }
            if (n == 0) { Debug.LogWarning("[CaelusFlame] bell glow: no transform " + string.Join("/", b.parents.ToArray()) + " on " + p.partInfo.name); return; }
            List<Glow> lg;
            if (!glows.TryGetValue(p, out lg)) glows[p] = lg = new List<Glow>();
            lg.Add(g);
            if (FlameSettings.debugLog) Debug.Log("[CaelusFlame] bell glow: " + n + " x " + b.model + " on " + p.partInfo.name);
        }

        void Drive(Glow g, float dt)
        {
            float target = 0f;
            foreach (PartModule pm in g.part.Modules)
            {
                var en = pm as ModuleEngines;
                if (en == null || !g.b.Matches(en)) continue;
                if (en.EngineIgnited && !en.flameout && en.finalThrust > 0.01f) target = Mathf.Max(target, Mathf.Clamp01(en.currentThrottle));
            }
            g.level = Mathf.MoveTowards(g.level, target, (target > g.level ? g.b.responseUp : g.b.responseDown) * dt);
            float br = Eval(g.b.brightness, g.level) * g.b.gain;
            if (debugGreen && br > 0.002f) br = 20f;

            g.mat.SetFloat(BrightnessId, br);
            foreach (Renderer r in g.rends)
            {
                if (r == null) continue;
                Material m = r.sharedMaterial;
                if (m == null || m == g.mat) continue;
                m.SetFloat(BrightnessId, br);
                if (debugGreen) { m.SetColor("_StartTint", Color.green); m.SetColor("_EndTint", Color.green); m.SetFloat("_Uniform", 1f); }
            }
            bool show = br > 0.002f && CaelusUser.bellGlow;
            if (show != g.shown)
            {
                foreach (Renderer r in g.rends) if (r != null) { r.enabled = show; if (show && !r.gameObject.activeInHierarchy) r.gameObject.SetActive(true); }
                g.shown = show;
            }
            if (show) g.litTime += dt;
            if (show && !g.logged && g.litTime > 3f && FlameSettings.debugLog && g.rends.Count > 0 && g.rends[0] != null)
            {
                g.logged = true; Renderer r0 = g.rends[0]; Transform par = r0.transform.parent != null ? r0.transform.parent : r0.transform;
                Transform thr = par; while (thr.parent != null && !g.b.parents.Contains(thr.name)) thr = thr.parent;
                int vis = 0; foreach (Renderer rr in g.rends) if (rr != null && rr.isVisible) vis++;
                Debug.Log("[CaelusFlame] bell glow check: " + g.part.partInfo.name + " axial offset " + Vector3.Dot(r0.bounds.center - thr.position, thr.forward).ToString("F2") + " m along " + thr.name + ".forward, visible " + vis + "/" + g.rends.Count
                    + ", material " + r0.sharedMaterial.name + " brightness " + r0.sharedMaterial.GetFloat(BrightnessId).ToString("F2") + ", queue " + r0.sharedMaterial.renderQueue + ", mats " + r0.sharedMaterials.Length);
                Debug.Log("[CaelusFlame] bell glow lit: " + g.part.partInfo.name + " " + g.b.model.Substring(g.b.model.LastIndexOf('/') + 1) + " x" + g.rends.Count + " brightness " + br.ToString("F2")
                    + " enabled " + r0.enabled + " active " + r0.gameObject.activeInHierarchy + " visible " + r0.isVisible + " layer " + r0.gameObject.layer
                    + " bounds " + r0.bounds.size.ToString("F2") + " centre-to-parent " + (r0.bounds.center - r0.transform.parent.position).magnitude.ToString("F2") + " m, shader " + r0.sharedMaterial.shader.name);
            }
        }

        static float Eval(float[] k, float x)
        {
            if (k == null || k.Length < 2) return 0f;
            if (x <= k[0]) return k[1];
            for (int i = 2; i + 1 < k.Length; i += 2)
                if (x <= k[i]) return Mathf.Lerp(k[i - 1], k[i + 1], Mathf.InverseLerp(k[i - 2], k[i], x));
            return k[k.Length - 1];
        }

        public void Destroy()
        {
            foreach (List<Glow> lg in glows.Values) foreach (Glow g in lg) if (g.mat != null) UnityEngine.Object.Destroy(g.mat);
            glows.Clear(); seen.Clear();
        }
    }
}
