using System;
using System.Collections.Generic;
using UnityEngine;

namespace Caelus
{
    public class ModuleCaelusReleaseFrames : PartModule
    {
        [KSPField] public string frames = "D07_MRG05_01,D07_MRG06_01,D07_MRG07_01,D07_MRG08_01";
        [KSPField] public string arms = "D07_MRG01_01:D07_MRG05_01,D07_MRG02_01:D07_MRG05_01,D07_MRG03_01:D07_MRG07_01,D07_MRG04_01:D07_MRG07_01";
        [KSPField] public string proxyMaterial = "Unapproved interface proxy";
        [KSPField] public string petalPrefix = "Caelus.D08";

        [KSPField] public string orbiterPrefix = "CaelusCorona";
        [KSPField] public float openDeg = 40f, seconds = 6f, autoAltitude = 100000f, baseBand = 0.45f;
        [KSPField(isPersistant = true)] public bool opened = false;

        [KSPField] public bool jettison = true;
        [KSPField] public float jettisonDelay = 4f, driftSpeed = 1.6f, tumbleDeg = 5f, hideAfter = 50f;
        [KSPField(isPersistant = true)] public bool gone = false;
        readonly List<Vector3> radials = new List<Vector3>(); float tOpen = -1f, tDrift = 0f; bool collOff;

        readonly List<Transform> pivots = new List<Transform>(); readonly List<Vector3> axes = new List<Vector3>(); readonly List<float> signs = new List<float>(); readonly List<Quaternion> base0 = new List<Quaternion>();
        float f; bool built; float scan;

        public override void OnStart(StartState state)
        {
            HideProxies();
            if (!HighLogic.LoadedSceneIsFlight) return;
            try { Build(); } catch (Exception e) { Debug.LogWarning("[CaelusReleaseFrames] build failed: " + e.Message); }
            f = opened ? 1f : 0f; Apply();
            if (gone) foreach (Transform pv in pivots) if (pv != null) pv.gameObject.SetActive(false);
            Events["OpenFrames"].active = !opened;
        }

        void HideProxies()
        {
            int n = 0;
            foreach (MeshRenderer mr in part.GetComponentsInChildren<MeshRenderer>(true))
                if (mr.sharedMaterial != null && mr.sharedMaterial.name.StartsWith(proxyMaterial)) {
                    mr.enabled = false; n++;
                    if (mr.transform.childCount == 0 && mr.GetComponent<Collider>() == null) mr.gameObject.SetActive(false);
                }
            if (n > 0) Debug.Log("[CaelusReleaseFrames] " + part.partInfo.name + ": " + n + " interface-proxy boxes hidden");
        }

        Transform FindT(string name) { foreach (Transform t in part.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t; return null; }

        void Build()
        {
            if (built) return;
            Transform pt = part.transform; var armOf = new Dictionary<string, List<Transform>>();
            foreach (string pair in arms.Split(','))
            {
                string[] ab = pair.Split(':'); if (ab.Length != 2) continue; Transform a = FindT(ab[0].Trim()); if (a == null) continue;
                if (!armOf.ContainsKey(ab[1].Trim())) armOf[ab[1].Trim()] = new List<Transform>(); armOf[ab[1].Trim()].Add(a);
            }
            foreach (string fname in frames.Split(','))
            {
                Transform ft = FindT(fname.Trim()); if (ft == null) { Debug.LogWarning("[CaelusReleaseFrames] frame " + fname + " not found"); continue; }
                MeshFilter mf = ft.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                Vector3[] v = mf.sharedMesh.vertices; Matrix4x4 toPart = pt.worldToLocalMatrix * ft.localToWorldMatrix;
                float yMin = float.MaxValue, yMax = float.MinValue; Vector3 c = Vector3.zero;
                var pp = new Vector3[v.Length];
                for (int i = 0; i < v.Length; i++) { pp[i] = toPart.MultiplyPoint3x4(v[i]); yMin = Mathf.Min(yMin, pp[i].y); yMax = Mathf.Max(yMax, pp[i].y); c += pp[i]; }
                c /= Mathf.Max(1, v.Length);
                float phi = Mathf.Atan2(c.z, c.x); Vector3 radial = new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi));
                float rBase = 0f; foreach (Vector3 q in pp) if (q.y < yMin + baseBand) rBase = Mathf.Max(rBase, Vector3.Dot(new Vector3(q.x, 0f, q.z), radial));
                Vector3 hinge = radial * rBase + Vector3.up * yMin;
                Vector3 axis = Vector3.Cross(Vector3.up, radial).normalized;

                Vector3 top = radial * (rBase - 1f) + Vector3.up * yMax;
                Vector3 test = hinge + Quaternion.AngleAxis(10f, axis) * (top - hinge);
                float sign = Vector3.Dot(new Vector3(test.x, 0f, test.z), radial) > Vector3.Dot(new Vector3(top.x, 0f, top.z), radial) ? 1f : -1f;
                var go = new GameObject("CaelusReleaseHinge_" + fname.Trim()); Transform pv = go.transform;
                pv.SetParent(ft.parent, false); pv.position = pt.TransformPoint(hinge); pv.rotation = pt.rotation;
                ft.SetParent(pv, true);
                if (armOf.ContainsKey(fname.Trim())) foreach (Transform a in armOf[fname.Trim()]) a.SetParent(pv, true);
                pivots.Add(pv); axes.Add(axis); signs.Add(sign); base0.Add(pv.localRotation); radials.Add(radial);
                Debug.Log("[CaelusReleaseFrames] " + fname.Trim() + ": hinge r " + rBase.ToString("F2") + " y " + yMin.ToString("F2") + " az " + (phi * Mathf.Rad2Deg).ToString("F0") + " top y " + yMax.ToString("F2") + " sign " + sign);
            }
            built = true;
        }

        void Apply()
        {
            float t = Mathf.Clamp01(f), e = t * t * t * (t * (t * 6f - 15f) + 10f);
            if (tDrift > 0f) return;
            for (int i = 0; i < pivots.Count; i++) if (pivots[i] != null) pivots[i].localRotation = base0[i] * Quaternion.AngleAxis(signs[i] * openDeg * e, axes[i]);
        }

        public void Update()
        {
            if (!HighLogic.LoadedSceneIsFlight || !built) return;
            if (!opened && vessel != null && Time.time > scan)
            {
                scan = Time.time + 1f; bool petals = false;
                foreach (Part p in vessel.parts) if (p.partInfo != null && p.partInfo.name.StartsWith(petalPrefix)) { petals = true; break; }
                bool orbiter = false; foreach (Part p in vessel.parts) if (p.partInfo != null && p.partInfo.name.StartsWith(orbiterPrefix)) { orbiter = true; break; }
                if (!petals && !orbiter && vessel.altitude > autoAltitude) { Debug.Log("[CaelusReleaseFrames] " + vessel.vesselName + ": Corona gone - opening the release frames"); OpenFrames(); }
            }
            float target = opened ? 1f : 0f;
            if (f != target) { f = Mathf.MoveTowards(f, target, Time.deltaTime / Mathf.Max(0.1f, seconds)); Apply(); }
            if (jettison && opened && !gone && f >= 1f)
            {
                if (tOpen < 0f) tOpen = Time.time;
                if (Time.time - tOpen > jettisonDelay)
                {
                    if (!collOff) { foreach (Transform pv in pivots) if (pv != null) foreach (Collider c in pv.GetComponentsInChildren<Collider>(true)) c.enabled = false; collOff = true;
                                    Debug.Log("[CaelusReleaseFrames] " + vessel.vesselName + ": release frames jettisoned"); }
                    float dt = Time.deltaTime; tDrift += dt; Transform pt = part.transform;
                    for (int i = 0; i < pivots.Count; i++)
                    {
                        if (pivots[i] == null) continue;
                        pivots[i].position += pt.TransformDirection(radials[i]) * driftSpeed * dt + pt.up * 0.15f * driftSpeed * dt;
                        pivots[i].rotation = Quaternion.AngleAxis(signs[i] * tumbleDeg * dt, pt.TransformDirection(axes[i])) * pivots[i].rotation;
                    }
                    if (tDrift > hideAfter) { foreach (Transform pv in pivots) if (pv != null) pv.gameObject.SetActive(false); gone = true; Debug.Log("[CaelusReleaseFrames] " + vessel.vesselName + ": jettisoned frames out of sight"); }
                }
            }
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Open release frames")]
        public void OpenFrames() { opened = true; Events["OpenFrames"].active = false; }
        [KSPAction("Open release frames")] public void OpenAction(KSPActionParam p) { OpenFrames(); }
    }
}
