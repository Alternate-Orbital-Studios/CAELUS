using System;
using UnityEngine;

namespace Caelus
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class CaelusSkyAmbient : MonoBehaviour
    {
        const string Dir = "GameData/Caelus_SkyAmbient/PluginData/";
        float baseBoost = 0.35f, last = -99f, playerBoost, airF;
        bool applied;

        internal static float ResolvePlayer(float cur)
        {
            float stored = ReadSetting("player_boost.txt", float.NaN), lastApplied = ReadSetting("applied_boost.txt", float.NaN);
            if (!float.IsNaN(lastApplied) && Mathf.Abs(cur - lastApplied) < 1e-5f && (float.IsNaN(stored) || Mathf.Abs(stored - cur) > 1e-5f))
            {
                float v = float.IsNaN(stored) ? 0f : stored;
                Debug.Log("[CaelusSkyAmbient] ambient boost " + cur + " in settings is our own leftover - player value " + v + " put back");
                GameSettings.AMBIENTLIGHT_BOOSTFACTOR = v; GameSettings.SaveSettings(); WriteSetting("applied_boost.txt", float.NaN); return v;
            }
            WriteSetting("player_boost.txt", cur); return cur;
        }

        internal static void WriteSetting(string file, float v)
        {
            try
            {
                string p = KSPUtil.ApplicationRootPath + Dir + file;
                if (float.IsNaN(v)) { if (System.IO.File.Exists(p)) System.IO.File.Delete(p); return; }
                System.IO.File.WriteAllText(p, v.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception e) { Debug.LogWarning("[CaelusSkyAmbient] could not write " + file + ": " + e.Message); }
        }

        void Start()
        {
            baseBoost = ReadSetting("boost.txt", 0.35f);
            playerBoost = ResolvePlayer(GameSettings.AMBIENTLIGHT_BOOSTFACTOR);
            Debug.Log("[CaelusSkyAmbient] sky ambient boost " + baseBoost + " in the lower atmosphere, 0 in vacuum, CAELUS vessels only (player setting " + playerBoost + " restored otherwise)");
        }

        internal static bool IsCaelusPart(Part p)
        {
            if (p == null || p.partInfo == null || p.partInfo.name == null) return false;
            string n = p.partInfo.name;
            return n.StartsWith("Caelus", StringComparison.Ordinal) || n.StartsWith("CaelusCorona", StringComparison.Ordinal) || n.StartsWith("RomaAeterna", StringComparison.Ordinal);
        }
        static Vessel scopeV; static int scopeN = -1; static bool scopeR;
        internal static bool IsCaelusVessel(Vessel v)
        {
            if (v == null || v.parts == null) return false;
            if (v == scopeV && v.parts.Count == scopeN) return scopeR;
            scopeV = v; scopeN = v.parts.Count; scopeR = false;
            for (int i = 0; i < v.parts.Count; i++) if (IsCaelusPart(v.parts[i])) { scopeR = true; break; }
            return scopeR;
        }

        bool Wanted()
        {
            var v = FlightGlobals.ActiveVessel;

            return v != null && v.mainBody != null && !FlightDriver.Pause && !PauseMenu.isOpen && (IsCaelusVessel(v) || CaelusSiteScope.Near());
        }

        void Take()
        {
            if (applied) return;
            playerBoost = ResolvePlayer(GameSettings.AMBIENTLIGHT_BOOSTFACTOR);
            last = -99f; applied = true;
            Debug.Log("[CaelusSkyAmbient] CAELUS vessel active or near our site - sky ambient on (player boost " + playerBoost + " kept aside)");
        }

        void GiveBack(string why)
        {
            if (!applied) return;
            applied = false;
            RestorePlayerSettings();
            WriteSetting("applied_boost.txt", float.NaN);
            Debug.Log("[CaelusSkyAmbient] " + why + " - player boost " + playerBoost + ", PlanetShine shadows and sun shadow strength restored");
        }

        internal static float ReadSetting(string file, float fallback)
        {
            string p = KSPUtil.ApplicationRootPath + Dir + file;
            if (!System.IO.File.Exists(p)) return fallback;
            float v;
            if (float.TryParse(System.IO.File.ReadAllText(p).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) return v;
            Debug.LogWarning("[CaelusSkyAmbient] " + Dir + file + " does not hold a number - using " + fallback);
            return fallback;
        }

        void Update()
        {

            if (!Wanted()) { GiveBack("no CAELUS vessel active (or paused)"); return; }
            Take();
            var v = FlightGlobals.ActiveVessel;
            double atm = v.mainBody.atmosphere ? v.staticPressurekPa / 101.325 : 0;
            airF = atm <= 0.001 ? 0f : atm >= 0.1 ? 1f : (float)((Math.Log10(atm) + 3.0) / 2.0);

            float sunF = 1f;
            if (Planetarium.fetch != null && Planetarium.fetch.Sun != null)
            {
                Vector3d up = (v.GetWorldPos3D() - v.mainBody.position).normalized;
                Vector3d toSun = (Planetarium.fetch.Sun.position - v.GetWorldPos3D()).normalized;
                double elev = 90.0 - Vector3d.Angle(up, toSun);
                sunF = elev >= 10 ? 1f : elev <= -8 ? 0.12f : 0.12f + 0.88f * (float)((elev + 8.0) / 18.0);
            }
            float want = baseBoost * airF * sunF;
            if (Mathf.Abs(want - last) > 0.005f)
            {
                if ((want == 0f) != (last == 0f) || want == baseBoost)
                    Debug.Log("[CaelusSkyAmbient] " + v.vesselName + " at " + (v.altitude / 1000).ToString("F1") + " km: boost " + want.ToString("F3"));
                GameSettings.AMBIENTLIGHT_BOOSTFACTOR = want; last = want; WriteSetting("applied_boost.txt", want);
            }
        }

        Light[] extra = new Light[0]; float nextScan;

        readonly System.Collections.Generic.Dictionary<Light, LightShadows> extraOrig = new System.Collections.Generic.Dictionary<Light, LightShadows>();
        void LateUpdate()
        {
            if (!applied || !Wanted()) return;
            if (Time.realtimeSinceStartup > nextScan)
            {
                nextScan = Time.realtimeSinceStartup + 3f;
                var l = new System.Collections.Generic.List<Light>();
                foreach (var x in FindObjectsOfType<Light>())
                    if (x != null && x.type == LightType.Directional && x.gameObject.name == "New Game Object" && x.transform.parent == null) l.Add(x);
                if (l.Count != extra.Length) Debug.Log("[CaelusSkyAmbient] PlanetShine directional lights found: " + l.Count + " (shadows off)");
                extra = l.ToArray();
            }
            foreach (var x in extra)
                if (x != null && x.shadows != LightShadows.None)
                {
                    if (!extraOrig.ContainsKey(x)) extraOrig[x] = x.shadows;
                    x.shadows = LightShadows.None;
                }
            Earthshine();
            ShadowFill.Apply(airF, "flight");
        }

        void RestorePlanetShine()
        {
            foreach (var kv in extraOrig) if (kv.Key != null) kv.Key.shadows = kv.Value;
            extraOrig.Clear();
        }

        Light earthLight; float earthBase = -1f; bool earthOn;
        void Earthshine()
        {
            var v = FlightGlobals.ActiveVessel;
            if (earthBase < 0f) earthBase = ReadSetting("earthshine.txt", 0.07f);
            float want = 0f; Vector3 dir = Vector3.down;
            var home = FlightGlobals.GetHomeBody();
            if (v != null && home != null && v.mainBody != null && v.mainBody != home && earthBase > 0f && Planetarium.fetch != null && Planetarium.fetch.Sun != null)
            {
                Vector3d p = v.GetWorldPos3D(), toE = home.position - p; double d = toE.magnitude;

                Vector3d b = v.mainBody.position - p; double t = Vector3d.Dot(b, toE / d);
                bool hidden = t > 0 && t < d && (b - toE / d * t).magnitude < v.mainBody.Radius;
                Vector3d eSun = (Planetarium.fetch.Sun.position - home.position).normalized, eShip = (p - home.position).normalized;
                double frac = (1.0 + Vector3d.Dot(eSun, eShip)) / 2.0;
                double dRef = 384400e3 * (home.Radius / 6371e3);
                if (!hidden) want = earthBase * (float)(frac * Math.Min(4.0, (dRef / d) * (dRef / d)));
                dir = -(Vector3)(toE / d);
            }
            if (want > 0.002f && earthLight == null)
            {
                var go = new GameObject("CaelusEarthshine"); earthLight = go.AddComponent<Light>();
                earthLight.type = LightType.Directional; earthLight.shadows = LightShadows.None;
                earthLight.color = new Color(0.72f, 0.82f, 1.0f);
                var sun = GameObject.Find("SunLight"); var sl = sun != null ? sun.GetComponent<Light>() : null;
                earthLight.cullingMask = sl != null ? sl.cullingMask : (1 << 0) | (1 << 15);
            }
            if (earthLight != null)
            {
                earthLight.intensity = want; earthLight.enabled = want > 0.002f;
                earthLight.transform.rotation = Quaternion.LookRotation(dir);
            }
            if ((want > 0.002f) != earthOn)
            {
                earthOn = want > 0.002f;
                Debug.Log("[CaelusSkyAmbient] earthshine " + (earthOn ? "on, " + want.ToString("F3") : "off"));
            }
        }

        void RestorePlayerSettings()
        {
            GameSettings.AMBIENTLIGHT_BOOSTFACTOR = playerBoost;
            ShadowFill.Restore();

            RestorePlanetShine();
            if (earthLight != null) earthLight.enabled = false;
            earthOn = false; last = -99f;
        }

        void OnDestroy()
        {
            GiveBack("flight scene ended");
            if (earthLight != null) Destroy(earthLight.gameObject);
        }

        void OnApplicationQuit() { GiveBack("quit"); }
    }

    static class ShadowFill
    {
        static float fill = -1f, last = -1f, original = -1f; static Light sun;

        public static void Apply(float air, string scene)
        {
            if (fill < 0f) fill = Mathf.Clamp01(CaelusSkyAmbient.ReadSetting("shadowfill.txt", 0.25f));
            if (sun == null) { var go = GameObject.Find("SunLight"); sun = go != null ? go.GetComponent<Light>() : null; if (sun == null) return; }
            if (original < 0f) original = sun.shadowStrength;
            float s = 1f - fill * Mathf.Clamp01(air);
            if (sun.shadowStrength != s) sun.shadowStrength = s;
            if ((s == 1f) != (last == 1f) || last < 0f)
                Debug.Log("[CaelusSkyAmbient] " + scene + ": sun shadow strength " + s.ToString("F2") + " (fill " + fill.ToString("F2") + " x air " + air.ToString("F2") + ")");
            last = s;
        }

        public static void Restore()
        {
            if (sun != null && original >= 0f) sun.shadowStrength = original;
            original = -1f; last = -1f;
        }
    }

    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    public class CaelusAmbientRepair : MonoBehaviour
    {
        void Start()
        {
            if (HighLogic.LoadedSceneIsFlight) return;
            try { CaelusSkyAmbient.ResolvePlayer(GameSettings.AMBIENTLIGHT_BOOSTFACTOR); }
            catch (Exception e) { Debug.LogWarning("[CaelusSkyAmbient] repair check failed: " + e.Message); }
        }
    }

    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public class CaelusShadowFillKsc : MonoBehaviour
    {
        bool applied;
        void LateUpdate()
        {
            if (CaelusSiteScope.Near()) { ShadowFill.Apply(1f, "space centre (our site)"); applied = true; }
            else if (applied) { ShadowFill.Restore(); applied = false; Debug.Log("[CaelusSkyAmbient] space centre away from our site - sun shadow strength restored"); }
        }
        void OnDestroy() { if (applied) ShadowFill.Restore(); }
    }

    internal static class CaelusSiteScope
    {
        const double Range = 25000.0;
        static readonly System.Collections.Generic.List<Transform> groups = new System.Collections.Generic.List<Transform>();
        static readonly System.Collections.Generic.List<CelestialBody> groupBody = new System.Collections.Generic.List<CelestialBody>();
        static readonly string[] Known = { "StatioAromata-PadI", "StatioAromata-PadII", "StatioAromata-Crawlerway", "StatioAromata-OrdoMissilium-M1",
                                           "StatioAromata-OrdoMissilium-M2", "StatioAromata-OrdoMissilium-M3", "StatioAromata-OrdoMissilium-M4" };
        static float nextScan = -1f, nextCam; static int lastCount = -1; static GameScenes scanScene = GameScenes.LOADING; static Camera kscCam;

        static void Scan()
        {
            if (HighLogic.LoadedScene == scanScene && Time.realtimeSinceStartup < nextScan) return;
            scanScene = HighLogic.LoadedScene; nextScan = Time.realtimeSinceStartup + 10f;
            groups.Clear(); groupBody.Clear();
            if (FlightGlobals.Bodies != null)
                foreach (var b in FlightGlobals.Bodies)
                {
                    if (b == null) continue;
                    AddChildren(b.transform, b);
                    if (b.pqsController != null && b.pqsController.transform != b.transform) AddChildren(b.pqsController.transform, b);
                }
            if (groups.Count == 0)
                foreach (var n in Known) { var go = GameObject.Find(n); if (go != null) Add(go.transform, BodyOf(go.transform)); }
            if (groups.Count != lastCount) { lastCount = groups.Count; Debug.Log("[CaelusSiteScope] " + HighLogic.LoadedScene + ": " + groups.Count + " active StatioAromata group(s) found"); }
        }

        static void AddChildren(Transform t, CelestialBody b)
        {
            if (t == null) return;
            for (int i = 0; i < t.childCount; i++) { var c = t.GetChild(i); if (c != null && c.name.StartsWith("StatioAromata", StringComparison.Ordinal)) Add(c, b); }
        }

        static void Add(Transform t, CelestialBody b)
        {
            if (t == null || !t.gameObject.activeInHierarchy || groups.Contains(t)) return;
            groups.Add(t); groupBody.Add(b);
        }

        static CelestialBody BodyOf(Transform t)
        {
            if (FlightGlobals.Bodies == null) return null;
            for (var p = t; p != null; p = p.parent)
                foreach (var b in FlightGlobals.Bodies)
                    if (b != null && (p == b.transform || (b.pqsController != null && p == b.pqsController.transform))) return b;
            return null;
        }

        static bool NearPos(Vector3d pos, CelestialBody body)
        {
            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                if (g == null || !g.gameObject.activeInHierarchy) continue;
                if (body != null && groupBody[i] != null && body != groupBody[i]) continue;
                if (((Vector3d)g.position - pos).sqrMagnitude < Range * Range) return true;
            }
            return false;
        }

        static Camera KscCamera()
        {
            if (FlightCamera.fetch != null && FlightCamera.fetch.mainCamera != null && FlightCamera.fetch.mainCamera.isActiveAndEnabled) return FlightCamera.fetch.mainCamera;
            if (kscCam != null && kscCam.isActiveAndEnabled) return kscCam;
            if (Time.realtimeSinceStartup < nextCam) return null;
            nextCam = Time.realtimeSinceStartup + 2f; kscCam = null;
            foreach (var c in Camera.allCameras) if (c != null && c.name == "Camera 00") { kscCam = c; break; }
            return kscCam;
        }

        public static bool Near()
        {
            Scan();
            if (groups.Count == 0) return false;
            if (HighLogic.LoadedSceneIsFlight)
            {
                var v = FlightGlobals.ActiveVessel;
                if (v != null && v.mainBody != null && NearPos(v.GetWorldPos3D(), v.mainBody)) return true;
                var fc = FlightCamera.fetch;
                return fc != null && fc.mainCamera != null && NearPos(fc.mainCamera.transform.position, FlightGlobals.currentMainBody);
            }
            if (HighLogic.LoadedScene == GameScenes.SPACECENTER)
            {
                var c = KscCamera();
                return c != null && NearPos(c.transform.position, FlightGlobals.GetHomeBody());
            }
            return false;
        }
    }
}
