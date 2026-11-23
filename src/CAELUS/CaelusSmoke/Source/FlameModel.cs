using UnityEngine;

namespace CaelusSmoke
{

    public class FlameLook
    {
        public string name = "kerolox-s1";
        public float tCore = 2450f;
        public float tMantle = 2000f;
        public float tTail = 1700f;
        public float tSheath = 1100f;
        public float sheath = 1.0f;
        public float sheathLength = 1.5f;
        public float sigma = 0.4f;
        public float exposure = 2.6f;
        public float thrustFloor = 0f;
        public float peAtm = 0.7f;
        public float lengthK = 0.48f;
        public float expandK = 0.55f;
        public float ramK = 0f;
        public float maxSwell = 9f;
        public float spread = 0.06f;

        public float vacTheta = 0f;
        public float vacGamma = 1.25f;
        public float mergeDiameters = 3f;
        public float speed = 60f;
        public float recirc = 0.3f;
        public float diamonds = 0f;
        public float soot = 1f;
        public Color color = new Color(0.45f, 0.55f, 1f);
        public float scatter = 1f;
        public bool cluster = true;
        public float densExp = 1.3f;
        public float rays = 0f;
        public float startTime = 2.5f;
        public float stagger = 1.2f;
        public float tailTime = 1.8f;
        public float lightIntensity = 3f;
        public Vector3 lightColor = new Vector3(-1f, -1f, -1f);
        public float lightRange = 120f;

        public float lightMinRange = 0f;
        public float flash = 0f;
        public float flashTime = 0.35f;
        public float sparks = 0f;
        public float sparkSpeed = 90f;
        public float sparkLife = 0.6f;
        public float sparkSize = 0.06f;
        public float sparkCone = 14f;
        public float growBurn = 1f;
        public float growTime = 3f;
        public float sparkStretch = 0.06f;
        public float flowCap = 0f;
        public float lightMinOffset = 0f;
        public float smokeWidth = 1.2f;
        public float smokeJoin = 1.0f;
        public float smokeLengthK = 1.13f;
        public float[] lengthVsAir = { 0f, 4f, 0.02f, 3.4f, 0.08f, 2.4f, 0.3f, 1.5f, 1f, 1f };
        public float[] smokeLengthVsAir = { 0f, 1.6f, 0.02f, 1.6f, 0.3f, 1.0f, 1f, 1f };

        public static float Eval(float[] k, float x)
        {
            if (k == null || k.Length < 2) return 1f;
            if (x <= k[0]) return k[1];
            for (int i = 2; i + 1 < k.Length; i += 2)
                if (x <= k[i]) return Mathf.Lerp(k[i - 1], k[i + 1], Mathf.InverseLerp(k[i - 2], k[i], x));
            return k[k.Length - 1];
        }
    }

    public struct FlameShape
    {
        public float E;
        public float Lp;
        public float mergeX;
        public float recirc;
        public float tailR;
        public float smokeLen, smokeR;
        public float joinLen, joinR;
        public float vac;
    }

    public static class FlameModel
    {
        public static FlameShape Shape(FlameLook l, float thrustKN, float paAtm, float airRel, float exitR, float clusterR, float qAtm = 0f)
        {
            var s = new FlameShape();
            float n = l.peAtm / Mathf.Max(paAtm, 1e-6f);
            float sq = Mathf.Sqrt(Mathf.Max(thrustKN, 1f));
            s.Lp = l.lengthK * sq * FlameLook.Eval(l.lengthVsAir, airRel);
            if (l.ramK > 0f)
            {

                float pEff = Mathf.Max((paAtm + l.ramK * qAtm) * 101325f, 1f);
                float Rbal = Mathf.Sqrt(thrustKN * 1000f / (Mathf.PI * pEff));
                s.E = Mathf.Clamp((Rbal - l.spread * s.Lp) / clusterR - 1f, 0f, l.maxSwell);
            }
            else s.E = Mathf.Clamp((Mathf.Sqrt(n) - 1f) * l.expandK, 0f, l.maxSwell);
            s.mergeX = l.mergeDiameters * 2f * exitR / (1f + 0.3f * s.E);
            s.recirc = l.recirc * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(10f, 150f, n));
            s.tailR = PlumeRadius(s.E, l.spread, clusterR, s.Lp);
            s.smokeLen = l.smokeLengthK * sq * FlameLook.Eval(l.smokeLengthVsAir, airRel);
            s.smokeR = 2f * s.tailR;
            s.joinR = l.smokeWidth * s.tailR;
            s.joinLen = s.Lp * l.smokeJoin;
            s.vac = l.vacTheta > 0f ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(5e-5f, 1e-3f, paAtm)) : 0f;
            return s;
        }

        public static float PlumeRadius(float E, float spread, float R0, float x)
        {
            x = Mathf.Max(x, 0f);
            return R0 * (1f + E * (1f - Mathf.Exp(-x / (R0 * (1.5f + 1.0f * E))))) + spread * x;
        }

        public static Vector3 Planck(float T)
        {
            T = Mathf.Max(T, 300f);
            float r = 11.86f / (Mathf.Exp(14388f / (0.610f * T)) - 1f), g = 19.85f / (Mathf.Exp(14388f / (0.550f * T)) - 1f), b = 46.0f / (Mathf.Exp(14388f / (0.465f * T)) - 1f);
            return new Vector3(r, g, b) / 1.36e-4f;
        }
    }
}
