using System;
using System.Collections.Generic;
using UnityEngine;

namespace Caelus
{

    public class CaelusPadArms : MonoBehaviour
    {
        const string PivotName = "SERVICE_ARM_R3_SWING_PIVOT_CONTROL_ART_ZERO", ArmPrefix = "CLD_R9_SERVICE_ARM_COPY_";
        const float SwingDeg = 80f, SwingSeconds = 4.0f, Stagger = 0.25f, Latch = 0.4f, MinTris = 150;

        class Arm { public Transform pivot; public Quaternion rest; public float delay; }
        class Pad { public Transform frame, launchpad; public List<Arm> arms = new List<Arm>(); public double liftUT = -1; }
        readonly List<Pad> pads = new List<Pad>(); readonly HashSet<int> done = new HashSet<int>();
        float nextScan;

        void Update()
        {
            try
            {

                pads.RemoveAll(pd => pd.frame == null);
                if (Time.realtimeSinceStartup >= nextScan && pads.Count == 0) { nextScan = Time.realtimeSinceStartup + 5f; done.Clear(); Scan(); }
                Drive();
            }
            catch (Exception e) { Debug.LogWarning("[CaelusPadArms] " + e); nextScan = Time.realtimeSinceStartup + 60f; }
        }

        void Scan()
        {
            foreach (Transform t in FindObjectsOfType<Transform>())
            {
                if (t.name != PivotName || t.parent == null || !t.parent.name.StartsWith(ArmPrefix)) continue;
                Transform gantry = t.parent.parent; if (gantry == null || gantry.parent == null) continue;
                Transform frame = gantry.parent;
                if (done.Contains(frame.GetInstanceID())) continue;
                done.Add(frame.GetInstanceID());
                Split(frame);
            }
        }

        void Split(Transform frame)
        {
            var pad = new Pad { frame = frame };
            foreach (Transform t in frame.GetComponentsInChildren<Transform>(true)) if (t.name == "_launchpad") { pad.launchpad = t; break; }
            if (pad.launchpad == null) return;
            Vector3 axisL = frame.InverseTransformPoint(pad.launchpad.position);
            var markers = new List<Transform>();
            foreach (Transform t in frame.GetComponentsInChildren<Transform>(true)) if (t.name == PivotName && t.parent != null && t.parent.name.StartsWith(ArmPrefix)) markers.Add(t);
            markers.Sort((a, b) => frame.InverseTransformPoint(a.position).y.CompareTo(frame.InverseTransformPoint(b.position).y));
            var filters = new List<MeshFilter>();
            foreach (MeshFilter mf in frame.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null && mf.sharedMesh.isReadable && mf.GetComponent<MeshRenderer>() != null && !mf.name.EndsWith("_COL")) filters.Add(mf);
            int k = 0;
            foreach (Transform mk in markers)
            {
                Vector3 p = frame.InverseTransformPoint(mk.position);
                Vector2 toAxis = new Vector2(axisL.x - p.x, axisL.z - p.z); float dist = toAxis.magnitude; if (dist < 6f) continue;
                Vector2 dir = toAxis / dist, perp = new Vector2(-dir.y, dir.x);
                Func<Vector3, bool> inside = q =>
                {
                    Vector2 h = new Vector2(q.x - p.x, q.z - p.z); float along = Vector2.Dot(h, dir), side = Vector2.Dot(h, perp);
                    return along > 0.5f && along < dist - 4f && Mathf.Abs(side) < 3f && q.y > p.y - 6f && q.y < p.y + 4f;
                };
                var pivotGo = new GameObject("CaelusPadArmPivot_" + mk.parent.name); pivotGo.transform.SetParent(frame, false);
                pivotGo.transform.position = mk.position; pivotGo.transform.rotation = frame.rotation;
                int tris = 0;
                foreach (MeshFilter mf in filters) tris += Cut(mf, frame, inside, pivotGo.transform);
                if (tris < MinTris) { Destroy(pivotGo); continue; }
                pad.arms.Add(new Arm { pivot = pivotGo.transform, rest = pivotGo.transform.localRotation, delay = k++ * Stagger });
                Debug.Log("[CaelusPadArms] " + mk.parent.name + ": " + tris + " triangles split off at height " + p.y.ToString("F1") + " m");
            }
            if (pad.arms.Count > 0) pads.Add(pad);
        }

        int Cut(MeshFilter mf, Transform frame, Func<Vector3, bool> inside, Transform pivot)
        {
            Mesh m = mf.sharedMesh; Vector3[] v = m.vertices; bool[] inV = new bool[v.Length]; bool any = false;
            Matrix4x4 toFrame = frame.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            for (int i = 0; i < v.Length; i++) { inV[i] = inside(toFrame.MultiplyPoint3x4(v[i])); any |= inV[i]; }
            if (!any) return 0;
            var keepSubs = new List<int[]>(); var cutSubs = new List<int[]>(); int cut = 0;
            for (int s = 0; s < m.subMeshCount; s++)
            {
                int[] t = m.GetTriangles(s); var keep = new List<int>(t.Length); var take = new List<int>();
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    if (inV[t[i]] && inV[t[i + 1]] && inV[t[i + 2]]) { take.Add(t[i]); take.Add(t[i + 1]); take.Add(t[i + 2]); }
                    else { keep.Add(t[i]); keep.Add(t[i + 1]); keep.Add(t[i + 2]); }
                }
                keepSubs.Add(keep.ToArray()); cutSubs.Add(take.ToArray()); cut += take.Count / 3;
            }
            if (cut == 0) return 0;
            Mesh rest = Instantiate(m); rest.name = m.name + "_noarms";
            for (int s = 0; s < keepSubs.Count; s++) rest.SetTriangles(keepSubs[s], s);
            mf.sharedMesh = rest;
            Mesh piece = Instantiate(m); piece.name = m.name + "_arm";
            for (int s = 0; s < cutSubs.Count; s++) piece.SetTriangles(cutSubs[s], s);
            var go = new GameObject(mf.name + "_arm"); go.layer = mf.gameObject.layer;
            go.transform.SetParent(pivot, false);
            go.transform.position = mf.transform.position; go.transform.rotation = mf.transform.rotation; go.transform.localScale = Vector3.one;
            Vector3 ls = mf.transform.lossyScale, ps = pivot.lossyScale;
            go.transform.localScale = new Vector3(ls.x / ps.x, ls.y / ps.y, ls.z / ps.z);
            go.AddComponent<MeshFilter>().sharedMesh = piece;
            var src = mf.GetComponent<MeshRenderer>(); var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = src.sharedMaterials; mr.shadowCastingMode = src.shadowCastingMode; mr.receiveShadows = src.receiveShadows;
            return cut;
        }

        void Drive()
        {
            Vessel v = FlightGlobals.ActiveVessel; double ut = Planetarium.GetUniversalTime();
            foreach (Pad pad in pads)
            {
                if (pad.frame == null || pad.launchpad == null) continue;
                if (v != null && Vector3.Distance(v.transform.position, pad.launchpad.position) < 300f)
                {
                    bool lit = false;
                    foreach (Part p in v.parts)
                    {
                        foreach (PartModule pm in p.Modules) { var en = pm as ModuleEngines; if (en != null && en.EngineIgnited && en.finalThrust > 1f) { lit = true; break; } }
                        if (lit) break;
                    }
                    double hAbove = v.altitude - FlightGlobals.getAltitudeAtPos(pad.launchpad.position);
                    if (pad.liftUT < 0 && (lit || hAbove > 2.0)) pad.liftUT = ut;
                    else if (pad.liftUT >= 0 && !lit && v.situation == Vessel.Situations.PRELAUNCH && hAbove < 2.0) pad.liftUT = -1;
                }
                foreach (Arm a in pad.arms)
                {
                    if (a.pivot == null) continue;
                    float t = pad.liftUT < 0 ? 0f : Mathf.Clamp01((float)(ut - pad.liftUT - Latch - a.delay) / SwingSeconds);
                    float f = t * t * t * (t * (t * 6f - 15f) + 10f);
                    a.pivot.localRotation = a.rest * Quaternion.AngleAxis(SwingDeg * f, Vector3.up);
                }
            }
        }
    }
}
