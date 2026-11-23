using System;
using System.Collections.Generic;
using UnityEngine;

namespace Caelus
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class CaelusSiteLights : MonoBehaviour
    {
        static readonly string[] LampMats = { "FLOOD_LENS", "R13_LAMP", "LIGHT_LENS", "HW_LENS", "HW_BEACON" };

        static readonly Color Warm = new Color(1f, 0.9f, 0.76f, 1f);
        const float ScanEvery = 10f, UpdateEvery = 1f, SpotIntensity = 4f, SpotAngle = 46f, StackTop = 115f;

        class Lamp { public Renderer r; public Material[] mats, orig; public Light[] spots; }
        readonly Dictionary<int, Lamp> lamps = new Dictionary<int, Lamp>();
        readonly HashSet<int> notLamps = new HashSet<int>();
        readonly List<Material> created = new List<Material>();
        Shader emissive; float nextScan, nextUpdate, farScan; bool logged;

        void Start()
        {
            emissive = Shader.Find("KSP/Emissive/Specular");
            if (emissive == null) Debug.LogWarning("[CaelusSiteLights] shader KSP/Emissive/Specular not found - lamp lenses will not glow (spot lights still work)");
        }

        void Update()
        {
            float t = Time.realtimeSinceStartup;
            try
            {

                if (t >= nextScan) { bool near = SiteNear(); nextScan = t + (near ? ScanEvery : 60f); if (near || t >= farScan) { farScan = t + 60f; Scan(); } }
                if (t >= nextUpdate) { nextUpdate = t + UpdateEvery; Apply(); }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CaelusSiteLights] lamp update failed, retrying in 60 s: " + e);
                nextScan = nextUpdate = t + 60f;
            }
        }

        static bool SiteNear()
        {
            var fc = FlightCamera.fetch; if (fc == null || fc.mainCamera == null || FlightGlobals.Bodies == null) return false;
            Vector3 c = fc.mainCamera.transform.position;
            foreach (var b in FlightGlobals.Bodies)
            {
                if (b == null) continue;
                if (NearIn(b.transform, c)) return true;
                if (b.pqsController != null && b.pqsController.transform != b.transform && NearIn(b.pqsController.transform, c)) return true;
            }
            return false;
        }
        static bool NearIn(Transform t, Vector3 c)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                var g = t.GetChild(i);
                if (g != null && g.gameObject.activeInHierarchy && g.name.StartsWith("StatioAromata", StringComparison.Ordinal) && (g.position - c).sqrMagnitude < 25000f * 25000f) return true;
            }
            return false;
        }

        static bool IsLamp(Material m)
        {
            if (m == null) return false;
            foreach (var k in LampMats) if (m.name.IndexOf(k, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        void Scan()
        {
            int added = 0, spots = 0;
            foreach (var r in FindObjectsOfType<MeshRenderer>())
            {
                if (r.gameObject.layer != 15 && !r.name.StartsWith("HW") && !r.name.Contains("_HW_")) continue;
                int id = r.GetInstanceID();
                if (lamps.ContainsKey(id) || notLamps.Contains(id)) continue;
                var shared = r.sharedMaterials; bool any = false, flood = false; var orig = (Material[])shared.Clone();
                foreach (var m in shared) if (IsLamp(m)) { any = true; flood |= m.name.Contains("FLOOD_LENS"); }

                if (any && r.name.Contains("FLOODS_LENSES")) flood = true;
                if (!any) { notLamps.Add(id); continue; }

                var lampMats = new List<Material>();
                for (int i = 0; i < shared.Length; i++)
                {
                    if (!IsLamp(shared[i])) continue;
                    var m = new Material(shared[i]);
                    if (emissive != null && !m.shader.name.Contains("Emissive")) m.shader = emissive;
                    m.SetColor("_EmissiveColor", Color.black);
                    shared[i] = m; lampMats.Add(m); created.Add(m);
                }
                r.sharedMaterials = shared;
                var lamp = new Lamp { r = r, mats = lampMats.ToArray(), orig = orig, spots = new Light[0] };
                if (flood) { lamp.spots = MakeSpots(r); spots += lamp.spots.Length; }
                lamps[id] = lamp; added++;
            }
            if (added > 0 || !logged) { logged = true; Debug.Log("[CaelusSiteLights] lamp renderers +" + added + " (total " + lamps.Count + "), spot lights +" + spots); }
        }

        Light[] MakeSpots(Renderer r)
        {
            var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) return new Light[0];
            Transform pad = null;
            if (r.transform.parent != null)
                foreach (var tr in r.transform.parent.GetComponentsInChildren<Transform>(true)) if (tr.name == "_launchpad") { pad = tr; break; }
            var verts = mf.sharedMesh.vertices;
            var centres = new List<Vector3>(); var counts = new List<int>();
            for (int i = 0; i < verts.Length; i += 7)
            {
                Vector3 p = verts[i]; int k = -1;
                for (int c = 0; c < centres.Count; c++) if ((centres[c] - p).sqrMagnitude < 36f) { k = c; break; }
                if (k < 0) { centres.Add(p); counts.Add(1); }
                else { centres[k] = (centres[k] * counts[k] + p) / (counts[k] + 1); counts[k]++; }
            }

            foreach (Transform ch in r.transform.GetComponentsInChildren<Transform>(true))
                if (ch != r.transform && ch.name == "CaelusSiteLight") Destroy(ch.gameObject);
            var lights = new List<Light>();
            foreach (var cLocal in centres)
            {
                Vector3 pos = r.transform.TransformPoint(cLocal);
                Vector3 up = (pos - (Vector3)FlightGlobals.currentMainBody.position).normalized;

                Vector3 dir; float angle, far;
                if (pad != null)
                {
                    Vector3 dB = (pad.position + up * 2f - pos), dT = (pad.position + up * StackTop - pos);
                    dir = (dB.normalized + dT.normalized).normalized;
                    angle = Mathf.Clamp(Vector3.Angle(dB, dT) + 12f, 24f, 120f); far = Mathf.Max(dB.magnitude, dT.magnitude);
                }
                else { dir = -up; angle = SpotAngle; far = 30f; }
                var go = new GameObject("CaelusSiteLight");
                go.transform.SetParent(r.transform, false);
                go.transform.position = pos;
                go.transform.rotation = Quaternion.LookRotation(dir, up);
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot; l.spotAngle = angle; l.color = Warm; l.intensity = 0f;
                l.range = Mathf.Max(60f, far * 1.8f); l.shadows = LightShadows.None;

                l.cullingMask = ~((1 << 16) | (1 << 20));

                l.renderMode = LightRenderMode.ForcePixel;
                l.enabled = false;
                lights.Add(l);
            }
            return lights.ToArray();
        }

        void Apply()
        {
            var sun = Planetarium.fetch != null ? Planetarium.fetch.Sun : null;
            var body = FlightGlobals.currentMainBody;
            if (sun == null || body == null) return;
            List<int> dead = null;
            foreach (var kv in lamps)
            {
                var lamp = kv.Value;
                if (lamp.r == null) { (dead ?? (dead = new List<int>())).Add(kv.Key); continue; }
                if (!lamp.r.gameObject.activeInHierarchy) continue;
                Vector3 pos = lamp.r.bounds.center;
                Vector3 up = (pos - (Vector3)body.position).normalized;
                float elev = Mathf.Asin(Mathf.Clamp(Vector3.Dot(up, ((Vector3)sun.position - pos).normalized), -1f, 1f)) * Mathf.Rad2Deg;
                float night = Mathf.Clamp01((1f - elev) / 5f);

                Material[] mats = lamp.mats;
                if (lamp.r.gameObject.layer != 15)
                {
                    var cur = lamp.r.sharedMaterials; var found = new List<Material>();
                    foreach (var m in cur) if (IsLamp(m)) { if (emissive != null && !m.shader.name.Contains("Emissive")) m.shader = emissive; found.Add(m); }
                    mats = found.ToArray();
                }
                foreach (var m in mats)
                {
                    if (m == null) continue;
                    if (m.name.IndexOf("HW_BEACON", StringComparison.Ordinal) >= 0) { Color c = m.color; c.a = 1f; m.SetColor("_EmissiveColor", c * (2.4f * night)); }
                    else m.SetColor("_EmissiveColor", Warm * (1.4f * night));
                }
                foreach (var l in lamp.spots) if (l != null) { l.enabled = night > 0.01f; l.intensity = SpotIntensity * night; }
            }
            if (dead != null) foreach (var k in dead) lamps.Remove(k);
        }

        void OnDestroy()
        {
            foreach (var lamp in lamps.Values)
            {
                if (lamp.r != null && lamp.orig != null && lamp.r.gameObject.layer == 15) lamp.r.sharedMaterials = lamp.orig;
                if (lamp.spots != null) foreach (var l in lamp.spots) if (l != null) Destroy(l.gameObject);
            }
            lamps.Clear();
            foreach (var m in created) if (m != null) Destroy(m); created.Clear();
        }
    }
}
