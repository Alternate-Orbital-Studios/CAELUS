using UnityEngine;

namespace CaelusSmoke
{
    public static partial class LunarDustSettings
    {
        public static bool enabled = true;
        public static float strength = 1f;
        public static float sigma = 0.2f;
        public static float sheet = 2f, rays = 0.6f, haze = 0.35f;
        public static float speed = 45f;
        public static float angleDeg = 2.5f;
        public static float startHeight = 35f;
        public static float refThrust = 15f;
        public static float maxRadius = 260f;
        public static float glow = 1f;
        public static Color albedo = new Color(0.62f, 0.60f, 0.57f);
        public static Color glowColor = new Color(1.0f, 0.42f, 0.12f);
        public static int resDivisor = 2, steps = 96;
        public static float riseTime = 0.6f, fallTime = 0.25f;
        public static bool debugLog = false;

    }

    public static class LunarDustModel
    {

        public static float StartHeight(float thrustKN) => LunarDustSettings.startHeight * Mathf.Sqrt(Mathf.Max(thrustKN, 0.1f) / LunarDustSettings.refThrust);

        public static float Strength(float thrustKN, float height)
        {
            float h1 = StartHeight(thrustKN);
            if (height > h1) return 0f;
            float k = 1f - height / h1;
            float th = Mathf.Sqrt(Mathf.Clamp(thrustKN / LunarDustSettings.refThrust, 0.05f, 4f));

            return Mathf.Clamp(k * k * k * (k * (6f * k - 15f) + 10f) * th, 0f, 1.3f) * LunarDustSettings.strength;
        }

        public static float Radius(float strength, float thrustKN)
        {
            float th = Mathf.Sqrt(Mathf.Clamp(thrustKN / LunarDustSettings.refThrust, 0.05f, 4f));
            return Mathf.Lerp(25f, LunarDustSettings.maxRadius, Mathf.Clamp01(strength)) * th;
        }

        public const int OccAz = 64, OccR = 32;

        public static void ShadowRow(int a, float range, float jetR, System.Func<float, float, float> heightAt, float[] tab)
        {
            float th = (a + 0.5f) / OccAz * 2f * Mathf.PI, c = Mathf.Cos(th), s = Mathf.Sin(th);
            float slope = 0f, rPrev = 0f, rMin = Mathf.Max(4f, 2.5f * jetR);
            for (int i = 0; i < OccR; i++)
            {
                float ri = (i + 0.5f) / OccR * range;
                for (int k = 1; k <= 2; k++)
                {
                    float r = Mathf.Lerp(rPrev, ri, k * 0.5f);
                    if (r < rMin) continue;
                    float h = heightAt(c * r, s * r) - 0.3f - 0.01f * r;
                    slope = Mathf.Max(slope, h / r);
                }
                tab[i * OccAz + a] = slope;
                rPrev = ri;
            }
        }

        public static float JetRadius(float exitRadius, float height) => exitRadius + 0.55f * height;
    }
}
