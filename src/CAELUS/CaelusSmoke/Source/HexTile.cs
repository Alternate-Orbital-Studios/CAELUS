using System;
using System.Collections.Generic;
using UnityEngine;

namespace CaelusSmoke
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class HexTileStarter : MonoBehaviour { void Start() { HexTile.ApplyToPrefabs(); } }

    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    public class HexTileScene : MonoBehaviour
    {
        float next = 2f, interval = 4f;
        void Update()
        {
            if (!HexTile.Ready || (next -= Time.unscaledDeltaTime) > 0f) return;
            HexTile.ScenePass(); next = interval; interval = Mathf.Min(interval * 1.5f, 30f);
        }
    }

    public static class HexTile
    {
        public static bool Ready { get; private set; }
        static bool enabled = true; static float minRepeats = 2f, density = 1.5f, mottle = 0.05f, sharpness = 4f;
        static readonly List<string> prefixes = new List<string>(), skips = new List<string>();

        static readonly HashSet<string> allow = new HashSet<string>(StringComparer.OrdinalIgnoreCase), block = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<Texture, Vector4> convertedTex = new Dictionary<Texture, Vector4>();
        static readonly List<KeyValuePair<string, Vector2>> steps = new List<KeyValuePair<string, Vector2>>();
        static readonly HashSet<Material> done = new HashSet<Material>();
        static readonly Dictionary<string, Vector4> converted = new Dictionary<string, Vector4>();
        static readonly Dictionary<Mesh, float[]> uvCache = new Dictionary<Mesh, float[]>();

        static void ReadSettings()
        {
            foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("CAELUS_HEXTILE"))
            {
                n.TryGetValue("enabled", ref enabled); n.TryGetValue("minRepeats", ref minRepeats); n.TryGetValue("density", ref density);
                n.TryGetValue("mottle", ref mottle); n.TryGetValue("sharpness", ref sharpness);
                prefixes.AddRange(n.GetValues("prefix")); skips.AddRange(n.GetValues("skip"));
                foreach (string t in n.GetValues("texture")) allow.Add(t.Trim()); foreach (string t in n.GetValues("block")) block.Add(t.Trim());
                foreach (string s in n.GetValues("steps"))
                {
                    string[] p = s.Split(',');
                    float a, b; var inv = System.Globalization.CultureInfo.InvariantCulture;
                    if (p.Length == 3 && float.TryParse(p[1].Trim(), System.Globalization.NumberStyles.Float, inv, out a) && float.TryParse(p[2].Trim(), System.Globalization.NumberStyles.Float, inv, out b))
                        steps.Add(new KeyValuePair<string, Vector2>(p[0].Trim(), new Vector2(a, b)));
                    else Debug.LogWarning("[CaelusSmoke] non-repeating textures: CAELUS_HEXTILE steps = '" + s + "' is not 'texture, a, b' - ignored");
                }
            }
            if (prefixes.Count == 0) { prefixes.Add("Caelus_"); prefixes.Add("CaelusFX_UpperStages/"); prefixes.Add("CaelusCorona_"); }
        }

        static bool Ours(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (string p in prefixes) if (name.StartsWith(p, StringComparison.OrdinalIgnoreCase) || name.Replace('.', '_').StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static Shader Target(Material m)
        {
            string s = m.shader != null ? m.shader.name : "";
            if (s == "TU/Metallic") return SmokeAssets.hexMetallic;
            if (s == "KSP/Bumped Specular" || s == "KSP/Specular" || s == "KSP/Bumped" || s == "KSP/Diffuse") return SmokeAssets.hexSpecular;
            return null;
        }

        static float[] UvRepeats(Mesh mesh)
        {
            float[] r;
            if (uvCache.TryGetValue(mesh, out r)) return r;
            r = new float[mesh.subMeshCount];
            try
            {
                Vector2[] uv = mesh.uv;
                if (uv != null && uv.Length > 0)
                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        int[] t = mesh.GetTriangles(s); double a = 0;
                        Vector2 lo = new Vector2(1e9f, 1e9f), hi = new Vector2(-1e9f, -1e9f);
                        for (int i = 0; i + 2 < t.Length; i += 3)
                        {
                            Vector2 p = uv[t[i]], q = uv[t[i + 1]] - p, w = uv[t[i + 2]] - p;
                            a += Math.Abs(q.x * w.y - q.y * w.x) * 0.5;
                            for (int k = 0; k < 3; k++) { lo = Vector2.Min(lo, uv[t[i + k]]); hi = Vector2.Max(hi, uv[t[i + k]]); }
                        }
                        Vector2 span = hi - lo;
                        r[s] = Mathf.Max(span.x, span.y) > 1.5f ? (float)a : 0f;
                    }
            }
            catch (Exception) { }
            uvCache[mesh] = r;
            return r;
        }

        static string TexName(Material m)
        {
            Texture t = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
            return t != null ? t.name : "";
        }

        static string Short(string url) { int i = url.LastIndexOf('/'); return i >= 0 ? url.Substring(i + 1) : url; }

        static bool Allowed(Material m, string tex)
        {
            if (allow.Count == 0) return !Skipped(tex);
            if (!allow.Contains(Short(tex)) || Skipped(tex)) return false;
            foreach (string k in new[] { "Serial", "RGBA", "Text", "Decal", "Mark", "Label", "Placard", "Livery", "Flag", "Logo" })
                if (tex.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return false;
            Texture b = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;
            return b == null || !block.Contains(Short(b.name));
        }

        static bool Convert(Material m, Vector4 hp)
        {
            Shader to = Target(m);
            if (to == null) return false;
            bool hadSpec = m.HasProperty("_SpecColor");
            Texture bump = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;
            m.shader = to;
            if (to == SmokeAssets.hexSpecular && !hadSpec) m.SetColor("_SpecColor", Color.black);
            if (bump == null) m.SetTexture("_BumpMap", FlatNormal());
            m.SetVector("_HexTile", hp); m.SetFloat("_HexSharp", sharpness);
            done.Add(m);
            return true;
        }

        static Texture2D flatNormal;
        static Texture2D FlatNormal()
        {
            if (flatNormal != null) return flatNormal;
            flatNormal = new Texture2D(4, 4, TextureFormat.RGBA32, false, true) { name = "CaelusHexFlatNormal" };
            var px = new Color32[16]; for (int i = 0; i < 16; i++) px[i] = new Color32(128, 128, 128, 128); flatNormal.SetPixels32(px); flatNormal.Apply(false, true);
            return flatNormal;
        }

        static Vector4 Params(string tex)
        {
            var hp = new Vector4(density, mottle, 0f, 0f);
            foreach (var kv in steps) if (tex.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0) { hp.z = kv.Value.x; hp.w = kv.Value.y; break; }
            return hp;
        }

        static bool Skipped(string tex)
        {
            foreach (string s in skips) if (tex.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        static int Walk(GameObject root)
        {
            int n = 0;
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                float[] rep = UvRepeats(mf.sharedMesh);
                Material[] ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length && i < rep.Length; i++)
                {
                    Material m = ms[i];
                    if (m == null || done.Contains(m) || rep[i] <= minRepeats) continue;
                    string tex = TexName(m);
                    if (tex.Length == 0 || !Allowed(m, tex)) continue;
                    Vector4 hp = Params(tex);
                    if (Convert(m, hp)) { converted[tex] = hp; convertedTex[m.GetTexture("_MainTex")] = hp; n++; }
                }
            }
            return n;
        }

        public static void ApplyToPrefabs()
        {
            ReadSettings();
            if (!enabled) { Debug.Log("[CaelusSmoke] non-repeating textures: off (CAELUS_HEXTILE enabled = false)"); return; }
            SmokeAssets.Load();
            if (SmokeAssets.hexMetallic == null || SmokeAssets.hexSpecular == null) { Debug.LogWarning("[CaelusSmoke] non-repeating textures: shaders missing from the bundle"); return; }
            DateTime t0 = DateTime.Now; int models = 0, parts = 0, mats = 0;
            foreach (GameObject g in GameDatabase.Instance.databaseModel)
                if (g != null && Ours(g.name)) { models++; mats += Walk(g); }
            foreach (AvailablePart ap in PartLoader.LoadedPartsList)
                if (ap != null && ap.partPrefab != null && Ours(ap.name)) { parts++; mats += Walk(ap.partPrefab.gameObject); }
            Ready = true;
            Debug.Log("[CaelusSmoke] non-repeating textures: " + mats + " materials (" + converted.Count + " textures) on " + models + " models + " + parts + " parts in "
                + (DateTime.Now - t0).TotalMilliseconds.ToString("F0") + " ms (allow-list " + allow.Count + ", blocked normals " + block.Count + "): " + string.Join(", ", new List<string>(converted.Keys).ConvertAll(Short).ToArray()));
        }

        public static void ScenePass()
        {
            if (converted.Count == 0) return;
            int n = 0;
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
            {
                Material[] ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    Material m = ms[i]; Vector4 hp;
                    if (m == null || done.Contains(m)) continue;
                    Texture mt = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                    if (mt == null || !convertedTex.TryGetValue(mt, out hp)) { done.Add(m); continue; }
                    MeshFilter mf = r.GetComponent<MeshFilter>();
                    float[] rep = mf != null && mf.sharedMesh != null ? UvRepeats(mf.sharedMesh) : null;
                    if (rep == null || i >= rep.Length || rep[i] <= minRepeats) continue;
                    if (Convert(m, hp)) n++; else done.Add(m);
                }
            }
            done.RemoveWhere(m => m == null);
            if (n > 0) Debug.Log("[CaelusSmoke] non-repeating textures: " + n + " more materials in the scene (" + HighLogic.LoadedScene + ")");
        }
    }
}
