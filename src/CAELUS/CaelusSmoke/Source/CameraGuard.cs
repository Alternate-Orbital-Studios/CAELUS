using System;
using System.Collections.Generic;
using UnityEngine;

namespace CaelusSmoke
{
    public class CameraGuard
    {
        bool enabled = true, debug, legacy; float clearance = 0.5f, probe = 0.3f;
        float stockMin = -1f, shipRadius, radiusTimer, logTimer, debugTimer, errTimer; Vessel radiusFor;
        readonly RaycastHit[] hits = new RaycastHit[256];

        GameObject probeGo; SphereCollider probeCol;
        readonly Collider[] overlaps = new Collider[128];
        readonly HashSet<Collider> own = new HashSet<Collider>(); Vessel ownFor; float ownTimer;
        int lastFrame = -1; float lastTime = -1f;
        public int pushes, blocks; public float lastPush, maxPush;

        public CameraGuard()
        {
            foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("CAELUS_CAMERA_GUARD"))
            {
                n.TryGetValue("enabled", ref enabled); n.TryGetValue("clearance", ref clearance); n.TryGetValue("probe", ref probe);
                n.TryGetValue("debug", ref debug); n.TryGetValue("legacy", ref legacy);
                n.TryGetValue("easeRate", ref easeRate); n.TryGetValue("captureFrames", ref captureFrames); n.TryGetValue("captureDir", ref captureDir);
            }
            probeGo = new GameObject("CaelusCameraGuardProbe");
            probeGo.layer = 2;
            probeGo.transform.position = new Vector3(0f, -5.0e6f, 0f);
            probeCol = probeGo.AddComponent<SphereCollider>(); probeCol.isTrigger = true; probeCol.radius = probe + clearance;
            Camera.onPreCull += PreCull;
            Camera.onPostRender += PostRender;
            if (captureFrames > 0 && !string.IsNullOrEmpty(captureDir)) StartCapture();
            Debug.Log("[CaelusSmoke] camera guard v4 (spring): enabled " + enabled + ", clearance " + clearance + " m, probe " + probe + " m" + (legacy ? ", LEGACY v1 mode" : ""));
        }

        public void Update(float dt)
        {
            if (legacy) LegacyUpdate(dt);
        }

        void PreCull(Camera c)
        {
            if (legacy || !enabled || c == null || Time.frameCount == lastFrame || !HighLogic.LoadedSceneIsFlight) return;
            if (CaelusUser.camMode == 0)
            { FlightCamera f0 = FlightCamera.fetch; if (f0 != null && stockMin >= 0f && f0.minDistance != stockMin) f0.minDistance = stockMin; lastFor = null; return; }
            FlightCamera fc = FlightCamera.fetch;
            if (fc == null) return;
            bool flightCam = c == fc.mainCamera;
            if (!flightCam && fc.cameras != null)
                for (int i = 0; i < fc.cameras.Length; i++) if (fc.cameras[i] == c) { flightCam = true; break; }
            if (!flightCam) return;
            lastFrame = Time.frameCount;
            float now = Time.realtimeSinceStartup, dt = lastTime < 0f ? 0f : Mathf.Clamp(now - lastTime, 0f, 0.5f); lastTime = now;
            try { Guard(fc, dt); }
            catch (Exception e) { if ((errTimer -= dt) < 0f) { errTimer = 10f; Debug.LogWarning("[CaelusSmoke] camera guard: " + e.Message); } }
        }

        Vector3 lastRel, lastDRel; Vessel lastFor; int lastGuardFrame = -1000; bool moved; Vector3 kspPos;
        public int corrected, insideRendered; public float maxCorr;
        float easeRate = 6f;

        void Guard(FlightCamera fc, float dt)
        {
            if (stockMin < 0f) stockMin = fc.minDistance;
            Vessel v = FlightGlobals.ActiveVessel;
            bool flightView = CameraManager.Instance != null && CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.Flight && !MapView.MapIsEnabled;
            bool onShip = v != null && v.loaded && !v.isEVA && (fc.targetMode == FlightCamera.TargetMode.Vessel
                || (fc.targetMode == FlightCamera.TargetMode.Part && fc.partTarget != null && fc.partTarget.vessel == v));
            Transform pivot = fc.GetPivot();
            if (!flightView || !onShip || pivot == null || !CaelusParts.Vessel(v)) { fc.minDistance = stockMin; lastFor = null; return; }
            Vector3 p = pivot.position;
            if (float.IsNaN(p.x) || float.IsInfinity(p.x)) { fc.minDistance = stockMin; lastFor = null; return; }
            if (v != ownFor || (ownTimer -= dt) < 0f) RefreshOwn(v);
            if (v != bellsFor || (bellTimer -= dt) < 0f) RefreshBells2(v);

            float rr = probe + clearance + NearReach(fc);
            Transform ct = fc.transform;
            Vector3 D = ct.position;
            if (float.IsNaN(D.x) || float.IsInfinity(D.x)) return;
            bool fresh = lastFor == v && Time.frameCount - lastGuardFrame <= 5;

            Vector3 R;
            if (CaelusUser.camMode == 1)
            {

                R = Inside(D, rr) ? OutAlongLine(p, D, rr) : D;
            }
            else if (!Inside(D, rr))
            {

                if (fresh)
                {
                    Vector3 prev = p + lastRel + (D - p - lastDRel);
                    R = Vector3.Lerp(prev, D, 1f - Mathf.Exp(-easeRate * dt));
                    if ((R - D).sqrMagnitude < 1e-4f || Inside(R, rr)) R = D;

                    R = Sweep(prev, R, rr);
                }
                else R = D;
            }
            else
            {

                Vector3 start = fresh ? p + lastRel + (D - p - lastDRel) : D;
                if (Inside(start, rr)) start = Depenetrate(start, rr);
                if (Inside(start, rr)) start = OutAlongLine(p, start, rr);
                float lo = 0f, hi = 1f;
                for (int j = 0; j < 12; j++) { float mid = (lo + hi) * 0.5f; if (Inside(Vector3.Lerp(start, D, mid), rr)) hi = mid; else lo = mid; }
                R = Vector3.Lerp(start, D, lo);
            }

            float corr = (R - D).magnitude;
            if (corr > 1e-3f)
            {
                kspPos = D; ct.position = R; moved = true; corrected++; pushes++;
                if (corr > maxCorr) maxCorr = corr;
                if (Inside(R, rr * 0.9f)) insideRendered++;
            }
            lastRel = R - p; lastDRel = D - p; lastFor = v; lastGuardFrame = Time.frameCount;

            Vector3 off = D - p; float Dd = off.magnitude;
            if (Dd > 1e-3f)
            {
                Vector3 d = off / Dd; float near = Dd;
                int m = Physics.SphereCastNonAlloc(D, rr * 0.98f, -d, hits, Dd, 1 << 0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < m; i++)
                    if (hits[i].distance > 0f && hits[i].distance < near && own.Contains(hits[i].collider)) near = hits[i].distance;
                Ray back = new Ray(D, -d);
                for (int i = 0; i < bells2.Count; i++) { float t; if (BellRay(bells2[i], back, rr, out t) && t < near) near = t; }
                float shn = ShellMarch(D, -d, near, rr); if (shn < near) near = shn;
                float minD = near < Dd ? Dd - near : stockMin;
                fc.minDistance = Mathf.Clamp(minD, stockMin, Mathf.Max(stockMin, Dd));
            }
            if (debug && (debugTimer -= dt) < 0f)
            {
                debugTimer = 1f;
                Debug.Log("[CaelusSmoke] camera guard v4: corrected frames " + corrected + " max correction " + maxCorr.ToString("F2") + " m, rendered-inside frames " + insideRendered + ", minDistance " + fc.minDistance.ToString("F2") + ", own colliders " + own.Count + ", bell boxes " + bells2.Count);
            }
        }

        void PostRender(Camera c)
        {
            if (!moved) return;
            FlightCamera fc = FlightCamera.fetch;
            if (fc == null || c != fc.mainCamera) return;
            fc.transform.position = kspPos; moved = false;
        }

        Vector3 Depenetrate(Vector3 cam, float rr)
        {
            probeCol.radius = rr;
            for (int it = 0; it < 6; it++)
            {
                bool any = false;
                int n = Physics.OverlapSphereNonAlloc(cam, rr, overlaps, 1 << 0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    Collider col = overlaps[i];
                    if (col == null || !own.Contains(col)) continue;
                    Vector3 dir; float dist;
                    if (Physics.ComputePenetration(probeCol, cam, Quaternion.identity, col, col.transform.position, col.transform.rotation, out dir, out dist)
                        && dist > 1e-4f && dist < 100f && !float.IsNaN(dir.x)) { cam += dir * (dist + 0.002f); any = true; }
                }
                for (int i = 0; i < bells2.Count; i++) { Vector3 push; if (BellPush(bells2[i], cam, rr, out push)) { cam += push; any = true; } }
                bool sp; cam = ShellPush(cam, rr, out sp); any |= sp;
                if (!any) break;
            }
            return cam;
        }

        Vector3 Sweep(Vector3 a, Vector3 b, float rr)
        {
            Vector3 d = b - a; float L = d.magnitude;
            if (L < 1e-3f) return b;
            d /= L; float best = L;
            int m = Physics.SphereCastNonAlloc(a, rr * 0.98f, d, hits, L, 1 << 0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < m; i++)
                if (own.Contains(hits[i].collider) && hits[i].distance > 0f && hits[i].distance < best) best = hits[i].distance;
            Ray ray = new Ray(a, d);
            for (int i = 0; i < bells2.Count; i++) { float t; if (BellRay(bells2[i], ray, rr, out t) && t > 0f && t < best) best = t; }
            float sh = ShellMarch(a, d, Mathf.Min(L, best), rr); if (sh < best) best = sh;
            return best < L ? a + d * Mathf.Max(0f, best - 0.01f) : b;
        }

        Vector3 OutAlongLine(Vector3 p, Vector3 c, float rr)
        {
            Vector3 d0 = c - p; float D0 = d0.magnitude; if (D0 < 1e-3f) return c; d0 /= D0;
            float lo = D0, hi = D0; int k = 0;
            while (Inside(p + d0 * hi, rr) && k < 14) { lo = hi; hi += Mathf.Max(0.5f, hi * 0.3f); k++; }
            for (int j = 0; j < 12; j++) { float mid = (lo + hi) * 0.5f; if (Inside(p + d0 * mid, rr)) lo = mid; else hi = mid; }
            return p + d0 * (hi + 0.01f);
        }

        int captureFrames; string captureDir = "";
        class CaptureHost : MonoBehaviour
        {
            public int frames; public string dir;
            System.Collections.IEnumerator Start()
            {
                System.IO.Directory.CreateDirectory(dir); int n = 0, f = 0;
                while (n < frames)
                {
                    yield return new WaitForEndOfFrame();
                    if (!HighLogic.LoadedSceneIsFlight || (f++ & 1) == 1) continue;
                    Texture2D t = ScreenCapture.CaptureScreenshotAsTexture();
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "f" + n.ToString("D5") + ".jpg"), t.EncodeToJPG(85));
                    UnityEngine.Object.Destroy(t); n++;
                }
            }
        }
        GameObject captureGo;
        void StartCapture()
        {
            captureGo = new GameObject("CaelusCameraGuardCapture"); UnityEngine.Object.DontDestroyOnLoad(captureGo);
            var h = captureGo.AddComponent<CaptureHost>(); h.frames = captureFrames; h.dir = captureDir;
        }

        float nearLogged = -1f;
        float NearReach(FlightCamera fc)
        {

            float best = 0f, nearest = float.MaxValue;
            Camera[] cams = fc.cameras;
            for (int i = -1; cams != null && i < cams.Length; i++)
            {
                Camera c = i < 0 ? fc.mainCamera : cams[i];
                if (c == null || !c.enabled || (c.cullingMask & 1) == 0 || c.nearClipPlane >= nearest) continue;
                nearest = c.nearClipPlane;
                float th = Mathf.Tan(c.fieldOfView * 0.5f * Mathf.Deg2Rad), a = c.aspect;
                best = Mathf.Min(2f, nearest * Mathf.Sqrt(1f + th * th * (1f + a * a)));
            }
            if (Mathf.Abs(best - nearLogged) > 0.05f)
            {
                nearLogged = best;
                Debug.Log("[CaelusSmoke] camera guard: near-plane reach " + best.ToString("F3") + " m, clear radius " + (probe + clearance + best).ToString("F2") + " m");
            }
            return best;
        }

        readonly List<MeshCollider> hollow = new List<MeshCollider>(); int hollowLogged = -1;
        static readonly Vector3[] Axes = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
        bool Enclosed(Vector3 c)
        {
            for (int i = 0; i < hollow.Count; i++)
            {
                MeshCollider mc = hollow[i];
                if (mc == null || !mc.enabled) continue;
                Bounds b = mc.bounds;
                if (!b.Contains(c)) continue;
                float L = b.extents.magnitude * 2f + 1f; int hit = 0; RaycastHit h;
                for (int k = 0; k < 6; k++) { if (mc.Raycast(new Ray(c + Axes[k] * L, -Axes[k]), out h, L)) hit++; else break; }
                if (hit == 6) return true;
            }
            return false;
        }

        class Shell { public Part part; public float hMin, hMax, rMax; public Vector3 o; public float cs; public int nx, ny, nz; public bool[] solid; }
        readonly List<Shell> shells = new List<Shell>(); readonly Dictionary<Part, Shell> shellCache = new Dictionary<Part, Shell>(); int shellsLogged = -1;

        static Shell BuildShell(Part part)
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                Transform pt = part.transform;
                Vector3 cmin = new Vector3(1e9f, 1e9f, 1e9f), cmax = -cmin; bool anyCol = false;
                foreach (Collider c in part.GetComponentsInChildren<Collider>(false))
                {
                    if (!c.enabled || c.isTrigger || c.gameObject.layer != 0) continue;
                    Bounds b = c.bounds; anyCol = true;
                    for (int k = 0; k < 8; k++)
                    {
                        Vector3 w = b.center + Vector3.Scale(b.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                        Vector3 l = pt.InverseTransformPoint(w); cmin = Vector3.Min(cmin, l); cmax = Vector3.Max(cmax, l);
                    }
                }
                var meshes = new List<KeyValuePair<Mesh, Matrix4x4>>();
                Vector3 vmin = new Vector3(1e9f, 1e9f, 1e9f), vmax = -vmin; int nv = 0;
                foreach (MeshRenderer r in part.GetComponentsInChildren<MeshRenderer>(false))
                {
                    if (!r.enabled || r.gameObject.layer != 0) continue;
                    MeshFilter mf = r.GetComponent<MeshFilter>(); Mesh m = mf != null ? mf.sharedMesh : null;
                    if (m == null || !m.isReadable) continue;
                    Matrix4x4 toPart = pt.worldToLocalMatrix * r.transform.localToWorldMatrix;
                    meshes.Add(new KeyValuePair<Mesh, Matrix4x4>(m, toPart));
                    Vector3[] vs = m.vertices; int step = Mathf.Max(1, vs.Length / 20000);
                    for (int i = 0; i < vs.Length; i += step) { Vector3 q = toPart.MultiplyPoint3x4(vs[i]); vmin = Vector3.Min(vmin, q); vmax = Vector3.Max(vmax, q); nv++; }
                }
                if (nv < 8) return null;

                if (anyCol && vmin.x > cmin.x - 1f && vmin.y > cmin.y - 1f && vmin.z > cmin.z - 1f && vmax.x < cmax.x + 1f && vmax.y < cmax.y + 1f && vmax.z < cmax.z + 1f) return null;
                Vector3 ext = vmax - vmin;
                float cs = Mathf.Max(0.15f, Mathf.Max(ext.x, Mathf.Max(ext.y, ext.z)) / 90f);
                var s = new Shell { part = part, hMin = vmin.y, hMax = vmax.y, cs = cs, o = vmin - Vector3.one * (cs * 1.5f) };
                s.nx = Mathf.CeilToInt(ext.x / cs) + 4; s.ny = Mathf.CeilToInt(ext.y / cs) + 4; s.nz = Mathf.CeilToInt(ext.z / cs) + 4;
                int nxy = s.nx * s.ny, N = nxy * s.nz;
                var surf = new bool[N];
                float inv = 1f / cs, half = cs * 0.5f;
                foreach (var kv in meshes)
                {
                    Vector3[] vs = kv.Key.vertices; Matrix4x4 M = kv.Value;
                    var lv = new Vector3[vs.Length]; for (int i = 0; i < vs.Length; i++) lv[i] = M.MultiplyPoint3x4(vs[i]);
                    int[] tri = kv.Key.triangles;
                    for (int i = 0; i + 2 < tri.Length; i += 3)
                    {
                        Vector3 A = lv[tri[i]], B = lv[tri[i + 1]], C = lv[tri[i + 2]];
                        float e = Mathf.Max((B - A).magnitude, Mathf.Max((C - A).magnitude, (C - B).magnitude));
                        int n = Mathf.Clamp(Mathf.CeilToInt(e / half), 1, 400);
                        for (int u = 0; u <= n; u++)
                            for (int w = 0; w <= n - u; w++)
                            {
                                Vector3 q = A + (B - A) * ((float)u / n) + (C - A) * ((float)w / n) - s.o;
                                int x = (int)(q.x * inv), y = (int)(q.y * inv), z = (int)(q.z * inv);
                                if (x >= 0 && y >= 0 && z >= 0 && x < s.nx && y < s.ny && z < s.nz) surf[x + y * s.nx + z * nxy] = true;
                            }
                    }
                }

                var outside = new bool[N]; var queue = new Queue<int>(); outside[0] = true; queue.Enqueue(0);
                while (queue.Count > 0)
                {
                    int id = queue.Dequeue(); int z = id / nxy, y = (id - z * nxy) / s.nx, x = id - z * nxy - y * s.nx;
                    for (int k = 0; k < 6; k++)
                    {
                        int xx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), yy = y + (k == 2 ? 1 : k == 3 ? -1 : 0), zz = z + (k == 4 ? 1 : k == 5 ? -1 : 0);
                        if (xx < 0 || yy < 0 || zz < 0 || xx >= s.nx || yy >= s.ny || zz >= s.nz) continue;
                        int j = xx + yy * s.nx + zz * nxy;
                        if (outside[j] || surf[j]) continue;
                        outside[j] = true; queue.Enqueue(j);
                    }
                }
                s.solid = new bool[N]; int solidN = 0;
                for (int i = 0; i < N; i++) if (!outside[i]) { s.solid[i] = true; solidN++; }
                s.rMax = Mathf.Max(Mathf.Max(Mathf.Abs(vmin.x), Mathf.Abs(vmax.x)), Mathf.Max(Mathf.Abs(vmin.z), Mathf.Abs(vmax.z))) * 1.415f;
                Debug.Log("[CaelusSmoke] camera guard: voxel hull for " + part.partInfo.name + " (drawn " + ext.ToString("F1") + " m, colliders " + (anyCol ? (cmax - cmin).ToString("F1") : "none")
                          + ", cell " + cs.ToString("F2") + " m, " + s.nx + "x" + s.ny + "x" + s.nz + ", solid " + (100f * solidN / N).ToString("F1") + "%, " + sw.ElapsedMilliseconds + " ms)");
                return s;
            }
            catch (Exception e) { Debug.LogWarning("[CaelusSmoke] camera guard: visual hull skipped for " + part.partInfo.name + ": " + e.Message); return null; }
        }

        static bool Solid(Shell s, Vector3 l)
        {
            Vector3 q = (l - s.o) / s.cs; int x = (int)Mathf.Floor(q.x), y = (int)Mathf.Floor(q.y), z = (int)Mathf.Floor(q.z);
            if (x < 0 || y < 0 || z < 0 || x >= s.nx || y >= s.ny || z >= s.nz) return false;
            return s.solid[x + y * s.nx + z * s.nx * s.ny];
        }

        static readonly Vector3[] Probe = BuildProbe();
        static Vector3[] BuildProbe()
        {
            var l = new List<Vector3> { Vector3.zero };
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++) if (x != 0 || y != 0 || z != 0) l.Add(new Vector3(x, y, z).normalized);
            return l.ToArray();
        }

        static bool ShellHit(Shell s, Vector3 l, float rl)
        {
            Vector3 q = l - s.o;
            if (q.x < -rl || q.y < -rl || q.z < -rl || q.x > s.nx * s.cs + rl || q.y > s.ny * s.cs + rl || q.z > s.nz * s.cs + rl) return false;
            for (int i = 0; i < Probe.Length; i++) if (Solid(s, l + Probe[i] * rl)) return true;
            if (rl > s.cs) for (int i = 1; i < Probe.Length; i++) if (Solid(s, l + Probe[i] * (rl * 0.5f))) return true;
            return false;
        }

        bool InShell(Vector3 c, float rr)
        {
            for (int i = 0; i < shells.Count; i++)
            {
                Shell s = shells[i]; if (s.part == null) continue;
                Transform pt = s.part.transform; float sc = Mathf.Max(1e-3f, pt.lossyScale.x);
                if (ShellHit(s, pt.InverseTransformPoint(c), rr / sc)) return true;
            }
            return false;
        }

        Vector3 ShellPush(Vector3 c, float rr, out bool any)
        {
            any = false;
            for (int i = 0; i < shells.Count; i++)
            {
                Shell s = shells[i]; if (s.part == null) continue;
                Transform pt = s.part.transform; float sc = Mathf.Max(1e-3f, pt.lossyScale.x), rl = rr / sc;
                Vector3 l = pt.InverseTransformPoint(c);
                if (!ShellHit(s, l, rl)) continue;
                float best = float.MaxValue; Vector3 bestP = l;
                for (int k = 1; k < Probe.Length; k++)
                    for (float d = s.cs; d < Mathf.Min(best, 60f); d += s.cs)
                        if (!ShellHit(s, l + Probe[k] * d, rl)) { if (d < best) { best = d; bestP = l + Probe[k] * (d + 0.01f); } break; }
                if (best < float.MaxValue) { c = pt.TransformPoint(bestP); any = true; }
            }
            return c;
        }

        float ShellMarch(Vector3 a, Vector3 d, float L, float rr)
        {
            if (shells.Count == 0) return float.MaxValue;

            float t0 = float.MaxValue, t1 = -1f;
            for (int i = 0; i < shells.Count; i++)
            {
                Shell s = shells[i]; if (s.part == null) continue;
                Transform pt = s.part.transform;
                Vector3 c = pt.TransformPoint(new Vector3(0f, (s.hMin + s.hMax) * 0.5f, 0f));
                float half = (s.hMax - s.hMin) * 0.5f, rad = Mathf.Sqrt(s.rMax * s.rMax + half * half) * Mathf.Max(1f, pt.lossyScale.x) + rr;
                Vector3 m = a - c; float b = Vector3.Dot(m, d), cc = m.sqrMagnitude - rad * rad, disc = b * b - cc;
                if (disc < 0f) continue;
                float sq = Mathf.Sqrt(disc), e0 = -b - sq, e1 = -b + sq;
                if (e1 < 0f || e0 > L) continue;
                t0 = Mathf.Min(t0, Mathf.Max(0f, e0)); t1 = Mathf.Max(t1, Mathf.Min(L, e1));
            }
            if (t1 < 0f) return float.MaxValue;
            if (InShell(a, rr)) return float.MaxValue;
            float step = Mathf.Clamp(rr * 0.5f, 0.1f, 0.3f);
            for (float s = Mathf.Max(step, t0); s <= t1 + 1e-4f; s += step) if (InShell(a + d * s, rr)) return Mathf.Max(0f, s - step);
            return float.MaxValue;
        }

        bool Inside(Vector3 c, float rr)
        {
            if (shells.Count > 0 && InShell(c, rr)) return true;
            if (hollow.Count > 0 && Enclosed(c)) return true;
            probeCol.radius = rr;
            int n = Physics.OverlapSphereNonAlloc(c, rr, overlaps, 1 << 0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider col = overlaps[i];
                if (col == null || !own.Contains(col)) continue;
                Vector3 dir; float dist;
                if (Physics.ComputePenetration(probeCol, c, Quaternion.identity, col, col.transform.position, col.transform.rotation, out dir, out dist) && dist > 1e-4f) return true;
            }
            for (int i = 0; i < bells2.Count; i++) { Vector3 push; if (BellPush(bells2[i], c, rr, out push)) return true; }
            return false;
        }

        void RefreshOwn(Vessel v)
        {
            own.Clear(); hollow.Clear(); ownFor = v; ownTimer = 0.5f;
            foreach (Part part in v.parts)
                if (part != null && !PadHardware(part))
                    foreach (Collider c in part.GetComponentsInChildren<Collider>(false))
                        if (c.enabled && !c.isTrigger && c.gameObject.layer == 0)
                        {
                            own.Add(c);
                            MeshCollider mc = c as MeshCollider;
                            if (mc != null && !mc.convex) hollow.Add(mc);
                        }
            shells.Clear();
            foreach (Part part in v.parts)
                if (part != null && !PadHardware(part))
                {
                    Shell s;
                    if (!shellCache.TryGetValue(part, out s)) { s = BuildShell(part); shellCache[part] = s; }
                    if (s != null) shells.Add(s);
                }
            if (shellCache.Count > 400) shellCache.Clear();
            if (shells.Count != shellsLogged) { shellsLogged = shells.Count; Debug.Log("[CaelusSmoke] camera guard: " + shells.Count + " parts drawn beyond their colliders get a visual hull"); }
            if (hollow.Count != hollowLogged) { hollowLogged = hollow.Count; Debug.Log("[CaelusSmoke] camera guard: " + own.Count + " hull colliders, " + hollow.Count + " hollow mesh shells (enclosure test)"); }
        }

        static bool PadHardware(Part p)
        {
            string n = p.partInfo != null ? p.partInfo.name : p.name;
            return n != null && (n.Contains("LaunchMount") || n.Contains("LaunchTower"));
        }

        struct Box { public Transform t; public Bounds local; }
        readonly List<Box> bells2 = new List<Box>();

        void RefreshBells2(Vessel v)
        {
            bells2.Clear(); bellsFor = v; bellTimer = 2f;
            foreach (Part part in v.parts)
            {
                if (part == null || part.FindModuleImplementing<ModuleEngines>() == null) continue;
                foreach (MeshRenderer r in part.GetComponentsInChildren<MeshRenderer>(false))
                {
                    if (!r.enabled || r.gameObject.layer != 0) continue;
                    MeshFilter mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                    Bounds b = mf.sharedMesh.bounds; Transform t = r.transform;
                    float ex = t.TransformVector(new Vector3(b.extents.x, 0f, 0f)).magnitude, ey = t.TransformVector(new Vector3(0f, b.extents.y, 0f)).magnitude,
                          ez = t.TransformVector(new Vector3(0f, 0f, b.extents.z)).magnitude;
                    float big = Mathf.Max(ex, Mathf.Max(ey, ez));
                    if (big > 3.2f || big < 0.15f) continue;
                    bells2.Add(new Box { t = t, local = b });
                    if (bells2.Count >= 600) return;
                }
            }
        }

        static void Obb(Box b, out Vector3 c, out Vector3 ax, out Vector3 ay, out Vector3 az, out Vector3 e)
        {
            Transform t = b.t; c = t.TransformPoint(b.local.center);
            ax = t.TransformVector(new Vector3(b.local.extents.x, 0f, 0f)); ay = t.TransformVector(new Vector3(0f, b.local.extents.y, 0f)); az = t.TransformVector(new Vector3(0f, 0f, b.local.extents.z));
            e = new Vector3(ax.magnitude, ay.magnitude, az.magnitude);
            ax = e.x > 1e-5f ? ax / e.x : Vector3.right; ay = e.y > 1e-5f ? ay / e.y : Vector3.up; az = e.z > 1e-5f ? az / e.z : Vector3.forward;
        }

        static bool BellPush(Box b, Vector3 cam, float rr, out Vector3 push)
        {
            push = Vector3.zero;
            if (b.t == null) return false;
            Vector3 c, ax, ay, az, e; Obb(b, out c, out ax, out ay, out az, out e);
            Vector3 w = cam - c;
            float qx = Vector3.Dot(w, ax), qy = Vector3.Dot(w, ay), qz = Vector3.Dot(w, az);
            float px = e.x + rr - Mathf.Abs(qx), py = e.y + rr - Mathf.Abs(qy), pz = e.z + rr - Mathf.Abs(qz);
            if (px <= 0f || py <= 0f || pz <= 0f) return false;
            if (px <= py && px <= pz) push = ax * (Mathf.Sign(qx) * (px + 0.002f));
            else if (py <= pz) push = ay * (Mathf.Sign(qy) * (py + 0.002f));
            else push = az * (Mathf.Sign(qz) * (pz + 0.002f));
            return true;
        }

        static bool BellRay(Box b, Ray r, float rr, out float tHit)
        {
            tHit = 0f;
            if (b.t == null) return false;
            Vector3 c, ax, ay, az, e; Obb(b, out c, out ax, out ay, out az, out e);
            Vector3 w = r.origin - c;
            float tmin = float.NegativeInfinity, tmax = float.PositiveInfinity;
            for (int k = 0; k < 3; k++)
            {
                Vector3 a = k == 0 ? ax : (k == 1 ? ay : az); float h = (k == 0 ? e.x : (k == 1 ? e.y : e.z)) + rr;
                float o = Vector3.Dot(w, a), dd = Vector3.Dot(r.direction, a);
                if (Mathf.Abs(dd) < 1e-6f) { if (Mathf.Abs(o) > h) return false; continue; }
                float t1 = (-h - o) / dd, t2 = (h - o) / dd; if (t1 > t2) { float s = t1; t1 = t2; t2 = s; }
                if (t1 > tmin) tmin = t1; if (t2 < tmax) tmax = t2;
                if (tmin > tmax) return false;
            }
            if (tmin <= 0f) return false;
            tHit = tmin; return true;
        }

        void LegacyUpdate(float dt)
        {
            FlightCamera fc = FlightCamera.fetch;
            if (!enabled || fc == null || fc.mainCamera == null) return;
            if (stockMin < 0f) stockMin = fc.minDistance;
            Vessel v = FlightGlobals.ActiveVessel;
            bool flightView = CameraManager.Instance != null && CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.Flight && !MapView.MapIsEnabled;
            bool onShip = v != null && v.loaded && !v.isEVA && (fc.targetMode == FlightCamera.TargetMode.Vessel
                || (fc.targetMode == FlightCamera.TargetMode.Part && fc.partTarget != null && fc.partTarget.vessel == v));
            Transform pivot = fc.GetPivot();
            if (!flightView || !onShip || pivot == null || !CaelusParts.Vessel(v)) { fc.minDistance = stockMin; return; }
            Vector3 p = pivot.position;
            if (float.IsNaN(p.x) || float.IsInfinity(p.x)) { fc.minDistance = stockMin; return; }
            if (v != radiusFor || (radiusTimer -= dt) < 0f)
            {
                radiusFor = v; radiusTimer = 0.5f; shipRadius = 0f;
                foreach (Part part in v.parts)
                    if (part != null)
                        foreach (Collider c in part.GetComponentsInChildren<Collider>(false))
                        {
                            if (!c.enabled || c.isTrigger) continue;
                            float r = Vector3.Distance(p, c.bounds.center) + c.bounds.extents.magnitude;
                            if (r < 2000f) shipRadius = Mathf.Max(shipRadius, r);
                        }
                shipRadius += 2f;
            }
            Vector3 d = fc.transform.position - p;
            d = d.sqrMagnitude > 1e-6f ? d.normalized : -fc.transform.forward;
            int n = Physics.SphereCastNonAlloc(p + d * shipRadius, probe, -d, hits, shipRadius, 1 << 0, QueryTriggerInteraction.Ignore);
            float first = shipRadius;
            for (int i = 0; i < n; i++)
            {
                if (hits[i].distance <= 0f || hits[i].distance >= first) continue;
                Part hp = hits[i].collider.GetComponentInParent<Part>();
                if (hp != null && hp.vessel == v) first = hits[i].distance;
            }
            if (v != bellsFor || (bellTimer -= dt) < 0f) RefreshBells(v);
            Ray back = new Ray(p + d * shipRadius, -d);
            for (int i = 0; i < bells.Count; i++)
            {
                Part bp = bells[i].part; if (bp == null) continue;
                Bounds lb = bells[i].local; Bounds wb = new Bounds(bp.transform.TransformPoint(lb.center), Vector3.zero);
                for (int c = 0; c < 8; c++)
                    wb.Encapsulate(bp.transform.TransformPoint(lb.center + Vector3.Scale(lb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1))));
                float dist;
                if (wb.IntersectRay(back, out dist) && dist > 0f && dist < first) first = dist;
            }
            if (first >= shipRadius) { fc.minDistance = stockMin; return; }
            float minD = shipRadius - first + clearance;
            if (float.IsNaN(minD) || float.IsInfinity(minD)) { fc.minDistance = stockMin; return; }
            fc.minDistance = Mathf.Max(stockMin, minD);
            if (fc.Distance < minD - 0.01f)
            {
                fc.SetDistanceImmediate(minD);
                fc.transform.position = p + d * minD;
                pushes++; lastPush = minD - fc.Distance;
            }
        }

        struct Bell { public Part part; public Bounds local; }
        readonly List<Bell> bells = new List<Bell>(); Vessel bellsFor; float bellTimer;
        void RefreshBells(Vessel v)
        {
            bells.Clear(); bellsFor = v; bellTimer = 2f;
            foreach (Part part in v.parts)
            {
                if (part == null || part.FindModuleImplementing<ModuleEngines>() == null) continue;
                bool any = false; Bounds lb = new Bounds();
                foreach (MeshRenderer r in part.GetComponentsInChildren<MeshRenderer>(false))
                {
                    if (!r.enabled || r.gameObject.layer != 0) continue;
                    MeshFilter mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                    Bounds mb = mf.sharedMesh.bounds; Matrix4x4 m = part.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                    for (int c = 0; c < 8; c++)
                    {
                        Vector3 q = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1)));
                        if (!any) { lb = new Bounds(q, Vector3.zero); any = true; } else lb.Encapsulate(q);
                    }
                }
                if (any && lb.extents.magnitude < 60f) bells.Add(new Bell { part = part, local = lb });
            }
        }

        public void Destroy()
        {
            Camera.onPreCull -= PreCull; Camera.onPostRender -= PostRender;
            if (captureGo != null) UnityEngine.Object.Destroy(captureGo);
            FlightCamera fc = FlightCamera.fetch;
            if (fc != null && stockMin >= 0f) fc.minDistance = stockMin;
            if (probeGo != null) UnityEngine.Object.Destroy(probeGo);
        }
    }
}
