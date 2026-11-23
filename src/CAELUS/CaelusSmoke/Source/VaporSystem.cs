using System;
using System.Collections.Generic;
using UnityEngine;

namespace CaelusSmoke
{
    public static class VaporSettings
    {
        public static bool enabled = true;
        public static float humidity = 1f;
        public static float strength = 1f;
        public static float sigma = 1.2f;
        public static int resDivisor = 2;
        public static int steps = 64;
        public static bool debugLog = false;

        public static void Load()
        {
            foreach (ConfigNode s in GameDatabase.Instance.GetConfigNodes("CAELUS_VAPOR"))
            {
                s.TryGetValue("enabled", ref enabled); s.TryGetValue("humidity", ref humidity); s.TryGetValue("strength", ref strength); s.TryGetValue("sigma", ref sigma);
                s.TryGetValue("resDivisor", ref resDivisor); s.TryGetValue("steps", ref steps); s.TryGetValue("debugLog", ref debugLog);
            }
        }
    }

    public class VaporSystem
    {
        public readonly Material mat;
        public bool active;
        readonly float[] prof = new float[VaporModel.NPROF];
        readonly VaporCollar[] cols = new VaporCollar[VaporModel.MAXK];
        readonly Vector4[] colV = new Vector4[VaporModel.MAXK], eggV = new Vector4[VaporModel.MAXK];
        Vessel profVessel; int profKey = -1; float profLen, noseY;
        float logT;

        const float BIN = 0.5f, Y0 = -200f; const int NBIN = 800, SECT = 16;
        readonly float[] cell = new float[NBIN * SECT];

        readonly List<MeshFilter> pend = new List<MeshFilter>(); int pendIdx = -1; Vessel pendVessel; int pendKey; long pendSamples; int pendMeshes; double pendMs;
        readonly List<Vector3> vbuf = new List<Vector3>(); readonly List<int> ibuf = new List<int>();

        public VaporSystem(Shader s, Texture3D noise)
        {
            mat = new Material(s) { name = "CaelusVapor" };
            mat.SetTexture("_Noise", noise);
        }
        public void Destroy() { if (mat != null) UnityEngine.Object.Destroy(mat); }

        public bool Prepare(Vessel v, double ut)
        {
            active = false;
            if (!VaporSettings.enabled || v == null || v.packed || v.rootPart == null || v.mainBody == null || !v.mainBody.atmosphere) return false;
            float M = (float)v.mach, alt = (float)v.altitude;
            if (alt > 16000f || M > 1.6f) return false;
            int key = PartKey(v);
            if ((v != profVessel || key != profKey) && (pendIdx < 0 || pendVessel != v || pendKey != key)) StartBuild(v, key);
            if (pendIdx >= 0) StepBuild(3.0);
            if (v != profVessel) return false;
            float I = VaporModel.MachWindow(M) * VaporModel.Humidity(alt, VaporSettings.humidity) * VaporSettings.strength;

            if (!v.directSunlight) I *= 0.12f;
            if (I < 0.01f || profLen < 2f) return false;
            int n = VaporModel.FindCollars(prof, profLen, M, cols);
            if (n == 0) return false;

            float tf = (float)(ut % 1000.0);
            for (int i = 0; i < VaporModel.MAXK; i++)
            {
                colV[i] = i < n ? new Vector4(cols[i].x, cols[i].r, cols[i].strength, cols[i].length) : Vector4.zero;
                float fl = Mathf.Clamp01(0.35f + 0.9f * Mathf.PerlinNoise(tf * 2.2f, 3.7f + i * 5.3f));
                eggV[i] = i < n ? new Vector4(cols[i].height, cols[i].shell, fl, 0f) : Vector4.zero;
            }
            Transform root = v.rootPart.transform;
            mat.SetVector("_VNose", root.position + root.up * noseY);
            mat.SetVector("_VAxis", -root.up);
            mat.SetVector("_VB1", root.right); mat.SetVector("_VB2", root.forward);
            mat.SetFloat("_VLen", profLen); mat.SetFloatArray("_VProf", prof);
            mat.SetInt("_VCount", n); mat.SetVectorArray("_VCol", colV); mat.SetVectorArray("_VEgg", eggV);
            mat.SetFloat("_VI", I); mat.SetFloat("_VTime", (float)(ut % 1000.0));
            mat.SetFloat("_VSigma", VaporSettings.sigma); mat.SetInt("_Steps", VaporSettings.steps);
            if (VaporSettings.debugLog && (logT -= Time.unscaledDeltaTime) < 0f)
            {
                logT = 2f;
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < n; i++) sb.Append($" [x {cols[i].x:F1} r {cols[i].r:F1} turn {cols[i].turnDeg:F0} s {cols[i].strength:F2} L {cols[i].length:F1} H {cols[i].height:F1} shell {cols[i].shell:F2}]");
                Debug.Log($"[CaelusVapor] mach {M:F2} alt {alt:F0} intensity {I:F2} collars {n}:{sb}");
            }
            active = true;
            return true;
        }

        public bool EnsureProfile(Vessel v)
        {
            if (v == null || v.packed || v.rootPart == null) return false;
            int key = PartKey(v);
            if ((v != profVessel || key != profKey) && (pendIdx < 0 || pendVessel != v || pendKey != key)) StartBuild(v, key);
            if (pendIdx >= 0) StepBuild(3.0);
            return v == profVessel && profLen >= 2f;
        }
        public Vector3 NoseWorld(Vessel v) { Transform root = v.rootPart.transform; return root.position + root.up * noseY; }
        public void ApplyProfile(Material m) { m.SetFloat("_VLen", profLen); m.SetFloatArray("_VProf", prof); }

        public void SetLight(Vector3 sunDir, Vector3 sunColor, Vector3 sky)
        {
            mat.SetVector("_SunDir", sunDir); mat.SetVector("_SunColor", sunColor); mat.SetVector("_AmbientSky", sky);
        }

        static int PartKey(Vessel v) { int k = 0; foreach (Part p in v.parts) if (!GroundSupport(p)) k++; return k; }

        static bool GroundSupport(Part p) { return p.Modules.Contains("LaunchClamp") || p.Modules.Contains("ModuleCaelusLaunchMount") || p.Modules.Contains("ModuleCaelusLaunchTower"); }

        void StartBuild(Vessel v, int key)
        {
            pend.Clear(); pendVessel = v; pendKey = key; pendIdx = 0; pendSamples = 0; pendMeshes = 0; pendMs = 0;
            Array.Clear(cell, 0, cell.Length);
            foreach (Part p in v.parts)
            {
                if (GroundSupport(p)) continue;
                foreach (MeshFilter mf in p.FindModelComponents<MeshFilter>()) pend.Add(mf);
            }
        }

        void StepBuild(double budgetMs)
        {
            Vessel v = pendVessel;
            if (v == null || v.rootPart == null) { pendIdx = -1; return; }
            var t0 = DateTime.Now;
            Transform root = v.rootPart.transform;
            Matrix4x4 toRoot = root.worldToLocalMatrix;
            while (pendIdx < pend.Count)
            {
                MeshFilter mf = pend[pendIdx++];
                if (mf == null || mf.sharedMesh == null || !mf.gameObject.activeInHierarchy || mf.gameObject.layer != 0) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || !mr.enabled) continue;
                var sm = mr.sharedMaterial; string shn = sm != null && sm.shader != null ? sm.shader.name : "";
                if (shn.StartsWith("CaelusSmoke") || shn.StartsWith("Waterfall") || shn.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) >= 0 || shn.IndexOf("Additive", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                Mesh mesh = mf.sharedMesh;
                if (!mesh.isReadable) continue;
                Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
                mesh.GetVertices(vbuf);
                int stride = Math.Max(1, vbuf.Count / 1500);
                for (int i = 0; i < vbuf.Count; i++) { vbuf[i] = m.MultiplyPoint3x4(vbuf[i]); if (i % stride == 0) { Put(vbuf[i]); pendSamples++; } }
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    mesh.GetTriangles(ibuf, sub);
                    for (int i = 0; i + 2 < ibuf.Count; i += 3)
                    {
                        pendSamples += Edge(vbuf[ibuf[i]], vbuf[ibuf[i + 1]]); pendSamples += Edge(vbuf[ibuf[i + 1]], vbuf[ibuf[i + 2]]); pendSamples += Edge(vbuf[ibuf[i + 2]], vbuf[ibuf[i]]);
                    }
                }
                pendMeshes++;
                if ((DateTime.Now - t0).TotalMilliseconds > budgetMs) break;
            }
            pendMs += (DateTime.Now - t0).TotalMilliseconds;
            if (pendIdx < pend.Count) return;
            pendIdx = -1;
            Finish(v);
        }

        void Finish(Vessel v)
        {

            var rad = new float[NBIN]; var tmp = new float[SECT]; int top = -1, bottom = NBIN;
            for (int b = 0; b < NBIN; b++)
            {
                int k = 0;
                for (int s = 0; s < SECT; s++) { float r = cell[b * SECT + s]; if (r > 0f) tmp[k++] = r; }
                if (k < SECT / 2) { rad[b] = 0f; continue; }
                Array.Sort(tmp, 0, k); rad[b] = tmp[k / 2];
                if (rad[b] > 0.3f) { top = Math.Max(top, b); bottom = Math.Min(bottom, b); }
            }
            profVessel = v; profKey = pendKey;
            if (top < 0) { Debug.Log("[CaelusVapor] no hull found on " + v.vesselName + " (" + pendMeshes + " meshes)"); profLen = 0f; return; }

            for (int b = bottom; b <= top; b++)
            {
                if (rad[b] > 0.3f) continue;
                int lo = b - 1, hi = b + 1;
                while (hi <= top && rad[hi] <= 0.3f) hi++;
                rad[b] = hi <= top ? Mathf.Lerp(rad[lo], rad[hi], (float)(b - lo) / (hi - lo)) : rad[lo];
            }

            noseY = Y0 + (top + 1) * BIN; profLen = (top - bottom + 1) * BIN;
            for (int i = 0; i < VaporModel.NPROF; i++)
            {
                float y = noseY - profLen * i / (VaporModel.NPROF - 1);
                float f = (y - Y0) / BIN - 0.5f; int b0 = Mathf.Clamp(Mathf.FloorToInt(f), bottom, top), b1 = Mathf.Clamp(b0 + 1, bottom, top);
                prof[i] = Mathf.Lerp(Med3(rad, b0, bottom, top), Med3(rad, b1, bottom, top), Mathf.Clamp01(f - b0));
            }
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < VaporModel.NPROF; i += 4) sb.Append(prof[i].ToString("F1")).Append(' ');
                Debug.Log($"[CaelusVapor] profile {v.vesselName}: {pendKey} parts (no clamps), {pendMeshes} meshes, {pendSamples} samples, length {profLen:F1} m, nose y {noseY:F1}, {pendMs:F0} ms in 3 ms slices | r every {4 * profLen / (VaporModel.NPROF - 1):F1} m: {sb}");
                int n = VaporModel.FindCollars(prof, profLen, 1.02f, cols);
                for (int i = 0; i < n; i++) Debug.Log($"[CaelusVapor] shoulder x {cols[i].x:F1} m r {cols[i].r:F2} turn {cols[i].turnDeg:F1} deg strength {cols[i].strength:F2}");
            }
        }

        static float Med3(float[] a, int i, int lo, int hi)
        {
            float x = a[Math.Max(lo, i - 1)], y = a[i], z = a[Math.Min(hi, i + 1)];
            return Math.Max(Math.Min(x, y), Math.Min(Math.Max(x, y), z));
        }

        int Edge(Vector3 a, Vector3 b)
        {
            float l2 = (a - b).sqrMagnitude;
            if (l2 < 0.64f) return 0;
            int n = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(l2) / 0.4f), 2, 400);
            for (int k = 1; k < n; k++) Put(Vector3.LerpUnclamped(a, b, (float)k / n));
            return n - 1;
        }

        void Put(Vector3 p)
        {
            int bin = (int)((p.y - Y0) / BIN); if (bin < 0 || bin >= NBIN) return;
            float r = Mathf.Sqrt(p.x * p.x + p.z * p.z); if (r > 40f) return;
            int s = (int)((Mathf.Atan2(p.z, p.x) + Mathf.PI) / (2f * Mathf.PI) * SECT) % SECT;
            int c = bin * SECT + s; if (r > cell[c]) cell[c] = r;
        }
    }
}
