using System.Collections.Generic;
using UnityEngine;

namespace CaelusSmoke
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class IvaInstruments : MonoBehaviour
    {
        class Needle { public Transform t; public Quaternion q0; public string code; public float val = -1f; public float seed; }
        class Cabin { public List<Needle> needles = new List<Needle>(); public List<Transform> balls = new List<Transform>(); public double ec = -1, prop; public List<Light> floods = new List<Light>(); }
        readonly Dictionary<InternalModel, Cabin> cabins = new Dictionary<InternalModel, Cabin>();
        float needleSign = 1f, damping = 0.35f, nextScan, floodDay = 0.25f, floodNight = 0.7f, floodRange = 1.8f; bool ballFlip;

        void Start()
        {
            foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("CAELUS_IVAINSTRUMENTS"))
            { n.TryGetValue("needleSign", ref needleSign); n.TryGetValue("damping", ref damping); n.TryGetValue("ballFlip", ref ballFlip);
              n.TryGetValue("floodDay", ref floodDay); n.TryGetValue("floodNight", ref floodNight); n.TryGetValue("floodRange", ref floodRange); }
        }

        void Scan()
        {
            var vs = FlightGlobals.VesselsLoaded;
            if (vs == null) return;
            foreach (Vessel v in vs)
                foreach (Part p in v.parts)
                {
                    InternalModel im = p.internalModel;
                    if (im == null || cabins.ContainsKey(im)) continue;
                    var c = new Cabin();
                    foreach (Transform t in im.GetComponentsInChildren<Transform>(true))
                    {
                        string nm = t.name;
                        if (nm.StartsWith("CLD_FDAI_")) c.balls.Add(t);
                        else if (nm.StartsWith("CLD_FLOOD_"))
                        {
                            Light l = t.gameObject.GetComponent<Light>() ?? t.gameObject.AddComponent<Light>();
                            l.type = LightType.Point; l.color = new Color(1f, 0.86f, 0.66f); l.range = floodRange; l.intensity = floodDay;
                            l.shadows = LightShadows.None; l.cullingMask = (1 << 16) | (1 << 20); l.renderMode = LightRenderMode.ForcePixel;
                            c.floods.Add(l);
                        }
                        else if (nm.StartsWith("CLD_NDL_"))
                        {
                            string[] s = nm.Split('_');
                            if (s.Length >= 4) c.needles.Add(new Needle { t = t, q0 = t.localRotation, code = s[2], seed = (nm.GetHashCode() & 1023) * 0.37f });
                        }
                    }
                    cabins[im] = c;
                    if (c.needles.Count > 0) RemoveStaticNeedles(im, c);
                    if (c.balls.Count + c.needles.Count + c.floods.Count > 0)
                        Debug.Log("[CaelusSmoke] IvaInstruments: " + p.partInfo.name + " balls " + c.balls.Count + " needles " + c.needles.Count + " floods " + c.floods.Count);
                }
        }

        void LateUpdate()
        {
            if (Time.time >= nextScan) { nextScan = Time.time + 2f; Scan(); }
            var dead = new List<InternalModel>();
            float k = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.02f, damping));
            foreach (var kv in cabins)
            {
                InternalModel im = kv.Key; Cabin c = kv.Value;
                if (im == null || im.part == null || im.part.vessel == null) { dead.Add(im); continue; }
                if (c.balls.Count + c.needles.Count + c.floods.Count == 0) continue;
                Vessel v = im.part.vessel;
                if (c.floods.Count > 0)
                {
                    float lv = v.directSunlight ? floodDay : floodNight;
                    foreach (Light l in c.floods) if (l != null) l.intensity = Mathf.MoveTowards(l.intensity, lv, Time.deltaTime * 0.5f);
                }
                if (c.balls.Count > 0 && v.mainBody != null)
                {
                    Vector3 up = (v.CoMD - v.mainBody.position).normalized;
                    Vector3 north = Vector3.ProjectOnPlane(v.mainBody.transform.up, up);
                    if (north.sqrMagnitude < 1e-6f) north = Vector3.ProjectOnPlane(v.mainBody.transform.forward, up);
                    Quaternion w = Quaternion.LookRotation(north.normalized, up);
                    if (ballFlip) w = w * Quaternion.AngleAxis(180f, Vector3.up);
                    Quaternion r = InternalSpace.WorldToInternal(w);
                    foreach (Transform b in c.balls) if (b != null) b.rotation = r;
                }
                double ec = 0, prop = 0;
                if (c.needles.Count > 0 && Time.frameCount % 15 == 0 || c.ec < 0) { c.ec = Fraction(v, "ElectricCharge"); prop = Fraction(v, "LiquidFuel", "Oxidizer"); c.prop = prop > 0 ? prop : Fraction(v, "MonoPropellant"); }
                ec = c.ec; prop = c.prop;
                foreach (Needle n in c.needles)
                {
                    if (n.t == null) continue;
                    float target = Mathf.Clamp01(Value(n, v, ec, prop));
                    n.val = n.val < 0f ? target : n.val + (target - n.val) * k;
                    n.t.localRotation = n.q0 * Quaternion.AngleAxis(needleSign * (-135f + 270f * n.val), Vector3.up);
                }
            }
            foreach (InternalModel im in dead) cabins.Remove(im);
        }

        static readonly string[] StaticNeedleMeshes = { "CORIVA_MRG_007", "CORIVA_MRG_010", "AQIVA_MRG_012", "AQIVA_MRG_032" };
        static void RemoveStaticNeedles(InternalModel im, Cabin c)
        {
            var dials = new List<KeyValuePair<Transform, float>>();
            foreach (Needle n in c.needles)
            {
                if (n.t == null) continue;
                MeshFilter nf = n.t.GetComponent<MeshFilter>();
                float L = 0.03f;
                if (nf != null && nf.sharedMesh != null) { Bounds b = nf.sharedMesh.bounds; L = Mathf.Max(Mathf.Abs(b.max.z), Mathf.Abs(b.min.z), Mathf.Abs(b.max.x), Mathf.Abs(b.max.y)); }
                dials.Add(new KeyValuePair<Transform, float>(n.t, L / 0.8f));
            }
            int removed = 0;
            foreach (MeshFilter mf in im.GetComponentsInChildren<MeshFilter>(true))
            {
                bool hit = false; foreach (string s in StaticNeedleMeshes) if (mf.name.StartsWith(s)) { hit = true; break; }
                if (!hit) continue;
                if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) { Debug.Log("[CaelusSmoke] IvaInstruments: " + mf.name + " mesh " + (mf.sharedMesh == null ? "null" : "NOT readable") + " - static needles kept"); continue; }
                int before = removed;
                Mesh m = Object.Instantiate(mf.sharedMesh); Vector3[] v = m.vertices; Matrix4x4 w = mf.transform.localToWorldMatrix;
                Vector3[] wv = new Vector3[v.Length]; for (int i = 0; i < v.Length; i++) wv[i] = w.MultiplyPoint3x4(v[i]);
                bool any = false;
                for (int sm = 0; sm < m.subMeshCount; sm++)
                {
                    int[] t = m.GetTriangles(sm); var keep = new List<int>(t.Length);
                    for (int i = 0; i + 2 < t.Length; i += 3)
                    {
                        bool drop = false;
                        foreach (var d in dials)
                        {
                            Vector3 o = d.Key.position, nrm = d.Key.up; float r = d.Value * 1.02f; bool all = true;
                            for (int k = 0; k < 3 && all; k++)
                            {
                                Vector3 q = wv[t[i + k]] - o; float hgt = Vector3.Dot(q, nrm);
                                all = hgt > -0.0022f && hgt < 0.020f && (q - nrm * hgt).magnitude < r;
                            }
                            if (all) { drop = true; break; }
                        }
                        if (drop) { removed++; any = true; } else { keep.Add(t[i]); keep.Add(t[i + 1]); keep.Add(t[i + 2]); }
                    }
                    m.SetTriangles(keep, sm);
                }
                if (any) mf.sharedMesh = m; else Object.Destroy(m);
            }
            Debug.Log("[CaelusSmoke] IvaInstruments: " + im.name + " static needle triangles removed " + removed + " (" + dials.Count + " live dials)");
        }

        static double Fraction(Vessel v, params string[] res)
        {
            double a = 0, m = 0;
            foreach (Part p in v.parts)
                foreach (PartResource r in p.Resources)
                    foreach (string s in res) if (r.resourceName == s) { a += r.amount; m += r.maxAmount; }
            return m > 0 ? a / m : 0;
        }

        float Value(Needle n, Vessel v, double ec, double prop)
        {
            float t = Time.time, wob = 0.006f * Mathf.Sin(t * 0.7f + n.seed);
            float thr = v.ctrlState != null ? v.ctrlState.mainThrottle : 0f;
            switch (n.code)
            {
                case "RATE": { Vector3 av = v.angularVelocity * Mathf.Rad2Deg; return 0.5f + av.y / 40f; }
                case "TANK": return 0.55f + 0.08f * thr + wob;
                case "OXY": case "CABIN": return 0.34f + wob;
                case "CTEMP": return (22f + 1.5f * Mathf.Sin(t * 0.05f + n.seed) + 20f) / 80f;
                case "CELL": return (31f + 20f) / 80f + wob;
                case "BUS": case "MBUS": return ec > 0.02 ? (float)(26.5 + 2.0 * ec) / 32f + wob : 0.05f;
                case "MET": return (float)((v.missionTime / 60.0) % 60.0 / 60.0);
                case "VTRIM": return Mathf.Clamp01((float)v.verticalSpeed / 100f + 0.5f);
                case "PROP": return (float)prop;
                case "O2S": return 0.86f - 0.10f * Mathf.Clamp01((float)(v.missionTime / 864000.0)) + wob;
                case "CO2": return 0.22f + 0.05f * Mathf.Sin(t * 0.03f + n.seed);
                case "FLOW": return 0.58f + wob;

                case "RADAR": return Mathf.Clamp01((float)(v.radarAltitude > 0 ? v.radarAltitude : v.altitude) / 1000f);
                case "VVEL": return 0.5f + (float)v.verticalSpeed / 40f;
                case "HVEL": return (float)v.horizontalSrfSpeed / 20f;
                case "ENG": return thr;
                case "MPROP": return (float)prop;
                case "RCS": return (float)Fraction(v, "MonoPropellant");
                case "TRNG": case "CLOSE": case "TELEV": case "TLAT":
                {
                    ITargetable tg = v.targetObject; if (tg == null || tg.GetTransform() == null) return n.code == "TRNG" ? 0f : 0.5f;
                    Vector3 d = tg.GetTransform().position - v.ReferenceTransform.position; float dist = d.magnitude;
                    if (n.code == "TRNG") return dist / 20000f;
                    if (n.code == "CLOSE") { Vector3 rv = (Vector3)(v.obt_velocity - tg.GetObtVelocity()); return 0.5f + Vector3.Dot(rv, d / Mathf.Max(dist, 1f)) / 40f; }
                    Vector3 l = v.ReferenceTransform.InverseTransformDirection(d / Mathf.Max(dist, 1f));
                    float a = n.code == "TELEV" ? Mathf.Atan2(l.z, l.y) : Mathf.Atan2(l.x, l.y);
                    return 0.5f + a * Mathf.Rad2Deg / 40f;
                }
                case "LOAD": return (11f + 14f * thr + (v.ActionGroups[KSPActionGroup.RCS] ? 3f : 0f)) / 30f + wob;
            }
            return 0.5f;
        }
    }
}
