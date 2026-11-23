using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CaelusSmoke
{
    public class FoilRule
    {
        public readonly List<string> textures = new List<string>(), renderers = new List<string>();
        public Color tint = new Color(1f, 0.78f, 0.34f);
        public float smoothness = 0.85f, diffuse = 0.06f, bump = 1f;
        public bool glass;
        public float opacity = 0.8f, reflectance = 0.08f;
        public bool Matches(Renderer r, Material m)
        {
            foreach (string s in renderers) if (glass ? r.name == s : r.name.Contains(s)) return true;
            if (m != null && m.HasProperty("_MainTex") && m.mainTexture != null)
                foreach (string s in textures) if (m.mainTexture.name.Contains(s)) return true;
            return false;
        }
    }

    public class ReflectionSystem
    {
        readonly Shader foilShader, glassShader;
        readonly List<FoilRule> rules = new List<FoilRule>();
        readonly HashSet<Part> seen = new HashSet<Part>();
        bool enabled = false; int size = 256, framesPerFace = 1;
        float scaledFar = 200000f;
        float nightIntensity = 0.12f;
        float localFar = 20000f;

        RenderTexture cube; ReflectionProbe probe; Camera scaledCam, localCam; GameObject root;
        int face, frame; float scanTimer; bool logged;
        readonly List<Material> foils = new List<Material>();

        public ReflectionSystem(Shader foilShader, Shader glassShader)
        {
            this.foilShader = foilShader; this.glassShader = glassShader;
            foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("CAELUS_REFLECTIONS"))
            { n.TryGetValue("enabled", ref enabled); n.TryGetValue("size", ref size); n.TryGetValue("framesPerFace", ref framesPerFace); n.TryGetValue("scaledFar", ref scaledFar); n.TryGetValue("localFar", ref localFar); n.TryGetValue("nightIntensity", ref nightIntensity); }
            foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("CAELUS_FOIL"))
            {
                var r = new FoilRule();
                foreach (string s in n.GetValues("texture")) r.textures.Add(s.Trim());
                foreach (string s in n.GetValues("renderer")) r.renderers.Add(s.Trim());
                n.TryGetValue("tint", ref r.tint); n.TryGetValue("smoothness", ref r.smoothness); n.TryGetValue("diffuse", ref r.diffuse); n.TryGetValue("bump", ref r.bump);
                r.glass = n.GetValue("shader") == "glass"; n.TryGetValue("opacity", ref r.opacity); n.TryGetValue("reflectance", ref r.reflectance);
                if (r.glass && glassShader == null) { Debug.LogWarning("[CaelusSmoke] reflections: CaelusSmoke/Glass is missing from the bundle - window glass rules skipped"); continue; }
                rules.Add(r);
            }
            Debug.Log("[CaelusSmoke] reflections: enabled " + enabled + ", cube " + size + ", scaled far " + scaledFar + ", " + rules.Count + " foil rules, foil shader " + (foilShader != null ? foilShader.name : "MISSING"));
        }

        void Setup()
        {
            root = new GameObject("CaelusReflections");
            cube = new RenderTexture(size, size, 16, RenderTextureFormat.ARGBHalf) { dimension = TextureDimension.Cube, useMipMap = true, autoGenerateMips = true, name = "CaelusEnvCube" };
            cube.Create();
            probe = root.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Custom; probe.customBakedTexture = cube;
            probe.size = new Vector3(4000f, 4000f, 4000f); probe.importance = 100; probe.intensity = 1f; probe.boxProjection = false;
            var g1 = new GameObject("CaelusReflScaled"); g1.transform.SetParent(root.transform, false); scaledCam = g1.AddComponent<Camera>(); scaledCam.enabled = false;
            var g2 = new GameObject("CaelusReflLocal"); g2.transform.SetParent(root.transform, false); localCam = g2.AddComponent<Camera>(); localCam.enabled = false;
            scaledCam.clearFlags = CameraClearFlags.SolidColor; scaledCam.backgroundColor = Color.black;
            localCam.clearFlags = CameraClearFlags.Depth; localCam.nearClipPlane = 1f; localCam.farClipPlane = localFar;
            localCam.cullingMask = (1 << 15) | (1 << 4);
        }

        public void Update(float dt)
        {
            if (!enabled) return;
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return;
            bool ours = CaelusParts.Vessel(v);
            if (probe != null && probe.enabled != ours) probe.enabled = ours;
            if (!ours) return;
            if (root == null) Setup();
            Vector3 c = (Vector3)v.CoMD;
            root.transform.position = c;

            Camera ksc = null;
            foreach (Camera k in Camera.allCameras) if (k.name == "Camera ScaledSpace") { ksc = k; break; }
            if (ksc != null)
            {

                scaledCam.cullingMask = ksc.cullingMask; scaledCam.nearClipPlane = ksc.nearClipPlane; scaledCam.farClipPlane = scaledFar > 0f ? Mathf.Min(ksc.farClipPlane, scaledFar) : ksc.farClipPlane;
                scaledCam.transform.position = (Vector3)ScaledSpace.LocalToScaledSpace(v.CoMD);
            }
            localCam.transform.position = c;
            if (++frame >= framesPerFace)
            {
                frame = 0;
                int mask = 1 << face;

                bool shade = !v.directSunlight;
                if (shade) SunVisible(false);

                var shadowsWas = QualitySettings.shadows;
                QualitySettings.shadows = ShadowQuality.Disable;
                try
                {
                    if (ksc != null) scaledCam.RenderToCubemap(cube, mask);
                    localCam.RenderToCubemap(cube, mask);
                }
                finally { QualitySettings.shadows = shadowsWas; }
                if (shade) SunVisible(true);
                face = (face + 1) % 6;
                if (!logged && face == 0) { logged = true; Debug.Log("[CaelusSmoke] reflections: first cube done at " + v.vesselName + ", scaled camera " + (ksc != null)); }
            }

            float target = v.directSunlight ? 1f : nightIntensity;
            probe.intensity = Mathf.MoveTowards(probe.intensity, target, dt * 0.5f);
            if ((scanTimer -= dt) < 0f) { scanTimer = 0.5f; Scan(); }
            KeepProbes();
        }

        static readonly Vector3 CabinOffset = new Vector3(0f, 20000f, 0f);
        readonly List<ReflectionProbe> cabinProbes = new List<ReflectionProbe>();
        void KeepProbes()
        {
            if (probe != null && probe.size.x != 4000f) probe.size = new Vector3(4000f, 4000f, 4000f);
            cabinProbes.RemoveAll(x => x == null);
            foreach (var c in cabinProbes)
            {
                if (c.size.x != 12f) c.size = new Vector3(12f, 12f, 12f);

                Part cp; float k = 1f;
                if (cabinPart.TryGetValue(c, out cp) && cp != null && cp.vessel != null) { try { k = Mathf.Lerp(cabinNight, 1f, IvaGlow.Sunlight(cp.vessel, cp.transform.position)); } catch { } }
                c.intensity = Mathf.MoveTowards(c.intensity, k, Time.deltaTime * 0.5f);
            }
        }
        readonly Dictionary<ReflectionProbe, Part> cabinPart = new Dictionary<ReflectionProbe, Part>();
        public float cabinNight = 0.08f;

        readonly List<Renderer> sunRenderers = new List<Renderer>(); readonly List<Renderer> sunHidden = new List<Renderer>(); float sunScan = -1f;
        void SunVisible(bool on)
        {
            if (!on)
            {
                if (Time.realtimeSinceStartup > sunScan)
                {
                    sunScan = Time.realtimeSinceStartup + 10f; sunRenderers.Clear();
                    var sun = Planetarium.fetch != null ? Planetarium.fetch.Sun : null;
                    if (sun != null && sun.scaledBody != null) sunRenderers.AddRange(sun.scaledBody.GetComponentsInChildren<Renderer>());
                    foreach (var r in Object.FindObjectsOfType<Renderer>())
                        if (r != null && r.sharedMaterial != null && r.sharedMaterial.shader != null && r.sharedMaterial.shader.name.IndexOf("sunflare", System.StringComparison.OrdinalIgnoreCase) >= 0) sunRenderers.Add(r);
                }
                sunHidden.Clear();
                foreach (var r in sunRenderers) if (r != null && r.enabled) { r.enabled = false; sunHidden.Add(r); }
            }
            else { foreach (var r in sunHidden) if (r != null) r.enabled = true; sunHidden.Clear(); }
        }

        static Cubemap cabinCube;

        static Cubemap CabinCube()
        {
            if (cabinCube != null) return cabinCube;
            const int N = 32; cabinCube = new Cubemap(N, TextureFormat.RGBA32, true) { name = "CaelusCabinEnv" };
            var faces = new[] { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ };
            var px = new Color[N * N];
            foreach (var f in faces)
            {
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float u = (x + 0.5f) / N * 2f - 1f, w = (y + 0.5f) / N * 2f - 1f; Vector3 d;
                        switch (f)
                        {
                            case CubemapFace.PositiveX: d = new Vector3(1f, -w, -u); break;
                            case CubemapFace.NegativeX: d = new Vector3(-1f, -w, u); break;
                            case CubemapFace.PositiveY: d = new Vector3(u, 1f, w); break;
                            case CubemapFace.NegativeY: d = new Vector3(u, -1f, -w); break;
                            case CubemapFace.PositiveZ: d = new Vector3(u, -w, 1f); break;
                            default: d = new Vector3(-u, -w, -1f); break;
                        }
                        float up = d.normalized.y;
                        Color wall = new Color(0.62f, 0.60f, 0.55f), ceil = new Color(0.95f, 0.92f, 0.84f), floor = new Color(0.26f, 0.25f, 0.23f);
                        Color c = up > 0 ? Color.Lerp(wall, ceil, Mathf.SmoothStep(0f, 1f, up)) : Color.Lerp(wall, floor, Mathf.SmoothStep(0f, 1f, -up));
                        c *= 1f + 0.08f * Mathf.Sin(up * 9f);
                        c.a = 1f; px[y * N + x] = c;
                    }
                cabinCube.SetPixels(px, f);
            }
            cabinCube.Apply(true, false);
            return cabinCube;
        }

        readonly HashSet<InternalModel> ivaDone = new HashSet<InternalModel>();
        readonly HashSet<Light> ivaLights = new HashSet<Light>();
        void Scan()
        {

            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                if (v == null) continue;
                foreach (Part p in v.parts)
                {
                    if (!CaelusParts.Is(p)) continue;
                    InternalModel im = p != null ? p.internalModel : null;
                    if (im == null || ivaDone.Contains(im)) continue;
                    ivaDone.Add(im); int k = 0;

                    var go = new GameObject("CaelusCabinProbe"); go.transform.SetParent(im.transform, false);

                    go.transform.localPosition = CabinOffset;
                    var pr = go.AddComponent<ReflectionProbe>(); pr.mode = ReflectionProbeMode.Custom; pr.customBakedTexture = CabinCube();
                    pr.size = new Vector3(12f, 12f, 12f); pr.importance = 100; pr.intensity = 1f; pr.boxProjection = false;
                    cabinProbes.Add(pr); cabinPart[pr] = p;
                    foreach (Renderer r in im.GetComponentsInChildren<Renderer>(true)) { r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes; r.probeAnchor = go.transform; k++; }
                    Debug.Log("[CaelusSmoke] reflections: cabin " + im.internalName + " of " + p.partInfo.name + " - cabin environment probe on " + k + " renderers (no outdoor cube)");
                }
            }
            ivaDone.RemoveWhere(m => m == null);

            foreach (InternalModel cim in ivaDone) if (cim != null) foreach (Light l in cim.GetComponentsInChildren<Light>(true))
            {
                if (l == null || l.shadows == LightShadows.None || (l.cullingMask & (1 << 16)) == 0 || (l.cullingMask & 1) != 0 || ivaLights.Contains(l)) continue;
                ivaLights.Add(l);
                Debug.Log("[CaelusSmoke] cabin light " + l.name + " (" + l.type + "): shadow bias " + l.shadowBias + " -> " + Mathf.Max(l.shadowBias, 0.12f) + ", normal bias " + l.shadowNormalBias + " -> " + Mathf.Max(l.shadowNormalBias, 0.7f));
                l.shadowBias = Mathf.Max(l.shadowBias, 0.12f); l.shadowNormalBias = Mathf.Max(l.shadowNormalBias, 0.7f);
            }
            if (foilShader == null || rules.Count == 0) return;
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                if (v == null) continue;
                foreach (Part p in v.parts) ApplyFoil(p);
            }
            seen.RemoveWhere(p => p == null);
        }

        public void ApplyFoil(Part p)
        {
                    if (foilShader == null || rules.Count == 0) return;
                    if (p == null || seen.Contains(p)) return;
                    seen.Add(p);
                    int n = 0, glassN = 0;
                    foreach (Renderer r in p.GetComponentsInChildren<Renderer>(true))
                    {
                        Material[] ms = r.sharedMaterials; bool changed = false;
                        for (int i = 0; i < ms.Length; i++)
                        {
                            Material m = ms[i];
                            if (m == null || m.shader == foilShader || (glassShader != null && m.shader == glassShader)) continue;
                            foreach (FoilRule rule in rules)
                            {
                                if (!rule.Matches(r, m)) continue;
                                if (rule.glass)
                                {
                                    var g = new Material(glassShader) { name = "CaelusGlass_" + r.name, renderQueue = 3000 };
                                    if (m.HasProperty("_BumpMap") && m.GetTexture("_BumpMap") != null) g.SetTexture("_BumpMap", m.GetTexture("_BumpMap"));
                                    g.SetColor("_Color", new Color(rule.tint.r, rule.tint.g, rule.tint.b, rule.opacity));
                                    g.SetColor("_SpecColor", new Color(rule.reflectance, rule.reflectance, rule.reflectance, 1f));
                                    g.SetFloat("_Glossiness", rule.smoothness); g.SetFloat("_BumpScale", rule.bump);
                                    ms[i] = g; foils.Add(g); changed = true; glassN++;
                                    break;
                                }
                                var f = new Material(foilShader) { name = "CaelusFoil_" + (m.mainTexture != null ? m.mainTexture.name : r.name) };
                                if (m.HasProperty("_MainTex")) { f.SetTexture("_MainTex", m.GetTexture("_MainTex")); f.SetTextureScale("_MainTex", m.GetTextureScale("_MainTex")); f.SetTextureOffset("_MainTex", m.GetTextureOffset("_MainTex")); }
                                if (m.HasProperty("_BumpMap") && m.GetTexture("_BumpMap") != null) f.SetTexture("_BumpMap", m.GetTexture("_BumpMap"));
                                Color t = rule.tint;
                                if (m.HasProperty("_Color")) { Color mc = m.GetColor("_Color"); if (mc.r + mc.g + mc.b < 2.9f) t = new Color(t.r * mc.r, t.g * mc.g, t.b * mc.b); }
                                f.SetColor("_Color", t); f.SetFloat("_Glossiness", rule.smoothness); f.SetFloat("_Diffuse", rule.diffuse); f.SetFloat("_BumpScale", rule.bump);
                                ms[i] = f; foils.Add(f); changed = true; n++;
                                break;
                            }
                        }
                        if (changed) { r.sharedMaterials = ms; r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox; }
                    }
                    if (n > 0 || glassN > 0) Debug.Log("[CaelusSmoke] reflections: " + n + " gold foil and " + glassN + " glass materials on " + p.partInfo.name);
        }
        public void ForgetDead() { seen.RemoveWhere(p => p == null); }

        public void Destroy()
        {
            if (cube != null) { cube.Release(); Object.Destroy(cube); }
            if (root != null) Object.Destroy(root);
            foreach (Material m in foils) if (m != null) Object.Destroy(m);
            foils.Clear(); seen.Clear();
        }
    }
}
