using System;
using System.Reflection;
using UnityEngine;

namespace Caelus
{

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class CaelusNearCloudShadowFlight : CaelusNearCloudShadow { }

    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public class CaelusNearCloudShadowKsc : CaelusNearCloudShadow { }

    public class CaelusNearCloudShadow : MonoBehaviour
    {
        static Type mgrType, rndType;
        static FieldInfo fInstance, fRenderers, fClouds2D, fEclipse, fLvMat;
        static MethodInfo mUpdateCb;
        static bool resolved, missing;
        bool logged, wasOff;
        bool applied;

        static bool IsCaelusPart(Part p)
        {
            if (p == null || p.partInfo == null || p.partInfo.name == null) return false;
            string n = p.partInfo.name;
            return n.StartsWith("Caelus", StringComparison.Ordinal) || n.StartsWith("CaelusCorona", StringComparison.Ordinal) || n.StartsWith("RomaAeterna", StringComparison.Ordinal);
        }
        static Vessel scopeV; static int scopeN = -1; static bool scopeR;
        static bool IsCaelusVessel(Vessel v)
        {
            if (v == null || v.parts == null) return false;
            if (v == scopeV && v.parts.Count == scopeN) return scopeR;
            scopeV = v; scopeN = v.parts.Count; scopeR = false;
            for (int i = 0; i < v.parts.Count; i++) if (IsCaelusPart(v.parts[i])) { scopeR = true; break; }
            return scopeR;
        }

        void GiveBack(string why)
        {
            if (!applied) return;
            applied = false; logged = false;
            try
            {
                if (missing || fInstance == null) return;
                var mgr = fInstance.GetValue(null); if (mgr == null) return;
                var lv = fLvMat != null ? fLvMat.GetValue(mgr) as Material : null;
                int n = Rebuild(mgr, lv);
                Debug.Log("[CaelusNearCloudShadow] " + why + " - EVE's own cloud shadow buffers restored on " + n + " camera(s)");
            }
            catch (Exception e) { Debug.LogWarning("[CaelusNearCloudShadow] restoring EVE's buffers failed (" + why + "): " + e.Message); }
        }

        void OnDestroy() { GiveBack("flight scene ended"); }

        static void Resolve()
        {
            resolved = true;
            foreach (var la in AssemblyLoader.loadedAssemblies)
            {
                if (mgrType == null) mgrType = la.assembly.GetType("Atmosphere.ScreenSpaceShadowsManager", false);
                if (rndType == null) rndType = la.assembly.GetType("Atmosphere.ScreenSpaceShadowsRenderer", false);
            }
            const BindingFlags bi = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, bs = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
            if (mgrType != null)
            {
                fInstance = mgrType.GetField("instance", bs); fRenderers = mgrType.GetField("cameraToShadowsRenderer", bi);
                fClouds2D = mgrType.GetField("cloud2DShadowMaterials", bi); fEclipse = mgrType.GetField("eclipseMaterials", bi); fLvMat = mgrType.GetField("lightVolumeShadowMaterial", bi);
            }
            if (rndType != null) mUpdateCb = rndType.GetMethod("UpdateCommandBuffer", bi);
            missing = fInstance == null || fRenderers == null || fClouds2D == null || fEclipse == null || mUpdateCb == null;
            Debug.Log(missing ? "[CaelusNearCloudShadow] EVE screen-space shadows not found (no EVE, or another version) - nothing to do"
                              : "[CaelusNearCloudShadow] EVE screen-space shadows found");
        }

        int fails;
        void LateUpdate()
        {
            if (missing) return;
            try { LateUpdateWork(); }
            catch (Exception e)
            {
                fails++;
                Debug.LogWarning("[CaelusNearCloudShadow] failed (" + fails + "/3): " + e.Message);
                try { GiveBack("error"); } catch (Exception) { }
                if (fails >= 3) { missing = true; Debug.LogWarning("[CaelusNearCloudShadow] switched off for this session - EVE's own cloud shadows are used"); }
            }
        }
        void LateUpdateWork()
        {
            if (!resolved) Resolve();
            if (missing) return;

            bool ours = HighLogic.LoadedSceneIsFlight && IsCaelusVessel(FlightGlobals.ActiveVessel);
            if (!ours && !CaelusSiteScope.Near()) { GiveBack("no CAELUS vessel active and not near our site"); return; }
            var mgr = fInstance.GetValue(null); if (mgr == null) return;
            bool off = Shader.GetGlobalFloat("_CaelusNearCloudShadowOff") > 0.5f;
            var lv = fLvMat != null ? fLvMat.GetValue(mgr) as Material : null;
            if (off && !wasOff) { Rebuild(mgr, lv); applied = false; wasOff = true; return; }
            wasOff = off;
            if (off) return;
            if (lv == null) { GiveBack("EVE light volume gone"); return; }
            applied = true;

            float s = Shader.GetGlobalFloat("_CaelusCloudShadowScale"); if (s <= 0f) s = CfgScale();
            int n = Rebuild(mgr, null);
            if (s < 0.999f) Lighten(mgr, 1f - s);
            if (!logged && n > 0) { logged = true; Debug.Log($"[CaelusNearCloudShadow] ground cloud shadows: 2D everywhere on {n} camera(s), light volume kept for the clouds, strength x{s:F2}"); }
        }

        static float cfgScale = -1f;
        static float CfgScale()
        {
            if (cfgScale >= 0f) return cfgScale;
            cfgScale = 0.4f;
            foreach (var n in GameDatabase.Instance.GetConfigNodes("CAELUS_NEARCLOUDSHADOW"))
            { float v; if (n.HasValue("shadowScale") && float.TryParse(n.GetValue("shadowScale"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) cfgScale = Mathf.Clamp01(v); }
            return cfgScale;
        }

        static FieldInfo fRenderCb; static Material lighten; static bool lightenTried; static int tempId;
        static void Lighten(object mgr, float a)
        {
            if (!lightenTried)
            {
                lightenTried = true;
                fRenderCb = rndType.GetField("renderingCommandBuffer", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var sh = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
                if (sh != null) lighten = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                tempId = Shader.PropertyToID("Temp screenspace shadows RT");
                Debug.Log("[CaelusNearCloudShadow] lighten pass " + (fRenderCb != null && lighten != null ? "ready (" + lighten.shader.name + ")" : "NOT available - ground shadows at EVE's full strength"));
            }
            if (fRenderCb == null || lighten == null) return;
            lighten.color = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
            var dict = fRenderers.GetValue(mgr) as System.Collections.IDictionary; if (dict == null) return;
            foreach (var r in dict.Values)
            {
                var cb = r != null ? fRenderCb.GetValue(r) as UnityEngine.Rendering.CommandBuffer : null; if (cb == null) continue;
                cb.Blit(Texture2D.whiteTexture, new UnityEngine.Rendering.RenderTargetIdentifier(tempId), lighten);
            }
        }

        static int Rebuild(object mgr, Material lv)
        {
            var dict = fRenderers.GetValue(mgr) as System.Collections.IDictionary; if (dict == null) return 0;
            var c2d = fClouds2D.GetValue(mgr); var ecl = fEclipse.GetValue(mgr); int n = 0;
            foreach (var r in dict.Values) if (r != null) { mUpdateCb.Invoke(r, new object[] { c2d, ecl, lv }); n++; }
            return n;
        }
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
