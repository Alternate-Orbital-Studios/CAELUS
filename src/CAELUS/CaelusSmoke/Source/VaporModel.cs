using UnityEngine;

namespace CaelusSmoke
{
    public struct VaporCollar { public float x, r, strength, length, height, shell, turnDeg; }

    public static class VaporModel
    {
        public const int NPROF = 128;
        public const int MAXK = 8;

        static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        public static float MachWindow(float mach) { return Smooth(0.92f, 0.96f, mach) * (1f - Smooth(1.05f, 1.1f, mach)); }

        public static float Humidity(float altM, float humidity) { return humidity * (1f - 0.35f * Smooth(2000f, 8000f, altM)) * (1f - Smooth(9000f, 13000f, altM)); }

        public static float LengthRadii(float mach) { return 0.8f + 1.8f * Smooth(0.93f, 1.0f, mach); }

        public static float Egg(float mach) { return Smooth(0.94f, 0.985f, mach) * (1f - Smooth(1.02f, 1.07f, mach)); }

        public static float Shell(float mach) { return Smooth(1.0f, 1.05f, mach); }

        public static float Height(float mach, float r, float s)
        {
            float shellPhase = Shell(mach) * (1f - Smooth(1.06f, 1.1f, mach));
            return r * (0.18f + 0.75f * Egg(mach) + 0.4f * shellPhase) * (0.55f + 0.45f * s);
        }

        public static int FindCollars(float[] prof, float len, float mach, VaporCollar[] outC)
        {
            int n = prof.Length; float dx = len / (n - 1);
            int w = Mathf.Max(1, Mathf.RoundToInt(1.2f / Mathf.Max(dx, 1e-3f)));
            float rMax = 0f; int last = 0;
            for (int i = 0; i < n; i++) { rMax = Mathf.Max(rMax, prof[i]); if (prof[i] > 0.3f) last = i; }
            var turn = new float[n];
            for (int i = w; i < n - w; i++)
            {

                if (prof[i] < 0.5f || i * dx < 1f || i > last - 3 * w || i * dx > 0.9f * last * dx) continue;
                float sb = (prof[i] - prof[i - w]) / (w * dx), sa = (prof[i + w] - prof[i]) / (w * dx);
                turn[i] = Mathf.Atan(sb) - Mathf.Atan(sa);

                int j = Mathf.Min(n - 1, i + Mathf.RoundToInt(3f / dx));
                if (prof[j] < 0.6f * prof[i]) turn[i] = 0f;
            }
            int cnt = 0;
            var tmp = new VaporCollar[n];
            int sup = Mathf.Max(1, Mathf.RoundToInt(2f / dx));
            for (int i = 0; i < n; i++)
            {
                if (turn[i] < 6f * Mathf.Deg2Rad) continue;
                bool max = true;
                for (int k = Mathf.Max(0, i - sup); k <= Mathf.Min(n - 1, i + sup) && max; k++) if (turn[k] > turn[i] || (turn[k] == turn[i] && k < i)) max = false;
                if (!max) continue;
                float r = Mathf.Max(prof[i], prof[Mathf.Min(n - 1, i + w)]);
                float s = Mathf.Clamp01(turn[i] / (20f * Mathf.Deg2Rad)) * Mathf.Sqrt(Mathf.Clamp01(r / Mathf.Max(rMax, 0.1f)));
                float L = Mathf.Min(r * LengthRadii(mach), (last - i) * dx);
                tmp[cnt++] = new VaporCollar { x = i * dx, r = r, strength = s, length = L, height = Height(mach, r, s), shell = Shell(mach), turnDeg = turn[i] * Mathf.Rad2Deg };
            }

            System.Array.Sort(tmp, 0, cnt, Comparer.Instance);
            int m = 0;
            for (int i = 0; i < cnt && m < Mathf.Min(MAXK, outC.Length); i++)
            {
                bool near = false;
                for (int k = 0; k < m && !near; k++) near = Mathf.Abs(tmp[i].x - outC[k].x) < 0.7f * Mathf.Max(tmp[i].r, outC[k].r);
                if (!near) outC[m++] = tmp[i];
            }
            return m;
        }

        class Comparer : System.Collections.Generic.IComparer<VaporCollar>
        {
            public static readonly Comparer Instance = new Comparer();
            public int Compare(VaporCollar a, VaporCollar b) { return b.strength.CompareTo(a.strength); }
        }
    }
}
