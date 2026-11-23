using System;
using System.Collections.Generic;
using UnityEngine;

namespace CaelusSmoke
{
    public class FlameSystem
    {
        const int MAXE = 32, MAXC = 6;
        public readonly Material mat;

        public bool CameraInside(Vector3 cam)
        {
            for (int k = 0; k < clusterCount; k++)
            {
                Vector3 C = cPos[k], A = cAxis[k]; float L = cAxis[k].w, Rc = cPos[k].w;
                Vector3 d = cam - C; float along = Vector3.Dot(d, A);
                if (along < -Rc) continue;
                float radial = (d - A * along).magnitude;

                float vac = cOpt2[k].w;
                if (vac > 0.1f)
                {
                    float th = Mathf.Min(cTr[k].z * vac, 1.35f);
                    if (radial < Rc * 1.5f + Mathf.Tan(th) * Mathf.Max(along, 0f) + 2f) return true;
                }
                if (along > L) continue;
                float rad = Rc * (1f + cShape[k].x) + cShape[k].y * Mathf.Max(along, 0f);
                if (radial < rad * 1.25f + 1f) return true;
            }
            return false;
        }
        public int clusterCount, engineCount;
        readonly Vector4[] ePos = new Vector4[MAXE], eAxis = new Vector4[MAXE];
        readonly Vector4[] cPos = new Vector4[MAXC], cAxis = new Vector4[MAXC], cShape = new Vector4[MAXC], cPar = new Vector4[MAXC];
        readonly Vector4[] cTemp = new Vector4[MAXC], cOpt = new Vector4[MAXC], cOpt2 = new Vector4[MAXC], cCol = new Vector4[MAXC], cOpt3 = new Vector4[MAXC], cTr = new Vector4[MAXC];

        class Cluster
        {
            public Part part; public FlameBinding bind; public FlameShape shape; public float thrust, intensity, Rc;
            public bool live, drawn; public Light light, light2; public double scanUT = -10; public int hidden; public bool keptLogged;
            public double igniteUT = -1, lastBurnUT = -1; public float lastF, lastThr = 1f, start = 1f, tail = 1f;
            public readonly List<Light> partLights = new List<Light>();
            public readonly List<Renderer> hiddenR = new List<Renderer>(); public readonly List<Light> offLights = new List<Light>();
            public Vector3 axLocal; public bool axInit; public double axUT; public float Fs = -1f;
            public float flowStep;
            public double flowPh = -1, flowUT;
            public readonly Dictionary<Transform, ParticleSystem> sparks = new Dictionary<Transform, ParticleSystem>();
            public float flashMul = 1f;
        }
        readonly Dictionary<ulong, Cluster> clusters = new Dictionary<ulong, Cluster>(); readonly List<ulong> goneKeys = new List<ulong>();
        float logTimer;

        public FlameSystem(Shader sh, Texture3D noise)
        {
            mat = new Material(sh) { name = "CaelusFlame" };
            mat.SetTexture("_Noise", noise);
        }

        public void Destroy()
        {
            foreach (Cluster c in clusters.Values) { if (c.light != null) UnityEngine.Object.Destroy(c.light.gameObject); if (c.light2 != null) UnityEngine.Object.Destroy(c.light2.gameObject); }
            clusters.Clear();
            if (mat != null) UnityEngine.Object.Destroy(mat);
        }

        public bool TryGetSmoke(List<ModuleEngines> engines, out float len, out float radius, out float reach, out float joinR)
        {
            len = 0; radius = 0; reach = 0; joinR = 0;
            if (engines == null || engines.Count == 0 || engines[0].part == null) return false;
            Cluster c;
            if (!FindLive(engines[0], out c)) return false;
            len = c.shape.joinLen; radius = c.shape.smokeR; reach = c.shape.smokeLen; joinR = c.shape.joinR;
            return true;
        }

        public void Update(double ut, CelestialBody body)
        {
            foreach (Cluster c in clusters.Values) { c.live = false; c.drawn = false; }
            clusterCount = 0; engineCount = 0; float haze = 0f;
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                if (v == null || v.packed || v.mainBody != body) continue;
                if (!CaelusParts.Vessel(v)) continue;
                float pa = (float)(v.staticPressurekPa / 101.325), air = (float)(v.atmDensity / Math.Max(1e-6, body.atmDensityASL)), q = (float)(v.dynamicPressurekPa / 101.325);
                foreach (Part p in v.parts)
                {
                    List<FlameBinding> bl = FlameSettings.For(p);
                    if (bl == null) continue;
                    for (int bi = 0; bi < bl.Count && bi < 16; bi++)
                    {
                    FlameBinding b = bl[bi];
                    Cluster c; ulong key = (ulong)p.flightID * 16UL + (ulong)bi;
                    if (!clusters.TryGetValue(key, out c)) { c = new Cluster { part = p, bind = b }; clusters[key] = c; }

                    bool ours = b.hideWaterfall && CaelusUser.flame && clusterCount < MAXC;
                    if (ours) HideWaterfall(c, ut); else if (c.hiddenR.Count > 0 || c.offLights.Count > 0) ShowWaterfall(c);
                    if (clusterCount >= MAXC) continue;

                    FlameLook lk = b.lookRef;
                    float F = 0, thr = 0; int nb = 0, e0 = engineCount, nT = 0; Vector3 C = Vector3.zero, A = Vector3.zero;
                    foreach (PartModule pm in p.Modules)
                    {
                        var en = pm as ModuleEngines;
                        if (en == null || !b.Matches(en) || en.thrustTransforms.Count == 0) continue;
                        if (en.EngineIgnited && !en.flameout && en.finalThrust > 0.5f) { F += Mathf.Max(en.finalThrust, en.maxThrust * lk.thrustFloor); thr += Mathf.Max(en.currentThrottle, lk.thrustFloor); nb++; }
                    }

                    bool burning = nb > 0;
                    if (burning)
                    {
                        if (c.igniteUT < 0 || ut - c.lastBurnUT > lk.tailTime + 0.3) c.igniteUT = ut;
                        c.lastBurnUT = ut; c.lastF = F; c.lastThr = thr / nb;
                    }
                    c.tail = burning ? 1f : 1f - (float)((ut - c.lastBurnUT) / Math.Max(0.05, lk.tailTime));
                    if (!burning && (c.lastBurnUT < 0 || c.tail <= 0f || ut < c.lastBurnUT)) continue;
                    c.start = Mathf.Clamp01((float)((ut - c.igniteUT) / Math.Max(0.05, lk.startTime)));
                    if (!burning) { F = c.lastF * c.tail; thr = c.lastThr; nb = 1; }
                    foreach (PartModule pm in p.Modules)
                    {
                        var en = pm as ModuleEngines;
                        if (en == null || !b.Matches(en) || en.thrustTransforms.Count == 0) continue;
                        if (burning && !(en.EngineIgnited && !en.flameout && en.finalThrust > 0.5f)) continue;
                        nT += en.thrustTransforms.Count;
                    }
                    int ti = 0;
                    foreach (PartModule pm in p.Modules)
                    {
                        var en = pm as ModuleEngines;
                        if (en == null || !b.Matches(en) || en.thrustTransforms.Count == 0) continue;
                        if (burning && !(en.EngineIgnited && !en.flameout && en.finalThrust > 0.5f)) continue;
                        foreach (Transform t in en.thrustTransforms)
                        {
                            if (engineCount >= MAXE) break;
                            Vector3 ax = t.forward, ex = t.position + ax * b.exitOffset;

                            float delay = nT > 1 ? lk.stagger * ti / (nT - 1) : 0f;
                            float es = Mathf.Clamp01((float)((ut - c.igniteUT - delay) / 0.6));
                            if (lk.sparks > 0f && !c.sparks.ContainsKey(t)) c.sparks[t] = MakeSparks(t, b, lk);
                            ePos[engineCount] = new Vector4(ex.x, ex.y, ex.z, b.exitRadius);
                            eAxis[engineCount] = new Vector4(ax.x, ax.y, ax.z, es);
                            C += ex; A += ax; engineCount++; ti++;
                        }
                    }
                    int ne = engineCount - e0;
                    if (ne == 0) { engineCount = e0; continue; }
                    C /= ne; A.Normalize();

                    {
                        Vector3 loc = p.transform.InverseTransformDirection(A);
                        double dtA = c.axInit ? ut - c.axUT : 0.0; c.axUT = ut;
                        if (!c.axInit || dtA < 0 || dtA > 2.0) { c.axLocal = loc; c.axInit = true; }
                        else c.axLocal = Vector3.Slerp(c.axLocal, loc, 1f - Mathf.Exp(-(float)dtA / 0.35f)).normalized;
                        A = p.transform.TransformDirection(c.axLocal).normalized;
                        if (c.Fs < 0f || dtA <= 0 || dtA > 2.0 || !burning) c.Fs = F; else c.Fs = Mathf.Lerp(c.Fs, F, 1f - Mathf.Exp(-(float)dtA / 0.3f));
                        if (burning) F = c.Fs;
                    }
                    float Rc = 0;
                    for (int i = e0; i < engineCount; i++) Rc = Mathf.Max(Rc, Vector3.ProjectOnPlane((Vector3)ePos[i] - C, A).magnitude);
                    Rc += b.exitRadius;
                    FlameLook l = b.lookRef;
                    c.shape = FlameModel.Shape(l, F, pa, air, b.exitRadius, Rc, q);
                    c.shape.Lp *= Mathf.Lerp(0.3f, 1f, Mathf.Pow(c.start, 0.7f)) * Mathf.Lerp(0.5f, 1f, c.tail);
                    if (l.growBurn != 1f && c.igniteUT >= 0) c.shape.Lp *= Mathf.Lerp(1f, l.growBurn, Mathf.SmoothStep(0f, 1f, (float)((ut - c.igniteUT) / Math.Max(0.1, l.growTime))));
                    c.shape.joinLen = c.shape.Lp * l.smokeJoin;
                    c.thrust = F; c.Rc = Rc; c.live = burning; c.intensity = Mathf.Clamp01(thr / Math.Max(1, nb)) * c.tail;
                    c.flashMul = burning && l.flash > 0f ? 1f + l.flash * Mathf.Exp(-(float)(ut - c.igniteUT) / Mathf.Max(0.05f, l.flashTime)) : 1f;
                    if (c.sparks.Count > 0)
                        foreach (KeyValuePair<Transform, ParticleSystem> sp in c.sparks)
                        {
                            if (sp.Value == null) continue;
                            var em = sp.Value.emission;
                            em.rateOverTime = burning && CaelusUser.flame ? l.sparks * c.intensity * Mathf.Lerp(1f, 2.5f, c.flashMul - 1f) : 0f;
                        }
                    float Rv = b.vehicleRadius > 0 ? b.vehicleRadius : Rc * 1.1f;
                    int k = clusterCount++; c.drawn = true;
                    cPos[k] = new Vector4(C.x, C.y, C.z, Rc);
                    cAxis[k] = new Vector4(A.x, A.y, A.z, c.shape.Lp);
                    cShape[k] = new Vector4(c.shape.E, l.spread, c.shape.mergeX, Rv);
                    cPar[k] = new Vector4(c.intensity, c.shape.recirc, e0, ne);
                    cTemp[k] = new Vector4(l.tCore, l.tMantle, l.tTail, l.tSheath);

                    {
                        float hiC = Mathf.Clamp01((c.shape.E - 3f) / 7f) * (l.cluster ? 1f : 0f) * FlameSettings.thinAir;
                        float vFlow = l.speed * Mathf.Lerp(1f, FlameSettings.thinAirFlow, hiC);
                        float baseScale = 1f / Mathf.Max(Rc * 3f, 4f);
                        double dtF = ut - c.flowUT; c.flowUT = ut;
                        if (c.flowPh < 0 || dtF < 0 || dtF > 2.0) { if (c.flowPh < 0) c.flowPh = (p.flightID % 997u) * 0.0371; }
                        else
                        {
                            double stepF = Mathf.Min(vFlow * 0.6f * baseScale, l.flowCap > 0f ? l.flowCap : FlameSettings.flowCap) * dtF;
                            c.flowPh = (c.flowPh + stepF) % 4096.0;
                            c.flowStep = (float)stepF;
                        }
                    }
                    cOpt[k] = new Vector4(l.sigma, l.exposure * c.flashMul, (float)c.flowPh, l.sheath);
                    cOpt2[k] = new Vector4(l.sheathLength * 2f * b.exitRadius, Mathf.Min(c.flowStep, 0.6f), (p.flightID % 997u) * 0.0137f, c.shape.vac);
                    cCol[k] = new Vector4(l.color.r, l.color.g, l.color.b, l.soot);
                    cOpt3[k] = new Vector4(l.scatter, l.cluster ? 1f : 0f, l.densExp, l.rays);
                    cTr[k] = new Vector4(c.start, c.tail, l.vacTheta * Mathf.Deg2Rad, 2f / Mathf.Max(l.vacGamma - 1f, 0.05f));
                    haze = Mathf.Max(haze, Mathf.Clamp01(air * 1.5f) * c.intensity * Mathf.Lerp(0.4f, 1f, c.start) * (l.soot > 0.5f ? 1f : 0.5f));
                    UpdateLight(c, C, A, l, ut, air);
                    }
                }
            }

            var gone = goneKeys; gone.Clear();
            foreach (KeyValuePair<ulong, Cluster> kv in clusters)
            {
                if (kv.Value.live || kv.Value.drawn) continue;
                if (kv.Value.light != null) kv.Value.light.enabled = false;
                if (kv.Value.light2 != null) kv.Value.light2.enabled = false;
                foreach (ParticleSystem ps in kv.Value.sparks.Values) if (ps != null) { var em = ps.emission; em.rateOverTime = 0f; }
                if (kv.Value.part == null || kv.Value.part.vessel == null) { if (kv.Value.light != null) UnityEngine.Object.Destroy(kv.Value.light.gameObject); if (kv.Value.light2 != null) UnityEngine.Object.Destroy(kv.Value.light2.gameObject); gone.Add(kv.Key); }
            }
            foreach (ulong id in gone) clusters.Remove(id);

            mat.SetVectorArray("_FE_Pos", ePos); mat.SetVectorArray("_FE_Axis", eAxis);
            mat.SetVectorArray("_FC_Pos", cPos); mat.SetVectorArray("_FC_Axis", cAxis); mat.SetVectorArray("_FC_Shape", cShape); mat.SetVectorArray("_FC_Par", cPar);
            mat.SetVectorArray("_FC_Temp", cTemp); mat.SetVectorArray("_FC_Opt", cOpt); mat.SetVectorArray("_FC_Opt2", cOpt2); mat.SetVectorArray("_FC_Col", cCol); mat.SetVectorArray("_FC_Opt3", cOpt3); mat.SetVectorArray("_FC_Tr", cTr);
            mat.SetInt("_ClusterCount", clusterCount); mat.SetInt("_Steps", FlameSettings.steps);
            mat.SetFloat("_Time01", (float)(ut % 100000.0)); mat.SetFloat("_Haze", haze * FlameSettings.haze);

            if (FlameSettings.debugLog && (logTimer -= Time.unscaledDeltaTime) < 0)
            {
                logTimer = 2f;
                foreach (Cluster c in clusters.Values)
                    if (c.live) Debug.Log("[CaelusFlame] " + c.part.partInfo.name + ": " + c.thrust.ToString("F0") + " kN, throttle " + c.intensity.ToString("F2") + ", Rc " + c.Rc.ToString("F2")
                        + " m, E " + c.shape.E.ToString("F2") + ", Lp " + c.shape.Lp.ToString("F0") + " m, merge " + c.shape.mergeX.ToString("F1") + " m, recirc " + c.shape.recirc.ToString("F2")
                        + ", smoke len " + c.shape.smokeLen.ToString("F0") + " m r " + c.shape.smokeR.ToString("F0") + " m, Waterfall renderers off " + c.hidden + ", part lights off " + c.partLights.Count);
            }
        }

        bool FindLive(ModuleEngines en, out Cluster c)
        {
            c = null;
            for (int bi = 0; bi < 16; bi++)
            {
                Cluster cc;
                if (!clusters.TryGetValue((ulong)en.part.flightID * 16UL + (ulong)bi, out cc)) break;
                if (cc.live && cc.bind.Matches(en)) { c = cc; return true; }
            }
            return false;
        }

        static void HideWaterfall(Cluster c, double ut)
        {
            if (ut - c.scanUT > 2.0 || ut < c.scanUT)
            {
                c.scanUT = ut; c.partLights.Clear();
                foreach (Renderer r in c.part.GetComponentsInChildren<Renderer>(true))
                {
                    Material m = r.sharedMaterial;
                    if (m == null || m.shader == null || !m.shader.name.StartsWith("Waterfall")) continue;
                    string nm = r.name.ToUpperInvariant();
                    if (nm.Contains("GLOW") || nm.Contains("CORE"))
                    {
                        if (FlameSettings.debugLog && !c.keptLogged) Debug.Log("[CaelusFlame] keep " + c.part.partInfo.name + ": " + r.name + " enabled " + r.enabled + " active " + r.gameObject.activeInHierarchy + " shader " + m.shader.name);
                        continue;
                    }
                    if (FlameSettings.debugLog && !c.keptLogged && c.hidden < 12) Debug.Log("[CaelusFlame] hide " + c.part.partInfo.name + ": " + r.name);
                    if (!r.forceRenderingOff) { r.forceRenderingOff = true; c.hidden++; c.hiddenR.Add(r); }
                }
                foreach (Light lt in c.part.GetComponentsInChildren<Light>(true)) c.partLights.Add(lt);
                c.keptLogged = true;
            }
            foreach (Light lt in c.partLights) if (lt != null && lt.enabled) { lt.enabled = false; if (!c.offLights.Contains(lt)) c.offLights.Add(lt); }
        }

        static void ShowWaterfall(Cluster c)
        {
            foreach (Renderer r in c.hiddenR) if (r != null) r.forceRenderingOff = false;
            foreach (Light lt in c.offLights) if (lt != null) lt.enabled = true;
            c.hiddenR.Clear(); c.offLights.Clear(); c.hidden = 0; c.scanUT = -10;
        }

        static Material sparkMat;

        static ParticleSystem MakeSparks(Transform t, FlameBinding b, FlameLook l)
        {
            if (sparkMat == null)
            {
                Shader sh = Shader.Find("KSP/Particles/Additive") ?? Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Particles/Additive");
                var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "CaelusSpark" };
                for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
                {
                    float dx = (x - 15.5f) / 15.5f, dy = (y - 15.5f) / 15.5f, g = Mathf.Exp(-4.5f * (dx * dx + dy * dy));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, g));
                }
                tex.Apply();
                sparkMat = new Material(sh) { name = "CaelusSparks", mainTexture = tex };
                if (sparkMat.HasProperty("_TintColor")) sparkMat.SetColor("_TintColor", new Color(1f, 1f, 1f, 1f));
            }
            var go = new GameObject("CaelusSparks");
            go.transform.SetParent(t, false); go.transform.localPosition = new Vector3(0f, 0f, b.exitOffset); go.transform.localRotation = Quaternion.identity;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            float k = Mathf.Clamp(b.exitRadius / 0.17f, 0.5f, 3f);
            var main = ps.main;
            main.playOnAwake = false; main.loop = true; main.simulationSpace = ParticleSystemSimulationSpace.Local; main.maxParticles = 600;
            main.startLifetime = new ParticleSystem.MinMaxCurve(l.sparkLife * 0.4f, l.sparkLife * 1.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(l.sparkSpeed * 0.5f, l.sparkSpeed * 1.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(l.sparkSize * k * 0.5f, l.sparkSize * k * 1.4f);
            main.startColor = Color.white; main.gravityModifier = 0f;
            var em = ps.emission; em.rateOverTime = 0f;
            var sh2 = ps.shape; sh2.enabled = true; sh2.shapeType = ParticleSystemShapeType.Cone; sh2.angle = l.sparkCone; sh2.radius = b.exitRadius * 0.6f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.97f, 0.85f), 0f), new GradientColorKey(new Color(1f, 0.62f, 0.22f), 0.3f), new GradientColorKey(new Color(0.85f, 0.28f, 0.06f), 1f) },
                       new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.35f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(gr);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = l.sparkStretch; r.lengthScale = 1.5f; r.sharedMaterial = sparkMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            go.layer = t.gameObject.layer;
            ps.Play();
            return ps;
        }

        static void UpdateLight(Cluster c, Vector3 C, Vector3 A, FlameLook l, double ut, float air)
        {
            if (l.lightIntensity <= 0f) return;
            if (c.light == null)
            {
                var go = new GameObject("CaelusFlameLight");
                c.light = go.AddComponent<Light>();
                c.light.type = LightType.Point; c.light.shadows = LightShadows.None; c.light.renderMode = LightRenderMode.ForcePixel;
                c.light.cullingMask = ~((1 << 16) | (1 << 20));
            }
            Vector3 pc = FlameModel.Planck(l.tCore); float m = Mathf.Max(pc.x, Mathf.Max(pc.y, pc.z));
            c.light.color = l.lightColor.x >= 0f ? new Color(l.lightColor.x, l.lightColor.y, l.lightColor.z) : new Color(pc.x / m, pc.y / m, pc.z / m);
            c.light.transform.position = C + A * Mathf.Max(l.lightMinOffset, Mathf.Min(c.shape.Lp * 0.25f, 30f));
            float flick = 1f + 0.05f * Mathf.Sin((float)(ut % 1000.0) * 23f) + 0.03f * Mathf.Sin((float)(ut % 1000.0) * 37f + 1.3f);
            c.light.intensity = l.lightIntensity * c.intensity * Mathf.Lerp(0.3f, 1f, c.start) * flick * c.flashMul * (l.soot > 0.5f && l.sparks <= 0f ? Mathf.Lerp(0.4f, 1f, Mathf.Clamp01(air * 4f)) : 1f);
            c.light.range = Mathf.Max(l.lightMinRange, l.lightRange * Mathf.Sqrt(Mathf.Max(c.thrust, 1f) / 50000f));
            c.light.enabled = CaelusUser.flame && c.intensity > 0.01f && c.tail > 0f;

            if (c.light2 == null)
            {
                var go2 = new GameObject("CaelusFlameLight2");
                c.light2 = go2.AddComponent<Light>();
                c.light2.type = LightType.Point; c.light2.shadows = LightShadows.None; c.light2.renderMode = LightRenderMode.ForcePixel;
                c.light2.cullingMask = ~((1 << 16) | (1 << 20));
            }
            c.light2.color = c.light.color;

            c.light2.transform.position = C + A * (c.Rc * 1.2f);
            c.light2.range = c.Rc * 6f;
            float sunUp = Vector3.Dot(((Vector3)(C - (Vector3)c.part.vessel.mainBody.position)).normalized, ((Vector3)(Planetarium.fetch.Sun.position - (Vector3d)C)).normalized);

            float dayK = l.soot > 0.5f && l.sparks <= 0f ? Mathf.Lerp(1f, 0.1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.05f, 0.2f, sunUp))) : 1f;
            c.light2.intensity = l.lightMinOffset > 0f ? 0f : c.light.intensity * 0.7f * dayK;
            c.light2.enabled = c.light.enabled;
        }
    }
}
