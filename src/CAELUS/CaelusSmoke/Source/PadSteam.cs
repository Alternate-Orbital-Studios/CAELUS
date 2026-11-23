using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace CaelusSmoke
{
    public class PadSteamSystem
    {
        public readonly PadSteamSettings set = new PadSteamSettings();
        float baseBlast = -1f;
        public SmokeDisturb disturb;
        readonly Vector4[] xJet = new Vector4[16], xWake = new Vector4[16]; readonly Vector3[] xJetDir = new Vector3[16], xWakeVel = new Vector3[16];
        public PadSteamModel model;
        LaunchPadFX fx; Transform deckT; Vector3 deckLocalImpact;
        readonly List<Transform> outletT = new List<Transform>();
        readonly Vector3[] outP = new Vector3[6], outD = new Vector3[6];
        float scan;
        Vector3 O, E, U, N;
        readonly Dictionary<long, float> groundCache = new Dictionary<long, float>();
        int raysThisFrame;
        static readonly RaycastHit[] hits = new RaycastHit[16];
        static readonly FieldInfo psField = typeof(LaunchPadFX).GetField("ps", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        readonly Func<float, float, float> groundFn;
        public float lastThrustKN;
        public int mouthCount; public readonly Vector3[] mouthLocal = new Vector3[4];
        readonly Vector4[] glowP = new Vector4[4], glowC = new Vector4[4];

        public int AddGlows(Vector4[] pos, Vector4[] col, int nf, float k, float nightK)
        {
            int n = model.Glows(mouthLocal, mouthCount, k, nightK, glowP, glowC, 0);
            for (int i = 0; i < n && nf < pos.Length; i++)
            {
                Vector3 w = ToWorld(glowP[i]); pos[nf] = new Vector4(w.x, w.y, w.z, glowP[i].w); col[nf++] = glowC[i];
            }
            return nf;
        }

        object vgfxRenderer; FieldInfo vgfxField; PropertyInfo vgfxProp; bool vgfxSearched, vgfxOff, vgfxWas;

        PadSteamSystem() { groundFn = Ground; }

        public static PadSteamSystem Create()
        {
            var s = new PadSteamSystem();
            foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("CAELUS_PADSTEAM"))
                foreach (FieldInfo f in typeof(PadSteamSettings).GetFields())
                {
                    string v = n.GetValue(f.Name); if (v == null) continue;
                    try
                    {
                        if (f.FieldType == typeof(float)) f.SetValue(s.set, float.Parse(v, System.Globalization.CultureInfo.InvariantCulture));
                        else if (f.FieldType == typeof(int)) f.SetValue(s.set, int.Parse(v));
                        else if (f.FieldType == typeof(bool)) f.SetValue(s.set, bool.Parse(v));
                    }
                    catch (Exception e) { Debug.LogWarning("[CaelusSmoke] CAELUS_PADSTEAM " + f.Name + " = " + v + ": " + e.Message); }
                }
            if (!s.set.enabled) return null;
            s.model = new PadSteamModel(s.set);
            Debug.Log("[CaelusSmoke] pad steam on: " + s.set.maxPuffs + " puffs, exit " + s.set.exitSpeedK + " sqrt(F), takeOverVgfx " + s.set.takeOverVgfx);
            return s;
        }

        public Vector3 ToWorld(Vector3 l) { return O + E * l.x + U * l.y + N * l.z; }
        public Vector3 ToWorldDir(Vector3 l) { return E * l.x + U * l.y + N * l.z; }
        Vector3 ToLocal(Vector3 w) { Vector3 d = w - O; return new Vector3(Vector3.Dot(d, E), Vector3.Dot(d, U), Vector3.Dot(d, N)); }

        void SetFrame(CelestialBody body)
        {
            O = deckT.TransformPoint(deckLocalImpact);
            U = ((Vector3)((Vector3d)O - body.position)).normalized;
            N = Vector3.ProjectOnPlane(body.transform.up, U).normalized;
            E = Vector3.Cross(U, N);
        }

        void FindPad(CelestialBody body)
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return;
            if (deckT != null && model.count > 0) return;
            LaunchPadFX best = null; float bd = 1500f;
            foreach (LaunchPadFX f in UnityEngine.Object.FindObjectsOfType<LaunchPadFX>())
            {
                Collider c = f.GetComponent<Collider>(); if (c == null) continue;
                if (!CaelusParts.Pad(f.transform)) continue;
                Vector3 up = ((Vector3)((Vector3d)c.bounds.center - body.position)).normalized;
                float d = Vector3.ProjectOnPlane(c.bounds.center - v.transform.position, up).magnitude;
                if (d < bd) { bd = d; best = f; }
            }
            if (best == fx) return;
            fx = best; deckT = fx != null ? fx.transform : null; outletT.Clear(); groundCache.Clear(); model.Clear();
            if (fx == null) return;
            Collider deck = fx.GetComponent<Collider>();
            Vector3 upD = ((Vector3)((Vector3d)deck.bounds.center - body.position)).normalized;
            RaycastHit h;
            Vector3 impact = deck.Raycast(new Ray(deck.bounds.center + upD * 200f, -upD), out h, 400f) ? h.point : deck.bounds.center;
            deckLocalImpact = deckT.InverseTransformPoint(impact);

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ConfigNode st in GameDatabase.Instance.GetConfigNodes("STATIC"))
                foreach (ConfigNode m in st.GetNodes("MODULE"))
                    if (string.Equals(m.GetValue("name"), "PadSmoke", StringComparison.OrdinalIgnoreCase) && m.GetValue("smokeEmittersNames") != null)
                        foreach (string nm in m.GetValue("smokeEmittersNames").Split(',', ';')) if (nm.Trim().Length > 0) names.Add(nm.Trim());
            Transform anc = deckT;
            for (int depth = 0; depth < 6 && anc != null && outletT.Count == 0; depth++, anc = anc.parent)
                foreach (Transform t in anc.GetComponentsInChildren<Transform>(true))
                    if (names.Contains(t.name) || t.name.StartsWith("smokeEmitter", StringComparison.OrdinalIgnoreCase)) AddOutlet(t, impact, upD);
            var systems = psField != null ? psField.GetValue(fx) as ParticleSystem[] : null;
            if (systems != null) foreach (ParticleSystem ps in systems) if (ps != null) AddOutlet(ps.transform, impact, upD);
            int hidden = HideOldSmoke();
            SetFrame(body);
            var sb = new System.Text.StringBuilder("[CaelusSmoke] pad steam: pad '" + deckT.name + "' (" + (fx.GetType().Name) + "), " + outletT.Count + " trench mouth(s)");
            foreach (Transform t in outletT) { Vector3 l = ToLocal(t.position); sb.Append(" [" + l.x.ToString("F0") + " E, " + l.z.ToString("F0") + " N, " + l.y.ToString("F0") + " up, heading " + ((Mathf.Atan2(l.x, l.z) * Mathf.Rad2Deg + 360f) % 360f).ToString("F0") + "]"); }
            sb.Append(", " + hidden + " particle renderers hidden");
            Debug.Log(sb.ToString());
        }

        void AddOutlet(Transform t, Vector3 impact, Vector3 up)
        {
            float hd = Vector3.ProjectOnPlane(t.position - impact, up).magnitude;
            if (hd < 3f || hd > 250f || outletT.Count >= outP.Length) return;
            foreach (Transform o in outletT) if ((o.position - t.position).sqrMagnitude < 1f) return;
            outletT.Add(t);
        }

        int HideOldSmoke()
        {
            int hidden = 0;
            foreach (Transform t in outletT)
            {
                if (t == null) continue;
                foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true)) if (r.enabled) { r.enabled = false; hidden++; }
            }
            var systems = fx != null && psField != null ? psField.GetValue(fx) as ParticleSystem[] : null;
            if (systems != null) foreach (ParticleSystem ps in systems) if (ps != null) foreach (ParticleSystemRenderer r in ps.GetComponentsInChildren<ParticleSystemRenderer>(true)) if (r.enabled) { r.enabled = false; hidden++; }
            return hidden;
        }

        float Ground(float x, float z)
        {
            float gx = x / 10f, gz = z / 10f; int ix = Mathf.FloorToInt(gx), iz = Mathf.FloorToInt(gz); float fx_ = gx - ix, fz = gz - iz;
            float h00 = Cell(ix, iz), h10 = Cell(ix + 1, iz), h01 = Cell(ix, iz + 1), h11 = Cell(ix + 1, iz + 1);
            return Mathf.Lerp(Mathf.Lerp(h00, h10, fx_), Mathf.Lerp(h01, h11, fx_), fz);
        }
        float Cell(int ix, int iz)
        {
            long key = ((long)(ix + 1000000) << 32) | (uint)(iz + 1000000);
            float h;
            if (groundCache.TryGetValue(key, out h)) return h;
            if (raysThisFrame >= 120) return 0f;
            raysThisFrame++;
            Vector3 top = ToWorld(new Vector3(ix * 10f, 400f, iz * 10f));
            int nh = Physics.RaycastNonAlloc(new Ray(top, -U), hits, 900f, (1 << 15) | (1 << 28), QueryTriggerInteraction.Ignore);
            float lo = float.MaxValue;
            for (int i = 0; i < nh; i++) lo = Mathf.Min(lo, Vector3.Dot(hits[i].point - O, U));
            h = 0f;
            if (nh > 0) { h = lo; for (int i = 0; i < nh; i++) { float y = Vector3.Dot(hits[i].point - O, U); if (y <= lo + 30f) h = Mathf.Max(h, y); } }
            groundCache[key] = h;
            return h;
        }

        public void Update(float dt, CelestialBody body)
        {
            raysThisFrame = 0;
            if ((scan -= Time.unscaledDeltaTime) < 0f) { scan = 3f; FindPad(body); if (deckT != null) HideOldSmoke(); }
            if (deckT == null) { if (model.count > 0) model.Clear(); SetVgfx(true); return; }
            SetFrame(body);

            float F = 0f; Vector3 c = Vector3.zero, ax = Vector3.zero;
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                if (v == null || v.packed || v.mainBody != body) continue;

                if (Vector3.ProjectOnPlane(v.transform.position - O, U).sqrMagnitude > 600f * 600f) continue;
                foreach (Part p in v.parts)
                    foreach (PartModule pm in p.Modules)
                    {
                        var en = pm as ModuleEngines;
                        if (en == null || !en.EngineIgnited || en.flameout || en.finalThrust <= 0.5f || en.thrustTransforms.Count == 0) continue;
                        float per = en.finalThrust / en.thrustTransforms.Count;
                        foreach (Transform t in en.thrustTransforms)
                        {
                            if (Vector3.Dot(t.forward, -U) < 0.5f) continue;
                            if (Vector3.ProjectOnPlane(t.position - O, U).sqrMagnitude > 300f * 300f) continue;
                            F += per; c += t.position * per; ax += t.forward * per;
                        }
                    }
            }
            lastThrustKN = F;
            var inp = new PadSteamInputs { thrustKN = F, nozzle = F > 0f ? ToLocal(c / F) : new Vector3(0f, 1e4f, 0f), outlets = outP, outletDirs = outD };
            if (F > 0f) { Vector3 aw = ax / F; inp.axis = new Vector3(Vector3.Dot(aw, E), Vector3.Dot(aw, U), Vector3.Dot(aw, N)); }
            for (int i = 0; i < outletT.Count; i++)
            {
                if (outletT[i] == null) continue;
                Vector3 l = ToLocal(outletT[i].position); outP[inp.outletCount] = l;
                outD[inp.outletCount] = new Vector3(l.x, 0f, l.z).normalized;
                inp.outletCount++;
            }
            float hW = SmokeSettings.windHeadingDeg * Mathf.Deg2Rad;
            inp.wind = new Vector3(Mathf.Sin(hW), 0f, Mathf.Cos(hW)) * SmokeSettings.windSpeed;
            if (disturb != null)
            {
                inp.xJets = xJet; inp.xJetDirs = xJetDir; inp.wakes = xWake; inp.wakeVels = xWakeVel;
                for (int i = 0; i < disturb.jets.Count && inp.xJetCount < xJet.Length; i++)
                {
                    Vector4 j = disturb.jets[i]; Vector3 l = ToLocal(j);
                    if (F > 0f && (l - inp.nozzle).sqrMagnitude < 30f * 30f) continue;
                    xJet[inp.xJetCount] = new Vector4(l.x, l.y, l.z, j.w); Vector3 dw = disturb.jetDirs[i];
                    xJetDir[inp.xJetCount++] = new Vector3(Vector3.Dot(dw, E), Vector3.Dot(dw, U), Vector3.Dot(dw, N));
                }
                for (int i = 0; i < disturb.wakes.Count && inp.wakeCount < xWake.Length; i++)
                {
                    Vector4 w = disturb.wakes[i]; Vector3 l = ToLocal(w); Vector3 vw = disturb.wakeVels[i];
                    if (l.sqrMagnitude > 2000f * 2000f) continue;
                    xWake[inp.wakeCount] = new Vector4(l.x, l.y, l.z, w.w);
                    xWakeVel[inp.wakeCount++] = new Vector3(Vector3.Dot(vw, E), Vector3.Dot(vw, U), Vector3.Dot(vw, N));
                }
            }
            if (baseBlast < 0f) baseBlast = set.blast;
            set.blast = CaelusUser.blast ? baseBlast : 0f;
            model.Step(Mathf.Min(dt, 0.25f), ref inp, groundFn);
            mouthCount = 0;
            for (int i = 0; i < inp.outletCount && mouthCount < mouthLocal.Length; i++)
            {
                bool near = false; for (int k = 0; k < mouthCount; k++) if ((mouthLocal[k] - outP[i]).sqrMagnitude < 45f * 45f) near = true;
                if (!near) mouthLocal[mouthCount++] = outP[i] + outD[i] * 25f + Vector3.up * 8f;
            }
            SetVgfx(!(model.count > 0 || F > 0f));
        }

        internal static string MarkerPath => System.IO.Path.Combine(KSPUtil.ApplicationRootPath, "GameData/CaelusSmoke/PluginData/vgfx_paused.flag");

        public void RepairLeakedPause() { }

        void FindVgfx()
        {
            if (vgfxSearched) return;
            vgfxSearched = true;
            try
            {
                foreach (AssemblyLoader.LoadedAssembly a in AssemblyLoader.loadedAssemblies)
                {
                    if (a.name != "GroundBlastFx") continue;
                    Type t = a.assembly.GetType("GroundBlastFx.Config.GeSettings");
                    FieldInfo rf = t != null ? t.GetField("Renderer", BindingFlags.Static | BindingFlags.Public) : null;
                    vgfxRenderer = rf != null ? rf.GetValue(null) : null;
                    if (vgfxRenderer == null) continue;
                    vgfxField = vgfxRenderer.GetType().GetField("EnablePadSteam", BindingFlags.Instance | BindingFlags.Public);
                    if (vgfxField == null) vgfxProp = vgfxRenderer.GetType().GetProperty("EnablePadSteam", BindingFlags.Instance | BindingFlags.Public);
                }
                Debug.Log("[CaelusSmoke] pad steam: Volumetric Ground FX " + (vgfxRenderer != null && (vgfxField != null || vgfxProp != null) ? "found - its pad steam is paused while ours draws" : "not installed"));
            }
            catch (Exception e) { Debug.LogWarning("[CaelusSmoke] VGFX lookup: " + e.Message); }
        }

        public void SetVgfx(bool on)
        {
            if (!set.takeOverVgfx) return;
            if (!on) FindVgfx();
            if (vgfxRenderer == null || (vgfxField == null && vgfxProp == null)) return;
            try
            {
                if (!on && !vgfxOff)
                {
                    vgfxWas = (bool)(vgfxField != null ? vgfxField.GetValue(vgfxRenderer) : vgfxProp.GetValue(vgfxRenderer, null));
                    if (vgfxField != null) vgfxField.SetValue(vgfxRenderer, false); else vgfxProp.SetValue(vgfxRenderer, false, null);
                    vgfxOff = true;
                    if (vgfxWas) { try { System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(MarkerPath)); System.IO.File.WriteAllText(MarkerPath, "VGFX pad steam paused by CaelusSmoke"); } catch { } }
                }
                else if (on && vgfxOff)
                {
                    if (vgfxField != null) vgfxField.SetValue(vgfxRenderer, vgfxWas); else vgfxProp.SetValue(vgfxRenderer, vgfxWas, null);
                    vgfxOff = false;

                    if (vgfxWas) VgfxSettingsRepair.FixFile("clean restore");
                    try { if (System.IO.File.Exists(MarkerPath)) System.IO.File.Delete(MarkerPath); } catch { }
                }
            }
            catch (Exception e) { Debug.LogWarning("[CaelusSmoke] VGFX switch: " + e.Message); vgfxRenderer = null; }
        }

        public void Destroy() { SetVgfx(true); }
    }

    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class VgfxSettingsRepair : MonoBehaviour
    {
        void Awake()
        {
            try
            {
                if (!System.IO.File.Exists(PadSteamSystem.MarkerPath)) return;
                FixFile("an earlier CAELUS session ended without restoring it");
                System.IO.File.Delete(PadSteamSystem.MarkerPath);
            }
            catch (Exception e) { Debug.LogWarning("[CaelusSmoke] VGFX settings repair: " + e.Message); }
        }

        internal static void FixFile(string why)
        {
            try
            {
                string dir = null;
                foreach (AssemblyLoader.LoadedAssembly a in AssemblyLoader.loadedAssemblies)
                    if (a.name == "GroundBlastFx" && !string.IsNullOrEmpty(a.path)) dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(a.path));
                if (dir == null) return;
                string f = System.IO.Path.Combine(dir, "PluginData/Settings.cfg");
                if (!System.IO.File.Exists(f)) return;
                string t = System.IO.File.ReadAllText(f);
                string u = System.Text.RegularExpressions.Regex.Replace(t, @"(enablePadSteam\s*=\s*)False", "${1}True");
                if (u != t) { System.IO.File.WriteAllText(f, u); Debug.Log("[CaelusSmoke] Volumetric Ground FX: pad steam setting restored to True (" + why + ")"); }
            }
            catch (Exception e) { Debug.LogWarning("[CaelusSmoke] VGFX settings file: " + e.Message); }
        }
    }
}
