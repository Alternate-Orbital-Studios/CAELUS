using System.Collections.Generic;
using UnityEngine;

namespace CaelusSmoke
{
    public class RcsPlumeBinding
    {
        public string part = "";
        public float length = 1.8f, radius = 0.05f, angle = 20f, brightness = 0.8f, falloff = 1.2f, edgeSoft = 1.5f, tipFade = 1f, noise = 0.15f, flow = 12f;
        public float responseUp = 25f, responseDown = 12f;
        public Color startTint = new Color(1f, 0.92f, 0.78f), endTint = new Color(1f, 0.62f, 0.36f);

        public bool core = true, halo = true;
        public float coreLengthK = 0.3f, coreAngle = 5f, coreBrightness = 2.5f, coreFalloff = 2.2f;
        public float haloLengthK = 2f, haloAngle = 38f, haloBrightness = 0.18f, haloFalloff = 0.7f, haloEdgeSoft = 2.5f;
        public float flash = 0.8f;
        public float softDepth = 0.4f;
        public Color coreTint = new Color(1f, 0.97f, 0.92f);
    }

    public struct RcsLayer
    {
        public bool on; public string name;
        public float lenK, briK, r0, angle, falloff, edgeSoft, tipFade, noise, flow;
        public Color start, end;
    }

    public static class RcsPlumeModel
    {
        public const int Layers = 3;

        public static RcsLayer Layer(RcsPlumeBinding b, int k)
        {
            float len = Mathf.Max(b.length, 0.01f);
            if (k == 1)
                return new RcsLayer { on = b.core, name = "CaelusRcsCore", lenK = b.coreLengthK, briK = b.coreBrightness, r0 = b.radius * 0.8f / (len * b.coreLengthK), angle = b.coreAngle,
                    falloff = b.coreFalloff, edgeSoft = 1.2f, tipFade = 1f, noise = 0.06f, flow = b.flow * 1.5f, start = b.coreTint, end = b.startTint };
            if (k == 2)
                return new RcsLayer { on = b.halo, name = "CaelusRcsHalo", lenK = b.haloLengthK, briK = b.haloBrightness, r0 = b.radius * 1.5f / (len * b.haloLengthK), angle = b.haloAngle,
                    falloff = b.haloFalloff, edgeSoft = b.haloEdgeSoft, tipFade = 1f, noise = b.noise * 2f, flow = b.flow * 0.6f, start = b.startTint, end = b.endTint };
            return new RcsLayer { on = true, name = "CaelusRcsPlume", lenK = 1f, briK = 1f, r0 = b.radius / len, angle = b.angle,
                falloff = b.falloff, edgeSoft = b.edgeSoft, tipFade = b.tipFade, noise = b.noise, flow = b.flow, start = b.startTint, end = b.endTint };
        }

        public static float Grow(int k, float level)
        {
            return k == 1 ? Mathf.Lerp(0.7f, 1f, level) : (k == 2 ? Mathf.Lerp(0.3f, 1f, level * level) : Mathf.Lerp(0.45f, 1f, level));
        }

        public static float Brightness(RcsPlumeBinding b, RcsLayer l, int k, float level, float flash)
        {
            return b.brightness * l.briK * level * (1f + b.flash * flash * (k == 2 ? 0.3f : 1f));
        }

        public static void SetMaterial(Material m, RcsLayer l, Texture noise, float softDepth)
        {
            m.SetFloat("_SoftDepth", softDepth);
            m.SetTexture("_Noise", noise);
            m.SetColor("_StartTint", l.start); m.SetColor("_EndTint", l.end);
            m.SetFloat("_TintFalloff", 0.6f); m.SetFloat("_Falloff", l.falloff); m.SetFloat("_NoiseAmp", l.noise); m.SetFloat("_Flow", l.flow);
            m.SetFloat("_EdgeSoft", l.edgeSoft); m.SetFloat("_TipFade", l.tipFade); m.SetFloat("_Brightness", 0f);
        }

        public static Mesh Cone(float r0, float angleDeg)
        {
            const int N = 20, M = 10;
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            float k = Mathf.Tan(angleDeg * Mathf.Deg2Rad);
            for (int j = 0; j <= M; j++)
            {
                float v = j / (float)M, r = r0 + k * v;
                for (int i = 0; i <= N; i++)
                {
                    float a = i / (float)N * Mathf.PI * 2f;
                    verts.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, v));
                    norms.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), -k).normalized);
                    uvs.Add(new Vector2(i / (float)N, v));
                }
            }
            for (int j = 0; j < M; j++)
                for (int i = 0; i < N; i++)
                {
                    int a = j * (N + 1) + i, b = a + N + 1;
                    tris.Add(a); tris.Add(b); tris.Add(a + 1); tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
                }
            var m = new Mesh { name = "CaelusRcsCone" };
            m.SetVertices(verts); m.SetNormals(norms); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.bounds = new Bounds(new Vector3(0, 0, 0.5f), new Vector3(2f * (r0 + k) + 0.1f, 2f * (r0 + k) + 0.1f, 1.2f));
            return m;
        }
    }
}
