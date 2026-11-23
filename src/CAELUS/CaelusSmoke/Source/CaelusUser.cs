using System;
using System.IO;
using KSP.UI.Screens;
using UnityEngine;

namespace CaelusSmoke
{
    public static class CaelusUser
    {
        public static int quality = 2;
        public static bool flame = true, smoke = true, padSteam = true, vapour = true, rcs = true, bellGlow = true, lunarDust = true, marks = true, frost = true, flameGlow = true;
        public static bool blast = true;
        public static float brightness = 1f;
        public static float density = 1f;

        public static int camMode = 0;

        public static bool camFloor = false;
        public static readonly string[] CamModeNames = { "Stock", "Hull stop", "Slide" };
        public static readonly string[] QualityNames = { "Low", "Medium", "High", "Ultra" };
        static bool loaded, haveBase;
        static int bSmokeDiv, bSmokeSteps, bSmokeLight, bFlameDiv, bFlameSteps, bVapDiv, bVapSteps, bDustDiv, bDustSteps, bFrostDiv, bFrostSteps;
        static float bSun, bAmb, bDust;

        static string FilePath => Path.Combine(Path.Combine(Path.GetDirectoryName(typeof(CaelusUser).Assembly.Location), ".."), Path.Combine("PluginData", "Settings.cfg"));

        public static void Load()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                if (!File.Exists(FilePath)) return;
                ConfigNode root = ConfigNode.Load(FilePath); ConfigNode n = root != null ? (root.GetNode("CAELUS_USER") ?? root) : null;
                if (n == null) return;
                n.TryGetValue("quality", ref quality); n.TryGetValue("flame", ref flame); n.TryGetValue("smoke", ref smoke); n.TryGetValue("padSteam", ref padSteam);
                n.TryGetValue("vapour", ref vapour); n.TryGetValue("rcs", ref rcs); n.TryGetValue("bellGlow", ref bellGlow); n.TryGetValue("flameGlow", ref flameGlow); n.TryGetValue("lunarDust", ref lunarDust); n.TryGetValue("frost", ref frost);
                n.TryGetValue("marks", ref marks); n.TryGetValue("blast", ref blast); n.TryGetValue("brightness", ref brightness); n.TryGetValue("density", ref density); n.TryGetValue("camMode", ref camMode); n.TryGetValue("camFloor", ref camFloor);
                quality = Mathf.Clamp(quality, 0, 3); camMode = Mathf.Clamp(camMode, 0, 2); brightness = Mathf.Clamp(brightness, 0.5f, 1.5f); density = Mathf.Clamp(density, 0.5f, 1.5f);
            }
            catch (Exception e) { Debug.LogWarning("[CaelusSmoke] settings file unreadable, defaults used: " + e.Message); }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var root = new ConfigNode(); ConfigNode n = root.AddNode("CAELUS_USER");
                n.AddValue("quality", quality); n.AddValue("flame", flame); n.AddValue("smoke", smoke); n.AddValue("padSteam", padSteam);
                n.AddValue("vapour", vapour); n.AddValue("rcs", rcs); n.AddValue("bellGlow", bellGlow); n.AddValue("flameGlow", flameGlow); n.AddValue("lunarDust", lunarDust); n.AddValue("frost", frost);
                n.AddValue("marks", marks); n.AddValue("blast", blast); n.AddValue("brightness", brightness.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)); n.AddValue("density", density.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)); n.AddValue("camMode", camMode); n.AddValue("camFloor", camFloor);
                root.Save(FilePath, "CaelusSmoke player settings (in-game window). Delete this file to go back to the defaults.");
            }
            catch (Exception e) { Debug.LogWarning("[CaelusSmoke] settings not saved: " + e.Message); }
        }

        public static void Apply(bool captureBaseline)
        {
            Load();
            if (!haveBase)
            {
                haveBase = true;
                bSmokeDiv = SmokeSettings.resDivisor; bSmokeSteps = SmokeSettings.marchSteps; bSmokeLight = SmokeSettings.lightSteps;
                bFlameDiv = FlameSettings.resDivisor; bFlameSteps = FlameSettings.steps; bVapDiv = VaporSettings.resDivisor; bVapSteps = VaporSettings.steps;
                bDustDiv = LunarDustSettings.resDivisor; bDustSteps = LunarDustSettings.steps;
                bFrostDiv = FrostSettings.resDivisor; bFrostSteps = FrostSettings.steps;
                bSun = SmokeSettings.sunIntensity; bAmb = SmokeSettings.ambient; bDust = LunarDustSettings.strength;
            }

            int dd = quality == 0 ? 1 : (quality == 3 ? -1 : 0);
            float sk = quality == 0 ? 0.6f : (quality == 1 ? 0.8f : (quality == 3 ? 1.25f : 1f));
            int Div(int b, int lo) => Mathf.Clamp(b + dd, lo, 4);

            SmokeSettings.resDivisor = Div(bSmokeDiv, 2);
            SmokeSettings.marchSteps = Mathf.Max(24, Mathf.RoundToInt(bSmokeSteps * sk)); SmokeSettings.lightSteps = Mathf.Max(3, Mathf.RoundToInt(bSmokeLight * Mathf.Min(sk, 1.2f)));
            FlameSettings.resDivisor = Div(bFlameDiv, 1); FlameSettings.steps = Mathf.Max(32, Mathf.RoundToInt(bFlameSteps * sk));
            VaporSettings.resDivisor = Div(bVapDiv, 1); VaporSettings.steps = Mathf.Max(24, Mathf.RoundToInt(bVapSteps * sk));
            LunarDustSettings.resDivisor = Div(bDustDiv, 1); LunarDustSettings.steps = Mathf.Max(32, Mathf.RoundToInt(bDustSteps * sk));
            FrostSettings.resDivisor = Div(bFrostDiv, 1); FrostSettings.steps = Mathf.Max(32, Mathf.RoundToInt(bFrostSteps * sk));
            SmokeSettings.sunIntensity = bSun * brightness; SmokeSettings.ambient = bAmb * brightness;
            LunarDustSettings.strength = bDust * density;
            SetCameraFloor(camFloor);
        }

        static System.Reflection.FieldInfo floorOn; static bool floorLooked;

        static void SetCameraFloor(bool on)
        {
            try
            {
                if (!floorLooked)
                {
                    floorLooked = true;
                    foreach (var a in AssemblyLoader.loadedAssemblies)
                    {
                        if (a == null || a.assembly == null || a.name != "CaelusCameraFloor") continue;
                        foreach (var t in a.assembly.GetTypes()) if (t.Name == "CaelusCameraFloor") { floorOn = t.GetField("on", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public); break; }
                    }
                }
                if (floorOn != null) floorOn.SetValue(null, on);
            }
            catch (Exception e) { Debug.LogWarning("[CaelusSmoke] camera floor switch not set: " + e.Message); }
        }
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class CaelusSettingsWindow : MonoBehaviour
    {
        ApplicationLauncherButton button; Texture2D icon; bool iconOwned;
        bool show, uiHidden; Rect win = new Rect(-1f, 90f, 300f, 10f);
        string marksNote = "";
        float saveAt = -1f;
        void HideUI() { uiHidden = true; }
        void ShowUI() { uiHidden = false; }

        void Start()
        {
            CaelusUser.Load();
            GameEvents.onGUIApplicationLauncherReady.Add(AddButton);
            GameEvents.onHideUI.Add(HideUI); GameEvents.onShowUI.Add(ShowUI);
            if (ApplicationLauncher.Ready) AddButton();
        }

        void AddButton()
        {
            if (button != null || ApplicationLauncher.Instance == null) return;

            icon = GameDatabase.Instance.GetTexture("CaelusSmoke/Icons/caelus_effects", false); iconOwned = false;
            if (icon == null) { icon = MakeIcon(); iconOwned = true; }
            button = ApplicationLauncher.Instance.AddModApplication(() => show = true, () => show = false, null, null, null, null,
                ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.MAPVIEW, icon);
        }

        void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(AddButton);
            GameEvents.onHideUI.Remove(HideUI); GameEvents.onShowUI.Remove(ShowUI);
            if (saveAt >= 0f) CaelusUser.Save();
            if (button != null && ApplicationLauncher.Instance != null) ApplicationLauncher.Instance.RemoveModApplication(button);
            if (iconOwned && icon != null) Destroy(icon);
        }

        void OnGUI()
        {
            if (!show || uiHidden) return;
            GUI.skin = HighLogic.Skin;
            if (win.x < 0f) win.x = Mathf.Round(Screen.width * 0.5f - 150f);
            win = GUILayout.Window(0x5CAE1, win, Draw, "CAELUS Effects", GUILayout.Width(300f));
            win.x = Mathf.Clamp(win.x, 0f, Mathf.Max(0f, Screen.width - win.width)); win.y = Mathf.Clamp(win.y, 0f, Mathf.Max(0f, Screen.height - 40f));
        }

        void Update()
        {
            if (saveAt >= 0f && Time.realtimeSinceStartup >= saveAt) { saveAt = -1f; CaelusUser.Save(); }
        }

        void Draw(int id)
        {
            bool ch = false;
            GUILayout.Label("Quality (cost scales with it)");
            int q = GUILayout.SelectionGrid(CaelusUser.quality, CaelusUser.QualityNames, 4);
            if (q != CaelusUser.quality) { CaelusUser.quality = q; ch = true; }
            GUILayout.Space(4f);
            ch |= Toggle(ref CaelusUser.flame, "Engine flames");
            ch |= Toggle(ref CaelusUser.flameGlow, "Flame glow (bloom)");
            ch |= Toggle(ref CaelusUser.smoke, "Exhaust smoke column");
            ch |= Toggle(ref CaelusUser.padSteam, "Launch-pad steam");
            ch |= Toggle(ref CaelusUser.blast, "Engines and vessels push the smoke");
            ch |= Toggle(ref CaelusUser.vapour, "Mach condensation collars");
            ch |= Toggle(ref CaelusUser.frost, "Cryogenic tank frost + frost fog");
            ch |= Toggle(ref CaelusUser.rcs, "RCS plumes");
            ch |= Toggle(ref CaelusUser.bellGlow, "Nozzle glow");
            ch |= Toggle(ref CaelusUser.lunarDust, "Landing dust (airless bodies)");
            ch |= Toggle(ref CaelusUser.marks, "Landing marks on the ground");
            GUILayout.Space(4f);
            GUILayout.Label("Brightness " + Mathf.RoundToInt(CaelusUser.brightness * 100f) + " %");
            float b = GUILayout.HorizontalSlider(CaelusUser.brightness, 0.5f, 1.5f);
            if (Mathf.Abs(b - CaelusUser.brightness) > 0.004f) { CaelusUser.brightness = b; ch = true; }
            GUILayout.Label("Landing dust density " + Mathf.RoundToInt(CaelusUser.density * 100f) + " %");
            float d = GUILayout.HorizontalSlider(CaelusUser.density, 0.5f, 1.5f);
            if (Mathf.Abs(d - CaelusUser.density) > 0.004f) { CaelusUser.density = d; ch = true; }
            GUILayout.Space(4f);
            GUILayout.Label("Camera near CAELUS ships");
            int cm = GUILayout.SelectionGrid(CaelusUser.camMode, CaelusUser.CamModeNames, 3);
            if (cm != CaelusUser.camMode) { CaelusUser.camMode = cm; ch = true; }
            ch |= Toggle(ref CaelusUser.camFloor, "Camera floor at our pads (off = stock)");
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Defaults"))
            {
                CaelusUser.quality = 2; CaelusUser.flame = CaelusUser.smoke = CaelusUser.padSteam = CaelusUser.vapour = CaelusUser.rcs = CaelusUser.bellGlow = CaelusUser.flameGlow = CaelusUser.lunarDust = CaelusUser.marks = CaelusUser.blast = CaelusUser.frost = true;
                CaelusUser.brightness = 1f; CaelusUser.density = 1f; CaelusUser.camMode = 0; CaelusUser.camFloor = false; ch = true;
            }
            if (GUILayout.Button("Clear landing marks")) { int k = CaelusLunarMarks.marks.Count; CaelusLunarMarks.marks.Clear(); marksNote = k + " marks cleared (saved with the next save)"; }
            GUILayout.EndHorizontal();
            if (marksNote.Length > 0) GUILayout.Label(marksNote);
            if (ch) { CaelusUser.Apply(false); saveAt = Time.realtimeSinceStartup + 0.5f; }
            GUI.DragWindow();
        }

        static bool Toggle(ref bool v, string label) { bool n = GUILayout.Toggle(v, " " + label); if (n == v) return false; v = n; return true; }

        static Texture2D MakeIcon()
        {
            const int N = 38; var t = new Texture2D(N, N, TextureFormat.ARGB32, false); var px = new Color[N * N];
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f, a = 0f;

                a = Mathf.Max(a, Disc(u, v, -0.42f, -0.5f, 0.32f)); a = Mathf.Max(a, Disc(u, v, 0.0f, -0.42f, 0.4f)); a = Mathf.Max(a, Disc(u, v, 0.45f, -0.52f, 0.3f));

                float fy = (v + 0.05f) / 0.85f, fw = 0.28f * Mathf.Sqrt(Mathf.Max(0f, 1f - fy * fy)) * (1f - 0.45f * Mathf.Max(0f, fy));
                float fl = Mathf.Clamp01((fw - Mathf.Abs(u)) * N * 0.5f) * (fy > -1f && fy < 1f ? 1f : 0f);
                Color c = Color.Lerp(new Color(1f, 1f, 1f, a), new Color(1f, 0.62f, 0.25f, 1f), fl);
                c.a = Mathf.Max(a, fl);
                px[y * N + x] = c;
            }
            t.SetPixels(px); t.Apply(); return t;
        }
        static float Disc(float u, float v, float cx, float cy, float r) => Mathf.Clamp01((r - Mathf.Sqrt((u - cx) * (u - cx) + (v - cy) * (v - cy))) * 19f);
    }
}
