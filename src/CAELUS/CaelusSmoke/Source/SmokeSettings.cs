using System.Collections.Generic;
using UnityEngine;

namespace CaelusSmoke
{

    public class SmokeLook
    {
        public string name = "kerolox";
        public List<string> match = new List<string>();
        public Color color = new Color(0.86f, 0.85f, 0.83f);
        public Color soot = new Color(0.30f, 0.27f, 0.24f);
        public Color highColor = Color.clear;
        public float highAltKmLo = 1.5f, highAltKmHi = 6f, highMix = 0.5f;
        public float opacity = 1.0f;
        public float lifeS = 150f;
        public float growTauS = 6f;
        public float radiusK = 0.2f;
        public float maxRadiusFactor = 7f;
        public float flameLengthK = 0.45f;
        public float startFraction = 0.55f;
        public float flameStart = 0.6f;
        public float flameMidDensity = 0.08f;
        public float buoyancy = 0.6f;
        public float jetSpeedK = 0.06f;
        public float jetTauS = 1.8f;
        public float expandK = 1f;
        public float growExponent = 0.5f;
        public float priority = 0f;
        public float puffMode = 0f;
        public FloatCurve altitudeFade = new FloatCurve();
        public FloatCurve flameLengthVsDensity = new FloatCurve();

        public SmokeLook()
        {
            altitudeFade.Add(0f, 1f); altitudeFade.Add(12f, 0.75f); altitudeFade.Add(25f, 0.25f); altitudeFade.Add(40f, 0f);
            flameLengthVsDensity.Add(0f, 6f); flameLengthVsDensity.Add(0.08f, 4f); flameLengthVsDensity.Add(0.3f, 2f); flameLengthVsDensity.Add(1f, 1f);
        }

        public static SmokeLook Load(ConfigNode n)
        {
            var l = new SmokeLook();
            l.name = n.GetValue("name") ?? l.name;
            foreach (string m in n.GetValues("match")) foreach (string p in m.Split(',')) if (p.Trim().Length > 0) l.match.Add(p.Trim());
            n.TryGetValue("color", ref l.color); n.TryGetValue("soot", ref l.soot);
            n.TryGetValue("highColor", ref l.highColor); n.TryGetValue("highAltKmLo", ref l.highAltKmLo); n.TryGetValue("highAltKmHi", ref l.highAltKmHi); n.TryGetValue("highMix", ref l.highMix);
            if (!n.HasValue("highColor")) l.highColor = l.color;
            n.TryGetValue("opacity", ref l.opacity); n.TryGetValue("lifeS", ref l.lifeS); n.TryGetValue("growTauS", ref l.growTauS);
            n.TryGetValue("radiusK", ref l.radiusK); n.TryGetValue("maxRadiusFactor", ref l.maxRadiusFactor);
            n.TryGetValue("flameLengthK", ref l.flameLengthK); n.TryGetValue("startFraction", ref l.startFraction); n.TryGetValue("flameStart", ref l.flameStart); n.TryGetValue("flameMidDensity", ref l.flameMidDensity);
            n.TryGetValue("buoyancy", ref l.buoyancy); n.TryGetValue("priority", ref l.priority); n.TryGetValue("puffMode", ref l.puffMode);
            n.TryGetValue("jetSpeedK", ref l.jetSpeedK); n.TryGetValue("jetTauS", ref l.jetTauS); n.TryGetValue("growExponent", ref l.growExponent); n.TryGetValue("expandK", ref l.expandK);
            if (n.HasNode("altitudeFade")) { var c = new FloatCurve(); c.Load(n.GetNode("altitudeFade")); if (c.Curve.length > 0) l.altitudeFade = c; else Debug.LogWarning("[CaelusSmoke] " + l.name + ": altitudeFade has no keys, default kept"); }
            if (n.HasNode("flameLengthVsDensity")) { var c = new FloatCurve(); c.Load(n.GetNode("flameLengthVsDensity")); if (c.Curve.length > 0) l.flameLengthVsDensity = c; else Debug.LogWarning("[CaelusSmoke] " + l.name + ": flameLengthVsDensity has no keys, default kept"); }
            return l;
        }
    }

    public static class SmokeSettings
    {
        public static bool enabled = true;
        public static bool timingLog = false;
        public static bool padCloud = true;
        public static int maxSegments = 2048;
        public static int marchSteps = 72;
        public static int resDivisor = 2;
        public static int lightSteps = 5;
        public static int debugView = 0;
        public static bool edgeRefine = true;

        public static float refAtmosphereDepth = 140000f;
        public static double kmScaleNow = 1.0;
        public static double KmScale(CelestialBody b)
        {
            if (b == null || !b.atmosphere || b.atmosphereDepth <= 0) return 1.0;
            return System.Math.Max(0.1, System.Math.Min(2.0, b.atmosphereDepth / refAtmosphereDepth));
        }
        public static float windSpeed = 6f;
        public static float windHeadingDeg = 250f;
        public static float windVeerDegPerKm = 4f;
        public static float noiseScale = 0.012f;
        public static float erosion = 0.55f;
        public static float phaseG = 0.62f;
        public static float phaseBackG = -0.22f;
        public static float ambient = 0.55f;
        public static float sunIntensity = 1.0f;
        public static float flameLight = 1.0f;
        public static float cauliflower = 1.0f;
        public static float columnShadow = 0.6f;

        public static float headExpandS = 0.8f, headTaperS = 1.2f;
        public static float shadeLift = 1f;
        public static float sunsetLight = 1f;
        public static float closeDetail = 1f;
        public static float padValley = 0.05f;
        public static float stepGrow = 0.03f;
        public static float warp = 0f;
        public static float maxRadiusM = 300f;
        public static readonly List<SmokeLook> looks = new List<SmokeLook>();

        public static void Load()
        {
            looks.Clear();
            foreach (ConfigNode s in GameDatabase.Instance.GetConfigNodes("CAELUSSMOKE_SETTINGS"))
            {
                s.TryGetValue("enabled", ref enabled); s.TryGetValue("timingLog", ref timingLog); s.TryGetValue("padCloud", ref padCloud); s.TryGetValue("maxSegments", ref maxSegments);
                s.TryGetValue("marchSteps", ref marchSteps); s.TryGetValue("resDivisor", ref resDivisor); resDivisor = System.Math.Max(2, resDivisor); s.TryGetValue("lightSteps", ref lightSteps); s.TryGetValue("edgeRefine", ref edgeRefine); s.TryGetValue("debugView", ref debugView);
                s.TryGetValue("windSpeed", ref windSpeed); s.TryGetValue("windHeadingDeg", ref windHeadingDeg); s.TryGetValue("windVeerDegPerKm", ref windVeerDegPerKm);
                s.TryGetValue("noiseScale", ref noiseScale); s.TryGetValue("erosion", ref erosion);
                s.TryGetValue("phaseG", ref phaseG); s.TryGetValue("phaseBackG", ref phaseBackG);
                s.TryGetValue("ambient", ref ambient); s.TryGetValue("sunIntensity", ref sunIntensity); s.TryGetValue("flameLight", ref flameLight); s.TryGetValue("cauliflower", ref cauliflower); s.TryGetValue("stepGrow", ref stepGrow); s.TryGetValue("padValley", ref padValley); s.TryGetValue("closeDetail", ref closeDetail); s.TryGetValue("sunsetLight", ref sunsetLight); s.TryGetValue("shadeLift", ref shadeLift); s.TryGetValue("warp", ref warp); s.TryGetValue("columnShadow", ref columnShadow); s.TryGetValue("headExpandS", ref headExpandS); s.TryGetValue("headTaperS", ref headTaperS); s.TryGetValue("maxRadiusM", ref maxRadiusM);
            }
            maxSegments = Mathf.Clamp(maxSegments / 64 * 64, 256, 8192);
            foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("CAELUSSMOKE_PROPELLANT")) looks.Add(SmokeLook.Load(n));
            if (looks.Count == 0) looks.Add(new SmokeLook { name = "default-kerolox", match = new List<string> { "LiquidFuel", "Oxidizer" } });
            Debug.Log("[CaelusSmoke] settings: enabled " + enabled + ", " + looks.Count + " propellant looks, " + maxSegments + " segments, " + marchSteps + "/" + lightSteps + " steps");
        }

        public static SmokeLook Named(string n) { foreach (SmokeLook l in looks) if (l.name == n) return l; return null; }

        public static SmokeLook For(ModuleEngines e)
        {
            SmokeLook best = null;
            foreach (SmokeLook l in looks)
            {
                if (l.match.Count == 0) continue;
                bool all = true;
                foreach (string m in l.match) { bool has = false; foreach (Propellant p in e.propellants) if (p.name == m) { has = true; break; } if (!has) { all = false; break; } }
                if (!all) continue;
                if (best == null || l.priority > best.priority || (l.priority == best.priority && l.match.Count > best.match.Count)) best = l;
            }
            return best;
        }
    }
}
