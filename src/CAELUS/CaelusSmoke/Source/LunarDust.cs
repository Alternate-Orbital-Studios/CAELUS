using System;
using System.Collections.Generic;
using UnityEngine;

namespace CaelusSmoke
{
    public static partial class LunarDustSettings
    {
        public static void Load()
        {
            foreach (ConfigNode s in GameDatabase.Instance.GetConfigNodes("CAELUS_LUNARDUST"))
            {
                s.TryGetValue("enabled", ref enabled); s.TryGetValue("strength", ref strength); s.TryGetValue("sigma", ref sigma);
                s.TryGetValue("sheet", ref sheet); s.TryGetValue("rays", ref rays); s.TryGetValue("haze", ref haze);
                s.TryGetValue("speed", ref speed); s.TryGetValue("angleDeg", ref angleDeg); s.TryGetValue("startHeight", ref startHeight);
                s.TryGetValue("refThrust", ref refThrust); s.TryGetValue("maxRadius", ref maxRadius); s.TryGetValue("glow", ref glow);
                s.TryGetValue("albedo", ref albedo); s.TryGetValue("glowColor", ref glowColor);
                s.TryGetValue("resDivisor", ref resDivisor); s.TryGetValue("steps", ref steps);
                s.TryGetValue("riseTime", ref riseTime); s.TryGetValue("fallTime", ref fallTime);
                s.TryGetValue("debugLog", ref debugLog);
            }
        }
    }

    public class LunarMark
    {
        public string body; public double lat, lon, alt; public float exposure, halo, scour, seed;
    }

    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.FLIGHT, GameScenes.TRACKSTATION, GameScenes.SPACECENTER)]
    public class CaelusLunarMarks : ScenarioModule
    {
        public static readonly List<LunarMark> marks = new List<LunarMark>();
        const int MAX = 200;
        public override void OnLoad(ConfigNode node)
        {
            marks.Clear();
            foreach (ConfigNode n in node.GetNodes("MARK"))
            {
                var m = new LunarMark { body = n.GetValue("body") };
                n.TryGetValue("lat", ref m.lat); n.TryGetValue("lon", ref m.lon); n.TryGetValue("alt", ref m.alt);
                n.TryGetValue("exposure", ref m.exposure); n.TryGetValue("halo", ref m.halo); n.TryGetValue("scour", ref m.scour); n.TryGetValue("seed", ref m.seed);
                if (m.body != null) marks.Add(m);
            }
        }
        public override void OnSave(ConfigNode node)
        {
            while (marks.Count > MAX) marks.RemoveAt(0);
            foreach (LunarMark m in marks)
            {
                ConfigNode n = node.AddNode("MARK");
                n.AddValue("body", m.body); n.AddValue("lat", m.lat.ToString("R", System.Globalization.CultureInfo.InvariantCulture)); n.AddValue("lon", m.lon.ToString("R", System.Globalization.CultureInfo.InvariantCulture)); n.AddValue("alt", m.alt.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                n.AddValue("exposure", m.exposure.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)); n.AddValue("halo", m.halo.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)); n.AddValue("scour", m.scour.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)); n.AddValue("seed", m.seed.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }

    public class LunarDustSystem
    {
        public readonly Material mat;
        public bool drawDust, drawMarks;
        const int MAXD = 4, MAXM = 16;

        class Source { public Vector3d local, upLocal; public float s, target, thrust, height, used; public float seed; public float age, heat, heatTarget, fire; public Color col; public float colW, colT; public float[] occ; public int occA, occDone; }
        class Hit { public Vector3 p, n; public float thrust, s, height, w, heat; public RaycastHit rh; }

        readonly float[] occAll = new float[LunarDustModel.OccAz * LunarDustModel.OccR * MAXD];
        readonly Vector4[] dOcc = new Vector4[MAXD];
        Texture2D occTex;
        const int OccRowsPerFrame = 6;
        readonly List<Source> sources = new List<Source>();
        readonly List<Hit> hits = new List<Hit>();
        readonly Vector4[] dPos = new Vector4[MAXD], dUp = new Vector4[MAXD], dPar = new Vector4[MAXD], dCol = new Vector4[MAXD], dExtra = new Vector4[MAXD];
        readonly Vector4[] mPos = new Vector4[MAXM], mUp = new Vector4[MAXM], mPar = new Vector4[MAXM];
        readonly List<KeyValuePair<float, LunarMark>> near = new List<KeyValuePair<float, LunarMark>>();
        float logT; const int TerrainMask = 1 << 15;

        public LunarDustSystem(Shader s, Texture3D noise)
        {
            mat = new Material(s) { name = "CaelusLunarDust" };
            mat.SetTexture("_Noise", noise);
        }
        public void Destroy() { if (mat != null) UnityEngine.Object.Destroy(mat); if (occTex != null) UnityEngine.Object.Destroy(occTex); }

        public bool Prepare(double ut, CelestialBody body, float dt)
        {
            drawDust = drawMarks = false;
            if (!LunarDustSettings.enabled || body == null) return false;
            bool airless = !body.atmosphere;
            hits.Clear();
            if (airless) Collect(body);

            foreach (Source s in sources) { s.target = 0f; s.used = 0f; s.heatTarget = 0f; }
            foreach (Hit h in hits)
            {
                Source best = null; float bd = 15f;
                foreach (Source s in sources) { float d = Vector3.Distance(h.p, (Vector3)body.transform.TransformPoint(s.local)); if (d < bd && s.used == 0f) { bd = d; best = s; } }
                bool fresh = best == null;
                if (fresh) { if (sources.Count >= MAXD * 2) continue; best = new Source { s = 0f, seed = UnityEngine.Random.value * 7f }; sources.Add(best); }

                Vector3d hl = body.transform.InverseTransformPoint(h.p); Vector3d hu = body.transform.InverseTransformDirection(h.n);
                if (fresh) { best.local = hl; best.upLocal = hu; }
                else
                {
                    double k = 1.0 - Math.Exp(-dt / 0.35);
                    best.local += (hl - best.local) * k;
                    best.upLocal = (best.upLocal + (hu - best.upLocal) * k).normalized;
                }
                best.target = h.s; best.thrust = h.thrust; best.used = 1f; best.heatTarget = h.heat;
                best.height = fresh ? h.height : best.height + (h.height - best.height) * (1f - Mathf.Exp(-dt / 0.35f));
                if ((best.colT -= dt) <= 0f)
                {
                    best.colT = 1f;
                    if (GroundColour(h.rh, out Color gc)) { best.col = best.colW > 0f ? Color.Lerp(best.col, gc, 0.5f) : gc; best.colW = 1f; }
                }
            }
            for (int i = sources.Count - 1; i >= 0; i--)
            {
                Source s = sources[i];
                float tau = s.target > s.s ? LunarDustSettings.riseTime : LunarDustSettings.fallTime;
                s.s += (s.target - s.s) * (1f - Mathf.Exp(-dt / Mathf.Max(tau, 0.02f)));

                s.heat += (s.heatTarget - s.heat) * (1f - Mathf.Exp(-dt / (s.heatTarget > s.heat ? 1.5f : 0.6f)));
                float fireT = s.target > 0.005f ? 1f : 0f;
                s.fire += (fireT - s.fire) * (1f - Mathf.Exp(-dt / 0.1f));
                if (s.target > 0.005f) s.age += dt; else if (s.s < 0.02f) s.age = 0f;
                if (s.target <= 0f && s.s < 0.01f && s.heatTarget <= 0f && s.heat < 0.01f) { sources.RemoveAt(i); continue; }

                if (CaelusUser.marks && s.target > 0.1f && s.height < 0.8f * LunarDustModel.StartHeight(s.thrust)) Mark(body, s, dt);
            }
            sources.Sort((a, b) => b.s.CompareTo(a.s));
            int n = Math.Min(MAXD, sources.Count);
            for (int i = 0; i < MAXD; i++)
            {
                if (i >= n) { dPos[i] = dUp[i] = dPar[i] = dCol[i] = dExtra[i] = Vector4.zero; continue; }
                Source s = sources[i];
                Vector3 p = body.transform.TransformPoint(s.local), up = ((Vector3)body.transform.TransformDirection(s.upLocal)).normalized;
                float exitR = 0.8f * Mathf.Sqrt(Mathf.Clamp(s.thrust / LunarDustSettings.refThrust, 0.05f, 9f));
                dPos[i] = new Vector4(p.x, p.y, p.z, s.s);
                dUp[i] = new Vector4(up.x, up.y, up.z, LunarDustModel.Radius(s.s, s.thrust));
                Color dc = s.colW > 0f ? DustFromGround(s.col) : LunarDustSettings.albedo;
                dCol[i] = new Vector4(dc.r, dc.g, dc.b, s.colW > 0f ? 0.8f : 0f);
                dPar[i] = new Vector4(s.height, LunarDustModel.JetRadius(exitR, s.height), LunarDustSettings.glow * Mathf.Clamp01(1f - s.height / 25f) * s.fire, s.seed);
                dExtra[i] = new Vector4(s.age, s.heat * LunarDustSettings.glow, 0f, 0f);
                if (!CaelusUser.lunarDust) continue;

                float range = dUp[i].w * 1.05f;
                if (s.occ == null) s.occ = new float[LunarDustModel.OccAz * LunarDustModel.OccR];
                Vector3 ax = Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.right, tb = Vector3.Cross(ax, up).normalized, bb = Vector3.Cross(up, tb);
                Func<float, float, float> heightAt = (x, y) =>
                {
                    Vector3 o = p + tb * x + bb * y + up * 80f;
                    return Physics.Raycast(o, -up, out RaycastHit rh, 200f, 1 << 15) ? Vector3.Dot(rh.point - p, up) : 0f;
                };
                for (int k = 0; k < OccRowsPerFrame; k++) { LunarDustModel.ShadowRow(s.occA, range, dPar[i].y, heightAt, s.occ); s.occA = (s.occA + 1) % LunarDustModel.OccAz; s.occDone++; }
                Array.Copy(s.occ, 0, occAll, i * s.occ.Length, s.occ.Length);
                dOcc[i] = new Vector4(range, s.occDone >= LunarDustModel.OccAz ? 1f : 0f, 0f, 0f);
            }
            drawDust = n > 0 && CaelusUser.lunarDust;
            if (drawDust)
            {
                if (occTex == null) occTex = new Texture2D(LunarDustModel.OccAz, LunarDustModel.OccR * MAXD, TextureFormat.RFloat, false, true) { wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                occTex.SetPixelData(occAll, 0); occTex.Apply(false);
                mat.SetTexture("_DOccTex", occTex); mat.SetVectorArray("_DOcc", dOcc);
                mat.SetInt("_DCount", n); mat.SetVectorArray("_DPos", dPos); mat.SetVectorArray("_DUp", dUp); mat.SetVectorArray("_DPar", dPar); mat.SetVectorArray("_DCol", dCol); mat.SetVectorArray("_DExtra", dExtra);
                mat.SetFloat("_DTime", (float)(ut % 1000.0)); mat.SetFloat("_DSigma", LunarDustSettings.sigma);
                mat.SetFloat("_DSheet", LunarDustSettings.sheet); mat.SetFloat("_DRays", LunarDustSettings.rays); mat.SetFloat("_DHaze", LunarDustSettings.haze);
                mat.SetFloat("_DSpeed", LunarDustSettings.speed); mat.SetFloat("_DAngle", Mathf.Tan(LunarDustSettings.angleDeg * Mathf.Deg2Rad));
                Color al = LunarDustSettings.albedo, gc = LunarDustSettings.glowColor;
                mat.SetVector("_DAlbedo", new Vector3(al.r, al.g, al.b)); mat.SetVector("_GlowColor", new Vector3(gc.r, gc.g, gc.b));
                mat.SetInt("_Steps", LunarDustSettings.steps);
            }
            drawMarks = CaelusUser.marks && SetMarks(body);
            if (LunarDustSettings.debugLog && (logT -= Time.unscaledDeltaTime) < 0f)
            {
                logT = 2f;
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < n; i++) sb.Append($" [s {sources[i].s:F2} h {sources[i].height:F1} m thrust {sources[i].thrust:F0} kN R {dUp[i].w:F0} m ground col {(sources[i].colW > 0f ? sources[i].col.ToString("F2") : "none")}]");
                Debug.Log($"[CaelusLunarDust] {body.bodyName} hits {hits.Count} sources {n}:{sb} marks drawn {(drawMarks ? mat.GetInt("_MCount") : 0)} of {CaelusLunarMarks.marks.Count}");
            }
            return drawDust || drawMarks;
        }

        void Collect(CelestialBody body)
        {
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                if (v == null || v.packed || v.mainBody != body) continue;
                if (v.heightFromTerrain > 300f && v.heightFromTerrain >= 0f) continue;
                if (!CaelusParts.Vessel(v)) continue;
                foreach (Part p in v.parts)
                {
                    if (!CaelusParts.Is(p)) continue;
                    foreach (PartModule pm in p.Modules)
                    {
                        var e = pm as ModuleEngines;
                        if (e == null || !e.EngineIgnited || e.flameout || e.finalThrust <= 0.01f || e.thrustTransforms == null || e.thrustTransforms.Count == 0) continue;
                        float per = e.finalThrust / e.thrustTransforms.Count;
                        foreach (Transform t in e.thrustTransforms)
                        {
                            if (t == null) continue;
                            float maxH = LunarDustModel.StartHeight(per);
                            if (!Physics.Raycast(t.position, t.forward, out RaycastHit rh, maxH * 1.6f, TerrainMask)) continue;
                            if (Shielded(t.position, t.forward, rh.distance, v)) continue;
                            Vector3 up = rh.normal;

                            Vector3 vert = (rh.point - (Vector3)body.position).normalized;
                            if (Vector3.Angle(up, vert) > 15f) up = Vector3.RotateTowards(vert, up, 15f * Mathf.Deg2Rad, 0f);
                            float h = rh.distance * Mathf.Max(Vector3.Dot(-t.forward, up), 0.2f);
                            float s = LunarDustModel.Strength(per, h) * Mathf.Clamp01(Vector3.Dot(-t.forward, up) * 1.5f);

                            float heat = Mathf.Clamp01(1f - h / (1.5f * maxH)); heat = heat * heat * (3f - 2f * heat) * Mathf.Sqrt(Mathf.Clamp(per / LunarDustSettings.refThrust, 0.05f, 4f));
                            if (s <= 0.005f && heat <= 0.01f) continue;
                            Hit near = null;
                            foreach (Hit x in hits) if ((x.p - rh.point).sqrMagnitude < 64f) { near = x; break; }
                            if (near == null) hits.Add(new Hit { p = rh.point, n = up, thrust = per, s = s, height = h, w = Mathf.Max(s, 0.005f), rh = rh, heat = heat });
                            else
                            {
                                near.heat = Mathf.Max(near.heat, heat);
                                float w = near.w + s;
                                near.p = (near.p * near.w + rh.point * s) / w; near.height = (near.height * near.w + h * s) / w; near.w = w;
                                near.thrust += per; near.s = Mathf.Min(1.3f * LunarDustSettings.strength, LunarDustModel.Strength(near.thrust, near.height));
                            }
                        }
                    }
                }
            }
        }

        readonly RaycastHit[] shieldBuf = new RaycastHit[16];
        bool Shielded(Vector3 from, Vector3 dir, float dist, Vessel own)
        {
            int n = Physics.RaycastNonAlloc(from, dir, shieldBuf, dist, 1 << 0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = shieldBuf[i].collider; if (c == null) continue;
                Part hp = c.GetComponentInParent<Part>();
                if (hp != null && hp.vessel != null && hp.vessel != own) return true;
            }
            return false;
        }

        readonly List<Color> cbuf = new List<Color>(); readonly List<int> tbuf = new List<int>();

        bool GroundColour(RaycastHit rh, out Color c)
        {
            c = default(Color);
            var mc = rh.collider as MeshCollider;
            if (mc == null || mc.sharedMesh == null || rh.triangleIndex < 0) return false;
            Mesh m = mc.sharedMesh;
            if (!m.isReadable || m.vertexCount > 20000) return false;
            m.GetColors(cbuf); if (cbuf.Count != m.vertexCount) return false;
            m.GetTriangles(tbuf, 0); int t = rh.triangleIndex * 3; if (t + 2 >= tbuf.Count) return false;
            Vector3 b = rh.barycentricCoordinate;
            c = cbuf[tbuf[t]] * b.x + cbuf[tbuf[t + 1]] * b.y + cbuf[tbuf[t + 2]] * b.z;
            return c.maxColorComponent > 0.02f && !(c.r > 0.99f && c.g > 0.99f && c.b > 0.99f);
        }

        static Color DustFromGround(Color g)
        {
            float l = 0.3f * g.r + 0.59f * g.g + 0.11f * g.b;

            Color d = Color.Lerp(new Color(l, l, l), g, 0.75f);
            float k = Mathf.Clamp(0.50f / Mathf.Max(l, 0.05f), 0.6f, 1.8f);
            d = Color.Lerp(d, d * k, 0.5f);
            return new Color(Mathf.Clamp01(d.r), Mathf.Clamp01(d.g), Mathf.Clamp01(d.b), 1f);
        }

        void Mark(CelestialBody body, Source s, float dt)
        {
            Vector3d wp = body.transform.TransformPoint(s.local);
            double lat = body.GetLatitude(wp), lon = body.GetLongitude(wp), alt = body.GetAltitude(wp);
            float th = Mathf.Sqrt(Mathf.Clamp(s.thrust / LunarDustSettings.refThrust, 0.05f, 9f));
            LunarMark m = null; double best = 4.0;
            foreach (LunarMark x in CaelusLunarMarks.marks)
            {
                if (x.body != body.bodyName) continue;
                double d = (body.GetWorldSurfacePosition(x.lat, x.lon, x.alt) - wp).magnitude;
                if (d < best) { best = d; m = x; }
            }
            if (m == null) { m = new LunarMark { body = body.bodyName, lat = lat, lon = lon, alt = alt, seed = UnityEngine.Random.value * 7f }; CaelusLunarMarks.marks.Add(m); }
            float gain = dt * s.target / 8f;
            float wOld = Mathf.Max(m.exposure, 1e-3f);
            m.lat = (m.lat * wOld + lat * gain) / (wOld + gain); m.lon = (m.lon * wOld + lon * gain) / (wOld + gain); m.alt = (m.alt * wOld + alt * gain) / (wOld + gain);
            m.exposure = Mathf.Min(1f, m.exposure + gain);
            m.halo = Mathf.Max(m.halo, Mathf.Lerp(30f, 130f, Mathf.Clamp01(s.target)) * th);
            m.scour = Mathf.Max(m.scour, 5f * th);
        }

        bool SetMarks(CelestialBody body)
        {
            near.Clear();
            Vector3 cam = FlightCamera.fetch != null && FlightCamera.fetch.mainCamera != null ? FlightCamera.fetch.mainCamera.transform.position : Vector3.zero;
            foreach (LunarMark m in CaelusLunarMarks.marks)
            {
                if (m.body != body.bodyName || m.exposure < 0.02f) continue;
                float d = (float)(body.GetWorldSurfacePosition(m.lat, m.lon, m.alt) - (Vector3d)cam).magnitude;
                if (d < 3000f + m.halo) near.Add(new KeyValuePair<float, LunarMark>(d, m));
            }
            if (near.Count == 0) return false;
            near.Sort((a, b) => a.Key.CompareTo(b.Key));
            int n = Math.Min(MAXM, near.Count);
            for (int i = 0; i < MAXM; i++)
            {
                if (i >= n) { mPos[i] = mUp[i] = mPar[i] = Vector4.zero; continue; }
                LunarMark m = near[i].Value;
                Vector3 p = body.GetWorldSurfacePosition(m.lat, m.lon, m.alt);
                Vector3 up = ((Vector3)(body.GetWorldSurfacePosition(m.lat, m.lon, m.alt) - body.position)).normalized;
                mPos[i] = new Vector4(p.x, p.y, p.z, m.exposure); mUp[i] = new Vector4(up.x, up.y, up.z, m.halo); mPar[i] = new Vector4(m.scour, m.seed, 0, 0);
            }
            mat.SetInt("_MCount", n); mat.SetVectorArray("_MPos", mPos); mat.SetVectorArray("_MUp", mUp); mat.SetVectorArray("_MPar", mPar);
            return true;
        }

        public void SetLight(Vector3 sunDir, float sunIntensity)
        {
            float bounce = 0.12f * sunIntensity * 0.5f * Mathf.Max(0f, drawDust ? Vector3.Dot(sunDir, (Vector3)dUp[0]) : 0.3f) + 0.01f;
            mat.SetVector("_SunDir", sunDir); mat.SetVector("_SunColor", new Vector3(sunIntensity, sunIntensity, sunIntensity));
            mat.SetVector("_Ambient", new Vector3(bounce, bounce * 0.98f, bounce * 0.95f));
        }
    }
}
