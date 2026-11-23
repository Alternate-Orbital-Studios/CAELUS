using System;
using System.Collections.Generic;
using UnityEngine;

namespace Caelus
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class CoronaChuteFx : MonoBehaviour
    {
        static readonly string[] Rotators = { "B_DrogueRotator", "B_ParachuteLargeRotator" };

        class Canopy { public Transform t; public Vector3 restS; public Quaternion restR; public Vector3 wroteS; public Quaternion wroteR; public float phase; public bool drogue;
                       public float openT = -1, delay, k, rCur, sx, sy, vx, vy;
                       public Transform riser; public Quaternion rRestR, rWroteR; public Renderer rend; public bool splayed; public float ox, oy;

                       public bool meshTried; public MeshFilter mfC, mfL; public Vector3[] c0, l0, cb, lb; public float zs, zt, R; public int skip, ax = 2, ng; public float sg = 1, gph;

                       public List<Canopy> sib; public float spin, spinRate; public Material inner; public float nextGlow, glowCur = -1f, preFl, glowTgt, openF, infl = 1f, inflT = 7f, lineT = -1f; public LineThick lth; public bool lthTried; public int ncp; public Vector3 cpM0, cpN0, cpM1, cpN1;

                       public int idx; public bool opArm, opDone; public float ripK = 1f, lineOut, emerge = 1f, fanPhi = -1f, nextDiag, cutNow; public ChuteCloth.Opening op;
                       public Vector3 restP, wroteP, restRS, wroteRS, hOff, cenW; public bool cenOk; public Transform rScale; public bool rScaleTried;

                       public ChuteCloth.Descent dl; public bool dlOk, dlNo; public float dlW, dlWD, dlNext, cpK = 1f; }
        class Watch { public Transform rot; public bool wasOpen, wasActive, init; public Vector3 pos; public Quaternion rotW; public Vector3 lossy; public List<Canopy> canopies = new List<Canopy>();
                      public Vector3 lastFwd; public bool hasFwd; public Part part; public float splashT = -1, pre; public bool cutFired; }
        class Falling { public GameObject go; public CelestialBody body; public Vector3d rel; public Vector3d vel; public float t0; public bool drogue; public Vector3 s0;
                        public float floatT = -1; public Vector3 flatFwd; public float life;
                        public Transform anchor; public Part part; public bool floatNow; public float rHull = 2f; public List<int> mfSim;
                        public List<MeshFilter> mfs; public List<Vector3[]> v0; public float seed; public List<Renderer> rends;
                        public List<Sim> sims; public Vector3 breeze; public List<Vector3[]> buf; public List<Material> mats;
                        public List<Vector2[]> uvs; public List<float> merid; public List<KeyValuePair<Material, Color>> emis; public LayCache[] lay; public LineThick[] lth; public Vector3[][] nbuf;
                        public bool[] pbMesh; }

        class Sim { public Transform root, riser; public Vector3 axisL; public float L, landT = -1, defl = 1; public Vector3 rc, v; public Vector3 s0;
                    public float rCan = 8f; public Vector3 chainS0; public int chainAx = 1; public float sink; public bool gone; public float kChain = 1f, skirtFrac = 0.86f;
                    public ChuteCloth.Playback pb; }

        readonly Dictionary<uint, List<Watch>> watched = new Dictionary<uint, List<Watch>>();
        readonly List<Falling> falling = new List<Falling>();
        float nextScan, nextSlowLog;

        static int otherCloth = -1; static string otherClothName;
        void Start()
        {
            if (otherCloth < 0)
            {
                otherCloth = 0;
                foreach (var la in AssemblyLoader.loadedAssemblies)
                {
                    string n = (la.name ?? "").ToLowerInvariant();
                    if (n == "parasoft" || n.Contains("canopies")) { otherCloth = 1; otherClothName = la.name; break; }
                }
            }
            if (otherCloth == 1)
            {
                Debug.Log("[CaelusEntryDrag] ChuteFx: " + otherClothName + " is installed - it animates the parachute cloth, so the CAELUS canopy animation stays off (no two systems on one canopy)");
                enabled = false;
            }
        }

        void LateUpdate()
        {
            if (!FlightGlobals.ready) return;
            long tA = System.Diagnostics.Stopwatch.GetTimestamp(); int gA = GC.CollectionCount(0);
            if (Time.time > nextScan) { nextScan = Time.time + 2f; Scan(); }
            long tB = System.Diagnostics.Stopwatch.GetTimestamp();
            float now = Time.time;
            foreach (var kv in watched)
                foreach (var w in kv.Value)
                {
                    if (w.rot == null) continue;
                    bool active = w.rot.gameObject.activeInHierarchy;
                    bool open = active && OpenScale(w) > 0.3f;
                    if (active)
                    {
                        if (open) { w.pos = w.rot.position; w.rotW = w.rot.rotation; w.lossy = w.rot.lossyScale; }

                        Vector3 fwd = w.rot.forward, kick = Vector3.zero;
                        if (w.hasFwd)
                        {
                            float ang; Vector3 ax; Quaternion.FromToRotation(w.lastFwd, fwd).ToAngleAxis(out ang, out ax);
                            if (ang > 180f) ang -= 360f;
                            if (Math.Abs(ang) < 30f) kick = w.rot.InverseTransformDirection(ax) * ang;
                        }
                        w.lastFwd = fwd; w.hasFwd = true;

                        for (int a = 0; a < w.canopies.Count; a++)
                            for (int b = a + 1; b < w.canopies.Count; b++) Separate(w.canopies[a], w.canopies[b], Time.deltaTime);
                        if (open) Splashdown(w, now);
                        foreach (var c in w.canopies) Dynamics(c, now, Time.deltaTime, kick, w.pre);
                    }
                    if (!w.init) { w.init = true; w.wasActive = active; }
                    else if (active && !w.wasActive) { DeploySound(w); PopCap(w, kv.Key); }
                    w.wasActive = active;

                    if (!active && w.wasOpen)
                    {
                        bool wet = w.part != null && w.part.vessel != null && w.part.vessel.LandedOrSplashed;
                        foreach (var c in w.canopies) if (wet || c.dlWD <= 0.001f) Unripple(c);
                        SpawnFalling(w, kv.Key);
                    }
                    w.wasOpen = open;
                }
            long tC = System.Diagnostics.Stopwatch.GetTimestamp();
            StepFalling(now);
            StepPops(Time.deltaTime);
            long tD = System.Diagnostics.Stopwatch.GetTimestamp();

            double fq = 1000.0 / System.Diagnostics.Stopwatch.Frequency, msAll = (tD - tA) * fq;
            if (msAll > 40.0 && Time.realtimeSinceStartup > nextSlowLog)
            {
                nextSlowLog = Time.realtimeSinceStartup + 30f;
                Debug.Log("[CaelusEntryDrag] ChuteFx slow frame " + msAll.ToString("F0") + " ms (scan " + ((tB - tA) * fq).ToString("F0") + ", canopies " + ((tC - tB) * fq).ToString("F0") +
                          ", water " + ((tD - tC) * fq).ToString("F0") + ", gc +" + (GC.CollectionCount(0) - gA) + ", floating " + falling.Count + ")");
            }

        }
        float nextSepLog; static float sepMin = 99f; static bool sepSeen; static int sepPush; static string sepKind = "";

        static AudioClip deployClip; static bool clipTried;

        const bool DeploySoundOn = false;
        void DeploySound(Watch w)
        {
            if (!DeploySoundOn) return;
            if (!clipTried)
            {
                clipTried = true; deployClip = GameDatabase.Instance.GetAudioClip("CaelusSmoke/Sounds/caelus_chute_deploy");
                if (deployClip == null) Debug.LogWarning("[CaelusEntryDrag] ChuteFx: sound CaelusSmoke/Sounds/caelus_chute_deploy not found - parachute deploy is silent");
            }
            if (deployClip == null || w.rot == null) return;
            var src = w.rot.gameObject.GetComponent<AudioSource>();
            if (!src) src = w.rot.gameObject.AddComponent<AudioSource>();
            src.spatialBlend = 1f; src.rolloffMode = AudioRolloffMode.Logarithmic; src.minDistance = 8f; src.maxDistance = 800f; src.playOnAwake = false;
            bool drogue = w.canopies.Count > 0 && w.canopies[0].drogue;
            src.PlayOneShot(deployClip, GameSettings.SHIP_VOLUME * (drogue ? 0.6f : 1f));
        }

        void Scan()
        {
            var seen = new HashSet<uint>();
            foreach (var v in FlightGlobals.VesselsLoaded)
            {
                if (v == null || v.packed) continue;
                foreach (var p in v.parts)
                {
                    if (p.partInfo == null || !p.partInfo.name.StartsWith("CaelusCorona.Capsule", StringComparison.Ordinal)) continue;
                    seen.Add(p.flightID);
                    if (watched.ContainsKey(p.flightID)) continue;
                    var list = new List<Watch>();
                    foreach (var rn in Rotators)
                    {
                        var r = p.FindModelTransform(rn); if (r == null) continue;
                        var w = new Watch { rot = r, part = p };
                        foreach (var tr in r.GetComponentsInChildren<Transform>(true))
                            if (tr.name.Contains("Root") && !tr.name.Contains("RiserRoot") && tr.name.StartsWith("CLD_"))
                                w.canopies.Add(new Canopy { t = tr, restS = tr.localScale, restR = tr.localRotation, wroteS = new Vector3(-1, -1, -1), phase = UnityEngine.Random.value * 6.28f, drogue = rn == "B_DrogueRotator",
                                                            idx = w.canopies.Count, opArm = true,
                                                            riser = tr.parent != null && tr.parent.name.Contains("RiserRoot") ? tr.parent : null, rRestR = tr.parent != null ? tr.parent.localRotation : Quaternion.identity,
                                                            rend = tr.GetComponentInChildren<Renderer>(true) });

                        int nd = 0; foreach (var c0 in w.canopies) if (c0.drogue) nd++;
                        int id = 0;
                        foreach (var c0 in w.canopies)
                        {
                            if (!c0.drogue || nd < 2) continue;
                            float a0 = id * 6.2832f / nd + 0.4f; c0.ox = 15f * Mathf.Cos(a0); c0.oy = 15f * Mathf.Sin(a0); id++;
                        }

                        foreach (var c1 in w.canopies) { c1.sib = w.canopies; c1.spinRate = UnityEngine.Random.Range(-0.07f, 0.07f); }
                        if (rn != "B_DrogueRotator" && w.canopies.Count > 0) ChuteCloth.PreloadOpening(w.canopies.Count);
                        else if (w.canopies.Count > 0) ChuteCloth.PreloadDrogueOpening(w.canopies.Count);
                        if (w.canopies.Count > 0) ChuteCloth.PreloadDescent(rn == "B_DrogueRotator");
                        list.Add(w);
                    }
                    watched[p.flightID] = list;
                }
            }
            var gone = new List<uint>(); foreach (var k in watched.Keys) if (!seen.Contains(k)) gone.Add(k);
            foreach (var k in gone) watched.Remove(k);
        }

        static float OpenScale(Watch w)
        {
            float m = 0; foreach (var c in w.canopies) if (c.t != null) m = Math.Max(m, c.t.localScale.x);
            return m;
        }

        void Splashdown(Watch w, float now)
        {
            if (w.part == null || w.part.vessel == null || w.canopies.Count == 0 || w.canopies[0].drogue) return;
            var v = w.part.vessel; if (v.mainBody == null || !v.mainBody.ocean) return;
            ChuteCloth.Preload(w.canopies.Count);
            if (v.Splashed)
            {
                if (w.splashT < 0) { w.splashT = now; Debug.Log("[CaelusEntryDrag] ChuteFx: splashdown at " + (-v.verticalSpeed).ToString("F1") + " m/s - mains slack, releasing"); }
                w.pre = Mathf.MoveTowards(w.pre, 1f, Time.deltaTime * 3f);
                if (!w.cutFired && now - w.splashT > 0.35f) { w.cutFired = true; CutMains(w.part); }
                return;
            }
            float target = 0f, vs = (float)-v.verticalSpeed;
            if (v.pqsAltitude < 0 && vs > 0.5f)
            {
                float tImp = (float)((v.altitude - 1.5) / vs);
                target = Mathf.Clamp01(1f - (tImp - 0.2f) / 1.6f);
            }
            float was = w.pre;
            w.pre = Mathf.MoveTowards(w.pre, target, Time.deltaTime * 2f);
            if (was < 0.5f && w.pre >= 0.5f) Debug.Log("[CaelusEntryDrag] ChuteFx: mains going slack before splashdown, " + v.altitude.ToString("F1") + " m, " + vs.ToString("F1") + " m/s");
        }

        static void CutMains(Part p)
        {
            int n = 0;
            foreach (PartModule m in p.Modules)
                foreach (BaseEvent e in m.Events)
                {
                    if (!e.active || !e.guiActive) continue;
                    string g = (e.guiName ?? "").ToLowerInvariant();
                    if (g.Contains("cut") && g.Contains("chute")) { try { e.Invoke(); n++; } catch (Exception x) { Debug.LogWarning("[CaelusEntryDrag] ChuteFx: cut failed " + x.Message); } }
                }
            Debug.Log("[CaelusEntryDrag] ChuteFx: mains released on the water (" + n + " cut event(s))");
        }

        static void Dynamics(Canopy c, float now, float dt, Vector3 kick, float pre)
        {
            if (c.t == null || dt <= 0) return;
            if (c.t.localScale != c.wroteS || c.t.localRotation != c.wroteR) { c.restS = c.t.localScale; c.restR = c.t.localRotation; }
            if (c.drogue ? c.restS.z < 0.05f : c.restS.y < 0.02f)
            {
                c.openT = -1; c.lineT = -1f;
                if (c.meshTried) { c.opArm = true; c.op = null; c.opDone = false; c.ripK = 1f; }
                if (c.meshTried) { c.dl = null; c.dlNo = false; c.dlOk = false; c.dlW = c.dlWD = c.dlNext = 0f; c.cpK = 1f; c.ripK = 1f; }
                if (c.riser != null && c.drogue)
                {
                    if (c.riser.localRotation != c.rWroteR) c.rRestR = c.riser.localRotation;
                    c.rWroteR = c.rRestR * Quaternion.Euler(c.sx + c.ox, 0, c.sy + c.oy); c.riser.localRotation = c.rWroteR;
                }
                return;
            }
            if (c.openT < 0)
            {
                c.openT = now; c.rCur = c.restS.x; c.sx = c.sy = c.vx = c.vy = 0;
                c.delay = UnityEngine.Random.Range(0f, c.drogue ? 0.25f : 0.9f);
                c.k = UnityEngine.Random.Range(c.drogue ? 1.8f : 0.9f, c.drogue ? 2.6f : 1.8f);
                c.inflT = c.drogue ? UnityEngine.Random.Range(1.6f, 2.2f) : UnityEngine.Random.Range(6.4f, 7.8f); c.infl = 0f;
            }
            float age = now - c.openT;

            if (!c.drogue && c.lineT < 0f && (c.restS.y >= 0.9f || age > 3f)) c.lineT = now;
            float ux = c.drogue ? Mathf.Clamp01((age - c.delay) / c.inflT) : (c.lineT < 0f ? 0f : Mathf.Clamp01((now - c.lineT - c.delay) / c.inflT));
            c.infl = ux * ux * (3f - 2f * ux);

            if (c.op != null)
            {
                float tpo = c.drogue ? age - 0.3f * c.delay : ChuteCloth.HasDeploy(c.idx % ChuteCloth.NOpen) ? age - 0.25f * c.delay : (c.lineT < 0f ? 0f : now - c.lineT - c.delay);
                if (tpo >= c.op.dur) { c.op = null; c.opDone = true; c.ripK = 1f; if (c.drogue) c.rCur = c.restS.x; }
                else { c.op.tp = Mathf.Max(0f, tpo); c.infl = ChuteCloth.Openness(c.op); c.ripK = ChuteCloth.RipW(c.op); }
            }

            bool opOn = !c.drogue && (c.op != null || c.opArm) && !ChuteCloth.HasDeploy(c.idx % ChuteCloth.NOpen);
            c.emerge = opOn ? Mathf.Clamp01(age / 0.5f) : 1f;
            c.lineOut = opOn ? 1f - Mathf.Clamp01(Mathf.Max(c.restS.y / 0.9f, (age - 0.4f) / 1.0f)) : 0f;

            float tgt = c.restS.x;
            if (c.drogue && age < c.delay + 1.5f) tgt = Mathf.Min(tgt, 0.35f * c.restS.x);

            c.rCur += (tgt - c.rCur) * (1f - Mathf.Exp(-dt * (age < c.delay ? 0f : c.k)));

            {
                float rrW = c.restS.x > 1e-4f ? c.rCur / c.restS.x : 1f;
                if (!c.drogue && c.restS.x > 1e-4f) rrW = ChuteCloth.MainWidth(rrW, c.restS, c.infl, c.op, c.opDone);
                float wr = c.restS.y > 1e-4f ? c.restS.x * rrW / c.restS.y : 0f;
                c.dlOk = c.op == null && c.infl >= 0.999f && wr >= ChuteCloth.LoopFullW && c.mfC != null;
                c.dlW = Mathf.MoveTowards(c.dlW, c.dlOk && c.dl != null ? 1f : 0f, dt / ChuteCloth.LoopBlend);
                c.dlWD = c.dl == null ? 0f : ChuteCloth.LoopW(c.dlW) * (1f - pre);
                if (c.op == null) c.ripK = 1f + (ChuteCloth.LoopRip - 1f) * c.dlWD;
            }

            float thin = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((c.rCur - 0.04f) / 0.45f));
            float lively = 1f - Mathf.Clamp01(age / 8f);
            float flap = c.drogue ? 0.24f * thin + 0.05f * lively + 0.14f * pre : 0.03f * thin + 0.012f * lively + 0.14f * pre;
            flap *= c.ripK;
            float fa = c.drogue ? 6.5f : 1.1f;
            float fx = flap * (0.6f * Mathf.Sin(now * fa * 6.283f + c.phase) + 0.4f * Mathf.Sin(now * fa * 1.73f * 6.283f + 2.1f * c.phase));
            float fz = flap * (0.6f * Mathf.Sin(now * fa * 1.21f * 6.283f + 1.3f * c.phase) + 0.4f * Mathf.Sin(now * fa * 0.67f * 6.283f + 0.7f * c.phase));

            float open = c.drogue ? Mathf.Clamp01((c.rCur - 0.3f) / 0.4f) : Mathf.Clamp01((c.infl - 0.75f) / 0.25f);
            float f = c.drogue ? 1.1f : 0.28f, bre = open * (c.drogue ? 0.05f : 0.006f), ov = open * (c.drogue ? 0.05f : 0.012f);
            bre *= c.ripK; ov *= c.ripK;
            float s = c.drogue ? Mathf.Sin(now * f * 6.283f + c.phase) : (Mathf.PerlinNoise(now * 0.15f, c.phase * 3.1f) - 0.5f) * 2f, ang = now * 0.21f + c.phase;
            float bx = bre * s + ov * Mathf.Cos(2f * ang), bz = bre * s - ov * Mathf.Cos(2f * ang);

            float w0 = c.drogue ? 1.3f : 0.62f, z = c.drogue ? 0.22f : 0.22f + 0.6f * (1f - c.infl), lim = c.drogue ? 24f : 16f;
            c.sx -= Mathf.Clamp(kick.x, -6f, 6f) * 0.6f; c.sy -= Mathf.Clamp(kick.z, -6f, 6f) * 0.6f;
            float turb = (c.drogue ? 9f : 1.8f) * (0.4f + 0.6f * lively);
            c.vx += (-w0 * w0 * c.sx - 2f * z * w0 * c.vx + (Mathf.PerlinNoise(now * 0.35f, c.phase * 3f) - 0.5f) * turb * 4f) * dt;
            c.vy += (-w0 * w0 * c.sy - 2f * z * w0 * c.vy + (Mathf.PerlinNoise(c.phase * 3f, now * 0.35f) - 0.5f) * turb * 4f) * dt;
            c.sx = Mathf.Clamp(c.sx + c.vx * dt, -lim, lim); c.sy = Mathf.Clamp(c.sy + c.vy * dt, -lim, lim);
            Vector3 fdC; bool fanC = !c.drogue && ChuteCloth.MainFan(c.idx, out fdC);
            if (((c.dlWD > 0f && c.dl != null && c.dl.pose) || (fanC && c.lineT >= 0f)) && c.sib != null)
            {

                float mx = 0f, my = 0f, mvx = 0f, mvy = 0f; int nm = 0;
                foreach (var o in c.sib) if (o.drogue == c.drogue) { mx += o.sx; my += o.sy; mvx += o.vx; mvy += o.vy; nm++; }
                if (nm > 0)
                {
                    float kr = 1f - Mathf.Exp(-dt * 1.5f * (fanC && c.lineT >= 0f ? 1f : c.dlWD)), inv = 1f / nm;
                    c.sx += (mx * inv - c.sx) * kr; c.sy += (my * inv - c.sy) * kr; c.vx += (mvx * inv - c.vx) * kr; c.vy += (mvy * inv - c.vy) * kr;
                }
            }
            if (!c.drogue && open > 0.5f)
            {
                float ca = Mathf.Cos(dt * 0.075f), sa = Mathf.Sin(dt * 0.075f);
                float nx = c.sx * ca - c.sy * sa, ny = c.sx * sa + c.sy * ca; c.sx = nx; c.sy = ny;
                nx = c.vx * ca - c.vy * sa; ny = c.vx * sa + c.vy * ca; c.vx = nx; c.vy = ny;
            }
            float rr = c.restS.x > 1e-4f ? c.rCur / c.restS.x : 1f;

            if (!c.drogue && c.restS.x > 1e-4f) rr = ChuteCloth.MainWidth(rr, c.restS, c.infl, c.op, c.opDone);
            rr *= 1f - 0.2f * pre;
            Ripple(c, now, open, lively, pre);
            Vector3 sc = new Vector3(c.restS.x * rr * (1f + fx + bx), c.restS.y * (1f - 0.3f * bre * s - 0.06f * pre), c.restS.z * rr * (1f + fz + bz));
            if (c.op != null)
            {
                float U = Mathf.Max(c.restS.y, 1f);
                sc = new Vector3(U * (1f + fx + bx), U * (1f - 0.3f * bre * s - 0.06f * pre), U * (1f + fz + bz));
            }
            c.t.localScale = sc; c.wroteS = sc; c.wroteR = c.t.localRotation;

            if (!c.drogue && c.riser != null)
            {
                if (c.t.localPosition != c.wroteP) c.restP = c.t.localPosition;
                c.wroteP = c.restP * ChuteCloth.RiserK; c.t.localPosition = c.wroteP;
                if (!c.rScaleTried) { c.rScaleTried = true; foreach (Transform ch in c.riser) if (ch.name.Contains("RiserScale")) c.rScale = ch; }
                if (c.rScale != null)
                {
                    if (c.rScale.localScale != c.wroteRS) c.restRS = c.rScale.localScale;
                    Vector3 ap = c.restP; int ia = Mathf.Abs(ap.x) > Mathf.Abs(ap.y) ? (Mathf.Abs(ap.x) > Mathf.Abs(ap.z) ? 0 : 2) : (Mathf.Abs(ap.y) > Mathf.Abs(ap.z) ? 1 : 2);
                    Vector3 rs = c.restRS; rs[ia] *= ChuteCloth.RiserK; c.wroteRS = rs; c.rScale.localScale = rs;
                }
            }
            c.cutNow = c.drogue ? 0f : ChuteCloth.LineCut * (1f - pre);

            float spl = c.drogue ? 1f : 0.25f + 0.75f * c.infl;
            Quaternion q = Quaternion.Euler(c.sx + c.ox * spl, 0, c.sy + c.oy * spl);
            if (c.riser != null)
            {
                if (c.riser.localRotation != c.rWroteR) c.rRestR = c.riser.localRotation;
                Quaternion rw = c.rRestR * q;
                if (!c.drogue)
                {

                    Vector3 fd;
                    if (ChuteCloth.MainFan(c.idx, out fd) && c.rend != null && c.mfC != null)
                    {
                        int nmn = 0; if (c.sib != null) foreach (var o in c.sib) if (!o.drogue) nmn++;
                        float rW = c.R * Mathf.Abs(c.mfC.transform.lossyScale.x) * (0.2f + 0.8f * c.infl);

                        float Dp = c.cenOk ? (c.cenW - c.riser.position).magnitude : (c.rend.bounds.center - c.riser.position).magnitude - c.cutNow;
                        float sphi = nmn >= 2 && Dp > 1f ? Mathf.Clamp(0.75f * 2f * rW / (2f * Dp * Mathf.Sin(Mathf.PI / nmn)), 0f, 0.5f) : 0f;

                        float phi = Mathf.Asin(sphi);
                        c.fanPhi = c.fanPhi < 0f ? phi : c.fanPhi + (phi - c.fanPhi) * (1f - Mathf.Exp(-dt / 0.4f));
                        Vector3 hz = new Vector3(fd.x, 0f, fd.z); hz = hz.sqrMagnitude > 1e-8f ? hz.normalized : Vector3.right;

                        Transform pf = c.riser.parent;
                        if (pf != null && c.sib != null)
                        {
                            Vector3 ci = c.cenOk ? c.cenW : c.rend.bounds.center - c.riser.TransformDirection(Vector3.up) * c.cutNow, mv = Vector3.zero; int nn = 0;
                            foreach (var o in c.sib)
                            {
                                if (o == c || o.drogue || o.rend == null || !o.rend.enabled || o.mfC == null || o.riser == null) continue;
                                Vector3 cj = o.cenOk ? o.cenW : o.rend.bounds.center - o.riser.TransformDirection(Vector3.up) * o.cutNow, dv = cj - ci; float dist = dv.magnitude;
                                if (dist < 1e-3f) continue;
                                float rWo = o.R * Mathf.Abs(o.mfC.transform.lossyScale.x) * (0.2f + 0.8f * o.infl), need = 0.75f * (rW + rWo);
                                mv += dv / dist * (0.5f * (dist - need)); nn++;
                            }
                            if (nn > 0)
                            {
                                Vector3 mvL = pf.InverseTransformDirection(mv); mvL.y = 0f;
                                c.hOff += mvL * Mathf.Min(1f, dt * 0.9f);
                                c.hOff *= 1f - Mathf.Min(1f, dt * 0.05f);
                                if (c.hOff.magnitude > 4f) c.hOff = c.hOff.normalized * 4f;
                            }
                        }
                        Vector3 hp = hz * (Dp * Mathf.Sin(c.fanPhi)) + c.hOff;
                        float hv = Mathf.Min(hp.magnitude, Dp * 0.6f); if (hp.sqrMagnitude > 1e-8f) hp = hp.normalized * hv;
                        Vector3 dirF = (Vector3.up * Mathf.Sqrt(Mathf.Max(0.01f, Dp * Dp - hv * hv)) + hp).normalized;
                        rw = Quaternion.Euler(c.sx, 0, c.sy) * ChuteCloth.Arc(Vector3.up, dirF) * c.rRestR;
                        if (ChuteCloth.Diag && c.openT >= 0f && now - c.openT < 40f && now >= c.nextDiag)
                        {
                            c.nextDiag = now + 0.5f;
                            Debug.Log("[CaelusEntryDrag] ChuteFx diag " + c.t.name + " age " + (now - c.openT).ToString("F1") + " op " + (c.op != null ? c.op.tp.ToString("F1") : (c.opDone ? "done" : "-"))
                                + " infl " + c.infl.ToString("F2") + " phi " + (c.fanPhi * Mathf.Rad2Deg).ToString("F1") + " target " + (phi * Mathf.Rad2Deg).ToString("F1") + " sx " + c.sx.ToString("F1") + " sy " + c.sy.ToString("F1")
                                + " D " + Dp.ToString("F1") + " rW " + rW.ToString("F1") + " conf " + (c.t.position - c.riser.position).magnitude.ToString("F2") + " lp " + c.t.localPosition.ToString("F2") + " hOff " + c.hOff.ToString("F1") + " restS " + c.restS.ToString("F2") + " scale " + c.t.localScale.ToString("F2") + " dlWD " + c.dlWD.ToString("F2"));
                        }
                    }
                }

                if (c.dlWD > 0f && c.dl != null && c.dl.pose) rw = ChuteCloth.Nlerp(rw, Quaternion.Euler(c.sx, 0, c.sy) * ChuteCloth.Arc(Vector3.up, ChuteCloth.DescRiser(c.dl, now)), c.dlWD);
                c.rWroteR = rw; c.riser.localRotation = c.rWroteR;
            }
            else { c.t.localRotation = c.restR * q; c.wroteR = c.t.localRotation; }
        }

        static void Ripple(Canopy c, float now, float open, float lively, float pre)
        {
            if (!c.meshTried)
            {
                c.meshTried = true;
                foreach (var mf in c.t.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                    if (mf.name.Contains("Canopy") && c.mfC == null) { c.mfC = mf; var m = mf.mesh; c.c0 = m.vertices; c.cb = new Vector3[c.c0.Length]; }

                    if (mf.name.Contains("Canopy")) { var sr = mf.GetComponent<Renderer>(); if (sr != null) sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
                    else if (mf.name.Contains("Lines") && c.mfL == null) { c.mfL = mf; var m = mf.mesh; c.l0 = m.vertices; c.lb = new Vector3[c.l0.Length]; }
                }
                if (c.mfC != null)
                {

                    Vector3 mean = Vector3.zero; foreach (var v in c.c0) mean += v; mean /= Mathf.Max(1, c.c0.Length);
                    c.ax = Mathf.Abs(mean.y) > Mathf.Abs(mean.x) && Mathf.Abs(mean.y) > Mathf.Abs(mean.z) ? 1 : (Mathf.Abs(mean.x) > Mathf.Abs(mean.z) ? 0 : 2);
                    float sg = mean[c.ax] < 0 ? -1f : 1f;
                    if (c.ax != 2 || sg < 0)
                    {
                        for (int i = 0; i < c.c0.Length; i++) c.c0[i] = ToZ(c.c0[i], c.ax, sg);
                        if (c.l0 != null) for (int i = 0; i < c.l0.Length; i++) c.l0[i] = ToZ(c.l0[i], c.ax, sg);
                    }
                    c.sg = sg;
                    c.zs = 1e9f; c.zt = -1e9f; c.R = 0;
                    foreach (var v in c.c0) { if (v.z < c.zs) c.zs = v.z; if (v.z > c.zt) c.zt = v.z; float r = Mathf.Sqrt(v.x * v.x + v.y * v.y); if (r > c.R) c.R = r; }

                    c.ng = c.drogue ? 12 : 24; c.gph = 0;
                    if (c.l0 != null)
                    {

                        var azs = new List<float>();
                        foreach (var v in c.l0) if (v.z >= c.zs * 0.93f) azs.Add(Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg);
                        azs.Sort(); int nCl = 0; float first = 999f;
                        for (int q = 0; q < azs.Count; q++)
                            if (q == 0 || azs[q] - azs[q - 1] > 2.5f) { nCl++; if (azs[q] > -0.5f && azs[q] < first) first = azs[q]; }
                        if (azs.Count > 1 && (azs[0] + 360f) - azs[azs.Count - 1] <= 2.5f) nCl--;
                        if (nCl >= 6 && nCl <= 64) { c.ng = nCl; if (first < 90f) c.gph = first * Mathf.Deg2Rad; }
                    }
                    if (c.zt - c.zs < 1e-3f || c.R < 1e-3f || c.zs < 1e-3f) c.mfC = null;
                    else { var bb = c.mfC.sharedMesh.bounds; bb.Expand(c.R * 1.6f); c.mfC.sharedMesh.bounds = bb; }
                    if (c.mfC != null) FixSubMats(c.mfC.GetComponent<Renderer>(), c.mfC.sharedMesh, c.t.name);
                    if (c.mfL != null) { var lr = c.mfL.GetComponent<Renderer>(); if (lr != null) foreach (var lm in lr.materials) if (lm != null && lm.HasProperty("_Color")) lm.SetColor("_Color", new Color(1f, 1f, 1f, 1f)); }
                    Debug.Log("[CaelusEntryDrag] ChuteFx: ripple on " + c.t.name + " axis " + c.ax + " gores " + c.ng + " hem " + c.zs.ToString("F1") + " apex " + c.zt.ToString("F1") + " R " + c.R.ToString("F1") + " clip scale " + c.t.localScale.ToString("F2"));
                }
            }

            if (c.opArm && c.mfC != null)
            {
                c.opArm = false;
                if (!c.lthTried) { c.lthTried = true; c.lth = BuildThick(c.l0); }
                c.op = c.drogue ? ChuteCloth.StartOpening(c.idx % ChuteCloth.NDOpen, c.c0, c.l0, c.lth, c.ax, c.sg, c.t.name, true)
                                : ChuteCloth.StartOpening(c.idx % ChuteCloth.NOpen, c.c0, c.l0, c.lth, c.ax, c.sg, c.t.name);
                if (c.op != null)
                {
                    c.inflT = Mathf.Min(c.inflT, c.op.dur); c.op.tp = 0f; c.ripK = ChuteCloth.RipW(c.op); c.infl = ChuteCloth.Openness(c.op);
                }
            }

            if (c.dlOk && c.dl == null && !c.dlNo && c.mfC != null && now >= c.dlNext)
            {
                if (!c.lthTried) { c.lthTried = true; c.lth = BuildThick(c.l0); }

                float inAz = float.NaN; Transform bas = c.riser != null ? c.riser.parent : c.t.parent;
                if (bas != null) { Vector3 ia = ToZ(c.mfC.transform.InverseTransformDirection(bas.up), c.ax, c.sg); if (ia.x * ia.x + ia.y * ia.y > 1e-6f) inAz = Mathf.Atan2(ia.y, ia.x); }
                int st; c.dl = ChuteCloth.StartDescent(c.drogue, c.idx, c.c0, c.l0, c.lth, c.ax, c.sg, c.t.name, c.spin, inAz, c.riser != null, out st);
                if (c.dl == null) { if (st == 1) c.dlNext = now + 1f; else c.dlNo = true; }
            }
            if (c.mfC == null || c.rend == null || !c.rend.isVisible) return;

            if (c.inner == null && c.nextGlow >= 0f)
            {
                c.nextGlow = -1f; var rr_ = c.mfC.GetComponent<Renderer>();
                if (rr_ != null) foreach (var mm in rr_.materials) if (mm != null && mm.name.Contains("Inner") && mm.HasProperty("_EmissiveColor")) c.inner = mm;
            }
            if (c.inner != null)
            {
                c.glowCur = c.glowCur < 0f ? c.glowTgt : Mathf.Lerp(c.glowCur, c.glowTgt, Mathf.Clamp01(Time.deltaTime * 0.8f));
                c.inner.SetColor("_EmissiveColor", new Color(c.glowCur, c.glowCur * 0.985f, c.glowCur * 0.96f, 1f));
            }
            if (c.inner != null && now > c.nextGlow + 0.25f)
            {
                c.nextGlow = now; var v_ = FlightGlobals.ActiveVessel; float g_ = 0f;
                if (v_ != null && Planetarium.fetch != null && Planetarium.fetch.Sun != null)
                {
                    Vector3 sunDir = ((Vector3)(Planetarium.fetch.Sun.position - (Vector3d)c.mfC.transform.position)).normalized;
                    Vector3 apexDir = c.mfC.transform.TransformDirection(FromZ(new Vector3(0, 0, 1), c.ax, c.sg)).normalized;
                    float through = Mathf.Clamp01(Vector3.Dot(sunDir, apexDir) * 0.8f + 0.2f);
                    Vector3 upW = ((Vector3)(c.mfC.transform.position - v_.mainBody.position)).normalized;
                    float elev = Mathf.Clamp01(Vector3.Dot(sunDir, upW) * 4f + 0.2f);
                    g_ = (v_.directSunlight ? 1f : 0.2f) * elev * (0.10f + 0.32f * through);
                }
                c.glowTgt = g_;
            }
            c.preFl = pre; c.openF = open;
            float sp = c.drogue ? 2.4f : 0.55f;
            float amp = c.R * ((c.drogue ? 0.022f : 0.010f) + (c.drogue ? 0.03f : 0.012f) * lively + 0.11f * pre + (c.drogue ? 0.06f : 0.015f) * (1f - open));
            float t = now * sp, ph = c.phase;

            float ga = 6.283f * 2f * Mathf.PerlinNoise(now * 0.11f * sp, ph * 5f), gs = c.R * 0.09f * Mathf.PerlinNoise(ph * 7f, now * 0.4f * sp) * (0.6f + 0.4f * lively + pre);

            if (c.dl == null) c.spin += Time.deltaTime * c.spinRate; else c.spin = c.dl.s0 + (c.dl.thT - c.dl.s0) * c.dlWD;
            c.cpK = c.dl != null && c.dl.pose ? 1f - c.dlWD : 1f;

            c.ncp = 0;
            if (c.sib != null && !c.drogue)
            {
                var mt = c.mfC.transform; float rw = c.R * Mathf.Abs(mt.lossyScale.x);
                Vector3 oc = c.rend.bounds.center;
                foreach (var o in c.sib)
                {
                    if (o == c || o.rend == null || !o.rend.enabled || o.mfC == null || c.ncp >= 2) continue;
                    Vector3 occ = o.rend.bounds.center; float dd = (occ - oc).magnitude;
                    if (dd > rw * 2f || dd < 1e-3f) continue;
                    Vector3 mL = ToZ(mt.InverseTransformPoint((oc + occ) * 0.5f), c.ax, c.sg), aL = ToZ(mt.InverseTransformPoint(oc), c.ax, c.sg), bL = ToZ(mt.InverseTransformPoint(occ), c.ax, c.sg);
                    Vector3 nL = (bL - aL).normalized;
                    if (c.ncp == 0) { c.cpM0 = mL; c.cpN0 = nL; } else { c.cpM1 = mL; c.cpN1 = nL; }
                    c.ncp++;
                }
            }
            if (c.op != null)
            {
                ChuteCloth.OpenFrame(c.op, c.c0); int nvo = c.op.bk.nv;
                if (c.lineOut > 0f || c.emerge < 1f)
                {
                    var w_ = c.op.work; float zmin = 1e9f, zmax = -1e9f;
                    for (int i = 0; i < nvo; i++) { float zz = w_[i].z; if (zz < zmin) zmin = zz; if (zz > zmax) zmax = zz; }
                    float pp = 1f - c.lineOut, spp = pp * pp * (3f - 2f * pp), qe = c.emerge * c.emerge * (3f - 2f * c.emerge);
                    float B = -(zmax - zmin) * (1f - qe) + zmin * spp;
                    const float neck = 0.6f;
                    for (int i = 0; i < nvo; i++)
                    {
                        Vector3 v = w_[i]; float z2 = B + (v.z - zmin);
                        if (z2 >= neck) { w_[i] = new Vector3(v.x, v.y, z2); continue; }
                        float k = z2 <= 0f ? 0f : z2 / neck; k = k * k * (3f - 2f * k);
                        w_[i] = new Vector3(v.x * k, v.y * k, Mathf.Max(0f, z2));
                    }
                }
                DeformN(c, c.op.work, c.cb, nvo, t, ph, amp, ga, gs);
                for (int i = 0; i < nvo; i++) c.cb[i + nvo] = c.cb[i];
            }
            else if (c.dl != null && c.dlWD > 0f)
            {
                ChuteCloth.DescFrame(c.dl, c.c0, now, c.dlWD); int nvd = c.dl.bk.nv;
                DeformN(c, c.dl.work, c.cb, nvd, t, ph, amp, ga, gs);
                for (int i = 0; i < nvd; i++) c.cb[i + nvd] = c.cb[i];
            }
            else Deform(c, c.c0, c.cb, t, ph, amp, ga, gs);
            if (c.cutNow > 0f && c.zs > 1e-3f)
            {

                int ax = c.ax; float inv = 1f / c.zs;
                for (int i = 0; i < c.cb.Length; i++) { Vector3 v = c.cb[i]; float fr = Mathf.Clamp01(v[ax] * c.sg * inv); v[ax] -= c.sg * c.cutNow * fr; c.cb[i] = v; }
            }
            var mesh = c.mfC.sharedMesh; mesh.vertices = c.cb;
            {
                Vector3 sum = Vector3.zero; int nS = 0, half = c.cb.Length / 2;
                for (int i = 0; i < half; i += 37) { sum += c.cb[i]; nS++; }
                if (nS > 0) { c.cenW = c.mfC.transform.TransformPoint(sum / nS); c.cenOk = true; }
            }
            mesh.RecalculateNormals();
            if (c.mfL != null)
            {
                Vector3[] lsrc = c.l0;
                if (!c.lthTried) { c.lthTried = true; c.lth = BuildThick(c.l0); }
                if (c.lth != null)
                {
                    float kk = ThickK(c.mfL.transform, c.mfL.transform.TransformPoint(FromZ(new Vector3(0, 0, c.zs * 0.5f), c.ax, c.sg)), c.lth.w);
                    if (c.op != null) lsrc = ChuteCloth.OpenLines(c.op, c.lth, kk);
                    else if (c.dl != null && c.dlWD > 0f) lsrc = ChuteCloth.DescLines(c.dl, c.lth, kk);
                    else lsrc = Thick(c.lth, kk);
                }
                Deform(c, lsrc, c.lb, t, ph, amp, ga, gs);
                if (c.cutNow > 0f && c.zs > 1e-3f)
                {
                    int ax = c.ax; float inv = 1f / c.zs;
                    for (int i = 0; i < c.lb.Length; i++) { Vector3 v = c.lb[i]; float zw = v[ax] * c.sg; float fr = Mathf.Clamp01(zw * inv); v[ax] -= c.sg * c.cutNow * fr; c.lb[i] = v; }
                }
                c.mfL.sharedMesh.vertices = c.lb;
            }
        }

        internal static Vector3 ToZ(Vector3 v, int ax, float sg) { return ax == 1 ? new Vector3(v.x, v.z, v.y * sg) : ax == 0 ? new Vector3(v.z, v.y, v.x * sg) : new Vector3(v.x, v.y, v.z * sg); }
        internal static Vector3 FromZ(Vector3 v, int ax, float sg) { return ax == 1 ? new Vector3(v.x, v.z * sg, v.y) : ax == 0 ? new Vector3(v.z * sg, v.y, v.x) : new Vector3(v.x, v.y, v.z * sg); }
        static void Deform(Canopy c, Vector3[] src, Vector3[] dst, float t, float ph, float amp, float ga, float gs) { DeformN(c, src, dst, src.Length, t, ph, amp, ga, gs); }

        static void DeformN(Canopy c, Vector3[] src, Vector3[] dst, int n, float t, float ph, float amp, float ga, float gs)
        {
            float zs = c.zs, ih = 1f / (c.zt - zs), rk = c.ripK;
            for (int i = 0; i < n; i++)
            {
                Vector3 v = src[i];
                float r = Mathf.Sqrt(v.x * v.x + v.y * v.y);
                if (r < 1e-4f) { dst[i] = FromZ(v, c.ax, c.sg); continue; }
                float w = v.z < zs ? Mathf.Clamp01(v.z / zs) : Mathf.Clamp01(1f - (v.z - zs) * ih);
                float az = Mathf.Atan2(v.y, v.x);
                if (c.spin != 0f) { float cs_ = Mathf.Cos(c.spin), sn_ = Mathf.Sin(c.spin); v = new Vector3(v.x * cs_ - v.y * sn_, v.x * sn_ + v.y * cs_, v.z); az += c.spin; }
                float wave = 0.55f * Mathf.Sin(3f * az + 1.9f * t + ph) + 0.30f * Mathf.Sin(5f * az - 2.7f * t + 1.7f * ph) + 0.15f * Mathf.Sin(8f * az + 4.1f * t + 0.6f * ph);
                float gc = Mathf.Cos(az - ga); gc = gc > 0 ? gc * gc : 0;
                float w2 = w * w;

                float ga2 = (az - c.gph) * c.ng; float gore = 0.5f - 0.5f * Mathf.Cos(ga2);
                float gi = Mathf.Floor(ga2 / 6.2832f); float gb = 0.9f + 0.1f * (Mathf.PerlinNoise(0.4f * t + gi * 1.7f, ph) - 0.5f) * 2f;
                float lobe = Mathf.Sin(2f * az + 0.6f * t + ph) * 0.6f + Mathf.Sin(az - 0.37f * t + 2f * ph) * 0.4f;
                float wb = Mathf.Sqrt(w);
                float dr = amp * w * wb * wave - gs * gc * w + amp * 0.22f * w2 * w * Mathf.Sin(11f * az - 9f * t + 2.3f * ph) * (c.drogue ? 1f : Mathf.Clamp01(0.25f * (1f - c.openF) + 2f * c.preFl)) + c.R * 0.012f * w2 * gore * gb + amp * 0.55f * wb * w * lobe;
                float dz = amp * 0.55f * w2 * Mathf.Sin(4f * az - 2.3f * t + ph) + gs * 0.35f * gc * w2 + c.R * 0.008f * w2 * gore * gb;
                float k = 1f + rk * dr / r;
                Vector3 p = new Vector3(v.x * k, v.y * k, v.z + rk * dz);
                if (c.infl < 0.999f && c.op == null) p = Inflate(c, p, v, az, zs, ih, t, ph);

                if (c.ncp > 0) { float d0 = Vector3.Dot(p - c.cpM0, c.cpN0); if (d0 > -0.6f) p -= c.cpN0 * ((d0 > 0f ? d0 + 0.3f : 0.3f * (1f + d0 / 0.6f) * (1f + d0 / 0.6f)) * c.cpK); }
                if (c.ncp > 1) { float d1 = Vector3.Dot(p - c.cpM1, c.cpN1); if (d1 > -0.6f) p -= c.cpN1 * ((d1 > 0f ? d1 + 0.3f : 0.3f * (1f + d1 / 0.6f) * (1f + d1 / 0.6f)) * c.cpK); }
                dst[i] = FromZ(p, c.ax, c.sg);
            }
        }

        static Vector3 Inflate(Canopy c, Vector3 p, Vector3 v, float az, float zs, float ih, float t, float ph)
        {
            float u = c.infl, tc = t * 0.35f;
            bool line = v.z < zs;
            float zn = line ? 0f : Mathf.Clamp01((v.z - zs) * ih);
            float li = Mathf.Clamp01(u * 1.4f - (1f - zn) * 0.4f); li = li * li * (3f - 2f * li);
            float closed = 1f - li;
            float pleat = 0.5f - 0.5f * Mathf.Cos((az - c.gph) * c.ng);
            float crk = (Mathf.PerlinNoise(az * 3.1f + ph * 7f, zn * 2.2f + tc) - 0.5f) * 2f * 0.45f
                      + (Mathf.PerlinNoise(az * 9.7f - ph * 3f, zn * 6.5f - tc * 1.3f) - 0.5f) * 2f * 0.25f
                      + (Mathf.PerlinNoise(az * 23f + ph, zn * 14f + tc * 0.7f) - 0.5f) * 2f * 0.12f
                      + (Mathf.PerlinNoise(az * 1.6f + zn * 3.5f + ph, tc * 0.8f) - 0.5f) * 2f * 0.40f;

            float r0 = line ? 0.88f * c.R : Mathf.Sqrt(v.x * v.x + v.y * v.y);
            float cz = Mathf.Clamp01((zn - 0.86f) / 0.14f), cap = zn < 0.86f ? 1f : Mathf.Sqrt(Mathf.Max(0f, 1f - cz * cz));
            float rT = 0.26f * c.R * cap;
            float rN = Mathf.Lerp(rT, r0, li) * (1f - closed * (0.38f * pleat + 0.42f * crk));

            float mid2 = 4f * u * (1f - u), cb = Mathf.Clamp01((zn - 0.5f) / 0.35f); cb = cb * cb * (3f - 2f * cb);
            rN *= (1f + 0.14f * mid2 * cb) * (1f + mid2 * 0.12f * (Mathf.PerlinNoise(az * 2.2f + ph, zn * 2.5f + tc) - 0.5f) * 2f);
            float f = r0 > 1e-4f ? rN / r0 : 1f;

            float hemW = 1f - Mathf.SmoothStep(0f, 1f, zn / 0.35f), mid = 4f * u * (1f - u);
            f *= 1f + hemW * mid * (0.16f * Mathf.Sin(5f * az + 2.4f * Mathf.PerlinNoise(az * 0.8f + ph, tc)) + 0.10f * Mathf.Sin(13f * az + ph * 3f + tc));
            if (line) { float wl = zs > 1e-3f ? Mathf.Clamp01(v.z / zs) : 1f; f = Mathf.Lerp(1f, f, wl); return new Vector3(p.x * f, p.y * f, p.z); }

            float zf = (1f + 0.5f * (1f - u) * (1f - u) * zn) * (1f - 0.32f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(u * 1.2f)));
            float dz = closed * c.R * (0.05f * (Mathf.PerlinNoise(az * 5f + ph, zn * 7f + tc) - 0.5f) * 2f + 0.05f * Mathf.Sin(zn * 22f + az * 2f + 3f * Mathf.PerlinNoise(az * 1.3f + ph, zn * 2f + tc)));
            float bx = closed * zn * c.R * 0.14f * (Mathf.PerlinNoise(zn * 1.3f + tc * 0.5f, ph * 3f) - 0.5f) * 2f;
            float by = closed * zn * c.R * 0.14f * (Mathf.PerlinNoise(ph * 5f, zn * 1.3f - tc * 0.5f) - 0.5f) * 2f;
            return new Vector3(p.x * f + bx, p.y * f + by, zs + (p.z - zs) * zf + dz);
        }

        static void Unripple(Canopy c)
        {
            if (c.mfC != null && c.c0 != null) { var m = c.mfC.sharedMesh; if (m != null) { var o = new Vector3[c.c0.Length]; for (int i = 0; i < o.Length; i++) o[i] = FromZ(c.c0[i], c.ax, c.sg); m.vertices = o; m.RecalculateNormals(); } }
            if (c.mfL != null && c.l0 != null) { var m = c.mfL.sharedMesh; if (m != null) { var o = new Vector3[c.l0.Length]; for (int i = 0; i < o.Length; i++) o[i] = FromZ(c.l0[i], c.ax, c.sg); m.vertices = o; } }
        }

        static readonly HashSet<string> subMatLogged = new HashSet<string>();
        static void FixSubMats(Renderer r, Mesh m, string tag)
        {
            if (r == null || m == null) return;
            var ms = r.sharedMaterials; int n = m.subMeshCount;
            bool bad = n > 1 && ms.Length > 0 && ms[0] != null && (ms.Length < n || ms[1] == null);
            string key = (r.name ?? "") + (bad ? " bad" : " ok");
            if (subMatLogged.Add(key))
            {
                string nm = ""; foreach (var x in ms) nm += (x == null ? "null" : x.name + " [" + (x.shader != null ? x.shader.name : "-") + "]") + "; ";
                Debug.Log("[CaelusEntryDrag] ChuteFx: canopy materials on " + tag + ": " + n + " submeshes, " + ms.Length + " materials: " + nm + (bad ? "inner shell had no material - using the outer fabric" : "ok"));
            }
            if (!bad) return;
            var fx = new Material[n]; for (int i = 0; i < n; i++) fx[i] = i < ms.Length && ms[i] != null ? ms[i] : ms[0];
            r.sharedMaterials = fx;
        }

        class Pop { public GameObject go; public Transform anchor; public Vector3 off, vel, spinAx; public float spin, age; }
        static readonly List<Pop> pops = new List<Pop>();
        void PopCap(Watch w, uint partId)
        {
            try
            {
                Transform rig = w.rot != null ? w.rot.parent : null; if (rig == null || w.part == null) return;
                Transform cap = null;
                foreach (Transform ch in rig) if (ch.name == "ChuteCap" || ch.name == "DrogueCap") cap = ch;
                if (cap == null) return;
                var go = Instantiate(cap.gameObject, cap.position, cap.rotation);
                go.transform.localScale = cap.lossyScale; go.SetActive(true);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
                foreach (var col in go.GetComponentsInChildren<Collider>(true)) Destroy(col);
                Vector3 trail = w.part.vessel != null && w.part.vessel.srf_velocity.sqrMagnitude > 1.0 ? -((Vector3)w.part.vessel.srf_velocity).normalized : w.rot.up;
                Vector3 side = Vector3.Cross(trail, UnityEngine.Random.onUnitSphere).normalized;
                pops.Add(new Pop { go = go, anchor = w.part.transform, off = cap.position - w.part.transform.position,
                                   vel = trail * UnityEngine.Random.Range(11f, 15f) + side * UnityEngine.Random.Range(1.5f, 3.5f),
                                   spinAx = UnityEngine.Random.onUnitSphere, spin = UnityEngine.Random.Range(280f, 520f) });
            }
            catch (Exception e) { Debug.LogWarning("[CaelusEntryDrag] ChuteFx: cover jettison skipped - " + e.Message); }
        }
        static void StepPops(float dt)
        {
            for (int i = pops.Count - 1; i >= 0; i--)
            {
                var p = pops[i]; p.age += dt;
                if (p.go == null || p.anchor == null || p.age > 6f) { if (p.go != null) Destroy(p.go); pops.RemoveAt(i); continue; }

                Vector3 trail = p.vel.sqrMagnitude > 1e-4f ? p.vel.normalized : Vector3.up;
                p.vel += trail * 6f * dt - p.vel * 0.15f * dt;
                p.off += p.vel * dt;
                p.go.transform.position = p.anchor.position + p.off;
                p.go.transform.rotation = Quaternion.AngleAxis(p.spin * dt, p.spinAx) * p.go.transform.rotation;
            }
        }

        static void Separate(Canopy a, Canopy b, float dt)
        {
            if (a.rend == null || b.rend == null || !a.rend.enabled || !b.rend.enabled || a.rCur <= 0f || b.rCur <= 0f) return;
            if (a.dlWD > 0.5f && b.dlWD > 0.5f && a.dl != null && b.dl != null && a.dl.pose && b.dl.pose) return;

            if (!a.drogue && !b.drogue) return;
            Bounds A = a.rend.bounds, B = b.rend.bounds;

            float rA = Mathf.Max(A.extents.x, Mathf.Max(A.extents.y, A.extents.z)), rB = Mathf.Max(B.extents.x, Mathf.Max(B.extents.y, B.extents.z));
            if (a.mfC != null) rA = a.R * Mathf.Abs(a.mfC.transform.lossyScale.x); if (b.mfC != null) rB = b.R * Mathf.Abs(b.mfC.transform.lossyScale.x);

            rA *= (!a.drogue && a.op != null) ? 0.2f + 0.8f * a.infl : 0.3f + 0.7f * a.infl * a.infl;
            rB *= (!b.drogue && b.op != null) ? 0.2f + 0.8f * b.infl : 0.3f + 0.7f * b.infl * b.infl;
            Vector3 d = A.center - B.center; float dist = d.magnitude, need = (rA + rB) * (a.drogue ? 1.08f : 0.85f);
            if (!sepSeen) { sepSeen = true; if (false) Debug.Log("[CaelusEntryDrag] ChuteFx: separation uses '" + a.rend.name + "' ext " + A.extents.ToString("F2") + " / '" + b.rend.name + "' ext " + B.extents.ToString("F2")); }
            float ratio = dist / Math.Max(1e-3f, rA + rB); if (ratio < sepMin) { sepMin = ratio; sepKind = a.drogue ? "drogue" : "main"; }
            if (dist < need) sepPush++;
            if (dist >= need || dist < 1e-3f) return;

            float frac = (need - dist) / need;

            if (a.drogue) { Kick(a, d / dist, frac * 1200f * dt, frac * 60f * dt); Kick(b, -d / dist, frac * 1200f * dt, frac * 60f * dt); }
            else { float sh = Mathf.Min(frac * 160f, 8f) * dt; Kick(a, d / dist, 0f, sh); Kick(b, -d / dist, 0f, sh); }
        }

        static void Kick(Canopy c, Vector3 n, float push, float shift)
        {
            Transform frame = c.riser != null ? c.riser.parent : c.t.parent;
            if (frame == null) return;
            Vector3 l = Quaternion.Inverse(frame.rotation * (c.riser != null ? c.rRestR : c.restR)) * n;
            float ux = l.z, uy = -l.x, inward = c.vx * ux + c.vy * uy;
            if (inward < 0f) { c.vx -= inward * ux; c.vy -= inward * uy; }
            c.vx += push * ux; c.vy += push * uy;
            c.sx += shift * ux; c.sy += shift * uy;
        }

        void SpawnFalling(Watch w, uint partId)
        {
            Part p = null;
            foreach (var v in FlightGlobals.VesselsLoaded) foreach (var pp in v.parts) if (pp.flightID == partId) p = pp;
            if (p == null || p.vessel == null || p.vessel.mainBody == null) return;
            var go = (GameObject)Instantiate(w.rot.gameObject);
            go.name = w.rot.name + "_CutAway";
            go.transform.SetParent(null, false);
            go.transform.position = w.pos; go.transform.rotation = w.rotW; go.transform.localScale = w.lossy;
            foreach (var col in go.GetComponentsInChildren<Collider>(true)) Destroy(col);
            go.SetActive(true);
            var body = p.vessel.mainBody;
            bool drogue = w.rot.name == "B_DrogueRotator";

            float rHull = 2f;
            if (p.vessel.LandedOrSplashed)
            {
                Bounds hb = new Bounds(p.transform.position, Vector3.zero); bool any = false;
                foreach (var col in p.GetComponentsInChildren<Collider>(false)) { if (col == null || col.isTrigger) continue; if (!any) { hb = col.bounds; any = true; } else hb.Encapsulate(col.bounds); }
                Vector3 upP = (p.transform.position - body.position).normalized;
                if (any) rHull = Mathf.Clamp(Mathf.Max(Vector3.ProjectOnPlane(hb.extents, upP).magnitude * 0.72f, 1.2f), 1.2f, 4f);
            }
            falling.Add(new Falling { go = go, body = body, rel = (Vector3d)w.pos - body.position, vel = p.vessel.srf_velocity, t0 = Time.time, drogue = drogue, s0 = go.transform.localScale,
                                      life = p.vessel.LandedOrSplashed ? 420f : 60f,
                                      anchor = p.vessel.Splashed ? w.rot : null, part = p, floatNow = p.vessel.Splashed, rHull = rHull });
            Debug.Log("[CaelusEntryDrag] ChuteFx: " + w.rot.name + " cut away at " + p.vessel.altitude.ToString("F0") + " m");
        }

        void StepFalling(float now)
        {
            float dt = Time.deltaTime; if (dt <= 0) return;
            for (int i = falling.Count - 1; i >= 0; i--)
            {
                var f = falling[i];
                float age = now - f.t0;
                if (f.go == null || age > f.life) { Remove(f); falling.RemoveAt(i); continue; }
                Vector3d pos = f.body.position + f.rel;
                if (f.floatT >= 0 || f.floatNow || (f.body.ocean && f.body.GetAltitude(pos) <= 0.3)) { Float(f, now, pos); continue; }
                Vector3d g = FlightGlobals.getGeeForceAtPosition(pos);

                double vt = f.drogue ? 6.0 : 3.5, sp = f.vel.magnitude;
                Vector3d drag = sp > 1e-3 ? -f.vel / sp * (g.magnitude * (sp * sp) / (vt * vt)) : Vector3d.zero;
                f.vel += (g + drag) * dt;
                if (f.vel.magnitude > 400) f.vel = f.vel.normalized * 400;
                f.rel += f.vel * dt;
                var tr = f.go.transform;
                tr.position = f.body.position + f.rel;

                Vector3 up = sp > 0.5 ? (Vector3)(-f.vel.normalized) : tr.forward;
                Quaternion want = Quaternion.LookRotation(up, tr.up) * Quaternion.Euler(10f * Mathf.Sin(age * 0.9f), 8f * Mathf.Sin(age * 0.6f + 1f), 0);
                tr.rotation = Quaternion.Slerp(tr.rotation, want, Mathf.Clamp01(dt * 1.5f));

                float col = Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(age / 4f)), fl = 1f + 0.12f * Mathf.Sin(age * 7.3f) * Mathf.Clamp01(age / 2f);
                tr.localScale = new Vector3(f.s0.x * col * fl, f.s0.y * col / fl, f.s0.z * Mathf.Lerp(1f, 0.85f, Mathf.Clamp01(age / 4f)));
                if (FlightGlobals.ActiveVessel != null && Vector3d.Distance(pos, FlightGlobals.ActiveVessel.GetWorldPos3D()) > 6000) { Remove(f); falling.RemoveAt(i); }
            }
        }

        static void Float(Falling f, float now, Vector3d pos)
        {
            var tr = f.go.transform;
            Vector3d upD = (pos - f.body.position).normalized; Vector3 up = (Vector3)upD;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (f.floatT < 0)
            {
                f.floatT = now; f.vel = Vector3d.zero; f.seed = UnityEngine.Random.value * 100f;
                Vector3 h0 = Vector3.ProjectOnPlane(tr.forward, up); if (h0.sqrMagnitude < 1e-4f) h0 = Vector3.ProjectOnPlane(tr.right, up);
                f.flatFwd = h0.normalized;
                float ba = UnityEngine.Random.value * 6.283f;
                f.breeze = (Vector3.ProjectOnPlane(new Vector3(Mathf.Cos(ba), 0, Mathf.Sin(ba)), up).normalized) * 0.7f;
                f.mfs = new List<MeshFilter>(); f.v0 = new List<Vector3[]>(); f.buf = new List<Vector3[]>(); f.rends = new List<Renderer>(); f.mats = new List<Material>(); f.sims = new List<Sim>();
                foreach (var mf in f.go.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                    var m = mf.mesh;
                    m.MarkDynamic();
                    f.mfs.Add(mf); f.v0.Add(m.vertices); f.buf.Add(new Vector3[m.vertexCount]);
                    if (f.uvs == null) { f.uvs = new List<Vector2[]>(); f.merid = new List<float>(); }
                    var uv_ = m.uv; f.uvs.Add(uv_);

                    float ml = 0f;
                    if (mf.name.Contains("Canopy") && uv_ != null && uv_.Length == m.vertexCount)
                    {
                        var col = new List<KeyValuePair<float, Vector3>>(); var vv_ = m.vertices;
                        for (int q = 0; q < vv_.Length; q++) if (Mathf.Abs(uv_[q].x) < 1e-4f) col.Add(new KeyValuePair<float, Vector3>(uv_[q].y, vv_[q]));
                        col.Sort((x1, x2) => x1.Key.CompareTo(x2.Key));
                        for (int q = 1; q < col.Count; q++) if (col[q].Key > col[q - 1].Key + 1e-5f) ml += (col[q].Value - col[q - 1].Value).magnitude;
                    }
                    f.merid.Add(ml);
                }
                foreach (var rr in f.go.GetComponentsInChildren<Renderer>(true))
                {
                    f.rends.Add(rr);
                    if (rr.name.Contains("Canopy")) { var mfx = rr.GetComponent<MeshFilter>(); if (mfx != null) FixSubMats(rr, mfx.sharedMesh, rr.name + " (on the water)"); }
                    if (rr.sharedMaterial == null) continue;

                    foreach (var mm in rr.materials)
                    {
                        if (mm == null || !mm.HasProperty("_Color")) continue;
                        f.mats.Add(mm);
                        if (mm.HasProperty("_EmissiveColor")) { if (f.emis == null) f.emis = new List<KeyValuePair<Material, Color>>(); f.emis.Add(new KeyValuePair<Material, Color>(mm, mm.GetColor("_EmissiveColor"))); }
                    }
                }
                var all = f.go.GetComponentsInChildren<Transform>(true);
                foreach (var root in all)
                {
                    if (!root.name.StartsWith("CLD_") || !root.name.Contains("Root") || root.name.Contains("RiserRoot")) continue;
                    Transform riser = root.parent != null && root.parent.name.Contains("RiserRoot") ? root.parent : null;
                    Renderer canopyR = null; foreach (var rd in root.GetComponentsInChildren<Renderer>(true)) if (rd.name.Contains("Canopy")) canopyR = rd;
                    if (canopyR == null) continue;
                    Vector3 c0 = canopyR.bounds.center - tr.position;
                    var sim = new Sim { root = root, riser = riser, L = c0.magnitude, rc = c0, s0 = root.localScale };
                    sim.axisL = Quaternion.Inverse((riser != null ? riser : root).rotation) * c0.normalized;

                    Transform chainT = riser != null ? riser : root; sim.chainS0 = chainT.localScale;
                    Vector3 al = new Vector3(Mathf.Abs(sim.axisL.x), Mathf.Abs(sim.axisL.y), Mathf.Abs(sim.axisL.z));
                    sim.chainAx = al.x > al.y && al.x > al.z ? 0 : (al.z > al.y ? 2 : 1);
                    var cb = canopyR.bounds; sim.rCan = Mathf.Clamp(Mathf.Max(cb.extents.x, cb.extents.z, cb.extents.y) * 0.9f, 3f, 25f);

                    var cmf = canopyR.GetComponent<MeshFilter>();
                    if (cmf != null && cmf.sharedMesh != null)
                    {
                        Bounds bb = cmf.sharedMesh.bounds; Vector3 bc = bb.center; int bax = Mathf.Abs(bc.x) > Mathf.Abs(bc.y) && Mathf.Abs(bc.x) > Mathf.Abs(bc.z) ? 0 : (Mathf.Abs(bc.z) > Mathf.Abs(bc.y) ? 2 : 1);
                        if (Mathf.Abs(bc[bax]) > 1e-3f) sim.skirtFrac = Mathf.Clamp01((bc[bax] > 0 ? bb.min[bax] : bb.max[bax]) / bc[bax]);
                    }
                    Vector3 outw = Vector3.ProjectOnPlane(c0, up); if (outw.sqrMagnitude > 1e-4f) outw = outw.normalized;
                    sim.v = outw * 0.6f + f.breeze;
                    f.sims.Add(sim);
                }
                f.mfSim = new List<int>();
                foreach (var mf in f.mfs) { int k = -1; for (int s = 0; s < f.sims.Count; s++) if (mf != null && mf.transform.IsChildOf(f.sims[s].root)) k = s; f.mfSim.Add(k); }
                SetupPlayback(f, up);
                Debug.Log("[CaelusEntryDrag] ChuteFx: " + f.go.name + " on the water, " + f.sims.Count + " canopies settling" + (f.anchor != null ? ", tied to the capsule, hull r " + f.rHull.ToString("F1") + " m" : ""));
            }
            float age = now - f.floatT;
            bool anchored = f.anchor != null && f.part != null && f.part.vessel != null && f.part.vessel.LandedOrSplashed;
            if (anchored) { tr.position = f.anchor.position; f.rel = (Vector3d)f.anchor.position - f.body.position; }
            else
            {
                double r = f.body.Radius + 0.15 * Math.Sin(now * 0.7 + f.t0) - 0.1;
                f.rel = upD * r;
                tr.position = f.body.position + f.rel;
            }

            float seaOff = anchored ? -(float)f.body.GetAltitude(tr.position) : 0f;
            float g = (float)FlightGlobals.getGeeForceAtPosition(pos).magnitude;

            for (int a = 0; a < f.sims.Count; a++)
            {
                var c = f.sims[a];
                if (c.pb != null) { PlaySim(c, now); continue; }
                float hc = Vector3.Dot(c.rc, up), surf = seaOff + SeaHeight(Vector3.Dot(c.rc, f.flatFwd), Vector3.Dot(c.rc, Vector3.Cross(up, f.flatFwd)), now, f.seed);
                if (c.landT < 0)
                {

                    float tc = anchored ? 3f : 8f, dmin = anchored ? 0.58f : 0.72f;
                    c.defl = Mathf.Lerp(1f, dmin, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / tc)));
                    float lost = (1f - c.defl) / (1f - dmin);
                    float vt = anchored ? Mathf.Lerp(3f, 9f, lost) : Mathf.Lerp(2.4f, 4.0f, lost);
                    Vector3 vr = c.v - f.breeze; float sp = vr.magnitude;
                    Vector3 acc = -up * g - (sp > 1e-3f ? vr / sp * (g * sp * sp / (vt * vt)) : Vector3.zero);
                    if (anchored)
                    {
                        Vector3 hzc = Vector3.ProjectOnPlane(c.rc, up); float hd = hzc.magnitude, ht = c.rCan + f.rHull + 1.5f;
                        if (hd > 1e-3f) acc -= hzc / hd * (hd - ht) * 0.35f * lost;
                    }

                    acc += (f.flatFwd * Mathf.Sin(age * 0.9f + a) + Vector3.Cross(up, f.flatFwd) * Mathf.Cos(age * 0.7f + 2 * a)) * 0.6f;

                    for (int b = 0; b < f.sims.Count; b++)
                    {
                        if (b == a) continue; Vector3 dd = Vector3.ProjectOnPlane(c.rc - f.sims[b].rc, up); float dl = dd.magnitude;
                        float keep = anchored ? (c.rCan + f.sims[b].rCan) * 0.85f : 14f;
                        if (dl < keep && dl > 1e-3f) acc += dd / dl * (keep - dl) * 0.8f;
                    }
                    c.v += acc * dt; c.rc += c.v * dt;
                    float len = c.rc.magnitude;
                    if (len > c.L) { c.rc *= c.L / len; float vo = Vector3.Dot(c.v, c.rc / c.L); if (vo > 0) c.v -= c.rc / c.L * vo; }
                    hc = Vector3.Dot(c.rc, up);
                    if (hc <= surf + 0.4f) c.landT = now;
                }
                else
                {
                    c.v = Vector3.ProjectOnPlane(c.v, up) * Mathf.Exp(-dt * 1.2f) + f.breeze * 0.15f * dt;
                    c.rc += Vector3.ProjectOnPlane(c.v, up) * dt;

                    c.sink = Mathf.Min(0.22f, Mathf.Max(0f, now - c.landT - 15f) * 0.004f) + Mathf.Max(0f, now - c.landT - 240f) * 0.05f;
                    if (c.sink > 3f && !c.gone)
                    {
                        c.gone = true;
                        foreach (var rd in (c.riser != null ? c.riser : c.root).GetComponentsInChildren<Renderer>(true)) rd.enabled = false;
                    }
                    c.rc += up * (surf + 0.2f - c.sink - Vector3.Dot(c.rc, up)) * Mathf.Clamp01(dt * 6f);
                    if (c.rc.magnitude > c.L) c.rc *= c.L / c.rc.magnitude;

                    Vector3 hz = Vector3.ProjectOnPlane(c.rc, up); float hmin = anchored ? c.rCan * 0.8f + f.rHull + 0.5f : f.rHull + 5f;
                    if (hz.magnitude < hmin && hz.sqrMagnitude > 1e-4f) c.rc += hz.normalized * (hmin - hz.magnitude) * Mathf.Clamp01(dt * 2f);

                    c.defl = Mathf.Lerp(c.defl, anchored ? 0.8f : 0.62f, dt * (anchored ? 0.6f : 0.3f));
                }
                Transform chain = c.riser != null ? c.riser : c.root;
                Quaternion turn = Quaternion.FromToRotation(chain.rotation * c.axisL, c.rc.normalized);
                chain.rotation = turn * chain.rotation;
                if (anchored)
                {
                    float k = Mathf.Clamp(c.rc.magnitude / Mathf.Max(1f, c.L), 0.12f, 1f);
                    c.kChain = c.riser != null ? k : 1f;
                    Vector3 cs0 = c.chainS0; cs0[c.chainAx] *= k;
                    if (c.riser != null) chain.localScale = cs0;
                }
                c.root.localScale = new Vector3(c.s0.x * c.defl, c.s0.y, c.s0.z * c.defl);
            }

            if (f.mfs == null) return;
            Vector3 o = tr.position, e1 = f.flatFwd, e2 = Vector3.Cross(up, e1);
            float slump = 0; foreach (var c in f.sims) if (c.landT >= 0) slump = Mathf.Max(slump, Mathf.Clamp01((now - c.landT) / 6f)); slump = slump * slump * (3 - 2 * slump);
            if (f.pbMesh != null) StepPlayback(f, now, o, up, seaOff, e1, e2);
            for (int m = 0; m < f.mfs.Count; m++)
            {
                var mf = f.mfs[m]; if (mf == null) continue;
                if (f.pbMesh != null && f.pbMesh[m]) continue;
                var mt = mf.transform; var src = f.v0[m]; var dst = f.buf[m];
                bool fabric = mf.name.Contains("Canopy");
                if (!fabric && mf.name.Contains("Lines"))
                {
                    if (f.lth == null) f.lth = new LineThick[f.mfs.Count];
                    if (f.lth[m] == null) f.lth[m] = BuildThick(src) ?? new LineThick();
                    if (f.lth[m].cen != null) { float kk = ThickK(mt, tr.position, f.lth[m].w); src = Thick(f.lth[m], kk); }
                }
                int si = f.mfSim != null && m < f.mfSim.Count ? f.mfSim[m] : -1;
                Sim cs = si >= 0 ? f.sims[si] : null;

                float slumpC = slump;
                if (cs != null) { slumpC = cs.landT >= 0 ? Mathf.Clamp01((now - cs.landT) / 6f) : 0f; slumpC = slumpC * slumpC * (3 - 2 * slumpC); }
                float band = 1.2f + (fabric ? 0f : slumpC * 40f);

                float wrA = fabric && cs != null ? 0.55f * Mathf.Clamp01(age / 1.8f) * (cs.landT < 0 ? 1f : 1f - 0.6f * Mathf.Clamp01((now - cs.landT) / 4f)) : 0f;
                Vector3 cc = cs != null ? o + cs.rc : o, ax = cs != null ? cs.rc.normalized : up;
                Vector3 a1 = Vector3.Cross(ax, Mathf.Abs(Vector3.Dot(ax, up)) > 0.9f ? e1 : up).normalized, a2 = Vector3.Cross(ax, a1);

                var uvm = f.uvs != null && m < f.uvs.Count ? f.uvs[m] : null; float mlen = f.merid != null && m < f.merid.Count ? f.merid[m] : 0f;
                float lay = fabric && cs != null && cs.landT >= 0 ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((now - cs.landT) / 7f)) : 0f;
                LayCache lc = null;
                if (lay > 0f && uvm != null && uvm.Length >= src.Length && mlen > 0f)
                {
                    if (f.lay == null) f.lay = new LayCache[f.mfs.Count];
                    lc = f.lay[m] ?? (f.lay[m] = BuildLay(f.seed, m, src, uvm, mlen * mt.lossyScale.x));
                }
                float ramp = cs != null ? Mathf.Clamp01((now - cs.landT - 2f) / 6f + 0.35f) : 0f, sinkC = cs != null ? cs.sink : 0f;
                Vector3 cOff = Vector3.ProjectOnPlane(cc - o, up), h1 = cOff.sqrMagnitude > 1e-6f ? cOff.normalized : e1, h2 = Vector3.Cross(h1, up);
                float wallL = lc != null ? 0.40f * lc.Mw : 0f, lipL = lc != null ? 0.12f * lc.Mw : 0f, brT = now * 0.5f, wrL = wrA * (1f - lay), dW = lc != null ? (1f - lay) * (1f - lay) : 1f;

                float kC = cs != null ? cs.kChain : 1f, lSk = cs != null ? cs.L * cs.skirtFrac : 0f;
                bool unsq = fabric && anchored && kC < 0.999f;
                for (int i = 0; i < src.Length; i++)
                {
                    Vector3 fpos = Vector3.zero;
                    if (lc != null)
                    {
                        float st = lc.standF[i] * ramp, H = st * lc.H0[i], rf, hB = 0f;
                        if (lc.rFloor[i] >= 0f) rf = lc.rFloor[i];
                        else
                        {
                            float span = Mathf.Sqrt(Mathf.Max(0f, wallL * wallL - H * H)) * 0.88f;
                            rf = lc.floorR[i] + span * lc.sinA[i] + lc.puff[i]; hB = H * lc.omc[i];
                            float lt = lc.lipT[i];
                            if (lt >= 0f) { rf += lipL * (0.25f + 0.55f * lt) * (0.4f + 0.6f * st); hB -= lipL * 0.9f * lt * lt * st; }
                        }
                        Vector3 hv = cOff + (h1 * lc.cph[i] + h2 * lc.sph[i]) * rf;
                        float hgtF = seaOff + 0.35f * SeaHeight(Vector3.Dot(hv, e1), Vector3.Dot(hv, e2), now, f.seed) + 0.85f
                                   + hB * (0.92f + 0.08f * Mathf.Sin(brT + lc.bph[i])) + lc.hS[i] - sinkC;
                        fpos = o + hv + up * hgtF;
                        if (lay >= 0.999f) { dst[i] = mt.InverseTransformPoint(fpos); continue; }
                    }
                    Vector3 p = mt.TransformPoint(src[i]);
                    if (unsq) { float A = Vector3.Dot(p - o, ax); p += ax * (A / kC - lSk * (1f - kC) - A); }
                    if (wrL > 0.01f)
                    {
                        Vector3 dv = p - cc; float qa = Vector3.Dot(dv, a1), qb = Vector3.Dot(dv, a2);
                        float nz = (Mathf.PerlinNoise(qa * 0.22f + f.seed + now * 0.35f, qb * 0.22f - now * 0.21f) - 0.5f) * 1.6f
                                 + (Mathf.PerlinNoise(qa * 0.85f - f.seed, qb * 0.85f + now * 0.6f) - 0.5f) * 0.5f;
                        p += (dv.sqrMagnitude > 1e-4f ? dv.normalized : ax) * nz * wrL + ax * Mathf.Abs(nz) * wrL * 0.4f;
                    }
                    Vector3 d = p - o; float hgt = Vector3.Dot(d, up);
                    float u = Vector3.Dot(d, e1), w = Vector3.Dot(d, e2);
                    float shU = SeaHeight(u, w, now, f.seed);

                    bool high = fabric && (dW < 0.04f || hgt - (seaOff + 0.35f * shU + 0.60f - sinkC) > band + 3.5f);
                    float target = seaOff + (fabric ? 0.35f : 1f) * shU + (fabric ? (high ? 0f : Crinkle(u, w, f.seed) * (anchored ? 1.5f : 1f) * dW) + 0.60f : 0.50f) - sinkC;
                    if (fabric && anchored && cs != null && !high)
                    {
                        Vector3 rel = Vector3.ProjectOnPlane(p - cc, up); float rr = rel.magnitude;
                        if (rr > 1e-3f)
                        {
                            float ang = Mathf.Atan2(Vector3.Dot(rel, e2), Vector3.Dot(rel, e1));
                            float sk = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((rr - 0.2f * cs.rCan) / (0.5f * cs.rCan)));
                            target += dW * 0.28f * sk * Mathf.Sin(ang * 24f + 3f * Mathf.PerlinNoise(rr * 0.15f + f.seed, ang)) * (cs.landT < 0 ? 0f : Mathf.Clamp01((now - cs.landT) / 3f));
                        }
                    }
                    if (!fabric && anchored)
                    {
                        float dh = Vector3.ProjectOnPlane(d, up).magnitude;
                        target = Mathf.Max(target, seaOff * Mathf.Clamp01(dh / f.rHull) + 0.06f - (cs != null ? cs.sink : 0f));
                    }
                    float b = Mathf.Clamp01((target + band - hgt) / band); b = b * b * (3 - 2 * b);
                    if (fabric)
                    {

                        float bf = lc != null ? b : Mathf.Max(b, slumpC);
                        Vector3 away = Vector3.ProjectOnPlane(d, up); away = away.sqrMagnitude > 1e-4f ? away.normalized : e1;
                        float dh = (hgt - target) * bf;
                        Vector3 q0 = p - up * dh + away * dh * 0.42f;
                        if (lc != null) q0 = Vector3.Lerp(q0, fpos, lay);
                        dst[i] = mt.InverseTransformPoint(q0);
                    }
                    else dst[i] = mt.InverseTransformPoint(p - up * ((hgt - target) * b));
                }
                var mesh = mf.mesh; mesh.vertices = dst;
                if (fabric) mesh.RecalculateNormals();
                else
                {
                    if (f.nbuf == null) f.nbuf = new Vector3[f.mfs.Count][];
                    if (f.nbuf[m] == null || f.nbuf[m].Length != dst.Length) f.nbuf[m] = new Vector3[dst.Length];
                    Vector3 nl = mt.InverseTransformDirection(up).normalized; var nb = f.nbuf[m];
                    for (int i = 0; i < nb.Length; i++) nb[i] = nl;
                    mesh.normals = nb;
                }
                mesh.RecalculateBounds();
            }
            float wet = Mathf.Lerp(1f, 0.78f, slump);
            foreach (var mat in f.mats) if (mat != null) { var cl = mat.color; mat.color = new Color(wet, wet, wet, cl.a); }
            if (f.emis != null) { float ge = Mathf.Clamp01(1f - age / 4f); foreach (var kv in f.emis) if (kv.Key != null) kv.Key.SetColor("_EmissiveColor", kv.Value * ge); }
        }

        static void SetupPlayback(Falling f, Vector3 up)
        {
            if (f.drogue || f.anchor == null || f.sims == null || f.sims.Count == 0 || f.mfs == null || f.mfSim == null) return;
            Vector3 e1 = f.flatFwd, e2 = Vector3.Cross(up, e1);
            float sW = (Mathf.Abs(f.s0.x) + Mathf.Abs(f.s0.y) + Mathf.Abs(f.s0.z)) / 3f; if (!(sW > 1e-3f)) sW = 1f;
            var list = new List<Sim>(); string info = "";
            for (int a = 0; a < f.sims.Count; a++)
            {
                var c = f.sims[a]; int mC = -1, mL = -1, mR = -1;
                for (int m = 0; m < f.mfs.Count; m++)
                {
                    var mf = f.mfs[m]; if (mf == null) continue;
                    if (f.mfSim[m] == a) { if (mC < 0 && mf.name.Contains("Canopy")) mC = m; else if (mL < 0 && mf.name.Contains("Lines")) mL = m; }
                    else if (f.mfSim[m] < 0 && mR < 0 && c.riser != null && mf.name.Contains("Riser") && mf.transform.IsChildOf(c.riser)) mR = m;
                }
                if (mC < 0 || mL < 0) continue;
                var bk = ChuteCloth.Get(a % 4); if (bk == null) continue;
                Vector3[] v0 = f.v0[mC], l0 = f.v0[mL];
                if (v0.Length != 2 * bk.nv || l0.Length != 12 * bk.nAtt)
                {
                    Debug.LogWarning("[CaelusEntryDrag] ChuteFx: cloth playback skipped on " + c.root.name + " - mesh has " + v0.Length + " / " + l0.Length + " vertices, data wants " + (2 * bk.nv) + " / " + (12 * bk.nAtt));
                    continue;
                }
                float err = ChuteCloth.Check(bk, v0);
                if (!(err < 0.05f)) { Debug.LogWarning("[CaelusEntryDrag] ChuteFx: cloth playback skipped on " + c.root.name + " - frame 0 does not match the canopy mesh (" + err.ToString("F3") + " m)"); continue; }
                var pb = new ChuteCloth.Playback(bk, v0.Length) { t0 = f.floatT, sW = sW, mC = mC, mL = mL, mR = mR, side = e1 };
                Transform ct = f.mfs[mC].transform, lt = f.mfs[mL].transform;

                float sE = 0f, sO = 0f; for (int i = 0; i + 1 < l0.Length; i += 2) { sE += l0[i].magnitude; sO += l0[i + 1].magnitude; }
                pb.cEnd = sE <= sO ? 0 : 1;
                pb.lineAtt = new int[bk.nAtt]; Vector3 confAcc = Vector3.zero;
                for (int g = 0; g < bk.nAtt; g++)
                {
                    Vector3 cc = Vector3.zero, hc = Vector3.zero; int b0 = g * 12;
                    for (int k = 0; k < 6; k++) { cc += l0[b0 + 2 * k + pb.cEnd]; hc += l0[b0 + 2 * k + 1 - pb.cEnd]; }
                    cc /= 6f; hc /= 6f; confAcc += cc;
                    Vector3 hw = lt.TransformPoint(hc); float best = float.MaxValue; int bi = g;
                    for (int k = 0; k < bk.nAtt; k++) { float dd = (ct.TransformPoint(v0[pb.att[k]]) - hw).sqrMagnitude; if (dd < best) { best = dd; bi = k; } }
                    pb.lineAtt[g] = bi;
                    if (g == 0)
                    {
                        Vector3 axw = lt.TransformVector(hc - cc), d0 = lt.TransformVector(l0[b0 + pb.cEnd] - cc), d1 = lt.TransformVector(l0[b0 + 2 + pb.cEnd] - cc);
                        pb.ring = Vector3.Dot(Vector3.Cross(d0, d1), axw) >= 0f ? 1f : -1f;
                    }
                }
                pb.confL = confAcc / bk.nAtt;
                var th = BuildThick(l0); if (th != null && th.w > 1e-5f) pb.lineR = th.w;

                Vector3[] n0 = f.mfs[mC].sharedMesh != null ? f.mfs[mC].sharedMesh.normals : null;
                pb.nOut = ChuteCloth.NormalSign(bk, v0, n0, 0, 1f); pb.nIn = ChuteCloth.NormalSign(bk, v0, n0, bk.nv, -1f);
                if (mR >= 0)
                {
                    var r0 = f.v0[mR]; Vector3 lo = r0.Length > 0 ? r0[0] : Vector3.zero, hi = lo;
                    for (int i = 1; i < r0.Length; i++) { lo = Vector3.Min(lo, r0[i]); hi = Vector3.Max(hi, r0[i]); }
                    Vector3 ext = hi - lo; pb.rAx = ext.x > ext.y && ext.x > ext.z ? 0 : (ext.z > ext.y ? 2 : 1);
                    pb.rBase = Mathf.Abs(lo[pb.rAx]) <= Mathf.Abs(hi[pb.rAx]) ? lo[pb.rAx] : hi[pb.rAx];
                }

                Vector3 dh = Vector3.ProjectOnPlane(c.rc, up);
                pb.az0 = dh.sqrMagnitude > 0.25f ? Mathf.Atan2(Vector3.Dot(dh, e2), Vector3.Dot(dh, e1)) : a * 2.0944f;
                float la = bk.leanAz * Mathf.Deg2Rad; pb.cosL = Mathf.Cos(la); pb.sinL = Mathf.Sin(la);

                Vector3 m1, m2; ChuteCloth.Level(pb, up, e1, e2, 0f, out m1, out m2); ChuteCloth.Match(pb, ct.rotation, m1, m2, up);
                c.pb = pb; list.Add(c);
                info += (info.Length > 0 ? ", " : "") + c.root.name + " v" + (a % 4 + 1) + " (" + (err * 1000f).ToString("F1") + " mm)";
            }
            if (list.Count == 0) return;

            int n = list.Count; var az = new float[n]; for (int i = 0; i < n; i++) az[i] = list[i].pb.az0;
            float sep = ChuteCloth.MinSepDeg * Mathf.Deg2Rad;
            for (int it = 0; it < 24; it++)
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                    {
                        float dl = Mathf.DeltaAngle(az[i] * Mathf.Rad2Deg, az[j] * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                        if (Mathf.Abs(dl) >= sep) continue;
                        float push = (sep - Mathf.Abs(dl)) * 0.5f * (dl >= 0f ? 1f : -1f); az[i] -= push; az[j] += push;
                    }
            f.pbMesh = new bool[f.mfs.Count];
            if (f.nbuf == null) f.nbuf = new Vector3[f.mfs.Count][];
            for (int i = 0; i < n; i++)
            {
                var pb = list[i].pb; pb.azOff = Mathf.DeltaAngle(pb.az0 * Mathf.Rad2Deg, az[i] * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                f.pbMesh[pb.mC] = true; f.pbMesh[pb.mL] = true; if (pb.mR >= 0) f.pbMesh[pb.mR] = true;
                if (f.nbuf[pb.mL] == null || f.nbuf[pb.mL].Length != f.buf[pb.mL].Length) f.nbuf[pb.mL] = new Vector3[f.buf[pb.mL].Length];
            }
            Debug.Log("[CaelusEntryDrag] ChuteFx: baked cloth collapse on " + n + " canopies: " + info);
        }

        static void PlaySim(Sim c, float now)
        {
            var pb = c.pb;
            if (c.landT < 0f && now - pb.t0 >= pb.bk.tTouch) c.landT = pb.t0 + pb.bk.tTouch;
            if (c.landT < 0f) return;
            c.sink = Mathf.Min(0.22f, Mathf.Max(0f, now - c.landT - 15f) * 0.004f) + Mathf.Max(0f, now - c.landT - 240f) * 0.05f;
            if (c.sink > 3f && !c.gone)
            {
                c.gone = true;
                foreach (var rd in (c.riser != null ? c.riser : c.root).GetComponentsInChildren<Renderer>(true)) rd.enabled = false;
            }
        }

        static void StepPlayback(Falling f, float now, Vector3 o, Vector3 up, float seaOff, Vector3 e1, Vector3 e2)
        {
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : Camera.main;
            float pxm = cam != null ? 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, Screen.height) : 0f;
            Vector3 cp = cam != null ? cam.transform.position : o;
            for (int a = 0; a < f.sims.Count; a++)
            {
                var c = f.sims[a]; var pb = c.pb; if (pb == null || c.gone) continue;
                MeshFilter mfC = f.mfs[pb.mC], mfL = f.mfs[pb.mL]; if (mfC == null || mfL == null) continue;
                float tp = now - pb.t0, dw = ChuteCloth.DropW(pb, tp);
                Vector3 h1, h2; ChuteCloth.Level(pb, up, e1, e2, tp, out h1, out h2);
                Transform ct = mfC.transform, lt = mfL.transform;
                Matrix4x4 l2w = lt.localToWorldMatrix;

                Vector3 sea = o + up * (seaOff + ChuteCloth.Lift), origin = sea - up * c.sink;
                if (dw < 1f) { Vector3 off = l2w.MultiplyPoint3x4(pb.confL) - sea; float vu = Vector3.Dot(off, up); if (vu < 0f) off -= up * vu; origin += off * (1f - dw); }
                var cdst = f.buf[pb.mC];
                ChuteCloth.Verts(pb, f.v0[pb.mC], cdst, ct.worldToLocalMatrix, origin, h1, h2, up, e1, e2, tp, now, f.seed);
                var cm = mfC.sharedMesh;
                if (cm != null)
                {
                    cm.vertices = cdst;
                    if (!pb.nrmDone) { ChuteCloth.Normals(cdst, pb.nrm, pb.bk.rows, pb.bk.cols, pb.bk.nv, pb.nOut, pb.nIn); cm.normals = pb.nrm; pb.nrmDone = tp > pb.tEnd; }
                    cm.RecalculateBounds();
                }
                var ldst = f.buf[pb.mL];
                ChuteCloth.Lines(pb, cdst, ldst, ct.localToWorldMatrix, l2w, lt.worldToLocalMatrix, o, up, dw, cp, pxm);
                var lm = mfL.sharedMesh;
                if (lm != null)
                {
                    lm.vertices = ldst;
                    var nb = f.nbuf[pb.mL]; Vector3 nl = lt.InverseTransformDirection(up).normalized;
                    for (int i = 0; i < nb.Length; i++) nb[i] = nl;
                    lm.normals = nb; lm.RecalculateBounds();
                }
                if (pb.mR >= 0 && !pb.riserOff && f.mfs[pb.mR] != null)
                {
                    var mfR = f.mfs[pb.mR];
                    if (dw >= 1f) { var rr = mfR.GetComponent<Renderer>(); if (rr != null) rr.enabled = false; pb.riserOff = true; }
                    else
                    {
                        Vector3[] rs = f.v0[pb.mR], rd = f.buf[pb.mR]; float kr = 1f - dw;
                        for (int i = 0; i < rs.Length; i++) { var p = rs[i]; p[pb.rAx] = pb.rBase + (p[pb.rAx] - pb.rBase) * kr; rd[i] = p; }
                        var rm = mfR.sharedMesh; if (rm != null) { rm.vertices = rd; rm.RecalculateBounds(); }
                    }
                }
            }
        }

        internal static float SeaHeight(float u, float w, float t, float s)
        {
            return 0.35f * Mathf.Sin(0.14f * u + 0.05f * w - 0.9f * t + s) + 0.15f * Mathf.Sin(-0.09f * u + 0.25f * w - 1.3f * t + 2f * s);
        }

        static float Crinkle(float u, float w, float s)
        {
            float fold = (Mathf.PerlinNoise(u * 0.35f + s, w * 0.35f + 7f) - 0.5f) * 0.55f;
            float crk = (Mathf.PerlinNoise(u * 1.6f + 3f * s, w * 1.6f - s) - 0.5f) * 0.18f + 0.04f * Mathf.Sin(u * 3.1f + w * 1.7f);
            float pocket = Mathf.Max(0f, Mathf.PerlinNoise(u * 0.11f - s, w * 0.11f + 2f * s) - 0.58f) * 3.2f;
            return fold + crk + pocket;
        }

        class LayCache { public float Mw; public float[] cph, sph, standF, H0, rFloor, floorR, sinA, omc, lipT, puff, bph, hS; }
        static LayCache BuildLay(float seed, int m, Vector3[] src, Vector2[] uv, float Mw)
        {
            int n = src.Length; float sd = seed + m * 7.3f, flapC = sd * 2.39f % 6.2832f;
            var c = new LayCache { Mw = Mw, cph = new float[n], sph = new float[n], standF = new float[n], H0 = new float[n], rFloor = new float[n], floorR = new float[n],
                                   sinA = new float[n], omc = new float[n], lipT = new float[n], puff = new float[n], bph = new float[n], hS = new float[n] };
            float wallL = 0.40f * Mw;
            for (int i = 0; i < n; i++)
            {
                Vector3 sl = src[i]; float phi = Mathf.Atan2(sl.z, sl.x), rN = 1f - uv[i].y;
                c.cph[i] = Mathf.Cos(phi); c.sph[i] = Mathf.Sin(phi); c.bph[i] = phi * 2f + sd;
                float dphi = Mathf.Abs(Mathf.DeltaAngle(phi * Mathf.Rad2Deg, flapC * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                float flap = 0.4f * Mathf.Clamp01(1f - dphi / 0.55f) * Mathf.Clamp01((rN - 0.62f) / 0.12f);
                float rNf = Mathf.Lerp(rN, 0.62f - (rN - 0.62f) * 0.85f, flap);
                float bunch = 1f + 0.16f * (Mathf.PerlinNoise(phi * 0.9f + sd, 3.7f) - 0.5f) * 2f + 0.06f * (Mathf.PerlinNoise(phi * 4f + sd, 9.1f) - 0.5f) * 2f;
                float ruf = Mathf.PerlinNoise(phi * 0.75f + sd, 5.3f), ruf2 = Mathf.PerlinNoise(phi * 2.4f + 2f * sd, 1.9f);
                c.standF[i] = Mathf.Clamp01(0.35f + 1.4f * (ruf - 0.3f)); c.H0[i] = 0.28f * Mw * (0.75f + 0.5f * ruf2);
                float floorR = 0.26f * Mw * bunch, onWall, rfEnd; c.floorR[i] = floorR;
                if (rNf < 0.48f) { c.rFloor[i] = (0.04f + rNf / 0.48f * 0.96f) * floorR; c.lipT[i] = -1f; onWall = 0f; rfEnd = c.rFloor[i]; }
                else
                {
                    float sW = Mathf.Clamp01((rNf - 0.48f) / 0.40f), aW = sW * 1.5708f; onWall = sW;
                    c.rFloor[i] = -1f; c.sinA[i] = Mathf.Sin(aW); c.omc[i] = 1f - Mathf.Cos(aW); c.lipT[i] = rNf > 0.88f ? (rNf - 0.88f) / 0.12f : -1f;
                    c.puff[i] = 0.30f * Mathf.Sin(phi * 16f + 2.5f * ruf2 * 6.283f) * onWall;
                    float H = c.standF[i] * c.H0[i]; rfEnd = floorR + Mathf.Sqrt(Mathf.Max(0f, wallL * wallL - H * H)) * 0.88f * c.sinA[i] + c.puff[i];
                }
                float flr = 1f - onWall, sm = Mathf.SmoothStep(0f, 1f, rN), xL = rfEnd * c.cph[i] + sd * 13f, yL = rfEnd * c.sph[i] - sd * 7f;
                c.hS[i] = 0.18f * Mathf.Sin(phi * 7f + 6.283f * ruf2) * onWall
                        + 0.22f * Mathf.Sin(phi * 16f + 2.5f * Mathf.PerlinNoise(rN * 3f + seed, phi)) * sm * flr
                        + Crinkle(xL, yL, seed) * (1.0f + 1.2f * flr) - 0.45f * (1f - rN)
                        + 0.55f * (Mathf.PerlinNoise(xL * 0.07f + sd, yL * 0.07f) - 0.5f) * sm
                        + 0.30f * flap;
            }
            return c;
        }

        public class LineThick { public Vector3[] cen, off, buf; public float w; }
        internal static LineThick BuildThick(Vector3[] l)
        {
            const int vpl = 12; if (l == null || l.Length == 0 || l.Length % vpl != 0) return null;
            var t = new LineThick { cen = new Vector3[l.Length], off = new Vector3[l.Length], buf = new Vector3[l.Length] }; double ws = 0;
            for (int b0 = 0; b0 < l.Length; b0 += vpl)
            {
                float dmax = 0f; for (int i = b0; i < b0 + vpl; i++) dmax = Mathf.Max(dmax, l[i].magnitude);
                Vector3 a = Vector3.zero, h = Vector3.zero; int na = 0, nh = 0;
                for (int i = b0; i < b0 + vpl; i++) { if (l[i].magnitude < dmax * 0.5f) { a += l[i]; na++; } else { h += l[i]; nh++; } }
                if (na == 0 || nh == 0) return null;
                a /= na; h /= nh; Vector3 ax = (h - a).normalized;
                for (int i = b0; i < b0 + vpl; i++) { Vector3 cc = a + ax * Vector3.Dot(l[i] - a, ax); t.cen[i] = cc; t.off[i] = l[i] - cc; ws += t.off[i].magnitude; }
            }
            t.w = (float)(ws / l.Length); return t;
        }
        static Vector3[] Thick(LineThick t, float k) { for (int i = 0; i < t.buf.Length; i++) t.buf[i] = t.cen[i] + t.off[i] * k; return t.buf; }
        static float ThickK(Transform mt, Vector3 worldMid, float wLocal)
        {
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : Camera.main;
            if (cam == null || wLocal <= 0f) return 1f;
            float dist = (cam.transform.position - worldMid).magnitude;
            float px = dist * 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, Screen.height);
            float wW = wLocal * Mathf.Abs(mt.lossyScale.x); if (wW < 1e-6f) return 1f;
            return Mathf.Clamp(0.4f * px / wW, 1f, 8f);
        }

        static void Remove(Falling f)
        {
            if (f.mfs != null) foreach (var mf in f.mfs) if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
            if (f.mats != null) foreach (var m in f.mats) if (m != null) Destroy(m);
            if (f.go != null) Destroy(f.go);
        }

        void OnDestroy() { foreach (var f in falling) Remove(f); falling.Clear(); }
    }

    internal static class ChuteCloth
    {
        internal const float Lift = 0.6f;
        internal const float Swell = 0.25f;
        internal const float Blend = 0.3f;
        internal const float MinSepDeg = 80f;

        internal class Bake
        {
            internal int nv, nf, rows, cols, nAtt, attRow, attStep, bytes; internal float fps, tTouch, leanAz, tilt, mnX, mnY, mnZ, scX, scY, scZ;
            internal readonly float[] R0 = new float[9]; internal ushort[] q;
            internal int kind; internal float[] open;
            internal int ncan = 1; internal float period, riserLen; internal float[] pose;
        }
        internal class Playback
        {
            internal readonly Bake bk; internal readonly int ns; internal readonly float[] swl; internal readonly Vector3[] nrm; internal readonly int[] att, colMap; internal readonly float tEnd;
            internal int mC = -1, mL = -1, mR = -1, cEnd, rAx = 1, colShift; internal int[] lineAtt;
            internal float t0, sW = 1f, az0, azOff, cosL = 1f, sinL, nOut = 1f, nIn = -1f, ring = 1f, lineR = 0.013f, rBase, qth;
            internal Vector3 confL, side, qk; internal bool nrmDone, riserOff;
            internal Playback(Bake k, int meshVerts)
            {
                bk = k; ns = (k.cols - 1 + 3) / 4 + 1; swl = new float[k.rows * ns]; nrm = new Vector3[meshVerts];
                att = new int[k.nAtt]; for (int i = 0; i < k.nAtt; i++) att[i] = k.attRow * k.cols + k.attStep * i;
                colMap = new int[k.cols]; for (int j = 0; j < k.cols; j++) colMap[j] = j;
                tEnd = (k.nf - 1) / k.fps + Blend + 0.5f;
            }
        }

        internal const int NCollapse = 4, NOpen = 3, NDesc = 2, NDOpen = 2, NSlots = NCollapse + NOpen + NDesc + NDOpen;
        internal static int DOpenSlot(int v) { return NCollapse + NOpen + NDesc + v; }
        static readonly Bake[] bakes = new Bake[NSlots]; static readonly int[] state = new int[NSlots]; static readonly string[] note = new string[NSlots];
        static readonly bool[] logged = new bool[NSlots]; static readonly object gate = new object(); static int asked, askedOpen; static bool openNoted, openWaitNoted;

        static readonly int[] deployKnown = new int[8];
        internal static bool HasDeploy(int v)
        {
            if (v < 0 || v >= deployKnown.Length) return false;
            if (deployKnown[v] == 0) deployKnown[v] = System.IO.File.Exists(System.IO.Path.Combine(KSPUtil.ApplicationRootPath, "GameData/Caelus_Corona/PluginData/ChuteCloth/opening2_v" + (v + 1) + ".ccb")) ? 1 : 2;
            return deployKnown[v] == 1;
        }
        internal static string PathOf(int v)
        {
            return System.IO.Path.Combine(KSPUtil.ApplicationRootPath, v < NCollapse ? "GameData/Caelus_Corona/PluginData/ChuteCloth/collapse_v" + (v + 1) + ".ccb"
                                                                     : v < NCollapse + NOpen ? "GameData/Caelus_Corona/PluginData/ChuteCloth/" + (HasDeploy(v - NCollapse) ? "opening2_v" : "opening_v") + (v - NCollapse + 1) + ".ccb"
                                                                     : v < NCollapse + NOpen + NDesc ? "GameData/Caelus_Corona/PluginData/ChuteCloth/descent_" + (v == NCollapse + NOpen ? "mains" : "drogues") + ".ccb"
                                                                     : "GameData/Caelus_Corona/PluginData/ChuteCloth/drogue_open_v" + (v - NCollapse - NOpen - NDesc + 1) + ".ccb");
        }

        internal static void Preload(int count)
        {
            count = Math.Min(Math.Max(count, 0), NCollapse);
            if (count <= asked) return;
            asked = count;
            for (int v = 0; v < count; v++) Queue(v);
        }

        static void Queue(int v)
        {
            lock (gate) { if (state[v] != 0) return; state[v] = 1; }
            string path = PathOf(v);
            System.Threading.WaitCallback job = delegate (object unused)
            {
                Bake k = null; string err = null;
                try { k = Load(path, out err); } catch (Exception x) { k = null; err = x.GetType().Name + ": " + x.Message; }
                lock (gate) { bakes[v] = k; note[v] = err; state[v] = 2; System.Threading.Monitor.PulseAll(gate); }
            };
            bool queued = false;
            try { queued = System.Threading.ThreadPool.QueueUserWorkItem(job); } catch (Exception) { queued = false; }
            if (!queued) job(null);
        }

        internal static Bake Get(int v)
        {
            if (v < 0 || v >= NCollapse) return null;
            Queue(v);
            lock (gate)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (state[v] == 1 && sw.ElapsedMilliseconds < 5000) System.Threading.Monitor.Wait(gate, 100);
                if (state[v] != 2) { Debug.LogWarning("[CaelusEntryDrag] ChuteFx: cloth data " + PathOf(v) + " still loading - procedural settle this time"); return null; }
                if (!logged[v])
                {
                    logged[v] = true; var k = bakes[v];
                    if (k != null) Debug.Log("[CaelusEntryDrag] ChuteFx: cloth collapse v" + (v + 1) + " loaded, " + k.nf + " frames at " + k.fps.ToString("F0") + " fps, " + k.nv + " vertices, " + (k.bytes / 1048576f).ToString("F1") + " MB, touchdown " + k.tTouch.ToString("F2") + " s");
                    else Debug.Log("[CaelusEntryDrag] ChuteFx: cloth collapse data " + PathOf(v) + " not used (" + note[v] + ") - released mains use the procedural settle");
                }
                return bakes[v];
            }
        }

        static int I32(byte[] b, int o) { return BitConverter.ToInt32(b, o); }
        static float F32(byte[] b, int o) { return BitConverter.ToSingle(b, o); }

        internal static Bake Load(string path, out string err)
        {
            err = null;
            if (!System.IO.File.Exists(path)) { err = "file not found"; return null; }
            if (!BitConverter.IsLittleEndian) { err = "big-endian machine"; return null; }
            byte[] b = System.IO.File.ReadAllBytes(path);
            if (b.Length < 128 || b[0] != (byte)'C' || b[1] != (byte)'C' || b[2] != (byte)'B' || b[3] != (byte)'1') { err = "not a CCB1 file"; return null; }
            int ver = I32(b, 4), hb = I32(b, 8);
            var k = new Bake { nv = I32(b, 12), nf = I32(b, 16), fps = F32(b, 20), rows = I32(b, 24), cols = I32(b, 28),
                               mnX = F32(b, 32), mnY = F32(b, 36), mnZ = F32(b, 40), scX = F32(b, 44), scY = F32(b, 48), scZ = F32(b, 52),
                               tTouch = F32(b, 56), leanAz = F32(b, 60), tilt = F32(b, 64), nAtt = I32(b, 104), attRow = I32(b, 108), attStep = I32(b, 112), bytes = b.Length };
            for (int i = 0; i < 9; i++) k.R0[i] = F32(b, 68 + 4 * i);
            if (ver != 1 || hb < 128 || k.rows < 3 || k.cols < 5 || k.rows * k.cols != k.nv || k.nf < 2 || k.nf > 100000 || !(k.fps > 0f) || !(k.tTouch >= 0f)
                || k.nAtt <= 0 || k.attRow < 0 || k.attRow >= k.rows || k.attStep <= 0 || (k.nAtt - 1) * k.attStep >= k.cols) { err = "bad header (version " + ver + ")"; return null; }
            k.kind = I32(b, 124);
            if (k.kind == 2)
            {
                k.ncan = I32(b, 128); k.period = F32(b, 132); k.riserLen = F32(b, 136); int po = I32(b, 148);
                if (k.ncan < 1 || k.ncan > 16 || po < 152 || (long)po + (long)k.nf * k.ncan * 12 > hb || !(Math.Abs(k.period - k.nf / k.fps) < 1e-3f) || !(k.riserLen > 0f))
                { err = "bad descent header (" + k.ncan + " canopies, period " + k.period + " s)"; return null; }
                k.pose = new float[k.nf * k.ncan * 3];
                Buffer.BlockCopy(b, po, k.pose, 0, k.nf * k.ncan * 12);
            }
            long need = hb + (long)k.nf * k.ncan * k.nv * 6;
            if (b.Length != need) { err = "size " + b.Length + " bytes, header says " + need; return null; }
            k.q = new ushort[k.nf * k.ncan * k.nv * 3];
            Buffer.BlockCopy(b, hb, k.q, 0, k.nf * k.ncan * k.nv * 6);
            if (k.kind == 1)
            {
                k.open = new float[k.nf];
                for (int f = 0; f < k.nf; f++)
                {
                    double s = 0; int o = f * k.nv * 3;
                    for (int i = 0; i < k.nv; i++) { double x = k.mnX + k.scX * k.q[o + 3 * i], y = k.mnY + k.scY * k.q[o + 3 * i + 1]; s += Math.Sqrt(x * x + y * y); }
                    k.open[f] = (float)(s / k.nv);
                }
                float r0 = k.open[0], r1 = k.open[k.nf - 1];
                if (!(r1 - r0 > 1e-3f)) { err = "opening does not widen (" + r0 + " -> " + r1 + " m)"; return null; }
                for (int f = 0; f < k.nf; f++) k.open[f] = (k.open[f] - r0) / (r1 - r0);
            }
            return k;
        }

        internal const float RipLow = 0.25f;
        internal const float RipRamp = 1.5f;
        internal const bool OpenKeepFull = true;

        internal class Opening
        {
            internal readonly Bake bk; internal readonly float dur; internal readonly Vector3[] work, lbuf, lC, lD, lA, hOff; internal readonly float[] lt; internal readonly int[] lAtt;
            internal readonly int[] src = new int[3]; internal readonly float[] off = new float[3], scl = new float[3];
            internal float tp; internal readonly string name;
            internal Opening(Bake k, int lineVerts, string nm)
            {
                bk = k; name = nm; dur = (k.nf - 1) / k.fps; work = new Vector3[k.nv]; lbuf = new Vector3[lineVerts]; lt = new float[lineVerts];
                lC = new Vector3[k.nAtt]; lD = new Vector3[k.nAtt]; lA = new Vector3[k.nAtt]; hOff = new Vector3[k.nAtt]; lAtt = new int[k.nAtt];
            }
        }

        internal static void PreloadDrogueOpening(int count)
        {
            count = Math.Min(Math.Max(count, 0), NDOpen);
            for (int v = 0; v < count; v++) Queue(DOpenSlot(v));
        }
        internal static void PreloadOpening(int count)
        {
            count = Math.Min(Math.Max(count, 0), NOpen);
            if (count <= askedOpen) return;
            askedOpen = count;
            for (int v = 0; v < count; v++) Queue(NCollapse + v);
        }

        internal static Bake TryGetOpening(int v, bool drogue = false)
        {
            if (v < 0 || v >= (drogue ? NDOpen : NOpen)) return null;
            int s = drogue ? DOpenSlot(v) : NCollapse + v; Queue(s);
            lock (gate)
            {
                if (state[s] != 2)
                {
                    if (!openWaitNoted) { openWaitNoted = true; Debug.Log("[CaelusEntryDrag] ChuteFx: cloth opening data " + PathOf(s) + " still loading - procedural opening this time"); }
                    return null;
                }
                var k = bakes[s];
                if (k != null && k.kind != 1) { k = null; if (!logged[s]) note[s] = "not an opening file"; }
                if (!logged[s])
                {
                    logged[s] = true;
                    if (k != null) Debug.Log("[CaelusEntryDrag] ChuteFx: cloth opening v" + (v + 1) + " loaded, " + k.nf + " frames at " + k.fps.ToString("F0") + " fps (" + ((k.nf - 1) / k.fps).ToString("F1") + " s), " + k.nv + " vertices, " + (k.bytes / 1048576f).ToString("F1") + " MB");
                    else if (!openNoted) { openNoted = true; Debug.Log("[CaelusEntryDrag] ChuteFx: cloth opening data " + PathOf(s) + " not used (" + note[s] + ") - mains open with the procedural Inflate"); }
                }
                return k;
            }
        }

        internal static Opening StartOpening(int v, Vector3[] c0, Vector3[] l0, CoronaChuteFx.LineThick lth, int ax, float sg, string name, bool drogue = false)
        {
            var k = TryGetOpening(v, drogue); if (k == null) return null;
            string why; float err, worst;
            var op = SetupOpening(k, c0, l0, lth, ax, sg, name, out why, out err, out worst);
            if (op == null) Debug.LogWarning("[CaelusEntryDrag] ChuteFx: cloth opening skipped on " + name + " - " + why + " - procedural opening");
            else Debug.Log("[CaelusEntryDrag] ChuteFx: baked cloth opening on " + name + " v" + (v + 1) + " (rest match " + (err * 1000f).ToString("F1") + " mm, lines to hem " + (worst * 1000f).ToString("F1") + " mm)");
            return op;
        }

        internal static Opening SetupOpening(Bake k, Vector3[] c0, Vector3[] l0, CoronaChuteFx.LineThick lth, int ax, float sg, string name, out string why, out float err, out float worst)
        {
            why = null; err = -1f; worst = -1f;
            if (c0 == null || l0 == null || lth == null || lth.cen == null || c0.Length != 2 * k.nv || l0.Length != 12 * k.nAtt || lth.off.Length != l0.Length)
            {
                why = "mesh has " + (c0 != null ? c0.Length : 0) + " / " + (l0 != null ? l0.Length : 0) + " vertices, data wants " + (2 * k.nv) + " / " + (12 * k.nAtt);
                return null;
            }
            var op = new Opening(k, l0.Length, name);

            float[] mn = { k.mnX, k.mnY, k.mnZ }, sc = { k.scX, k.scY, k.scZ };
            for (int a = 0; a < 3; a++)
            {
                Vector3 e = a == 0 ? new Vector3(1, 0, 0) : (a == 1 ? new Vector3(0, 1, 0) : new Vector3(0, 0, 1));
                Vector3 w = CoronaChuteFx.ToZ(new Vector3(e.x, e.z, e.y), ax, sg);
                int j = Mathf.Abs(w.x) > 0.5f ? 0 : (Mathf.Abs(w.y) > 0.5f ? 1 : 2); float s = w[j] < 0f ? -1f : 1f;
                op.src[j] = a; op.off[j] = s * mn[a]; op.scl[j] = s * sc[a];
            }

            err = 0f; int step = Math.Max(1, k.nv / 211), oL = (k.nf - 1) * k.nv * 3;
            for (int i = 0; i < k.nv; i += step)
            {
                int i3 = oL + i * 3;
                var p = new Vector3(op.off[0] + op.scl[0] * k.q[i3 + op.src[0]], op.off[1] + op.scl[1] * k.q[i3 + op.src[1]], op.off[2] + op.scl[2] * k.q[i3 + op.src[2]]);
                err = Math.Max(err, Math.Max((p - c0[i]).magnitude, (c0[i + k.nv] - c0[i]).magnitude));
            }
            if (!(err < 0.05f)) { why = "the last frame does not match the canopy mesh (" + err.ToString("F3") + " m)"; return null; }

            why = RigLines(k, c0, l0, lth, op.lAtt, op.lC, op.lD, op.lA, op.hOff, op.lt, out worst); if (why != null) return null;
            if (!(worst < 0.25f)) { why = "line ends " + worst.ToString("F2") + " m from the hem attach points"; return null; }
            return op;
        }

        internal static string RigLines(Bake k, Vector3[] c0, Vector3[] l0, CoronaChuteFx.LineThick lth, int[] lAtt, Vector3[] lC, Vector3[] lD, Vector3[] lA, Vector3[] hOff, float[] lt, out float worst)
        {
            worst = 0f;
            for (int g = 0; g < k.nAtt; g++)
            {
                int b0 = g * 12; float dmax = 0f; for (int i = b0; i < b0 + 12; i++) dmax = Mathf.Max(dmax, l0[i].magnitude);
                Vector3 a = Vector3.zero, h = Vector3.zero; int na = 0, nh = 0;
                for (int i = b0; i < b0 + 12; i++) { if (l0[i].magnitude < dmax * 0.5f) { a += l0[i]; na++; } else { h += l0[i]; nh++; } }
                if (na == 0 || nh == 0) return "line " + g + " has no ends";
                a /= na; h /= nh;
                float best = float.MaxValue; int bi = 0;
                for (int q = 0; q < k.nAtt; q++) { float dd = (c0[k.attRow * k.cols + k.attStep * q] - h).sqrMagnitude; if (dd < best) { best = dd; bi = q; } }
                worst = Mathf.Max(worst, Mathf.Sqrt(best));
                int av = k.attRow * k.cols + k.attStep * bi; Vector3 d = h - a; float L2 = d.sqrMagnitude;
                if (!(L2 > 1e-6f)) return "line " + g + " has no length";
                lAtt[g] = av; lC[g] = a; lD[g] = d; lA[g] = d / Mathf.Sqrt(L2); hOff[g] = h - c0[av];
                for (int i = b0; i < b0 + 12; i++) lt[i] = Vector3.Dot(lth.cen[i] - a, d) / L2;
            }
            return null;
        }

        internal static void OpenFrame(Opening op, Vector3[] rest)
        {
            var k = op.bk; int nv = k.nv, last = k.nf - 1; ushort[] q = k.q; var w = op.work;
            float ft = op.tp * k.fps; int k0; float fa;
            if (!(ft > 0f)) { k0 = 0; fa = 0f; }
            else if (ft >= last) { k0 = last; fa = 0f; }
            else { k0 = (int)ft; fa = ft - k0; if (k0 >= last) { k0 = last; fa = 0f; } }
            if (k0 == last) { Array.Copy(rest, w, nv); return; }
            int s0 = op.src[0], s1 = op.src[1], s2 = op.src[2]; float f0 = op.off[0], f1 = op.off[1], f2 = op.off[2], c0 = op.scl[0], c1 = op.scl[1], c2 = op.scl[2], ia = 1f - fa;
            int o0 = k0 * nv * 3, o1 = (k0 + 1) * nv * 3;
            if (k0 + 1 == last)
            {
                for (int i = 0; i < nv; i++)
                {
                    int i3 = o0 + i * 3; Vector3 r = rest[i];
                    w[i] = new Vector3((f0 + c0 * q[i3 + s0]) * ia + r.x * fa, (f1 + c1 * q[i3 + s1]) * ia + r.y * fa, (f2 + c2 * q[i3 + s2]) * ia + r.z * fa);
                }
                return;
            }
            for (int i = 0; i < nv; i++)
            {
                int a3 = o0 + i * 3, b3 = o1 + i * 3;
                w[i] = new Vector3(f0 + c0 * (q[a3 + s0] * ia + q[b3 + s0] * fa), f1 + c1 * (q[a3 + s1] * ia + q[b3 + s1] * fa), f2 + c2 * (q[a3 + s2] * ia + q[b3 + s2] * fa));
            }
        }

        internal static Vector3[] OpenLines(Opening op, CoronaChuteFx.LineThick lth, float kThick)
        {
            return BuildLines(op.work, op.bk.nAtt, op.lAtt, op.lC, op.lA, op.hOff, op.lt, lth.off, kThick, op.lbuf);
        }

        internal static Vector3[] BuildLines(Vector3[] work, int nAtt, int[] lAtt, Vector3[] lC, Vector3[] lA, Vector3[] hOff, float[] lt, Vector3[] off, float kThick, Vector3[] lb)
        {
            for (int g = 0; g < nAtt; g++)
            {
                Vector3 C = lC[g], d = work[lAtt[g]] + hOff[g] - C; float L = d.magnitude;
                Vector3 a = lA[g], b = L > 1e-5f ? d / L : a;
                float cd = Vector3.Dot(a, b); Vector3 wv = Vector3.Cross(a, b); float fi = cd > -0.99f ? 1f / (1f + cd) : 0f;
                int b0 = g * 12;
                for (int i = b0; i < b0 + 12; i++)
                {
                    Vector3 o = off[i] * kThick;
                    Vector3 r = o * cd + Vector3.Cross(wv, o) + wv * (Vector3.Dot(wv, o) * fi);
                    lb[i] = C + d * lt[i] + r;
                }
            }
            return lb;
        }

        internal static float Openness(Opening op)
        {
            var k = op.bk; float ft = op.tp * k.fps; int last = k.nf - 1;
            if (!(ft > 0f)) return Mathf.Clamp01(k.open[0]);
            if (ft >= last) return 1f;
            int k0 = (int)ft; float fa = ft - k0; return Mathf.Clamp01(k.open[k0] + (k.open[Math.Min(k0 + 1, last)] - k.open[k0]) * fa);
        }
        static float S01o(float x) { x = x < 0f ? 0f : (x > 1f ? 1f : x); return x * x * (3f - 2f * x); }

        internal static float RipW(Opening op) { return RipLow + (1f - RipLow) * S01o((op.tp - (op.dur - RipRamp)) / RipRamp); }

        internal static float HandW(Opening op) { return OpenKeepFull ? 0f : S01o((op.tp - (op.dur - 2f)) / 2f); }

        internal static float MainWidth(float rr, Vector3 restS, float infl, Opening op, bool done)
        {
            float full = Mathf.Clamp(restS.y / restS.x, 1f, 25f);
            if (op != null) return Mathf.Max(rr, Mathf.Lerp(full, 1f, HandW(op)));
            if (done && OpenKeepFull) return Mathf.Max(rr, full);
            return Mathf.Max(rr, Mathf.Lerp(full, 1f, infl));
        }

        internal const float LoopBlend = 2.0f;
        internal const float LoopRip = 0.1f;
        internal const float LoopFullW = 0.9f;

        internal const bool MainPose = false;
        internal const bool Diag = false;
        internal const float RiserK = 1f / 3f;
        internal const float LineCut = 6.5f;
        internal const bool DroguePose = false;
        internal const int TexRepeat = 12;
        internal static float LoopW(float w) { return S01o(w); }
        static float Wrap(float a) { return a - 6.2831853f * Mathf.Floor((a + 3.14159265f) / 6.2831853f); }

        internal class Descent
        {
            internal readonly Bake bk; internal readonly int k; internal readonly bool pose; internal readonly string name;
            internal readonly int[] map; internal readonly Vector3[] work, lbuf, lC, lD, lA, hOff; internal readonly float[] lt; internal readonly int[] lAtt;
            internal readonly int[] src = new int[3]; internal readonly float[] off = new float[3], scl = new float[3];

            internal int shift; internal float cr = 1f, sr, s0, thT, psi, dev, devMax, devAng, worst;
            internal Descent(Bake b, int kk, bool ps, int lineVerts, string nm)
            {
                bk = b; k = kk; pose = ps; name = nm; map = new int[b.nv]; work = new Vector3[b.nv]; lbuf = new Vector3[lineVerts]; lt = new float[lineVerts];
                lC = new Vector3[b.nAtt]; lD = new Vector3[b.nAtt]; lA = new Vector3[b.nAtt]; hOff = new Vector3[b.nAtt]; lAtt = new int[b.nAtt];
            }
        }

        internal static void PreloadDescent(bool drogue) { Queue(NCollapse + NOpen + (drogue ? 1 : 0)); }

        internal static Bake TryGetDescent(bool drogue, out int st)
        {
            int s = NCollapse + NOpen + (drogue ? 1 : 0); Queue(s);
            lock (gate)
            {
                st = state[s]; if (st != 2) return null;
                var k = bakes[s];
                if (k != null && (k.kind != 2 || k.pose == null)) { k = null; if (!logged[s]) note[s] = "not a descent loop file"; }
                if (!logged[s])
                {
                    logged[s] = true;
                    if (k != null) Debug.Log("[CaelusEntryDrag] ChuteFx: cloth descent loop (" + (drogue ? "drogues" : "mains") + ") loaded, " + k.ncan + " canopies, " + k.nf + " frames at " + k.fps.ToString("F0") + " fps (" + k.period.ToString("F1") + " s loop), " + k.nv + " vertices, " + (k.bytes / 1048576f).ToString("F1") + " MB");
                    else Debug.Log("[CaelusEntryDrag] ChuteFx: cloth descent loop data " + PathOf(s) + " not used (" + note[s] + ") - the " + (drogue ? "drogues" : "mains") + " keep the procedural ripple");
                }
                return k;
            }
        }

        internal static Descent StartDescent(bool drogue, int idx, Vector3[] c0, Vector3[] l0, CoronaChuteFx.LineThick lth, int ax, float sg, string name, float spin0, float inAz, bool canPose, out int st)
        {
            var k = TryGetDescent(drogue, out st); if (k == null) return null;
            string why; bool pose = canPose && (drogue ? DroguePose : MainPose);
            var d = SetupDescent(k, idx, c0, l0, lth, ax, sg, name, spin0, inAz, pose, out why);
            if (d == null) Debug.LogWarning("[CaelusEntryDrag] ChuteFx: cloth descent loop skipped on " + name + " - " + why + " - procedural ripple");
            else Debug.Log("[CaelusEntryDrag] ChuteFx: baked descent loop on " + name + " (canopy " + (idx + 1) + " of " + k.ncan + ", frame 0 vs mesh: distance mean " + d.dev.ToString("F2") + " m / max " + d.devMax.ToString("F2") +
                           " m, direction " + (d.devAng * Mathf.Rad2Deg).ToString("F1") + " deg, columns +" + d.shift + ", turn " + ((d.thT - spin0) * Mathf.Rad2Deg).ToString("F1") + " deg" + (pose ? ", risers from the bake" : ", faced to the neighbour " + (d.psi * Mathf.Rad2Deg).ToString("F1") + " deg") +
                           (float.IsNaN(inAz) ? ", cluster axis unknown" : "") + ", lines to hem " + (d.worst * 1000f).ToString("F1") + " mm)");
            return d;
        }

        internal static Descent SetupDescent(Bake k, int idx, Vector3[] c0, Vector3[] l0, CoronaChuteFx.LineThick lth, int ax, float sg, string name, float spin0, float inAz, bool pose, out string why)
        {
            why = null;
            if (k.kind != 2 || k.pose == null) { why = "not a descent loop"; return null; }
            if (idx < 0 || idx >= k.ncan) { why = "canopy " + (idx + 1) + " of a " + k.ncan + "-canopy loop"; return null; }
            int nc = k.cols - 1;
            if (nc % TexRepeat != 0 || k.attRow != k.rows - 1) { why = "grid " + k.rows + " x " + k.cols + " does not fit"; return null; }
            if (c0 == null || l0 == null || lth == null || lth.cen == null || c0.Length != 2 * k.nv || l0.Length != 12 * k.nAtt || lth.off.Length != l0.Length)
            {
                why = "mesh has " + (c0 != null ? c0.Length : 0) + " / " + (l0 != null ? l0.Length : 0) + " vertices, data wants " + (2 * k.nv) + " / " + (12 * k.nAtt);
                return null;
            }
            var d = new Descent(k, idx, pose, l0.Length, name);

            float[] mn = { k.mnX, k.mnY, k.mnZ }, sc = { k.scX, k.scY, k.scZ };
            for (int a = 0; a < 3; a++)
            {
                Vector3 e = a == 0 ? new Vector3(1, 0, 0) : (a == 1 ? new Vector3(0, 1, 0) : new Vector3(0, 0, 1));
                Vector3 w = CoronaChuteFx.ToZ(new Vector3(e.x, e.z, e.y), ax, sg);
                int j = Mathf.Abs(w.x) > 0.5f ? 0 : (Mathf.Abs(w.y) > 0.5f ? 1 : 2); float s = w[j] < 0f ? -1f : 1f;
                d.src[j] = a; d.off[j] = s * mn[a]; d.scl[j] = s * sc[a];
            }

            float R = 0f; for (int i = 0; i < k.nv; i++) R = Mathf.Max(R, Mathf.Sqrt(c0[i].x * c0[i].x + c0[i].y * c0[i].y));
            int step = Math.Max(1, k.nv / 211), o0 = idx * k.nv * 3; double sum = 0, asum = 0; int cnt = 0; float mx = 0f, inner = 0f;
            for (int i = 0; i < k.nv; i += step)
            {
                int i3 = o0 + i * 3;
                var p = new Vector3(d.off[0] + d.scl[0] * k.q[i3 + d.src[0]], d.off[1] + d.scl[1] * k.q[i3 + d.src[1]], d.off[2] + d.scl[2] * k.q[i3 + d.src[2]]);
                float pm = p.magnitude, cm = c0[i].magnitude, e = Mathf.Abs(pm - cm); sum += e; cnt++; mx = Mathf.Max(mx, e); inner = Mathf.Max(inner, (c0[i + k.nv] - c0[i]).magnitude);
                if (pm > 1e-3f && cm > 1e-3f) asum += Math.Acos(Mathf.Clamp(Vector3.Dot(p, c0[i]) / (pm * cm), -1f, 1f));
            }
            d.dev = (float)(sum / Math.Max(1, cnt)); d.devMax = mx; d.devAng = (float)(asum / Math.Max(1, cnt));
            if (!(R > 1e-3f) || !(d.dev < 0.1f * R) || !(mx < 0.3f * R) || !(d.devAng < 0.15f) || !(inner < 0.05f))
            { why = "frame 0 does not match the canopy mesh (distance from the confluence: mean " + d.dev.ToString("F2") + " m, max " + mx.ToString("F2") + " m; direction mean " + (d.devAng * Mathf.Rad2Deg).ToString("F1") + " deg; inner shell " + inner.ToString("F3") + " m; R " + R.ToString("F2") + " m)"; return null; }
            float worst; why = RigLines(k, c0, l0, lth, d.lAtt, d.lC, d.lD, d.lA, d.hOff, d.lt, out worst); if (why != null) return null;
            d.worst = worst;
            if (!(worst < 0.03f * R)) { why = "line ends " + worst.ToString("F2") + " m from the hem attach points"; return null; }

            float psi = 0f;
            if (!pose && !float.IsNaN(inAz))
            {
                double rx = 0, ry = 0, rz = 0; float[] po = k.pose;
                for (int f = 0; f < k.nf; f++) { int b = (f * k.ncan + idx) * 3; rx += po[b]; ry += po[b + 1]; rz += po[b + 2]; }
                float[] inF = { -(float)rx, -(float)ry, 0f }; Vector3 iw = Vector3.zero;
                for (int j = 0; j < 3; j++) iw[j] = (d.scl[j] < 0f ? -1f : 1f) * inF[d.src[j]];
                if (iw.x * iw.x + iw.y * iw.y > 1e-8f) psi = Wrap(inAz - Mathf.Atan2(iw.y, iw.x));
            }
            d.psi = psi;

            int hr = k.attRow * k.cols; float a0 = Mathf.Atan2(c0[hr].y, c0[hr].x);
            float rep = Wrap(Mathf.Atan2(c0[hr + TexRepeat].y, c0[hr + TexRepeat].x) - a0);
            int nrep = nc / TexRepeat, m = Mathf.Abs(rep) > 1e-4f ? Mathf.RoundToInt(Wrap(spin0 - psi) / rep) : 0;
            m = ((m % nrep) + nrep) % nrep; d.shift = m * TexRepeat;
            float aD = Wrap(Mathf.Atan2(c0[hr + d.shift].y, c0[hr + d.shift].x) - a0);
            d.cr = Mathf.Cos(-aD); d.sr = Mathf.Sin(-aD);
            d.s0 = spin0; d.thT = spin0 + Wrap(aD + psi - spin0);
            for (int r = 0; r < k.rows; r++) for (int j = 0; j < k.cols; j++) d.map[r * k.cols + j] = r * k.cols + (j + d.shift) % nc;
            return d;
        }

        internal static void DescFrame(Descent d, Vector3[] rest, float now, float w)
        {
            var k = d.bk; int nv = k.nv; ushort[] q = k.q; var wk = d.work; int[] map = d.map;
            float per = k.period, tl = now - per * Mathf.Floor(now / per), ft = tl * k.fps; int f0 = (int)ft; float fa = ft - f0;
            if (f0 < 0 || f0 >= k.nf) { f0 = 0; fa = 0f; }
            int f1 = f0 + 1 >= k.nf ? 0 : f0 + 1;
            int o0 = (f0 * k.ncan + d.k) * nv * 3, o1 = (f1 * k.ncan + d.k) * nv * 3;
            int i0 = d.src[0], i1 = d.src[1], i2 = d.src[2]; float g0 = d.off[0], g1 = d.off[1], g2 = d.off[2], c0 = d.scl[0], c1 = d.scl[1], c2 = d.scl[2], ia = 1f - fa, cr = d.cr, sr = d.sr;
            bool full = w >= 1f;
            for (int i = 0; i < nv; i++)
            {
                int a3 = o0 + map[i] * 3, b3 = o1 + map[i] * 3;
                float x = g0 + c0 * (q[a3 + i0] * ia + q[b3 + i0] * fa), y = g1 + c1 * (q[a3 + i1] * ia + q[b3 + i1] * fa), z = g2 + c2 * (q[a3 + i2] * ia + q[b3 + i2] * fa);
                float xr = x * cr - y * sr, yr = x * sr + y * cr;
                if (full) wk[i] = new Vector3(xr, yr, z);
                else { Vector3 r = rest[i]; wk[i] = new Vector3(r.x + (xr - r.x) * w, r.y + (yr - r.y) * w, r.z + (z - r.z) * w); }
            }
        }

        internal static Vector3[] DescLines(Descent d, CoronaChuteFx.LineThick lth, float kThick)
        {
            return BuildLines(d.work, d.bk.nAtt, d.lAtt, d.lC, d.lA, d.hOff, d.lt, lth.off, kThick, d.lbuf);
        }

        internal static bool MainFan(int idx, out Vector3 dir)
        {
            dir = Vector3.up;
            int st; var k = TryGetDescent(false, out st); if (k == null || k.pose == null || idx < 0 || idx >= k.ncan) return false;
            int a = idx * 3; float[] p = k.pose; var u = new Vector3(p[a], p[a + 2], p[a + 1]); float m = u.magnitude;
            if (!(m > 1e-6f)) return false; dir = u / m; return true;
        }

        internal static Vector3 DescRiser(Descent d, float now)
        {
            var k = d.bk; float per = k.period, tl = now - per * Mathf.Floor(now / per), ft = tl * k.fps; int f0 = (int)ft; float fa = ft - f0;
            if (f0 < 0 || f0 >= k.nf) { f0 = 0; fa = 0f; }
            int f1 = f0 + 1 >= k.nf ? 0 : f0 + 1, a = (f0 * k.ncan + d.k) * 3, b = (f1 * k.ncan + d.k) * 3; float[] p = k.pose;
            var u = new Vector3(p[a] + (p[b] - p[a]) * fa, p[a + 2] + (p[b + 2] - p[a + 2]) * fa, p[a + 1] + (p[b + 1] - p[a + 1]) * fa);
            float m = u.magnitude; return m > 1e-6f ? u / m : Vector3.up;
        }

        internal static Quaternion Arc(Vector3 a, Vector3 b)
        {
            float c = Vector3.Dot(a, b);
            if (c < -0.99999f) { Vector3 p = Mathf.Abs(a.x) < 0.9f ? Vector3.Cross(a, new Vector3(1, 0, 0)) : Vector3.Cross(a, new Vector3(0, 1, 0)); p.Normalize(); return new Quaternion(p.x, p.y, p.z, 0f); }
            Vector3 v = Vector3.Cross(a, b); float s = Mathf.Sqrt((1f + c) * 2f), i = 1f / s;
            return new Quaternion(v.x * i, v.y * i, v.z * i, s * 0.5f);
        }

        internal static Quaternion Nlerp(Quaternion a, Quaternion b, float t)
        {
            if (!(t > 0f)) return a; if (t >= 1f) return b;
            float dt = a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w, u = 1f - t, v = dt < 0f ? -t : t;
            float x = a.x * u + b.x * v, y = a.y * u + b.y * v, z = a.z * u + b.z * v, w = a.w * u + b.w * v, m = Mathf.Sqrt(x * x + y * y + z * z + w * w);
            return m > 1e-6f ? new Quaternion(x / m, y / m, z / m, w / m) : b;
        }

        internal static float Check(Bake k, Vector3[] v0)
        {
            float e = 0f; var R = k.R0; int step = Math.Max(1, k.nv / 211);
            for (int i = 0; i < k.nv; i += step)
            {
                Vector3 v = v0[i]; float rx = v.x, ry = v.z, rz = v.y; int i3 = i * 3;
                float dx = R[0] * rx + R[1] * ry + R[2] * rz - (k.mnX + k.scX * k.q[i3]);
                float dy = R[3] * rx + R[4] * ry + R[5] * rz - (k.mnY + k.scY * k.q[i3 + 1]);
                float dz = R[6] * rx + R[7] * ry + R[8] * rz - (k.mnZ + k.scZ * k.q[i3 + 2]);
                e = Math.Max(e, (float)Math.Sqrt(dx * dx + dy * dy + dz * dz));
                e = Math.Max(e, (v0[i + k.nv] - v).magnitude);
            }
            return e;
        }

        internal static float NormalSign(Bake k, Vector3[] v0, Vector3[] n0, int off, float dflt)
        {
            if (n0 == null || n0.Length < off + k.nv) return dflt;
            var tmp = new Vector3[2 * k.nv]; Normals(v0, tmp, k.rows, k.cols, k.nv, 1f, 1f);
            double s = 0; for (int i = 0; i < k.nv; i++) s += Vector3.Dot(tmp[i], n0[i + off]);
            return s > 0 ? 1f : (s < 0 ? -1f : dflt);
        }

        static float S01(float x) { x = x < 0f ? 0f : (x > 1f ? 1f : x); return x * x * (3f - 2f * x); }
        internal static float BlendW(float tp) { return tp < Blend ? S01(tp / Blend) : 1f; }

        internal static float DropW(Playback pb, float tp) { return S01(tp / Math.Max(0.5f, 0.9f * pb.bk.tTouch)); }

        internal static Vector3 Rod(Vector3 k, float th, Vector3 v)
        {
            float c = Mathf.Cos(th), s = Mathf.Sin(th);
            return v * c + Vector3.Cross(k, v) * s + k * (Vector3.Dot(k, v) * (1f - c));
        }

        internal static void Match(Playback pb, Quaternion canopyRot, Vector3 h1, Vector3 h2, Vector3 up)
        {
            var k = pb.bk; var R = k.R0;

            Vector3 c0 = QCol(canopyRot, R[0], R[1], R[2], h1, h2, up), c1 = QCol(canopyRot, R[3], R[4], R[5], h1, h2, up), c2 = QCol(canopyRot, R[6], R[7], R[8], h1, h2, up);
            Vector3 ab = new Vector3(R[2], R[5], R[8]).normalized;
            Vector3 lb = (c0 * ab.x + c1 * ab.y + c2 * ab.z).normalized;
            Vector3 kt = Vector3.Cross(ab, lb); float st = kt.magnitude, tt = Mathf.Atan2(st, Vector3.Dot(ab, lb));
            kt = st > 1e-6f ? kt / st : Vector3.Cross(ab, Mathf.Abs(ab.x) < 0.9f ? new Vector3(1, 0, 0) : new Vector3(0, 1, 0)).normalized;

            Vector3 p = Vector3.Cross(ab, Mathf.Abs(ab.x) < 0.9f ? new Vector3(1, 0, 0) : new Vector3(0, 1, 0)).normalized;
            Vector3 p1 = Rod(kt, -tt, c0 * p.x + c1 * p.y + c2 * p.z);
            float psi = Mathf.Atan2(Vector3.Dot(Vector3.Cross(p, p1), ab), Vector3.Dot(p, p1));
            int cu = k.cols - 1, per = 2 * k.attStep;
            float step = 2f * Mathf.PI * per / cu; int cg = Mathf.RoundToInt(psi / step);
            pb.colShift = ((cg * per) % cu + cu) % cu;
            for (int j = 0; j < k.cols; j++) pb.colMap[j] = ((j % cu) - pb.colShift + cu) % cu;
            if (pb.lineAtt != null) for (int g = 0; g < pb.lineAtt.Length; g++) pb.lineAtt[g] = ((pb.lineAtt[g] + cg * per / k.attStep) % k.nAtt + k.nAtt) % k.nAtt;

            float pr = -cg * step;
            Vector3 d0 = Rod(ab, pr, new Vector3(1, 0, 0)), d1 = Rod(ab, pr, new Vector3(0, 1, 0)), d2 = Rod(ab, pr, new Vector3(0, 0, 1));
            Vector3 m0 = c0 * d0.x + c1 * d0.y + c2 * d0.z, m1 = c0 * d1.x + c1 * d1.y + c2 * d1.z, m2 = c0 * d2.x + c1 * d2.y + c2 * d2.z;
            AxisAngle(m0, m1, m2, out pb.qk, out pb.qth);
        }
        static Vector3 QCol(Quaternion rot, float rx, float ry, float rz, Vector3 h1, Vector3 h2, Vector3 up)
        {
            Vector3 w = rot * new Vector3(rx, rz, ry);
            return new Vector3(Vector3.Dot(w, h1), Vector3.Dot(w, h2), Vector3.Dot(w, up));
        }

        internal static void AxisAngle(Vector3 m0, Vector3 m1, Vector3 m2, out Vector3 axis, out float angle)
        {
            float m00 = m0.x, m10 = m0.y, m20 = m0.z, m01 = m1.x, m11 = m1.y, m21 = m1.z, m02 = m2.x, m12 = m2.y, m22 = m2.z, tr = m00 + m11 + m22, x, y, z, w;
            if (tr > 0f) { float s = Mathf.Sqrt(tr + 1f) * 2f; w = 0.25f * s; x = (m21 - m12) / s; y = (m02 - m20) / s; z = (m10 - m01) / s; }
            else if (m00 > m11 && m00 > m22) { float s = Mathf.Sqrt(1f + m00 - m11 - m22) * 2f; w = (m21 - m12) / s; x = 0.25f * s; y = (m01 + m10) / s; z = (m02 + m20) / s; }
            else if (m11 > m22) { float s = Mathf.Sqrt(1f + m11 - m00 - m22) * 2f; w = (m02 - m20) / s; x = (m01 + m10) / s; y = 0.25f * s; z = (m12 + m21) / s; }
            else { float s = Mathf.Sqrt(1f + m22 - m00 - m11) * 2f; w = (m10 - m01) / s; x = (m02 + m20) / s; y = (m12 + m21) / s; z = 0.25f * s; }
            if (w < 0f) { w = -w; x = -x; y = -y; z = -z; }
            float sn = Mathf.Sqrt(x * x + y * y + z * z);
            angle = 2f * Mathf.Atan2(sn, w); axis = sn > 1e-7f ? new Vector3(x / sn, y / sn, z / sn) : new Vector3(0, 0, 1);
            if (!(angle > 1e-6f) || float.IsNaN(axis.x)) { angle = 0f; axis = new Vector3(0, 0, 1); }
        }

        internal static void Level(Playback pb, Vector3 up, Vector3 e1, Vector3 e2, float tp, out Vector3 h1, out Vector3 h2)
        {
            float az = pb.az0 + pb.azOff * S01(tp / Math.Max(0.5f, pb.bk.tTouch));
            Vector3 d = e1 * Mathf.Cos(az) + e2 * Mathf.Sin(az);
            Vector3 H = d * pb.cosL - Vector3.Cross(d, up) * pb.sinL;
            h1 = Vector3.ProjectOnPlane(H, up); h1 = h1.sqrMagnitude > 1e-8f ? h1.normalized : e1; h2 = Vector3.Cross(h1, up);
        }

        internal static void Verts(Playback pb, Vector3[] v0, Vector3[] dst, Matrix4x4 w2l, Vector3 origin, Vector3 h1, Vector3 h2, Vector3 up,
                                   Vector3 e1, Vector3 e2, float tp, float now, float seed)
        {
            var k = pb.bk; int nv = k.nv, rows = k.rows, cols = k.cols, ns = pb.ns; float s = pb.sW; int[] cm = pb.colMap;
            float ft = tp * k.fps; int k0; float fa;
            if (!(ft > 0f)) { k0 = 0; fa = 0f; }
            else if (ft >= k.nf - 1) { k0 = k.nf - 1; fa = 0f; }
            else { k0 = (int)ft; fa = ft - k0; }
            int o0 = k0 * nv * 3, o1 = Math.Min(k0 + 1, k.nf - 1) * nv * 3; float ia = 1f - fa;

            Vector3 ax = new Vector3(1, 0, 0), ay = new Vector3(0, 1, 0), az = new Vector3(0, 0, 1);
            float th = pb.qth * (1f - DropW(pb, tp));
            if (th > 1e-5f) { ax = Rod(pb.qk, th, ax); ay = Rod(pb.qk, th, ay); az = Rod(pb.qk, th, az); }
            Vector3 bx = h1 * ax.x + h2 * ax.y + up * ax.z, by = h1 * ay.x + h2 * ay.y + up * ay.z, bz = h1 * az.x + h2 * az.y + up * az.z;

            Vector3 cx = w2l.MultiplyVector(bx * (s * k.scX)), cy = w2l.MultiplyVector(by * (s * k.scY)), cz = w2l.MultiplyVector(bz * (s * k.scZ)), cu = w2l.MultiplyVector(up);
            Vector3 c0 = w2l.MultiplyPoint3x4(origin + (bx * k.mnX + by * k.mnY + bz * k.mnZ) * s);
            ushort[] q = k.q; float[] sw = pb.swl;
            float swA = Swell * S01((tp - k.tTouch) / 2f); bool swOn = swA > 1e-4f;
            if (swOn)
            {
                float ux = s * Vector3.Dot(h1, e1), uy = s * Vector3.Dot(h2, e1), wx = s * Vector3.Dot(h1, e2), wy = s * Vector3.Dot(h2, e2);
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < ns; c++)
                    {
                        int i3 = (r * cols + Math.Min(c * 4, cols - 1)) * 3;
                        float X = k.mnX + k.scX * (q[o0 + i3] * ia + q[o1 + i3] * fa), Y = k.mnY + k.scY * (q[o0 + i3 + 1] * ia + q[o1 + i3 + 1] * fa);
                        sw[r * ns + c] = swA * (CoronaChuteFx.SeaHeight(X * ux + Y * uy, X * wx + Y * wy, now, seed) + 0.5f);
                    }
            }
            float bw = BlendW(tp), ib = 1f - bw;
            for (int r = 0; r < rows; r++)
            {
                int rb = r * cols, sb = r * ns;
                for (int j = 0; j < cols; j++)
                {
                    int i = rb + j, i3 = i * 3;
                    float qx = q[o0 + i3] * ia + q[o1 + i3] * fa, qy = q[o0 + i3 + 1] * ia + q[o1 + i3 + 1] * fa, qz = q[o0 + i3 + 2] * ia + q[o1 + i3 + 2] * fa;
                    float px = c0.x + cx.x * qx + cy.x * qy + cz.x * qz, py = c0.y + cx.y * qx + cy.y * qy + cz.y * qz, pz = c0.z + cx.z * qx + cy.z * qy + cz.z * qz;
                    if (swOn)
                    {
                        int c = j >> 2; float sv = sw[sb + c];
                        if (c < ns - 1) { int jA = c * 4, jB = Math.Min(jA + 4, cols - 1); sv += (sw[sb + c + 1] - sv) * ((j - jA) / (float)(jB - jA)); }
                        px += cu.x * sv; py += cu.y * sv; pz += cu.z * sv;
                    }
                    if (ib > 0f) { Vector3 a = v0[rb + cm[j]]; px = a.x * ib + px * bw; py = a.y * ib + py * bw; pz = a.z * ib + pz * bw; }
                    var p = new Vector3(px, py, pz); dst[i] = p; dst[i + nv] = p;
                }
            }
        }

        internal static void Normals(Vector3[] p, Vector3[] n, int rows, int cols, int nv, float sOut, float sIn)
        {
            int cu = cols - 1;
            for (int r = 0; r < rows; r++)
            {
                int bR = r * cols, bL = (r > 0 ? r - 1 : 0) * cols, bH = (r < rows - 1 ? r + 1 : rows - 1) * cols;
                for (int j = 0; j < cols; j++)
                {
                    int jj = j == cu ? 0 : j, jl = jj == 0 ? cu - 1 : jj - 1, jr = jj + 1;
                    Vector3 a = p[bR + jr], b = p[bR + jl], c = p[bH + jj], d = p[bL + jj];
                    float ux = a.x - b.x, uy = a.y - b.y, uz = a.z - b.z, vx = c.x - d.x, vy = c.y - d.y, vz = c.z - d.z;
                    float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx, l = nx * nx + ny * ny + nz * nz;
                    if (!(l > 1e-24f)) continue;
                    float inv = 1f / (float)Math.Sqrt(l); nx *= inv; ny *= inv; nz *= inv;
                    int i = bR + j; n[i] = new Vector3(nx * sOut, ny * sOut, nz * sOut); n[i + nv] = new Vector3(nx * sIn, ny * sIn, nz * sIn);
                }
            }
        }

        static readonly float[] Cs = { 1f, 0.5f, -0.5f, -1f, -0.5f, 0.5f }, Sn = { 0f, 0.8660254f, 0.8660254f, 0f, -0.8660254f, -0.8660254f };
        static float Rad(float r0, float px) { return r0 * Mathf.Clamp(0.4f * px / r0, 1f, 8f); }

        internal static void Lines(Playback pb, Vector3[] cdst, Vector3[] ldst, Matrix4x4 c2w, Matrix4x4 l2w, Matrix4x4 w2l, Vector3 o, Vector3 up, float cw, Vector3 cam, float pxm)
        {
            Vector3 conf = Vector3.Lerp(l2w.MultiplyPoint3x4(pb.confL), o, cw);
            float r0 = Math.Max(pb.lineR * pb.sW, 1e-4f), rC = Rad(r0, (cam - conf).magnitude * pxm);
            int ce = pb.cEnd, he = 1 - pb.cEnd;
            for (int g = 0; g < pb.lineAtt.Length; g++)
            {
                Vector3 hem = c2w.MultiplyPoint3x4(cdst[pb.att[pb.lineAtt[g]]]);
                Vector3 ax = hem - conf; float L = ax.magnitude; ax = L > 1e-4f ? ax / L : up;
                Vector3 u = Vector3.Cross(ax, up); if (u.sqrMagnitude < 1e-6f) u = Vector3.Cross(ax, pb.side); u.Normalize();
                Vector3 v = Vector3.Cross(ax, u) * pb.ring;
                float rH = Rad(r0, (cam - hem).magnitude * pxm);
                int b0 = g * 12;
                for (int k = 0; k < 6; k++)
                {
                    Vector3 d = u * Cs[k] + v * Sn[k];
                    ldst[b0 + 2 * k + ce] = w2l.MultiplyPoint3x4(conf + d * rC);
                    ldst[b0 + 2 * k + he] = w2l.MultiplyPoint3x4(hem + d * rH);
                }
            }
        }
    }
}
