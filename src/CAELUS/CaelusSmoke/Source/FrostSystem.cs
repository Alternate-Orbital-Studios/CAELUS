using System;
using System.Collections.Generic;
using UnityEngine;

namespace CaelusSmoke
{
    public class FrostTank
    {
        public string part, resource = "LqdOxygen";
        public float bottom, top, radius = 5f, frost = 1f, vapour = 1f; public bool seams;
        public float ventY = -1f, ventAz = 0f;
    }

    public static class FrostSettings
    {
        public static bool enabled = true, debugLog = false;
        public static float strength = 1f, sigma = 1.6f, shedTo = 0.6f, shedSeconds = 18f, brightness = 2.2f, vent = 1f;
        public static int resDivisor = 2, steps = 72;
        public static readonly List<FrostTank> tanks = new List<FrostTank>();

        public static void Load()
        {
            tanks.Clear();
            foreach (ConfigNode s in GameDatabase.Instance.GetConfigNodes("CAELUS_FROST"))
            {
                s.TryGetValue("enabled", ref enabled); s.TryGetValue("strength", ref strength); s.TryGetValue("sigma", ref sigma);
                s.TryGetValue("resDivisor", ref resDivisor); s.TryGetValue("steps", ref steps); s.TryGetValue("shedTo", ref shedTo);
                s.TryGetValue("shedSeconds", ref shedSeconds); s.TryGetValue("brightness", ref brightness); s.TryGetValue("debugLog", ref debugLog); s.TryGetValue("vent", ref vent);
            }
            foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("CAELUS_FROST_TANK"))
            {
                var t = new FrostTank();
                t.part = n.GetValue("part"); if (string.IsNullOrEmpty(t.part)) continue;
                n.TryGetValue("resource", ref t.resource); n.TryGetValue("bottom", ref t.bottom); n.TryGetValue("top", ref t.top);
                n.TryGetValue("radius", ref t.radius); n.TryGetValue("frost", ref t.frost); n.TryGetValue("vapour", ref t.vapour); n.TryGetValue("seams", ref t.seams); n.TryGetValue("ventY", ref t.ventY); n.TryGetValue("ventAz", ref t.ventAz);
                tanks.Add(t);
            }
            Debug.Log($"[CaelusFrost] {tanks.Count} frosted tanks configured, enabled {enabled}");
        }
    }

    public class FrostSystem
    {
        public readonly Material vapourMat;
        readonly Shader frostShader; readonly Texture3D noise;
        public bool vapourActive;

        class Shell { public Part part; public FrostTank cfg; public GameObject go; public Material mat; public PartResource res; }
        readonly List<Shell> shells = new List<Shell>();
        readonly Vector4[] bands = new Vector4[6];
        Vessel shellVessel; int shellKey = -1;
        double liftoffUT = -1; float logT;
        float plume;
        double ventShutUT = -1;
        double flowOff, ventOff, advUT = -1;

        readonly Vector4[] lPos = new Vector4[4], lCol = new Vector4[4]; int lCount; float lNext;

        public FrostSystem(Shader frost, Shader vapour, Texture3D noise)
        {
            frostShader = frost; this.noise = noise;
            vapourMat = new Material(vapour) { name = "CaelusFrostVapour" };
            vapourMat.SetTexture("_Noise", noise);
        }

        public void Destroy()
        {
            foreach (var s in shells) { if (s.go != null) UnityEngine.Object.Destroy(s.go); if (s.mat != null) UnityEngine.Object.Destroy(s.mat); }
            shells.Clear();
            if (vapourMat != null) UnityEngine.Object.Destroy(vapourMat);
        }

        public bool Update(Vessel v, double ut, VaporSystem hull, bool frostOn)
        {
            vapourActive = false;
            if (!FrostSettings.enabled || FrostSettings.tanks.Count == 0 || v == null || v.packed || v.rootPart == null) { SetShellsVisible(false); return false; }
            if (v != shellVessel || v.parts.Count != shellKey) BuildShells(v);
            SetShellsVisible(frostOn);
            if (!frostOn || shells.Count == 0) return false;

            bool onPad = v.situation == Vessel.Situations.PRELAUNCH || v.situation == Vessel.Situations.LANDED || v.situation == Vessel.Situations.SPLASHED;
            if (onPad) liftoffUT = -1; else if (liftoffUT < 0) liftoffUT = ut;
            float since = liftoffUT < 0 ? 0f : (float)(ut - liftoffUT);

            float shed = Mathf.Lerp(1f, FrostSettings.shedTo, Mathf.SmoothStep(0f, 1f, since / Mathf.Max(FrostSettings.shedSeconds, 0.1f)));
            int nb = 0;
            Transform root = v.rootPart.transform;

            float fogChance = FrostSettings.strength * VaporModel.Humidity((float)v.altitude, VaporSettings.humidity) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(150f, 260f, (float)v.srfSpeed)));
            bool haveHull = fogChance >= 0.01f && hull != null && hull.EnsureProfile(v);
            Vector3 nose = haveHull ? hull.NoseWorld(v) : Vector3.zero, axis = -root.up;
            foreach (var s in shells)
            {
                if (s.part == null || s.go == null) continue;
                float level = s.res != null && s.res.maxAmount > 0 ? (float)(s.res.amount / s.res.maxAmount) : 0f;
                s.mat.SetFloat("_FLevel", Mathf.Clamp01(level));
                s.mat.SetFloat("_FAmount", s.cfg.frost * shed * FrostSettings.strength * (level > 0.01f ? 1f : 0.7f));
                if (!haveHull || s.cfg.vapour <= 0f || level < 0.02f || nb >= bands.Length) continue;

                Vector3 pTop = s.part.transform.TransformPoint(0f, s.cfg.bottom + (s.cfg.top - s.cfg.bottom) * Mathf.Clamp01(level), 0f);
                Vector3 pBot = s.part.transform.TransformPoint(0f, s.cfg.bottom, 0f);
                float xTop = Vector3.Dot(pTop - nose, axis), xBot = Vector3.Dot(pBot - nose, axis);

                float strength = s.cfg.vapour * (s.cfg.seams ? 0.15f : 1f), xVent = 0f, zPack = strength;
                if (s.cfg.ventY >= 0f && !s.cfg.seams)
                {
                    xVent = Mathf.Max(0.01f, Vector3.Dot(s.part.transform.TransformPoint(0f, s.cfg.ventY, 0f) - nose, axis));
                    zPack = strength + 10f * Mathf.Round(((s.cfg.ventAz % 360f) + 360f) % 360f);
                }
                bands[nb++] = new Vector4(Mathf.Min(xTop, xBot), Mathf.Max(xTop, xBot), zPack, xVent);
            }
            if (nb == 0 || !v.mainBody.atmosphere) return false;
            float speed = (float)v.srfSpeed, alt = (float)v.altitude;

            float thr = 0f;
            foreach (Part p in v.parts)
                foreach (PartModule pm in p.Modules) { var en = pm as ModuleEngines; if (en != null && en.EngineIgnited && !en.flameout) thr += en.finalThrust; }
            plume = Mathf.MoveTowards(plume, Mathf.Clamp01(thr / 20000f), Time.deltaTime * 1.5f);

            float flow = Mathf.Max(2.5f + 7f * plume, speed);
            float I = FrostSettings.strength * VaporModel.Humidity(alt, VaporSettings.humidity) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(150f, 260f, speed)));
            if (!v.directSunlight && speed > 20f) I *= 0.25f;
            if (I < 0.01f) return false;

            Vector3 east = Vector3.Cross(v.mainBody.transform.up, (v.CoMD - v.mainBody.position).normalized).normalized * 1.2f;
            for (int i = nb; i < bands.Length; i++) bands[i] = Vector4.zero;
            vapourMat.SetVector("_VNose", nose); vapourMat.SetVector("_VAxis", axis);
            vapourMat.SetVector("_VB1", root.right); vapourMat.SetVector("_VB2", root.forward);
            hull.ApplyProfile(vapourMat);
            vapourMat.SetInt("_FCount", nb); vapourMat.SetVectorArray("_FB", bands);
            vapourMat.SetFloat("_FFlow", flow); vapourMat.SetFloat("_FPlume", plume);
            double adt = advUT < 0 ? 0 : System.Math.Max(0.0, System.Math.Min(1.0, ut - advUT)); advUT = ut;
            flowOff = (flowOff + flow * adt) % 20000.0; ventOff = (ventOff + System.Math.Max(flow, 3.0) * adt) % 20000.0;
            vapourMat.SetFloat("_FFlowOff", (float)flowOff); vapourMat.SetFloat("_FVentOff", (float)ventOff);
            if (plume > 0.05f) { if (ventShutUT < 0) ventShutUT = ut; } else if (onPad) ventShutUT = -1;
            vapourMat.SetFloat("_FVent", FrostSettings.vent); vapourMat.SetFloat("_FVentT", ventShutUT < 0 ? -1f : (float)(ut - ventShutUT)); vapourMat.SetVector("_FWind", new Vector4(Vector3.Dot(east, root.right), Vector3.Dot(east, root.forward), 0, 0));
            if ((lNext -= Time.unscaledDeltaTime) < 0f) { lNext = 0.5f; FindLights(v); }
            vapourMat.SetVectorArray("_FLightPos", lPos); vapourMat.SetVectorArray("_FLightCol", lCol); vapourMat.SetFloat("_FLightCount", lCount);
            vapourMat.SetFloat("_FI", I); vapourMat.SetFloat("_VTime", (float)(ut % 1000.0));
            vapourMat.SetFloat("_VSigma", FrostSettings.sigma); vapourMat.SetFloat("_FBright", FrostSettings.brightness); vapourMat.SetInt("_Steps", FrostSettings.steps);
            if (FrostSettings.debugLog && (logT -= Time.unscaledDeltaTime) < 0f)
            {
                logT = 2f;
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < nb; i++) sb.Append($" [x {bands[i].x:F1}-{bands[i].y:F1} s {bands[i].z:F2}]");
                Debug.Log($"[CaelusFrost] speed {speed:F0} alt {alt:F0} flow {flow:F1} plume {plume:F2} intensity {I:F2} shed {shed:F2} bands {nb}:{sb}");
            }
            vapourActive = true;
            return true;
        }

        void FindLights(Vessel v)
        {
            lCount = 0; Vector3 c = v.CoMD;
            var best = new List<KeyValuePair<float, Light>>();
            foreach (var l in UnityEngine.Object.FindObjectsOfType<Light>())
            {
                if (l == null || !l.enabled || !l.gameObject.activeInHierarchy || l.intensity <= 0.01f || l.range < 5f) continue;
                if (l.type != LightType.Point && l.type != LightType.Spot) continue;
                float d = Vector3.Distance(l.transform.position, c); if (d > 200f) continue;
                best.Add(new KeyValuePair<float, Light>(d, l));
            }
            best.Sort((a, b) => a.Key.CompareTo(b.Key));
            for (int i = 0; i < best.Count && lCount < 4; i++)
            {
                var l = best[i].Value; Color col = l.color * l.intensity;
                lPos[lCount] = new Vector4(l.transform.position.x, l.transform.position.y, l.transform.position.z, l.range);
                lCol[lCount] = new Vector4(col.r, col.g, col.b, 0f); lCount++;
            }
            for (int i = lCount; i < 4; i++) { lPos[i] = Vector4.zero; lCol[i] = Vector4.zero; }
        }

        public void SetLight(Vector3 sunDir, Vector3 sunColor, Vector3 sky)
        {
            vapourMat.SetVector("_SunDir", sunDir); vapourMat.SetVector("_SunColor", sunColor); vapourMat.SetVector("_AmbientSky", sky);
        }

        void SetShellsVisible(bool on) { foreach (var s in shells) if (s.go != null && s.go.activeSelf != on) s.go.SetActive(on); }

        void BuildShells(Vessel v)
        {
            foreach (var s in shells) { if (s.go != null) UnityEngine.Object.Destroy(s.go); if (s.mat != null) UnityEngine.Object.Destroy(s.mat); }
            shells.Clear(); shellVessel = v; shellKey = v.parts.Count;
            foreach (Part p in v.parts)
            {
                if (p == null || p.partInfo == null) continue;
                foreach (var t in FrostSettings.tanks)
                {
                    if (t.part != p.partInfo.name) continue;
                    var go = new GameObject("CaelusFrost " + t.resource);
                    go.layer = 0;
                    go.transform.SetParent(p.transform, false);
                    go.transform.localPosition = new Vector3(0f, t.bottom, 0f); go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
                    go.AddComponent<MeshFilter>().sharedMesh = ShellMesh(t.radius + 0.03f, t.top - t.bottom);
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = true;
                    var m = new Material(frostShader) { name = "CaelusFrost " + p.partInfo.name };
                    m.SetTexture("_Noise", noise); m.SetFloat("_FRadius", t.radius); m.SetFloat("_FTop", t.top - t.bottom); m.SetFloat("_FSeams", t.seams ? 1f : 0f);
                    mr.sharedMaterial = m;
                    shells.Add(new Shell { part = p, cfg = t, go = go, mat = m, res = p.Resources.Get(t.resource) });
                }
            }
            Debug.Log($"[CaelusFrost] {shells.Count} frost shells on {v.vesselName}");
        }

        static readonly Dictionary<long, Mesh> meshCache = new Dictionary<long, Mesh>();
        static Mesh ShellMesh(float r, float h)
        {
            long key = ((long)Mathf.RoundToInt(r * 100f) << 20) ^ Mathf.RoundToInt(h * 100f);
            if (meshCache.TryGetValue(key, out Mesh cached) && cached != null) return cached;
            int seg = 160, rows = Mathf.Clamp(Mathf.CeilToInt(h / 0.25f), 8, 160);
            var vs = new List<Vector3>(); var ns = new List<Vector3>(); var ts = new List<Vector4>(); var tri = new List<int>();
            for (int i = 0; i <= rows; i++)
            {
                float y = h * (1f - (float)i / rows);
                for (int j = 0; j <= seg; j++)
                {
                    float a = 2f * Mathf.PI * j / seg, c = Mathf.Cos(a), s = Mathf.Sin(a);
                    vs.Add(new Vector3(r * c, y, r * s)); ns.Add(new Vector3(c, 0f, s)); ts.Add(new Vector4(-s, 0f, c, 1f));
                }
            }
            for (int i = 0; i < rows; i++) for (int j = 0; j < seg; j++)
            {
                int a = i * (seg + 1) + j, b = a + seg + 1;
                tri.Add(a); tri.Add(a + 1); tri.Add(b); tri.Add(a + 1); tri.Add(b + 1); tri.Add(b);
            }
            var m = new Mesh { name = "CaelusFrostShell" };
            m.SetVertices(vs); m.SetNormals(ns); m.SetTangents(ts); m.SetTriangles(tri, 0); m.RecalculateBounds();
            meshCache[key] = m;
            return m;
        }
    }
}
