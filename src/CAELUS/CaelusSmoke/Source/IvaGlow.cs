using System.Collections.Generic;
using UnityEngine;

namespace CaelusSmoke
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class IvaGlow : MonoBehaviour
    {
        readonly List<InternalModel> deadModels = new List<InternalModel>();
        static readonly string[] Materials = { "CLD_IVAPNL_FACE", "RCX_CabinRealism", "CLD_IVARZ_ATLAS" };

        static readonly string[] WarmNames = { "CORIVA_MRG_018", "CLD_FDAI_", "UXI_DIALCOVER",
            "CORIVA_MRG_027", "CORIVA_MRG_034", "AQIVA_MRG_021" };
        static readonly float[] WarmScale = { 1.60f, 1.40f, 1.60f, 1.00f, 1.40f, 0.90f };
        static readonly string[] BallNames = { };
        static readonly int EmissiveColor = Shader.PropertyToID("_EmissiveColor");
        float dayGlow = 0.08f, nightGlow = 2.4f, fadeSeconds = 2.5f;
        public float nightLamp = 0.45f;
        class Cabin { public List<Light> lamps = new List<Light>(); public List<float> lampBase = new List<float>(); public List<Renderer> rends = new List<Renderer>(); public List<float> gain = new List<float>(); public List<Renderer> warm = new List<Renderer>(); public List<float> warmK = new List<float>(); public float level = -1f; }
        readonly Dictionary<InternalModel, Cabin> cabins = new Dictionary<InternalModel, Cabin>();
        MaterialPropertyBlock mpb; float nextScan;

        void Start()
        {
            mpb = new MaterialPropertyBlock();
            foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("CAELUS_IVAGLOW"))
            { n.TryGetValue("dayGlow", ref dayGlow); n.TryGetValue("nightGlow", ref nightGlow); n.TryGetValue("fadeSeconds", ref fadeSeconds); }
        }

        void Update()
        {
            if (Time.time >= nextScan) { nextScan = Time.time + 2f; Scan(); }
            float dt = Time.deltaTime;
            var dead = deadModels; dead.Clear();
            foreach (var kv in cabins)
            {
                InternalModel im = kv.Key; Cabin c = kv.Value;
                if (im == null || im.part == null || im.part.vessel == null) { dead.Add(im); continue; }
                float target = Mathf.Lerp(nightGlow, dayGlow, Sunlight(im.part.vessel, im.part.transform.position));
                c.level = c.level < 0f ? target : Mathf.MoveTowards(c.level, target, dt / Mathf.Max(0.05f, fadeSeconds));
                for (int ri = 0; ri < c.rends.Count; ri++)
                {
                    Renderer r = c.rends[ri]; if (r == null) continue;
                    float g = ri < c.gain.Count ? c.gain[ri] : 1f;

                    float lv = c.level * Mathf.Lerp(1f, g, Mathf.InverseLerp(dayGlow, nightGlow, c.level));
                    r.GetPropertyBlock(mpb); mpb.SetColor(EmissiveColor, new Color(lv, lv, lv, 1f)); r.SetPropertyBlock(mpb);
                }
                float night = Mathf.InverseLerp(dayGlow, nightGlow, c.level);
                for (int i = 0; i < c.lamps.Count; i++) if (c.lamps[i] != null) c.lamps[i].intensity = c.lampBase[i] * Mathf.Lerp(1f, nightLamp, night);
                for (int i = 0; i < c.warm.Count; i++)
                {
                    Renderer r = c.warm[i]; if (r == null) continue;
                    float w = night * c.warmK[i];
                    r.GetPropertyBlock(mpb); mpb.SetColor(EmissiveColor, new Color(w, w, w, 1f)); r.SetPropertyBlock(mpb);
                }
            }
            foreach (InternalModel im in dead) cabins.Remove(im);
        }

        internal static float Sunlight(Vessel v, Vector3 pos)
        {
            if (!v.directSunlight) return 0f;
            CelestialBody sun = FlightGlobals.Bodies.Count > 0 ? FlightGlobals.Bodies[0] : null;
            if (sun == null || v.mainBody == null || v.mainBody == sun) return 1f;
            Vector3 up = (pos - v.mainBody.position).normalized, toSun = (sun.position - (Vector3d)pos).normalized;
            if (v.mainBody.atmosphere && v.altitude < v.mainBody.atmosphereDepth) return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.03f, 0.12f, Vector3.Dot(up, toSun)));
            return 1f;
        }

        void Scan()
        {
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                if (v == null) continue;
                foreach (Part p in v.parts)
                {
                    if (!CaelusParts.Is(p)) continue;
                    InternalModel im = p != null ? p.internalModel : null;
                    if (im == null || cabins.ContainsKey(im)) continue;
                    var c = new Cabin();
                    foreach (Renderer r in im.GetComponentsInChildren<Renderer>(true))
                        for (int wi = 0; wi < WarmNames.Length; wi++)
                            if (r.name.StartsWith(WarmNames[wi])) { c.warm.Add(r); c.warmK.Add(WarmScale[wi]); break; }
                    foreach (Renderer r in im.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r == null || c.warm.Contains(r)) continue;
                        bool ball = false;
                        foreach (string bn in BallNames) if (r.name.StartsWith(bn)) { ball = true; break; }
                        if (ball)
                        {
                            Shader es = Shader.Find("KSP/Emissive/Specular");
                            if (es != null)
                            {
                                Material[] ms = r.materials;
                                foreach (Material m in ms) { if (m == null) continue; Texture mt = m.mainTexture; m.shader = es; if (mt != null) m.SetTexture("_Emissive", mt); }
                                r.materials = ms; c.warm.Add(r); c.warmK.Add(0.30f);
                            }
                            continue;
                        }
                        bool hit = false; float warmK = -1f;
                        foreach (Material m in r.sharedMaterials)
                        {
                            if (m == null) continue;
                            foreach (string n in Materials) if (m.name.StartsWith(n)) { hit = true; break; }
                            if (hit) break;

                            if (m.HasProperty("_Emissive")) { Texture et = m.GetTexture("_Emissive"); if (et != null && et.width > 16) { hit = true; break; } }
                            if (m.shader != null && m.shader.name.IndexOf("Emissive", System.StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                string ln = m.name.ToLowerInvariant();
                                if (ln.Contains("pointer")) warmK = 0.5f;
                                else if (ln.Contains("scale") || ln.Contains("analogue") || ln.Contains("face print") || ln.Contains("marking") || ln.Contains("dial")) warmK = 1.6f;
                                else hit = true;
                                break;
                            }
                        }
                        if (hit) { c.rends.Add(r); bool atlas = false; foreach (Material m in r.sharedMaterials) if (m != null) foreach (string n in Materials) if (m.name.StartsWith(n)) atlas = true; c.gain.Add(atlas ? 2f : 1f); }
                        else if (warmK > 0f) { c.warm.Add(r); c.warmK.Add(warmK); }
                    }
                    foreach (Light l in im.GetComponentsInChildren<Light>(true))
                        if (l != null && (l.name.StartsWith("CORFILL_") || l.name.StartsWith("AQIVA_Light_"))) { c.lamps.Add(l); c.lampBase.Add(l.intensity); }
                    cabins[im] = c;
                    if (c.rends.Count + c.warm.Count > 0) Debug.Log("[CaelusIvaGlow] " + p.partInfo.title + ": " + c.rends.Count + " glowing panel renderers, " + c.warm.Count + " lit instruments");
                }
            }
        }
    }
}
