using System.Collections.Generic;
using UnityEngine;

namespace CaelusSmoke
{

    public class FlameBinding
    {
        public string part = "";
        public string look = "kerolox-s1";
        public float exitRadius = 1.5f;
        public float exitOffset = 0f;
        public float vehicleRadius = 0f;
        public bool hideWaterfall = true;
        public readonly List<string> engineIDs = new List<string>();
        public FlameLook lookRef;

        public bool Matches(ModuleEngines en)
        {
            if (engineIDs.Count == 0) return true;
            var fx = en as ModuleEnginesFX;
            return fx != null && engineIDs.Contains(fx.engineID);
        }
    }

    public static class FlameSettings
    {
        public static bool enabled = true;
        public static int resDivisor = 2;
        public static int steps = 96;
        public static bool debugLog = false;
        public static bool smokeUnderFlame = true;
        public static float haze = 1f;
        public static float tailFlat = 1f;
        public static float thinAir = 1f;

        public static float flowCap = 3.0f;
        public static float flowLevel = 1f;
        public static float thinAirFlow = 0.3f;

        public static bool glow = true;
        public static float glowIntensity = 0.8f, glowThreshold = 0.6f, glowKnee = 0.5f, glowClamp = 40f, glowScatter = 0.7f, glowWithTUFX = 0.35f, glowOnFlame = 0.25f;
        public static int glowLevels = 6;
        public static Color glowTint = new Color(1f, 0.92f, 0.8f, 1f);
        public static bool tufxPresent;
        public static readonly List<FlameLook> looks = new List<FlameLook>();
        public static readonly List<FlameBinding> bindings = new List<FlameBinding>();
        static readonly Dictionary<string, List<FlameBinding>> byPart = new Dictionary<string, List<FlameBinding>>();

        static readonly Dictionary<AvailablePart, List<FlameBinding>> forInfo = new Dictionary<AvailablePart, List<FlameBinding>>();
        static string Norm(string n) { return (n ?? "").Replace('_', '.'); }

        public static void Load()
        {
            looks.Clear(); bindings.Clear(); byPart.Clear(); forInfo.Clear();
            foreach (ConfigNode s in GameDatabase.Instance.GetConfigNodes("CAELUSFLAME_SETTINGS"))
            {
                s.TryGetValue("enabled", ref enabled); s.TryGetValue("resDivisor", ref resDivisor); s.TryGetValue("steps", ref steps); s.TryGetValue("debugLog", ref debugLog); s.TryGetValue("haze", ref haze); s.TryGetValue("smokeUnderFlame", ref smokeUnderFlame); s.TryGetValue("thinAir", ref thinAir); s.TryGetValue("thinAirFlow", ref thinAirFlow); s.TryGetValue("flowCap", ref flowCap); s.TryGetValue("flowLevel", ref flowLevel); s.TryGetValue("tailFlat", ref tailFlat);
            }
            foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("CAELUSFLAME_LOOK"))
            {
                var l = new FlameLook();
                l.name = n.GetValue("name") ?? l.name;
                n.TryGetValue("tCore", ref l.tCore); n.TryGetValue("tMantle", ref l.tMantle); n.TryGetValue("tTail", ref l.tTail); n.TryGetValue("tSheath", ref l.tSheath);
                n.TryGetValue("sheath", ref l.sheath); n.TryGetValue("sheathLength", ref l.sheathLength); n.TryGetValue("sigma", ref l.sigma); n.TryGetValue("exposure", ref l.exposure); n.TryGetValue("thrustFloor", ref l.thrustFloor);
                n.TryGetValue("peAtm", ref l.peAtm); n.TryGetValue("lengthK", ref l.lengthK); n.TryGetValue("expandK", ref l.expandK); n.TryGetValue("ramK", ref l.ramK); n.TryGetValue("maxSwell", ref l.maxSwell); n.TryGetValue("spread", ref l.spread);
                n.TryGetValue("mergeDiameters", ref l.mergeDiameters); n.TryGetValue("speed", ref l.speed); n.TryGetValue("recirc", ref l.recirc); n.TryGetValue("diamonds", ref l.diamonds);
                n.TryGetValue("soot", ref l.soot); n.TryGetValue("color", ref l.color); n.TryGetValue("scatter", ref l.scatter); n.TryGetValue("cluster", ref l.cluster); n.TryGetValue("densExp", ref l.densExp); n.TryGetValue("rays", ref l.rays); n.TryGetValue("vacTheta", ref l.vacTheta); n.TryGetValue("vacGamma", ref l.vacGamma);
                n.TryGetValue("startTime", ref l.startTime); n.TryGetValue("stagger", ref l.stagger); n.TryGetValue("tailTime", ref l.tailTime);
                n.TryGetValue("lightIntensity", ref l.lightIntensity); n.TryGetValue("lightColor", ref l.lightColor); n.TryGetValue("lightMinRange", ref l.lightMinRange); n.TryGetValue("flash", ref l.flash); n.TryGetValue("flashTime", ref l.flashTime); n.TryGetValue("sparks", ref l.sparks); n.TryGetValue("sparkSpeed", ref l.sparkSpeed); n.TryGetValue("sparkLife", ref l.sparkLife); n.TryGetValue("sparkSize", ref l.sparkSize); n.TryGetValue("sparkCone", ref l.sparkCone); n.TryGetValue("growBurn", ref l.growBurn); n.TryGetValue("growTime", ref l.growTime); n.TryGetValue("sparkStretch", ref l.sparkStretch); n.TryGetValue("lightMinOffset", ref l.lightMinOffset); n.TryGetValue("flowCap", ref l.flowCap); n.TryGetValue("lightRange", ref l.lightRange); n.TryGetValue("smokeLengthK", ref l.smokeLengthK); n.TryGetValue("smokeJoin", ref l.smokeJoin); n.TryGetValue("smokeWidth", ref l.smokeWidth);
                float[] k;
                if ((k = Keys(n.GetNode("lengthVsAir"))) != null) l.lengthVsAir = k;
                if ((k = Keys(n.GetNode("smokeLengthVsAir"))) != null) l.smokeLengthVsAir = k;
                looks.Add(l);
            }
            foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("CAELUSFLAME_ENGINE"))
            {
                var b = new FlameBinding();
                b.part = n.GetValue("part") ?? ""; b.look = n.GetValue("look") ?? b.look;
                n.TryGetValue("exitRadius", ref b.exitRadius); n.TryGetValue("exitOffset", ref b.exitOffset); n.TryGetValue("vehicleRadius", ref b.vehicleRadius); n.TryGetValue("hideWaterfall", ref b.hideWaterfall);
                foreach (string id in n.GetValues("engineID")) b.engineIDs.Add(id.Trim());
                foreach (FlameLook l in looks) if (l.name == b.look) b.lookRef = l;
                if (b.lookRef == null) { Debug.LogWarning("[CaelusFlame] " + b.part + ": look '" + b.look + "' not found - default kerolox look used"); b.lookRef = new FlameLook(); }
                if (b.part.Length > 0) { bindings.Add(b); List<FlameBinding> lb; if (!byPart.TryGetValue(Norm(b.part), out lb)) byPart[Norm(b.part)] = lb = new List<FlameBinding>(); lb.Add(b); }
            }
            foreach (ConfigNode s in GameDatabase.Instance.GetConfigNodes("CAELUSFLAME_GLOW"))
            {
                s.TryGetValue("enabled", ref glow); s.TryGetValue("intensity", ref glowIntensity); s.TryGetValue("threshold", ref glowThreshold); s.TryGetValue("knee", ref glowKnee);
                s.TryGetValue("clamp", ref glowClamp); s.TryGetValue("scatter", ref glowScatter); s.TryGetValue("levels", ref glowLevels); s.TryGetValue("tint", ref glowTint); s.TryGetValue("withTUFX", ref glowWithTUFX); s.TryGetValue("onFlame", ref glowOnFlame);
            }
            glowLevels = Mathf.Clamp(glowLevels, 1, 8); glowScatter = Mathf.Clamp01(glowScatter); glowOnFlame = Mathf.Clamp01(glowOnFlame); glowKnee = Mathf.Max(0.001f, glowKnee);
            tufxPresent = false;
            foreach (AssemblyLoader.LoadedAssembly la in AssemblyLoader.loadedAssemblies) if (la != null && la.name == "TUFX") tufxPresent = true;
            Debug.Log("[CaelusFlame] glow: " + (glow ? "on, intensity " + glowIntensity + (tufxPresent ? " x " + glowWithTUFX + " (TUFX installed)" : "") + ", threshold " + glowThreshold + ", " + glowLevels + " levels" : "off"));
            Debug.Log("[CaelusFlame] settings: enabled " + enabled + ", " + looks.Count + " looks, " + bindings.Count + " engine bindings, 1/" + resDivisor + " res, " + steps + " steps");
        }

        static float[] Keys(ConfigNode n)
        {
            if (n == null) return null;
            var pts = new List<Vector2>();
            foreach (string v in n.GetValues("key"))
            {
                string[] p = v.Split(new[] { ' ', '\t', ',' }, System.StringSplitOptions.RemoveEmptyEntries);
                float x, y;
                if (p.Length >= 2 && float.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x)
                    && float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y)) pts.Add(new Vector2(x, y));
            }
            if (pts.Count == 0) return null;
            pts.Sort((a, b) => a.x.CompareTo(b.x));
            var f = new float[pts.Count * 2];
            for (int i = 0; i < pts.Count; i++) { f[2 * i] = pts[i].x; f[2 * i + 1] = pts[i].y; }
            return f;
        }

        public static List<FlameBinding> For(Part p)
        {
            if (p == null || p.partInfo == null || bindings.Count == 0) return null;
            List<FlameBinding> b;

            if (forInfo.TryGetValue(p.partInfo, out b)) return b;
            b = byPart.TryGetValue(Norm(p.partInfo.name), out List<FlameBinding> f) ? f : null;
            forInfo[p.partInfo] = b; return b;
        }
    }
}
