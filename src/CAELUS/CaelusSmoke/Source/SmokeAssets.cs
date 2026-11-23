using System;
using System.IO;
using UnityEngine;

namespace CaelusSmoke
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class SmokeBakeStarter : MonoBehaviour { void Start() { SmokeAssets.StartBake(); } }

    public static class SmokeAssets
    {
        public static Shader volume, flame, bellGlow, foil, glass, vapor, hexMetallic, hexSpecular, lunarDust, frost, frostVapour, visor;
        public static Texture3D noise;
        static bool tried;

        public static bool Load()
        {
            if (tried) return volume != null;
            tried = true;
            string dll = Path.GetDirectoryName(typeof(SmokeAssets).Assembly.Location);
            string[] candidates = { Path.Combine(Path.Combine(dll, ".."), Path.Combine("Bundles", "caelussmoke.bundle")), Path.Combine(dll, "caelussmoke.bundle") };
            foreach (string c in candidates)
            {
                if (!File.Exists(c)) continue;
                AssetBundle b = AssetBundle.LoadFromFile(c);
                if (b == null) { Debug.LogError("[CaelusSmoke] bundle " + c + " failed to load (built with a Unity version other than 2019.4.18f1?)"); continue; }
                foreach (Shader s in b.LoadAllAssets<Shader>()) { if (s.name == "CaelusSmoke/Volume") volume = s; else if (s.name == "CaelusSmoke/Flame") flame = s; else if (s.name == "CaelusSmoke/BellGlow") bellGlow = s; else if (s.name == "CaelusSmoke/Foil") foil = s; else if (s.name == "CaelusSmoke/Glass") glass = s; else if (s.name == "CaelusSmoke/Vapor") vapor = s; else if (s.name == "CaelusSmoke/HexMetallic") hexMetallic = s; else if (s.name == "CaelusSmoke/HexSpecular") hexSpecular = s; else if (s.name == "CaelusSmoke/LunarDust") lunarDust = s; else if (s.name == "CaelusSmoke/Frost") frost = s; else if (s.name == "CaelusSmoke/FrostVapor") frostVapour = s; else if (s.name == "CaelusSmoke/Visor") visor = s; }
                b.Unload(false);
                break;
            }
            if (volume == null) { Debug.LogError("[CaelusSmoke] shader CaelusSmoke/Volume not found - smoke disabled"); return false; }
            if (!volume.isSupported || !SystemInfo.supportsComputeShaders)
            {
                Debug.LogError("[CaelusSmoke] needs Direct3D 11 / shader model 5 (device " + SystemInfo.graphicsDeviceType + ", SM " + SystemInfo.graphicsShaderLevel + ") - smoke disabled");
                volume = null; return false;
            }
            DateTime t0 = DateTime.Now;
            noise = BakeNoise(bakeN);
            Debug.Log("[CaelusSmoke] shader loaded (" + SystemInfo.graphicsDeviceType + "), noise 64^3 baked in " + (DateTime.Now - t0).TotalMilliseconds.ToString("F0") + " ms");
            return true;
        }

        static int Hash(int x, int y, int z, int seed) { unchecked { int h = x * 374761393 + y * 668265263 + z * 2147483647 + seed * 144269504; h = (h ^ (h >> 13)) * 1274126177; return h ^ (h >> 16); } }
        static float Rand(int x, int y, int z, int seed) { return (Hash(x, y, z, seed) & 0xFFFFFF) / 16777215f; }
        static int Wrap(int a, int p) { return a & (p - 1); }
        static float Fade(float t) { return t * t * t * (t * (t * 6 - 15) + 10); }

        static float Grad(int x, int y, int z, int p, float dx, float dy, float dz, int seed)
        {
            int h = Hash(Wrap(x, p), Wrap(y, p), Wrap(z, p), seed) & 15;
            float u = h < 8 ? dx : dy, v = h < 4 ? dy : (h == 12 || h == 14 ? dx : dz);
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }

        static float Perlin(float x, float y, float z, int p, int seed)
        {
            int X = Mathf.FloorToInt(x), Y = Mathf.FloorToInt(y), Z = Mathf.FloorToInt(z);
            float fx = x - X, fy = y - Y, fz = z - Z, u = Fade(fx), v = Fade(fy), w = Fade(fz);
            float a = Mathf.Lerp(Mathf.Lerp(Grad(X, Y, Z, p, fx, fy, fz, seed), Grad(X + 1, Y, Z, p, fx - 1, fy, fz, seed), u),
                                 Mathf.Lerp(Grad(X, Y + 1, Z, p, fx, fy - 1, fz, seed), Grad(X + 1, Y + 1, Z, p, fx - 1, fy - 1, fz, seed), u), v);
            float b = Mathf.Lerp(Mathf.Lerp(Grad(X, Y, Z + 1, p, fx, fy, fz - 1, seed), Grad(X + 1, Y, Z + 1, p, fx - 1, fy, fz - 1, seed), u),
                                 Mathf.Lerp(Grad(X, Y + 1, Z + 1, p, fx, fy - 1, fz - 1, seed), Grad(X + 1, Y + 1, Z + 1, p, fx - 1, fy - 1, fz - 1, seed), u), v);
            return Mathf.Lerp(a, b, w) * 0.5f + 0.5f;
        }

        static float Worley(float x, float y, float z, int p, int seed)
        {
            int X = Mathf.FloorToInt(x), Y = Mathf.FloorToInt(y), Z = Mathf.FloorToInt(z);
            float best = 9f;
            for (int i = -1; i <= 1; i++) for (int j = -1; j <= 1; j++) for (int k = -1; k <= 1; k++)
            {
                int cx = X + i, cy = Y + j, cz = Z + k;
                int wx = Wrap(cx, p), wy = Wrap(cy, p), wz = Wrap(cz, p);
                float px = cx + Rand(wx, wy, wz, seed), py = cy + Rand(wx, wy, wz, seed + 1), pz = cz + Rand(wx, wy, wz, seed + 2);
                float d = (px - x) * (px - x) + (py - y) * (py - y) + (pz - z) * (pz - z);
                if (d < best) best = d;
            }
            return 1f - Mathf.Clamp01(Mathf.Sqrt(best));
        }

        static Color32[] baked; static System.Threading.Thread bakeThread; static int bakeN = 64;

        public static void StartBake()
        {
            if (baked != null || bakeThread != null) return;
            bakeThread = new System.Threading.Thread(() => { try { baked = BakeNoiseData(bakeN); } catch (Exception ex) { Debug.LogError("[CaelusSmoke] noise bake failed: " + ex); } });
            bakeThread.IsBackground = true; bakeThread.Start();
        }
        public static Texture3D BakeNoise(int N)
        {
            if (bakeThread != null) { bakeThread.Join(); bakeThread = null; }
            Color32[] cols = (baked != null && N == bakeN) ? baked : BakeNoiseData(N);
            var t = new Texture3D(N, N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, name = "CaelusSmokeNoise" };
            t.SetPixels32(cols); t.Apply(true, true);
            return t;
        }
        static Color32[] BakeNoiseData(int N)
        {
            var cols = new Color32[N * N * N];
            for (int z = 0; z < N; z++) for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float u = (float)x / N, v = (float)y / N, w = (float)z / N;
                float pf = 0, amp = 0.5f; int per = 4;
                for (int o = 0; o < 4; o++) { pf += amp * Perlin(u * per, v * per, w * per, per, 11 + o); amp *= 0.5f; per *= 2; }
                pf /= 0.9375f;
                float wf = 0.625f * Worley(u * 4, v * 4, w * 4, 4, 31) + 0.25f * Worley(u * 8, v * 8, w * 8, 8, 37) + 0.125f * Worley(u * 16, v * 16, w * 16, 16, 41);
                wf /= 1.0f;
                float wfine = 0.5f * Worley(u * 8, v * 8, w * 8, 8, 53) + 0.3f * Worley(u * 16, v * 16, w * 16, 16, 59) + 0.2f * Worley(u * 32, v * 32, w * 32, 32, 61);
                float pw = Mathf.Clamp01(pf + (wf - 0.5f) * 0.6f);
                pw = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.30f, 0.78f, pw));
                cols[x + y * N + z * N * N] = new Color32((byte)(pw * 255), (byte)(Mathf.Clamp01(wfine) * 255), (byte)(Mathf.Clamp01(pf) * 255), 255);
            }
            return cols;
        }
    }
}
