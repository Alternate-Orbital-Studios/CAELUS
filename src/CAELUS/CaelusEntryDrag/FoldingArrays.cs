using System;
using System.Collections.Generic;
using UnityEngine;

namespace Caelus
{
    public class ModuleCaelusFoldingArrays : PartModule
    {
        [KSPField] public float hingeX = 1.57f;
        [KSPField] public float bandLow = 2.25f, bandHigh = 2.50f;
        [KSPField] public float hingeY = 2.37f;
        [KSPField] public float midX = 3.17f;
        [KSPField] public float foldDeg = 90f, outerFoldDeg = 175f, seconds = 6f, autoAltitude = 70000f;
        [KSPField] public string coverPrefix = "Caelus.D09";

        [KSPField] public string stackPrefixes = "Caelus.D05,Caelus.D06,Caelus.D07,Caelus.AquilaAdapter,Caelus.D08";
        [KSPField] public string landerPrefix = "RomaAeterna.Aquila";
        [KSPField] public float freeSeconds = 1800f;

        [KSPField] public float clearDistance = 25f, clearSeconds = 180f;
        [KSPField] public string autoMode = "afterDock";
        [KSPField(isPersistant = true)] public bool stackSeen = false;
        float freeSince = -1f;
        [KSPField(isPersistant = true)] public bool deployed = false;

        readonly List<Transform> pivots = new List<Transform>(), outers = new List<Transform>(); readonly List<float> sides = new List<float>();
        float f; ModuleDeployableSolarPanel solar; bool split; float scan;

        public override void OnStart(StartState state)
        {
            solar = part.FindModuleImplementing<ModuleDeployableSolarPanel>();
            try { Split(); } catch (Exception e) { Debug.LogWarning("[CaelusFoldingArrays] split failed: " + e.Message); }
            f = deployed ? 1f : 0f; Apply(); Events["DeployArrays"].active = !deployed; Events["StowArrays"].active = deployed;
        }

        void Split()
        {
            Transform model = part.transform.Find("model"); if (model == null || split) return;
            var filters = new List<MeshFilter>();
            foreach (MeshFilter mf in model.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null && mf.sharedMesh.isReadable && mf.GetComponent<MeshRenderer>() != null && mf.gameObject.layer == 0) filters.Add(mf);
            foreach (float sd in new[] { -1f, 1f })
            {
                float s = sd;
                var pivotGo = new GameObject("CaelusArrayHinge" + (s < 0 ? "L" : "R")); Transform pv = pivotGo.transform;
                pv.SetParent(model, false); pv.position = part.transform.TransformPoint(new Vector3(s * hingeX, hingeY, 0f)); pv.rotation = part.transform.rotation;
                var outerGo = new GameObject("CaelusArrayMidHinge" + (s < 0 ? "L" : "R")); Transform po = outerGo.transform;
                po.SetParent(pv, false); po.position = part.transform.TransformPoint(new Vector3(s * midX, hingeY, 0f)); po.rotation = part.transform.rotation;
                int tris = 0;
                foreach (MeshFilter mf in filters)
                    tris += Cut(mf, q => s * q.x > hingeX - 0.005f && q.y > bandLow && q.y < bandHigh, c => s * c.x > midX ? 1 : 0, new[] { pv, po });
                if (tris == 0) { Destroy(pivotGo); continue; }
                pivots.Add(pv); outers.Add(po); sides.Add(s);
                Debug.Log("[CaelusFoldingArrays] " + part.partInfo.name + " wing " + (s < 0 ? "L" : "R") + ": " + tris + " triangles on the hinges");
            }
            split = true;
        }

        int Cut(MeshFilter mf, Func<Vector3, bool> inside, Func<Vector3, int> which, Transform[] targets)
        {
            Mesh m = mf.sharedMesh; Vector3[] v = m.vertices; Vector3[] pp = new Vector3[v.Length]; bool[] inV = new bool[v.Length]; bool any = false;
            Matrix4x4 toPart = part.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            for (int i = 0; i < v.Length; i++) { pp[i] = toPart.MultiplyPoint3x4(v[i]); inV[i] = inside(pp[i]); any |= inV[i]; }
            if (!any) return 0;
            int nT = targets.Length; var keepSubs = new List<int[]>(); var cutSubs = new List<int>[nT][]; int cut = 0;
            for (int k = 0; k < nT; k++) cutSubs[k] = new List<int>[m.subMeshCount];
            for (int sub = 0; sub < m.subMeshCount; sub++)
            {
                int[] t = m.GetTriangles(sub); var keep = new List<int>(t.Length);
                for (int k = 0; k < nT; k++) cutSubs[k][sub] = new List<int>();
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    if (inV[t[i]] && inV[t[i + 1]] && inV[t[i + 2]])
                    {
                        int k = Mathf.Clamp(which((pp[t[i]] + pp[t[i + 1]] + pp[t[i + 2]]) / 3f), 0, nT - 1);
                        cutSubs[k][sub].Add(t[i]); cutSubs[k][sub].Add(t[i + 1]); cutSubs[k][sub].Add(t[i + 2]); cut++;
                    }
                    else { keep.Add(t[i]); keep.Add(t[i + 1]); keep.Add(t[i + 2]); }
                }
                keepSubs.Add(keep.ToArray());
            }
            if (cut == 0) return 0;
            Mesh rest = Instantiate(m); rest.name = m.name + "_nowing";
            for (int sub = 0; sub < keepSubs.Count; sub++) rest.SetTriangles(keepSubs[sub], sub);
            mf.sharedMesh = rest;
            var src = mf.GetComponent<MeshRenderer>();
            for (int k = 0; k < nT; k++)
            {
                bool has = false; for (int sub = 0; sub < m.subMeshCount; sub++) has |= cutSubs[k][sub].Count > 0;
                if (!has) continue;
                Mesh piece = Instantiate(m); piece.name = m.name + "_wing" + k;
                for (int sub = 0; sub < m.subMeshCount; sub++) piece.SetTriangles(cutSubs[k][sub].ToArray(), sub);
                var go = new GameObject(mf.name + "_wing" + k); go.layer = mf.gameObject.layer;
                go.transform.SetParent(targets[k], false);
                go.transform.position = mf.transform.position; go.transform.rotation = mf.transform.rotation;
                Vector3 ls = mf.transform.lossyScale, ps = targets[k].lossyScale;
                go.transform.localScale = new Vector3(ls.x / ps.x, ls.y / ps.y, ls.z / ps.z);
                go.AddComponent<MeshFilter>().sharedMesh = piece;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = src.sharedMaterials; mr.shadowCastingMode = src.shadowCastingMode; mr.receiveShadows = src.receiveShadows;
            }
            return cut;
        }

        void Apply()
        {
            float t = Mathf.Clamp01(f), e = t * t * t * (t * (t * 6f - 15f) + 10f);
            for (int i = 0; i < pivots.Count; i++)
            {
                if (pivots[i] != null) pivots[i].localRotation = Quaternion.AngleAxis(-sides[i] * foldDeg * (1f - e), Vector3.forward);
                if (outers[i] != null) outers[i].localRotation = Quaternion.AngleAxis(sides[i] * outerFoldDeg * (1f - e), Vector3.forward);
            }
            if (solar != null) { bool on = HighLogic.LoadedSceneIsFlight && f >= 0.999f; if (solar.isEnabled != on) { solar.isEnabled = on; solar.enabled = on; } }
        }

        public void Update()
        {
            if (!split) return;
            if (HighLogic.LoadedSceneIsFlight && vessel != null && !deployed && Time.time > scan)
            {
                scan = Time.time + 1f;
                bool cover = false, stack = false, lander = false; string[] sp = stackPrefixes.Split(',');
                foreach (Part p in vessel.parts)
                {
                    if (p.partInfo == null) continue; string n = p.partInfo.name;
                    if (n.StartsWith(coverPrefix)) cover = true;
                    if (n.StartsWith(landerPrefix)) lander = true;
                    foreach (string q in sp) if (q.Length > 0 && n.StartsWith(q.Trim())) { stack = true; break; }
                }
                if (stack) stackSeen = true;
                if (autoMode == "altitude" || !stackSeen) { if (vessel.altitude > autoAltitude && !cover) DeployArrays(); }
                else if (stack || cover) freeSince = -1f;
                else
                {
                    if (freeSince < 0f) freeSince = Time.time;
                    float near = NearestStack(sp);
                    bool clearOk = near > clearDistance || Time.time - freeSince > clearSeconds;
                    if ((lander && clearOk) || Time.time - freeSince > freeSeconds) { Debug.Log("[CaelusFoldingArrays] " + vessel.vesselName + ": clear of the stack" + (lander ? " with the lander, stage " + NearestStack(stackPrefixes.Split(',')).ToString("F0") + " m away" : " for " + freeSeconds + " s") + " - deploying the arrays"); DeployArrays(); }
                }
            }
            float target = deployed ? 1f : 0f;
            if (f != target) { f = Mathf.MoveTowards(f, target, Time.deltaTime / Mathf.Max(0.1f, seconds)); Apply(); }
        }

        float NearestStack(string[] sp)
        {
            float best = float.MaxValue;
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                if (v == null || v == vessel) continue; bool has = false;
                foreach (Part p in v.parts) { if (p.partInfo == null) continue; foreach (string q in sp) if (q.Length > 0 && p.partInfo.name.StartsWith(q.Trim())) { has = true; break; } if (has) break; }
                if (has) best = Mathf.Min(best, (float)(v.GetWorldPos3D() - vessel.GetWorldPos3D()).magnitude);
            }
            return best;
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Deploy solar arrays")]
        public void DeployArrays() { deployed = true; Events["DeployArrays"].active = false; Events["StowArrays"].active = true; }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Stow solar arrays")]
        public void StowArrays() { deployed = false; Events["DeployArrays"].active = true; Events["StowArrays"].active = false; }

        [KSPAction("Deploy solar arrays")] public void DeployAction(KSPActionParam p) { DeployArrays(); }
        [KSPAction("Stow solar arrays")] public void StowAction(KSPActionParam p) { StowArrays(); }
        [KSPAction("Toggle solar arrays")] public void ToggleAction(KSPActionParam p) { if (deployed) StowArrays(); else DeployArrays(); }
    }
}
