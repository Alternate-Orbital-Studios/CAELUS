using System;
using UnityEngine;

namespace CaelusSmoke
{
    public struct SteamPuff
    {
        public Vector3 p, v;
        public Vector3 p0;
        public float r, r0, rMax;
        public float age, life, seed, heat0, dens0;
        public int kind;
        public float tint0;

        public int lane;
        public Vector3 a, dir;
        public float s, L, lat, ventS, u;
    }

    public class PadSteamSettings
    {
        public bool enabled = true;
        public bool takeOverVgfx = true;
        public int maxPuffs = 560;
        public float exitSpeedK = 0.5f;
        public float exitSpeedMax = 125f;
        public float jetRadiusK = 0.06f;
        public float jetRate = 7f;
        public float fanDeg = 16f;
        public float dragTau = 0.9f;
        public float dragTauPerM = 0.025f;
        public float entrain = 0.06f;
        public float growBase = 0.3f;
        public float rMaxK = 0.36f;

        public float buoyancy = 0.8f;
        public float buoyTau = 25f;
        public float riseDrag = 0.6f;
        public float liftCap = 1.4f;
        public float pushUp = 0.5f;
        public float ignitionSmoke = 0.75f;
        public float ignitionSmokeS = 4f;
        public float baseRise = 2.5f;
        public float ventRise = 5f;
        public float hugSpeed = 30f;
        public float baseRate = 6f;
        public float washRate = 8f;
        public float footRate = 4f;

        public float actK = 3f;
        public float trenchClear = 70f;
        public float lifeS = 220f;
        public float push = 0.2f;
        public float mergeAt = 0.45f;
        public float mergeAge = 1.0f;
        public float mergeCapK = 1.25f;
        public float density = 1f;
        public float windScale = 0.7f;
        public float shadow = 0.6f;
        public float trenchHalfWidth = 9f;
        public float deflectorOffset = 12f;
        public float trenchTau = 3f;
        public float ventFraction = 0.8f;
        public float anchorTau = 0.8f;

        public float blast = 1f;
        public float blastCoreK = 0.03f;
        public float blastSpread = 0.1f;
        public float blastSpeed = 120f;

        public float entrainK = 0.6f;
        public float wallK = 1f;
    }

    public struct PadSteamInputs
    {
        public float thrustKN;
        public Vector3 nozzle;
        public int outletCount;
        public Vector3[] outlets;
        public Vector3[] outletDirs;
        public Vector3 wind;
        public Vector3 axis;
        public int xJetCount;
        public Vector4[] xJets; public Vector3[] xJetDirs;
        public int wakeCount;
        public Vector4[] wakes; public Vector3[] wakeVels;
    }

    public class PadSteamModel
    {
        public PadSteamSettings set;
        public SteamPuff[] puffs;
        public int count;
        public float time;
        public float lastF, pulse, startH = float.NaN, feedTrench, feedWash, sinceIgnition = 1e9f;
        public Vector3 lastNozzle; public float lastGround;
        readonly float[] acc = new float[9];
        System.Random rng = new System.Random(19690716);
        float interactT;
        int[] order;

        public PadSteamModel(PadSteamSettings s) { set = s; puffs = new SteamPuff[Mathf.Max(16, s.maxPuffs)]; order = new int[puffs.Length]; mark = new bool[puffs.Length]; }

        public void Clear() { count = 0; lastF = 0; pulse = 0; startH = float.NaN; for (int i = 0; i < acc.Length; i++) acc[i] = 0; }

        float R() { return (float)rng.NextDouble(); }
        static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        public static float Density(ref SteamPuff q)
        {
            float conc = Mathf.Clamp(Mathf.Pow(q.r0 / Mathf.Max(q.r, 0.1f), 0.5f), 0.45f, 1f);
            float fade = Mathf.Clamp01(q.age / 0.25f) * (1f - Smooth(0.6f * q.life, q.life, q.age));
            return q.dens0 * conc * fade;
        }
        public static float Heat(ref SteamPuff q) { return q.heat0 * Mathf.Exp(-q.age / 1.2f); }

        public static float Tint(ref SteamPuff q)
        {
            float t = q.kind == 0 ? 0.1f + 0.25f * Mathf.Exp(-q.age / 25f) : (q.kind == 2 ? 0.08f + 0.2f * Mathf.Exp(-q.age / 60f) : 0.04f);
            return Mathf.Min(0.95f, t + q.tint0 * Mathf.Exp(-q.age / 40f));
        }

        bool Spawn(int kind, Vector3 p, Vector3 v, float r0, float rMax, float heat)
        {
            if (count >= puffs.Length) { if (!MakeRoom()) return false; }
            float s = R();
            puffs[count++] = new SteamPuff
            {
                p = p, v = v, p0 = p, r = r0, r0 = r0, rMax = rMax * (0.7f + 0.6f * R()), age = 0f, life = set.lifeS * (0.7f + 0.6f * R()),
                seed = s, heat0 = heat, dens0 = set.density, kind = kind, lane = -1, ventS = float.MaxValue,
                tint0 = set.ignitionSmoke * Mathf.Exp(-sinceIgnition / Mathf.Max(0.1f, set.ignitionSmokeS)) * (kind == 2 ? 0.3f : 1f)
            };
            return true;
        }

        bool MakeRoom()
        {
            int bi = -1, bj = -1; float best = 0.55f;
            for (int i = 0; i < count; i++)
            {
                if (puffs[i].age < 3f) continue;
                for (int j = i + 1; j < count; j++)
                {
                    if (puffs[j].age < 3f) continue;
                    float s = puffs[i].r + puffs[j].r, d = (puffs[i].p - puffs[j].p).magnitude / s;
                    if (d < best) { best = d; bi = i; bj = j; }
                }
            }
            if (bi >= 0) { Merge(bi, bj); return true; }
            int old = -1; float oa = -1;
            for (int i = 0; i < count; i++) if (puffs[i].age > oa) { oa = puffs[i].age; old = i; }
            if (old < 0) return false;
            puffs[old] = puffs[--count];
            return true;
        }

        void Merge(int i, int j)
        {
            SteamPuff a = puffs[i], b = puffs[j];
            float va = a.r * a.r * a.r, vb = b.r * b.r * b.r, w = vb / (va + vb);
            SteamPuff big = va >= vb ? a : b;
            SteamPuff m = big;
            m.p = Vector3.Lerp(a.p, b.p, w); m.v = Vector3.Lerp(a.v, b.v, w);
            m.p0 = m.p - (big.p - big.p0);
            m.r = Mathf.Pow(va + vb, 1f / 3f); m.rMax = Mathf.Max(Mathf.Max(a.rMax, b.rMax), m.r); m.r0 = Mathf.Max(a.r0, b.r0);
            m.dens0 = Mathf.Lerp(a.dens0, b.dens0, w);
            puffs[i] = m;
            puffs[j] = puffs[--count];
        }

        public void Step(float dt, ref PadSteamInputs inp, Func<float, float, float> ground)
        {
            if (dt <= 0f) return;
            int n = Mathf.Clamp(Mathf.CeilToInt(dt / 0.04f), 1, 8);
            float h = dt / n;
            for (int k = 0; k < n; k++) SubStep(h, ref inp, ground);
        }

        void SubStep(float dt, ref PadSteamInputs inp, Func<float, float, float> ground)
        {
            time += dt;
            float F = Mathf.Max(0f, inp.thrustKN), sq = Mathf.Sqrt(F), scale = Mathf.Clamp(sq / 214f, 0.25f, 1.5f);

            if (F > 50f && F > lastF * 1.6f + 25f) { pulse = 1f; if (float.IsNaN(startH) || lastF < 1f) { startH = inp.nozzle.y; sinceIgnition = 0f; } }
            sinceIgnition += dt;
            if (F < 1f) startH = float.NaN;
            lastF = F;
            pulse = Mathf.Max(0f, pulse - dt / 1.4f);
            float climbed = float.IsNaN(startH) ? 0f : inp.nozzle.y - startH;
            float gUnder = ground(inp.nozzle.x, inp.nozzle.z);
            lastNozzle = inp.nozzle; lastGround = gUnder;
            float hGround = inp.nozzle.y - gUnder;
            float hAct = set.actK * sq;
            feedTrench = F > 1f ? 1f - Smooth(0.3f * set.trenchClear, set.trenchClear, climbed) : 0f;
            feedWash = F > 1f && hAct > 1f ? (1f - feedTrench) * Mathf.Pow(Mathf.Clamp01(1f - hGround / hAct), 1.5f) : 0f;

            float rMax = Mathf.Clamp(set.rMaxK * sq, 20f, 110f);

            if (feedTrench > 0.01f && inp.outletCount > 0)
            {
                float u0 = Mathf.Clamp(set.exitSpeedK * sq, 15f, set.exitSpeedMax) * (0.55f + 0.45f * feedTrench) * (1f + 0.5f * pulse);
                float r0 = Mathf.Clamp(set.jetRadiusK * sq, 3f, 16f);
                for (int o = 0; o < inp.outletCount && o < 6; o++)
                {

                    int same = 0; for (int q = 0; q < inp.outletCount; q++) if ((inp.outlets[q] - inp.outlets[o]).sqrMagnitude < 25f * 25f) same++;
                    acc[o] += set.jetRate * scale * feedTrench * (1f + 1.5f * pulse) / Mathf.Max(1, same) * dt;
                    while (acc[o] >= 1f)
                    {
                        acc[o] -= 1f;

                        Vector3 M = inp.outlets[o];
                        Vector3 d = new Vector3(M.x, 0f, M.z); if (d.sqrMagnitude < 1e-4f) d = inp.outletDirs[o]; d.y = 0f; if (d.sqrMagnitude < 1e-4f) d = Vector3.forward; d.Normalize();
                        float L = Mathf.Max(1f, new Vector3(M.x, 0f, M.z).magnitude - set.deflectorOffset);
                        Vector3 A = new Vector3(0f, M.y, 0f) + d * set.deflectorOffset;
                        float rt = Mathf.Min(Mathf.Clamp(0.045f * sq, 3f, 12f), set.trenchHalfWidth * 0.9f);
                        if (!Spawn(0, A, d * u0, rt * (0.8f + 0.3f * R()), rMax, 0.12f + 0.3f * pulse)) continue;
                        SteamPuff q = puffs[count - 1];
                        q.lane = o; q.a = A; q.dir = d; q.L = L; q.u = u0 * (0.9f + 0.2f * R()); q.s = R() * q.u * dt;
                        q.lat = Mathf.Clamp(Vector3.Dot(M - A, new Vector3(d.z, 0f, -d.x)) + (R() - 0.5f) * set.trenchHalfWidth * 0.6f, -set.trenchHalfWidth * 0.6f, set.trenchHalfWidth * 0.6f);
                        q.ventS = R() < set.ventFraction ? R() * L : float.MaxValue;
                        puffs[count - 1] = q;
                    }
                }
            }

            if (feedTrench > 0.05f)
            {
                acc[6] += set.baseRate * scale * feedTrench * (1f + 3f * pulse) * dt;
                while (acc[6] >= 1f)
                {
                    acc[6] -= 1f;

                    float a = R() * 6.2832f, rr = Mathf.Lerp(2f, 8f, R()) * Mathf.Sqrt(scale);
                    Vector3 rad = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Vector3 p = new Vector3(inp.nozzle.x, 0f, inp.nozzle.z) + rad * rr;
                    float r0 = Mathf.Clamp(0.025f * sq, 2.5f, 6f) * (0.7f + 0.6f * R());
                    p.y = ground(p.x, p.z) + 0.5f * r0;
                    Vector3 v = rad * Mathf.Lerp(10f, 28f, R()) * (1f + 1.5f * pulse) + Vector3.up * (set.baseRise * Mathf.Lerp(0.4f, 1.4f, R()));
                    Spawn(1, p, v, r0, rMax * 0.8f, 0.04f);
                }
            }

            if (feedWash > 0.01f)
            {
                acc[7] += set.washRate * scale * feedWash * dt;
                while (acc[7] >= 1f)
                {
                    acc[7] -= 1f;
                    float a = R() * 6.2832f, rr = 0.12f * hGround + 6f;
                    Vector3 rad = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Vector3 p = new Vector3(inp.nozzle.x, 0f, inp.nozzle.z) + rad * rr * (0.6f + 0.4f * R());
                    float r0 = Mathf.Clamp(0.04f * hGround + 5f, 5f, 28f) * (0.75f + 0.5f * R());
                    p.y = ground(p.x, p.z) + 0.45f * r0;
                    float u = Mathf.Clamp(0.4f * sq * (1f - hGround / hAct), 8f, 70f);
                    Spawn(2, p, rad * u * (0.8f + 0.4f * R()), r0, rMax, 0.03f);
                }

                acc[8] += set.footRate * scale * Mathf.Clamp01(feedWash * 1.5f) * dt;
                while (acc[8] >= 1f)
                {
                    acc[8] -= 1f;
                    float a = R() * 6.2832f;
                    Vector3 rad = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    float r0 = Mathf.Clamp(0.05f * hGround + 8f, 8f, 30f) * (0.75f + 0.5f * R());
                    Vector3 p = new Vector3(inp.nozzle.x, 0f, inp.nozzle.z) + rad * r0 * 0.5f * R();
                    p.y = ground(p.x, p.z) + r0 * (0.6f + 0.8f * R());
                    Spawn(3, p, rad * Mathf.Lerp(2f, 6f, R()) + Vector3.up * Mathf.Lerp(10f, 22f, R()), r0, rMax, 0.04f);
                }
            }

            for (int i = 0; i < count; i++)
            {
                SteamPuff q = puffs[i];
                q.age += dt;
                if (q.lane >= 0)
                {
                    q.u *= Mathf.Exp(-dt / set.trenchTau);
                    q.s += q.u * dt;
                    Vector3 side = new Vector3(q.dir.z, 0f, -q.dir.x);
                    Vector3 np = q.a + q.dir * q.s + side * q.lat;
                    q.v = (np - q.p) / dt; q.p = np;
                    q.r = Mathf.Min(set.trenchHalfWidth + 4f, q.r + (0.02f * q.u + 1f) * dt);
                    q.p0 += (q.p - q.p0) * (1f - Mathf.Exp(-dt / set.anchorTau));
                    if (q.s >= q.ventS)
                    {
                        q.ventS = float.MaxValue;
                        float off = set.trenchHalfWidth + 6f;
                        float lip = Mathf.Max(ground(q.p.x + side.x * off, q.p.z + side.z * off), ground(q.p.x - side.x * off, q.p.z - side.z * off));
                        Vector3 vp = new Vector3(q.p.x, Mathf.Max(q.p.y, lip) + 0.3f * q.r, q.p.z);
                        puffs[i] = q;
                        Spawn(1, vp, q.dir * q.u * 0.15f + side * ((R() - 0.5f) * 8f) + Vector3.up * (set.ventRise * Mathf.Lerp(0.6f, 1.4f, R())), q.r * 0.6f, q.rMax * 0.7f, 0.05f);
                        q = puffs[i];
                    }
                    if (q.s >= q.L)
                    {
                        q.lane = -1;
                        float fan = (R() + R() - 1f) * set.fanDeg * Mathf.Deg2Rad;
                        Vector3 fd = q.dir * Mathf.Cos(fan) + side * Mathf.Sin(fan);
                        q.v = fd * q.u + Vector3.up * (q.u * 0.04f * R());
                    }
                    puffs[i] = q;
                    continue;
                }
                float gy = ground(q.p.x, q.p.z);
                float hz = Mathf.Max(0f, q.p.y - gy);
                Vector3 w = inp.wind * set.windScale * (1f + Mathf.Min(hz, 300f) / 200f);
                Vector2 rel = new Vector2(q.v.x - w.x, q.v.z - w.z);
                float speed = rel.magnitude;
                float tau = set.dragTau + q.r * set.dragTauPerM;
                float decay = Mathf.Exp(-dt / tau);
                q.v.x = w.x + rel.x * decay; q.v.z = w.z + rel.y * decay;

                if (q.r < q.rMax) q.r = Mathf.Min(q.rMax, q.r + (set.entrain * speed + set.growBase + 1.2f * Mathf.Exp(-q.age / 3f)) * dt);

                float hug = Smooth(0.35f * set.hugSpeed, set.hugSpeed, speed);
                float b = set.buoyancy * (0.6f + 0.8f * q.seed) * Mathf.Exp(-q.age / set.buoyTau) * (1f - hug);
                q.v.y += (b * (q.kind == 3 ? 2f : 1f) - (q.kind == 3 ? 0.2f : set.riseDrag) * q.v.y) * dt;
                if (set.blast > 0f) Blast(ref q, dt, ref inp, sq, gUnder, hAct);
                q.p += q.v * dt;
                q.p0 += (q.p - q.p0) * (1f - Mathf.Exp(-dt / set.anchorTau));
                float minY = gy + q.r * Mathf.Lerp(0.45f, 0.3f, hug);
                if (q.p.y < minY) { q.p.y = minY; if (q.v.y < 0f) q.v.y = 0f; }
                float maxY = gy + Mathf.Max(set.liftCap * q.r, minY - gy);
                if (q.p.y > maxY && q.kind != 3) { q.p.y = Mathf.Lerp(q.p.y, maxY, 1f - Mathf.Exp(-dt / 0.5f)); if (q.v.y > 0f) q.v.y *= 0.5f; }
                puffs[i] = q;
            }

            interactT += dt;
            if (interactT >= 0.04f) { Interact(interactT); interactT = 0f; }

            for (int i = count - 1; i >= 0; i--) if (puffs[i].age > puffs[i].life) puffs[i] = puffs[--count];
        }

        void Blast(ref SteamPuff q, float dt, ref PadSteamInputs inp, float sq, float gUnder, float hAct)
        {
            if (sq > 1f) JetPush(ref q, dt, inp.nozzle, inp.axis.sqrMagnitude > 0.25f ? inp.axis.normalized : Vector3.down, sq, gUnder, hAct, 1f - feedTrench);
            for (int i = 0; i < inp.xJetCount; i++)
            {
                float sqi = Mathf.Sqrt(Mathf.Max(inp.xJets[i].w, 0f));
                Vector3 o = inp.xJets[i];
                if ((q.p - o).sqrMagnitude > Mathf.Pow(set.actK * sqi + q.r + 20f, 2f)) continue;
                JetPush(ref q, dt, o, inp.xJetDirs[i].sqrMagnitude > 0.25f ? inp.xJetDirs[i].normalized : Vector3.down, sqi, gUnder, set.actK * sqi, 1f);
            }

            for (int i = 0; i < inp.wakeCount; i++)
            {
                Vector3 P = inp.wakes[i], V = inp.wakeVels[i]; float Rw = inp.wakes[i].w, sp = V.magnitude;
                if (sp < 3f) continue;
                Vector3 a = P - V * 0.5f, ba = P - a;
                float h = Mathf.Clamp01(Vector3.Dot(q.p - a, ba) / Mathf.Max(ba.sqrMagnitude, 1e-4f));
                Vector3 n = q.p - (a + ba * h); float dist = n.magnitude, reach = Rw + q.r;
                if (dist >= reach) continue;
                n = dist > 1e-3f ? n / dist : Vector3.up;
                float over = 1f - dist / reach, mass = Mathf.Min(1f, 2f * Rw / Mathf.Max(q.r, 1f));
                q.v += (n * (0.5f * sp) + V * 0.25f) * (over * mass * (1f - Mathf.Exp(-dt / 0.3f)));
            }
        }

        void JetPush(ref SteamPuff q, float dt, Vector3 A, Vector3 D, float sq, float gUnder, float hAct, float wall)
        {
            float core = Mathf.Max(1f, set.blastCoreK * sq), hA = Mathf.Max(hAct, 1f), k = set.blast;

            float down = Mathf.Max(-D.y, 0.05f), Lg = Mathf.Max(0f, (A.y - gUnder) / down), Lend = Mathf.Min(Lg, hA);
            Vector3 d = q.p - A; float x = Vector3.Dot(d, D);
            if (x > -0.5f * core && x < Lend)
            {
                float xx = Mathf.Max(x, 0f), Rj = core + set.blastSpread * xx;
                Vector3 rv = d - D * x; float rad = rv.magnitude;
                Vector3 nOut = rad > 1e-3f ? rv / rad : new Vector3(1f, 0f, 0f);
                float u = set.blastSpeed * core / Rj * (1f - xx / hA);
                float edge = rad - Rj;

                float over = Mathf.Clamp01(1f - edge / Mathf.Max(0.6f * q.r, 1f));
                if (over > 0f)
                {
                    float vOut = Vector3.Dot(q.v, nOut), want = u * k * over * Mathf.Lerp(0.25f, 1f, wall);
                    if (vOut < want) q.v += nOut * (want - vOut) * (1f - Mathf.Exp(-dt / 0.12f));
                    q.v += D * (u * 0.5f * k - Vector3.Dot(q.v, D)) * (1f - Mathf.Exp(-dt / 0.3f)) * over;
                }

                float ring = Mathf.Clamp01(1f - Mathf.Max(edge, 0f) / (3f * Rj + q.r)) * (1f - over);
                if (ring > 0f) q.v += (-nOut * 0.6f + D * 0.4f) * (set.entrainK * u * 0.25f * k * ring * dt);
            }

            if (wall > 0.01f && Lg > 0f && Lg < hA)
            {
                Vector3 I = A + D * Lg;
                Vector3 h = new Vector3(q.p.x - I.x, 0f, q.p.z - I.z); float hr = h.magnitude;
                float Ri = core + set.blastSpread * Lg, uI = set.blastSpeed * core / Ri * (1f - Lg / hA);
                float reach = 6f * Ri + 40f, hz = q.p.y - gUnder, top = 2.5f * Ri + q.r;
                if (hr < reach && hz < top)
                {
                    Vector3 nh = hr > 1e-3f ? h / hr : new Vector3(1f, 0f, 0f);
                    float wj = uI * set.wallK * k * wall * Mathf.Min(1f, Ri / Mathf.Max(hr, 1f)) * (1f - hr / reach) * Mathf.Clamp01(1f - hz / top);
                    float vOut = Vector3.Dot(q.v, nh);
                    if (vOut < wj) q.v += nh * (wj - vOut) * (1f - Mathf.Exp(-dt / 0.4f));
                }
            }
        }

        void Interact(float dt)
        {
            if (count < 2) return;
            for (int i = 0; i < count; i++) order[i] = i;
            Array.Sort(order, 0, count, Comparer.Instance.Set(puffs));
            float rmaxAll = 0f; for (int i = 0; i < count; i++) rmaxAll = Mathf.Max(rmaxAll, puffs[i].r);
            bool crowded = count > puffs.Length * 3 / 4;
            int nm = 0;
            for (int a = 0; a < count; a++)
            {
                int i = order[a];
                for (int c = a + 1; c < count; c++)
                {
                    int j = order[c];
                    float dx = puffs[j].p.x - puffs[i].p.x;
                    if (dx > puffs[i].r + rmaxAll) break;
                    if (puffs[i].lane >= 0 || puffs[j].lane >= 0) continue;
                    float ri = puffs[i].r, rj = puffs[j].r, s = 0.85f * (ri + rj);
                    Vector3 d = puffs[j].p - puffs[i].p;
                    float dd = d.sqrMagnitude;
                    if (dd >= s * s) continue;
                    float dist = Mathf.Sqrt(dd);
                    float mAge = crowded ? 0.5f * set.mergeAge : set.mergeAge, mAt = crowded ? set.mergeAt * 1.3f : set.mergeAt;
                    if (nm < mergeI.Length && puffs[i].age > mAge && puffs[j].age > mAge && dist < mAt * (ri + rj)
                        && Mathf.Pow(ri * ri * ri + rj * rj * rj, 1f / 3f) < set.mergeCapK * Mathf.Max(puffs[i].rMax, puffs[j].rMax))
                    { mergeI[nm] = i; mergeJ[nm] = j; nm++; continue; }

                    float k = set.push * Mathf.Clamp01(Mathf.Min(puffs[i].age, puffs[j].age) - 0.6f);
                    if (k <= 0f) continue;
                    Vector3 nrm = dist > 1e-3f ? d / dist : new Vector3(1f, 0f, 0f);
                    nrm.y *= set.pushUp;

                    if (nrm.y < 0f && i < j) nrm.y *= 0.3f;
                    float vi = ri * ri * ri, vj = rj * rj * rj, o = (s - dist) * k;
                    puffs[i].p -= nrm * (o * vj / (vi + vj));
                    puffs[j].p += nrm * (o * vi / (vi + vj));
                }
            }

            for (int a = 0; a < count; a++) mark[a] = false;
            int done = 0;
            for (int k = 0; k < nm; k++)
            {
                int i = mergeI[k], j = mergeJ[k];
                if (mark[i] || mark[j]) { mergeI[k] = -1; continue; }
                mark[i] = mark[j] = true;
                if (i > j) { mergeI[k] = j; mergeJ[k] = i; }
            }

            for (int k = 0; k < nm; k++) if (mergeI[k] >= 0)
                for (int l = k + 1; l < nm; l++) if (mergeI[l] >= 0 && mergeJ[l] > mergeJ[k]) { int ti = mergeI[k], tj = mergeJ[k]; mergeI[k] = mergeI[l]; mergeJ[k] = mergeJ[l]; mergeI[l] = ti; mergeJ[l] = tj; }
            for (int k = 0; k < nm; k++) if (mergeI[k] >= 0) { Merge(mergeI[k], mergeJ[k]); done++; }
            merges += done;
        }
        readonly int[] mergeI = new int[24], mergeJ = new int[24];
        bool[] mark;
        public int merges;

        sealed class Comparer : System.Collections.Generic.IComparer<int>
        {
            public static readonly Comparer Instance = new Comparer();
            SteamPuff[] p;
            public Comparer Set(SteamPuff[] a) { p = a; return this; }
            public int Compare(int a, int b) { return p[a].p.x.CompareTo(p[b].p.x); }
        }

        public int Glows(Vector3[] mouths, int mouthCount, float k, float nightK, Vector4[] pos, Vector4[] col, int n)
        {
            if (lastF <= 1f) return n;

            float fire = (1f + 1.5f * pulse) * (1f - 0.68f * nightK);
            for (int i = 0; i < mouthCount && n < pos.Length && feedTrench > 0.02f; i++)
            {
                Vector3 m = mouths[i];
                pos[n] = new Vector4(m.x, m.y, m.z, Mathf.Lerp(120f, 220f, nightK));
                float I = 2.4f * k * feedTrench * fire;
                col[n++] = new Vector4(1.0f * I, 0.40f * I, 0.09f * I, 0f);
            }
            float deck = Mathf.Clamp01(feedTrench + feedWash * 1.5f);
            if (deck > 0.02f && n < pos.Length)
            {
                pos[n] = new Vector4(lastNozzle.x, lastGround + 18f, lastNozzle.z, Mathf.Lerp(90f, 200f, nightK));
                float I = 1.8f * k * deck * fire * Mathf.Lerp(0.5f, 1f, nightK);
                col[n++] = new Vector4(1.0f * I, 0.52f * I, 0.16f * I, 0f);
            }
            return n;
        }

        public int[] RenderOrder()
        {
            var keys = new long[count];
            for (int i = 0; i < count; i++)
            {
                order[i] = i;
                int x = Mathf.Clamp((int)(puffs[i].p.x / 32f) + 512, 0, 1023), y = Mathf.Clamp((int)(puffs[i].p.y / 32f) + 64, 0, 1023), z = Mathf.Clamp((int)(puffs[i].p.z / 32f) + 512, 0, 1023);
                keys[i] = Spread(x) | (Spread(y) << 1) | (Spread(z) << 2);
            }
            Array.Sort(keys, order, 0, count);
            return order;
        }
        static long Spread(int v) { long x = v & 0x3ff; x = (x | (x << 16)) & 0x30000ff; x = (x | (x << 8)) & 0x300f00f; x = (x | (x << 4)) & 0x30c30c3; x = (x | (x << 2)) & 0x9249249; return x; }
    }
}
