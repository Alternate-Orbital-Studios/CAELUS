using System;
using UnityEngine;

namespace Caelus
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class CaelusCameraFloor : MonoBehaviour
    {
        const float Margin = 1.5f, Probe = 600f, Step = 0.035f;

        public static bool on = false;
        int lifts, pushes; float lastLog, lastPushLog;
        readonly Collider[] hits = new Collider[16];

        void PushOutOfHull(FlightCamera fc)
        {
            var v = FlightGlobals.ActiveVessel; if (v == null) return;
            Vector3 p = fc.mainCamera.transform.position;
            int n = Physics.OverlapSphereNonAlloc(p, 0.4f, hits, 1 << 0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var part = hits[i].GetComponentInParent<Part>();
                if (part == null || part.vessel != v) continue;
                fc.SetDistanceImmediate(Mathf.Min(fc.Distance * 1.04f + 0.5f, fc.maxDistance));
                pushes++;
                if (Time.realtimeSinceStartup - lastPushLog > 5f) { lastPushLog = Time.realtimeSinceStartup; Debug.Log("[CaelusCameraFloor] camera inside " + part.partInfo.title + " - zoomed out to " + fc.Distance.ToString("F1") + " m (" + pushes + " frames)"); }
                return;
            }
        }

        static int guardState = -1;
        static bool GuardLoaded()
        {
            if (guardState < 0)
            {
                guardState = 0;
                foreach (var a in AssemblyLoader.loadedAssemblies) if (a != null && a.name == "CaelusSmoke") { guardState = 1; break; }
            }
            return guardState == 1;
        }

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

        void LateUpdate()
        {
            try
            {

                if (!on) return;
                bool ours = IsCaelusVessel(FlightGlobals.ActiveVessel);
                if (!ours && !CaelusSiteScope.Near()) return;
                if (CameraManager.Instance == null || CameraManager.Instance.currentCameraMode != CameraManager.CameraMode.Flight) return;
                var fc = FlightCamera.fetch; if (fc == null || fc.mainCamera == null) return;
                if (ours && !GuardLoaded()) PushOutOfHull(fc);
                var body = FlightGlobals.currentMainBody; if (body == null) return;
                Vector3 p = fc.mainCamera.transform.position;
                Vector3 up = (p - (Vector3)body.position).normalized;
                double alt = body.GetAltitude(p);
                if (alt > 20000) return;
                float below = float.NegativeInfinity;
                RaycastHit hit;
                if (Physics.Raycast(p + up * Probe, -up, out hit, Probe + 50f, 1 << 15, QueryTriggerInteraction.Ignore))
                    below = Probe - hit.distance;
                if (body.pqsController != null)
                {
                    double terr = body.pqsController.GetSurfaceHeight(body.GetRelSurfaceNVector(body.GetLatitude(p), body.GetLongitude(p))) - body.Radius;
                    if (body.ocean && terr < 0) terr = 0;
                    below = Mathf.Max(below, (float)(terr - alt));
                }
                if (below > -Margin)
                {
                    fc.camPitch = Mathf.Min(fc.camPitch + Step, fc.maxPitch);
                    lifts++;
                    if (Time.realtimeSinceStartup - lastLog > 5f) { lastLog = Time.realtimeSinceStartup; Debug.Log("[CaelusCameraFloor] camera was " + below.ToString("F1") + " m below the surface at alt " + alt.ToString("F0") + " m - lifted (" + lifts + " frames)"); }
                }
            }
            catch (Exception e) { if (Time.realtimeSinceStartup - lastLog > 30f) { lastLog = Time.realtimeSinceStartup; Debug.LogWarning("[CaelusCameraFloor] camera floor check failed: " + e); } }
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
