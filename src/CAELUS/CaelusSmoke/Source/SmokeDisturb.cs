using System;
using System.Collections.Generic;
using UnityEngine;

namespace CaelusSmoke
{
    public class SmokeDisturb
    {
        public const int MAX = 8;
        public readonly Vector4[] A = new Vector4[MAX], B = new Vector4[MAX], S = new Vector4[MAX];
        public int count;

        public readonly List<Vector4> jets = new List<Vector4>(), wakes = new List<Vector4>();
        public readonly List<Vector3> jetDirs = new List<Vector3>(), wakeVels = new List<Vector3>();

        class Wake { public readonly List<Vector3d> lla = new List<Vector3d>(); public readonly List<double> t = new List<double>(); public float R, rT; public double lastUT; }
        readonly Dictionary<Guid, Wake> wakeHist = new Dictionary<Guid, Wake>();
        struct Cand { public Vector3 a, b; public float ra, rb, sa, sb, score; }
        readonly List<Cand> cands = new List<Cand>();
        const double WakeStep = 0.4, WakeLife = 6.0;
        struct Cl { public Vector3 o, d; public float F, reach; }
        readonly List<Cl> cl = new List<Cl>();

        public void Update(CelestialBody body, double ut, Dictionary<ModuleEngines, float> engineReach, HashSet<Vessel> denseVessels, Vector3 cam)
        {
            cands.Clear(); jets.Clear(); jetDirs.Clear(); wakes.Clear(); wakeVels.Clear(); count = 0;
            if (body == null || !body.atmosphere || !CaelusUser.blast) { wakeHist.Clear(); return; }
            Vector3 windW = Vector3.zero;
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                if (v == null || v.packed || v.mainBody != body || v.altitude > body.atmosphereDepth) continue;

                cl.Clear();
                foreach (Part p in v.parts)
                    foreach (PartModule pm in p.Modules)
                    {
                        var en = pm as ModuleEngines;
                        if (en == null || !en.EngineIgnited || en.flameout || en.finalThrust <= 0.5f || en.thrustTransforms.Count == 0) continue;
                        Vector3 o = Vector3.zero, d = Vector3.zero;
                        foreach (Transform t in en.thrustTransforms) { o += t.position; d += t.forward; }
                        o /= en.thrustTransforms.Count; d.Normalize();
                        float F = en.finalThrust, reach = engineReach.TryGetValue(en, out float rr) ? rr : -1f;
                        int k = -1;
                        for (int j = 0; j < cl.Count; j++) if (Vector3.Dot(cl[j].d.normalized, d) > 0.95f && (cl[j].o / cl[j].F - o).magnitude < 15f) { k = j; break; }
                        if (k < 0) { cl.Add(new Cl()); k = cl.Count - 1; }
                        Cl g = cl[k]; g.o += o * F; g.d += d * F; g.F += F; if (reach > 0f) g.reach = g.reach > 0f ? Mathf.Min(g.reach, reach) : reach; cl[k] = g;
                    }
                foreach (Cl g in cl)
                {
                    Vector3 o = g.o / g.F, d = g.d.normalized;
                    float F = g.F, sq = Mathf.Sqrt(F), r0 = Mathf.Max(0.5f, 0.03f * sq);
                    float L = Mathf.Clamp(1.2f * sq, 4f, 260f);
                    jets.Add(new Vector4(o.x, o.y, o.z, F)); jetDirs.Add(d);
                    if (g.reach > 0f) continue;
                    Vector3 b = o + d * L;
                    float dc = Mathf.Max(20f, (0.5f * (o + b) - cam).magnitude);
                    cands.Add(new Cand { a = o, b = b, ra = r0, rb = r0 + 0.1f * L, sa = 0.95f, sb = 0.7f, score = (r0 + 0.1f * L) * sq / dc });
                }

                Vector3 vAir = (Vector3)v.srf_velocity - windW;
                if (!wakeHist.TryGetValue(v.id, out Wake w)) { w = new Wake(); wakeHist[v.id] = w; }
                w.lastUT = ut;
                if ((w.rT -= Time.deltaTime) <= 0f) { w.rT = 5f; w.R = Radius(v); }
                if (vAir.magnitude > 8f && (w.t.Count == 0 || ut - w.t[w.t.Count - 1] >= WakeStep))
                {
                    Vector3d c = v.CoMD; double lat, lon, alt; body.GetLatLonAlt(c, out lat, out lon, out alt);
                    w.lla.Add(new Vector3d(lat, lon, alt)); w.t.Add(ut);
                }
                while (w.t.Count > 0 && ut - w.t[0] > WakeLife) { w.t.RemoveAt(0); w.lla.RemoveAt(0); }
                wakes.Add(new Vector4((float)v.CoMD.x, (float)v.CoMD.y, (float)v.CoMD.z, w.R)); wakeVels.Add(vAir);
                if (denseVessels.Contains(v) || w.t.Count == 0 || vAir.magnitude <= 8f) continue;

                Vector3 prev = v.CoMD; double prevAge = 0;
                int step = Math.Max(1, w.t.Count / 3);
                for (int i = w.t.Count - 1; i >= 0; i -= step)
                {
                    Vector3d ll = w.lla[i]; Vector3 pt = body.GetWorldSurfacePosition(ll.x, ll.y, ll.z);
                    double age = ut - w.t[i];
                    float ra = w.R * (1f + 0.25f * (float)prevAge), rb = w.R * (1f + 0.25f * (float)age);
                    float sa = 0.95f * Mathf.Exp(-(float)prevAge / 3f), sb = 0.95f * Mathf.Exp(-(float)age / 3f);
                    float dc = Mathf.Max(20f, (0.5f * (prev + pt) - cam).magnitude);
                    cands.Add(new Cand { a = prev, b = pt, ra = ra, rb = rb, sa = sa, sb = sb, score = rb * sa * 50f / dc });
                    prev = pt; prevAge = age;
                }
            }

            List<Guid> gone = null;
            foreach (KeyValuePair<Guid, Wake> kv in wakeHist) if (ut - kv.Value.lastUT > 10.0) (gone ?? (gone = new List<Guid>())).Add(kv.Key);
            if (gone != null) foreach (Guid g in gone) wakeHist.Remove(g);
            cands.Sort((x, y) => y.score.CompareTo(x.score));
            for (int i = 0; i < cands.Count && count < MAX; i++)
            {
                Cand c = cands[i];
                A[count] = new Vector4(c.a.x, c.a.y, c.a.z, c.ra); B[count] = new Vector4(c.b.x, c.b.y, c.b.z, c.rb); S[count] = new Vector4(c.sa, c.sb, 0f, 0f);
                count++;
            }
        }

        static float Radius(Vessel v)
        {
            float r = 1f; Vector3 c = v.CoMD;
            foreach (Part p in v.parts) r = Mathf.Max(r, (p.transform.position - c).magnitude);
            return Mathf.Min(r * 0.6f + 1.5f, 40f);
        }
    }
}
