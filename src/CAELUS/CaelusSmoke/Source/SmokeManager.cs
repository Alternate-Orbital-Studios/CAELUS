using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CaelusSmoke
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class SmokeManager : MonoBehaviour
    {
        public static float sigma = 0.08f;

        readonly SmokeTrail trail = new SmokeTrail();
        FlameSystem flames;
        BellGlowSystem bells; RcsPlumeSystem rcsPlumes; ReflectionSystem reflections; CameraGuard cameraGuard; ActuatorFollow actuators;
        IvaShadows ivaShadows;
        VaporSystem vapor;
        FrostSystem frost;
        LunarDustSystem dust;
        PadSteamSystem padSteam;
        readonly Dictionary<string, SmokeEmitter> emitters = new Dictionary<string, SmokeEmitter>();
        CelestialBody trailBody;
        double lastUT = -1, anchorLat, anchorLon; bool anchored;

        Material mat; Mesh tri; MaterialPropertyBlock mpb;

        CommandBuffer cb00, cb01; Camera cbCam00, cbCam01;
        static readonly int LowId = Shader.PropertyToID("_CaelusSmokeLow");

        public static bool beforeTransparents = true;
        bool foreignNow; float foreignUntil = -1f, foreignScan;
        CameraEvent ev00 = CameraEvent.AfterForwardAlpha, ev01 = CameraEvent.AfterForwardAlpha;
        CameraEvent CbEvent { get { return beforeTransparents && foreignNow ? CameraEvent.BeforeForwardAlpha : CameraEvent.AfterForwardAlpha; } }
        void ScanForeignEngines()
        {
            if (!beforeTransparents) { foreignNow = false; return; }
            float now = Time.realtimeSinceStartup;
            if (now >= foreignScan)
            {
                foreignScan = now + 0.25f;
                bool burning = false;
                var vs = FlightGlobals.VesselsLoaded;
                if (vs != null)
                    for (int i = 0; i < vs.Count && !burning; i++)
                    {
                        Vessel v = vs[i]; if (v == null || v.parts == null) continue;
                        for (int j = 0; j < v.parts.Count && !burning; j++)
                        {
                            Part p = v.parts[j]; if (p == null || CaelusParts.Is(p)) continue;
                            var ems = p.Modules.GetModules<ModuleEngines>();
                            for (int k = 0; k < ems.Count; k++) if (ems[k] != null && ems[k].EngineIgnited && ems[k].finalThrust > 0.01f) { burning = true; break; }
                        }
                    }
                if (burning) foreignUntil = now + 8f;
            }
            bool was = foreignNow; foreignNow = now < foreignUntil;
            if (foreignNow != was) Debug.Log("[CaelusSmoke] other mods' engine burning: " + (foreignNow ? "smoke drawn before transparent effects (their plumes in front)" : "back to the normal CAELUS order"));
        }
        static readonly int SmokeFrontId = Shader.PropertyToID("_CaelusSmokeFront"); RenderTargetIdentifier[] smokeMrt;
        static readonly int FlameFrontId = Shader.PropertyToID("_CaelusFlameFront");
        bool frontHeld;
        static readonly int FlameLowId = Shader.PropertyToID("_CaelusFlameLow"), FlameSceneId = Shader.PropertyToID("_CaelusFlameScene"), VaporLowId = Shader.PropertyToID("_CaelusVaporLow"), FrostLowId = Shader.PropertyToID("_CaelusFrostLow"), DustLowId = Shader.PropertyToID("_CaelusDustLow");
        void RecordCam(Camera cam, ref CommandBuffer cb, ref Camera attached, ref CameraEvent attEv, MaterialPropertyBlock props, float sliceNear, float sliceFar, bool drawSmoke, bool drawFlame, bool drawVapor, bool drawDust = false, bool drawFrost = false)
        {
            if (cb == null) cb = new CommandBuffer { name = "CaelusSmoke" };
            CameraEvent ev = CbEvent;
            if (attached != cam || attEv != ev) { if (attached != null) attached.RemoveCommandBuffer(attEv, cb); if (cam != null) cam.AddCommandBuffer(ev, cb); attached = cam; attEv = ev; }
            cb.Clear();
            if (cam == null) return;
            cb.SetGlobalFloat("_CaelusFlameFrontOn", 0f); frontHeld = false;
            if (drawDust && dust.drawMarks)
            {
                cb.SetGlobalFloat("_SliceNear", sliceNear); cb.SetGlobalFloat("_SliceFar", sliceFar);
                cb.DrawMesh(tri, Matrix4x4.identity, dust.mat, 0, 2);
            }
            if (drawFlame && !FlameSettings.smokeUnderFlame) DrawFlamePass(cb, cam, sliceNear, sliceFar);
            if (drawVapor)
            {
                int vdiv = Mathf.Max(1, VaporSettings.resDivisor);
                cb.GetTemporaryRT(VaporLowId, Mathf.Max(1, cam.pixelWidth / vdiv), Mathf.Max(1, cam.pixelHeight / vdiv), 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
                cb.SetRenderTarget(VaporLowId); cb.ClearRenderTarget(false, true, Color.clear);
                cb.SetGlobalFloat("_SliceNear", sliceNear); cb.SetGlobalFloat("_SliceFar", sliceFar);
                cb.DrawMesh(tri, Matrix4x4.identity, vapor.mat, 0, 0);
                cb.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                cb.SetGlobalTexture("_VaporLow", VaporLowId);
                cb.DrawMesh(tri, Matrix4x4.identity, vapor.mat, 0, 1);
                cb.ReleaseTemporaryRT(VaporLowId);
            }
            if (drawFrost)
            {
                int fdivF = Mathf.Max(1, FrostSettings.resDivisor);
                cb.GetTemporaryRT(FrostLowId, Mathf.Max(1, cam.pixelWidth / fdivF), Mathf.Max(1, cam.pixelHeight / fdivF), 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
                cb.SetRenderTarget(FrostLowId); cb.ClearRenderTarget(false, true, Color.clear);
                cb.SetGlobalFloat("_SliceNear", sliceNear); cb.SetGlobalFloat("_SliceFar", sliceFar);
                cb.DrawMesh(tri, Matrix4x4.identity, frost.vapourMat, 0, 0);
                cb.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                cb.SetGlobalTexture("_VaporLow", FrostLowId);
                cb.DrawMesh(tri, Matrix4x4.identity, frost.vapourMat, 0, 1);
                cb.ReleaseTemporaryRT(FrostLowId);
            }
            if (drawDust && dust.drawDust)
            {
                int ddiv = Mathf.Max(1, LunarDustSettings.resDivisor);
                cb.GetTemporaryRT(DustLowId, Mathf.Max(1, cam.pixelWidth / ddiv), Mathf.Max(1, cam.pixelHeight / ddiv), 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
                cb.SetRenderTarget(DustLowId); cb.ClearRenderTarget(false, true, Color.clear);
                cb.SetGlobalFloat("_SliceNear", sliceNear); cb.SetGlobalFloat("_SliceFar", sliceFar);
                cb.DrawMesh(tri, Matrix4x4.identity, dust.mat, 0, 0);
                cb.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                cb.SetGlobalTexture("_DustLow", DustLowId);
                cb.DrawMesh(tri, Matrix4x4.identity, dust.mat, 0, 1);
                cb.ReleaseTemporaryRT(DustLowId);
            }
            if (drawSmoke) DrawSmokePass(cb, cam, props, sliceNear, sliceFar);
            if (frontHeld) { cb.SetGlobalFloat("_CaelusFlameFrontOn", 0f); cb.ReleaseTemporaryRT(FlameFrontId); frontHeld = false; }

            if (drawFlame && FlameSettings.smokeUnderFlame) DrawFlamePass(cb, cam, sliceNear, sliceFar);
        }
        void DrawSmokePass(CommandBuffer cb, Camera cam, MaterialPropertyBlock props, float sliceNear, float sliceFar)
        {
            int div = Mathf.Max(1, smokeDivNow);
            int w = Mathf.Max(1, cam.pixelWidth / div), h = Mathf.Max(1, cam.pixelHeight / div);
            cb.GetTemporaryRT(LowId, w, h, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
            bool front = beforeTransparents && foreignNow;
            if (front)
            {
                cb.GetTemporaryRT(SmokeFrontId, w, h, 0, FilterMode.Point, RenderTextureFormat.RFloat);
                cb.SetRenderTarget(smokeMrt ?? (smokeMrt = new RenderTargetIdentifier[] { LowId, SmokeFrontId }), LowId);
            }
            else cb.SetRenderTarget(LowId);
            cb.ClearRenderTarget(false, true, Color.clear);
            cb.SetGlobalFloat("_SliceNear", sliceNear); cb.SetGlobalFloat("_SliceFar", sliceFar);

            cb.SetGlobalFloat("_CaelusPixAng", 2f * Mathf.Tan(0.5f * cam.fieldOfView * Mathf.Deg2Rad) / h);
            cb.DrawMesh(tri, Matrix4x4.identity, mat, 0, 0, props);
            cb.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            if (padShadow) cb.DrawMesh(tri, Matrix4x4.identity, mat, 0, 2, props);
            cb.SetGlobalTexture("_SmokeLow", LowId);
            cb.DrawMesh(tri, Matrix4x4.identity, mat, 0, 1, props);
            if (front) { cb.SetGlobalTexture(SmokeFrontId, SmokeFrontId); cb.DrawMesh(tri, Matrix4x4.identity, mat, 0, 3, props); cb.ReleaseTemporaryRT(SmokeFrontId); }
            cb.ReleaseTemporaryRT(LowId);
        }
        void DrawFlamePass(CommandBuffer cb, Camera cam, float sliceNear, float sliceFar)
        {
            {
                int fdiv = Mathf.Max(1, FlameSettings.resDivisor);
                if (flames.CameraInside(cam.transform.position)) fdiv = Mathf.Max(3, fdiv + 1);
                cb.GetTemporaryRT(FlameLowId, Mathf.Max(1, cam.pixelWidth / fdiv), Mathf.Max(1, cam.pixelHeight / fdiv), 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);

                cb.GetTemporaryRT(FlameFrontId, Mathf.Max(1, cam.pixelWidth / fdiv), Mathf.Max(1, cam.pixelHeight / fdiv), 0, FilterMode.Point, RenderTextureFormat.RFloat);
                cb.SetRenderTarget(flameMrt ?? (flameMrt = new RenderTargetIdentifier[] { FlameLowId, FlameFrontId }), FlameLowId); cb.ClearRenderTarget(false, true, Color.clear);
                cb.SetGlobalFloat("_SliceNear", sliceNear); cb.SetGlobalFloat("_SliceFar", sliceFar);
                cb.DrawMesh(tri, Matrix4x4.identity, flames.mat, 0, 0);
                cb.SetGlobalTexture(FlameFrontId, FlameFrontId);
                if (FlameSettings.smokeUnderFlame) cb.ReleaseTemporaryRT(FlameFrontId);
                else { cb.SetGlobalFloat("_CaelusFlameFrontOn", 1f); frontHeld = true; }

                cb.GetTemporaryRT(FlameSceneId, cam.pixelWidth, cam.pixelHeight, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
                cb.Blit(BuiltinRenderTextureType.CameraTarget, FlameSceneId);
                cb.SetGlobalTexture("_FlameScene", FlameSceneId);
                cb.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                cb.SetGlobalTexture("_FlameLow", FlameLowId);
                cb.DrawMesh(tri, Matrix4x4.identity, flames.mat, 0, 1);
                if (FlameSettings.glow && CaelusUser.flameGlow && GlowGain() > 0.0001f) DrawGlow(cb, cam, fdiv);
                cb.ReleaseTemporaryRT(FlameLowId);
                cb.ReleaseTemporaryRT(FlameSceneId);
            }
        }

        static readonly int GlowSrcId = Shader.PropertyToID("_GlowSrc"); static readonly int GlowCoverId = Shader.PropertyToID("_GlowCover"); static readonly int GlowSceneId = Shader.PropertyToID("_GlowScene");
        static readonly int[] GlowIds = { Shader.PropertyToID("_CaelusGlow0"), Shader.PropertyToID("_CaelusGlow1"), Shader.PropertyToID("_CaelusGlow2"), Shader.PropertyToID("_CaelusGlow3"),
                                          Shader.PropertyToID("_CaelusGlow4"), Shader.PropertyToID("_CaelusGlow5"), Shader.PropertyToID("_CaelusGlow6"), Shader.PropertyToID("_CaelusGlow7") };
        static float GlowGain() { return FlameSettings.glowIntensity * (FlameSettings.tufxPresent ? FlameSettings.glowWithTUFX : 1f); }
        void DrawGlow(CommandBuffer cb, Camera cam, int fdiv)
        {
            Material m = flames.mat;
            m.SetVector("_GlowThresh", new Vector4(FlameSettings.glowThreshold, FlameSettings.glowKnee, FlameSettings.glowClamp, 0));
            m.SetFloat("_GlowScatter", FlameSettings.glowScatter);
            Color t = FlameSettings.glowTint; m.SetColor("_GlowColor", new Color(t.r, t.g, t.b, GlowGain()));
            int w = Mathf.Max(1, cam.pixelWidth / fdiv), h = Mathf.Max(1, cam.pixelHeight / fdiv), n = 0;
            for (int i = 0; i < FlameSettings.glowLevels; i++)
            {
                w /= 2; h /= 2; if (w < 4 || h < 4) break;
                cb.GetTemporaryRT(GlowIds[i], w, h, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf); n++;
            }
            if (n == 0) return;
            cb.SetGlobalTexture(GlowSrcId, FlameLowId); cb.SetRenderTarget(GlowIds[0]); cb.DrawMesh(tri, Matrix4x4.identity, m, 0, 2);
            for (int i = 1; i < n; i++) { cb.SetGlobalTexture(GlowSrcId, GlowIds[i - 1]); cb.SetRenderTarget(GlowIds[i]); cb.DrawMesh(tri, Matrix4x4.identity, m, 0, 3); }
            for (int i = n - 1; i > 0; i--) { cb.SetGlobalTexture(GlowSrcId, GlowIds[i]); cb.SetRenderTarget(GlowIds[i - 1]); cb.DrawMesh(tri, Matrix4x4.identity, m, 0, 4); }
            cb.SetGlobalTexture(GlowCoverId, FlameLowId); cb.SetGlobalTexture(GlowSceneId, FlameSceneId); m.SetFloat("_GlowOnFlame", FlameSettings.glowOnFlame);
            cb.SetGlobalTexture(GlowSrcId, GlowIds[0]); cb.SetRenderTarget(BuiltinRenderTextureType.CameraTarget); cb.DrawMesh(tri, Matrix4x4.identity, m, 0, 5);
            for (int i = 0; i < n; i++) cb.ReleaseTemporaryRT(GlowIds[i]);
        }

        int smokeDivNow = 2; bool smokeFar;
        void PickSmokeDiv(Camera cam, int n)
        {
            smokeDivNow = SmokeSettings.resDivisor;
            if (cam == null || n <= 0 || aSegB == null) { smokeFar = false; return; }
            Vector3 cp = cam.transform.position; float near = float.MaxValue;
            for (int i = 0; i < n && i < aSegB.Length; i++) { Vector4 sb = aSegB[i]; if (sb.w <= 0) continue; float d = Vector3.Distance(cp, (Vector3)sb) - sb.w; if (d < near) near = d; }
            smokeFar = smokeFar ? near > 700f : near > 900f;
            if (smokeFar) smokeDivNow = Mathf.Max(1, SmokeSettings.resDivisor - 1);
        }
        void ClearCbs() { if (cb00 != null) cb00.Clear(); if (cb01 != null) cb01.Clear(); }
        void OnDisable() { ClearCbs(); }
        ComputeBuffer bSeg0, bSeg1, bSeg2, bSeg3, bSeg4, bChunk, bBlock, bSegB;
        Vector4[] aSeg0, aSeg1, aSeg2, aSeg3, aSeg4, aChunk, aBlock, aSegB;
        readonly Vector4[] lookCol = new Vector4[8], lookSoot = new Vector4[8], lookHigh = new Vector4[8], flamePos = new Vector4[4], flameCol = new Vector4[4];
        int padLook = -1; bool padShadow;

        readonly List<Transform> outlets = new List<Transform>();
        readonly Dictionary<Transform, SmokeEmitter> padEmitters = new Dictionary<Transform, SmokeEmitter>();
        float outletScan;

        void Start()
        {
            SmokeSettings.Load();
            foreach (ConfigNode s in GameDatabase.Instance.GetConfigNodes("CAELUSSMOKE_SETTINGS")) { s.TryGetValue("sigma", ref sigma); s.TryGetValue("drawBeforeTransparents", ref beforeTransparents); }
            if (!SmokeSettings.enabled || !SmokeAssets.Load()) { enabled = false; return; }
            mat = new Material(SmokeAssets.volume) { name = "CaelusSmokeVolume" };
            mat.SetTexture("_Noise", SmokeAssets.noise);
            tri = new Mesh { name = "CaelusSmokeFullscreen" };
            tri.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            tri.triangles = new[] { 0, 1, 2 };
            tri.bounds = new Bounds(Vector3.zero, Vector3.one * 1e9f);
            mpb = new MaterialPropertyBlock();
            Alloc(SmokeSettings.maxSegments);
            for (int i = 0; i < SmokeSettings.looks.Count && i < 8; i++)
            {
                SmokeLook l = SmokeSettings.looks[i];
                lookCol[i] = new Vector4(l.color.r, l.color.g, l.color.b, l.opacity);
                lookSoot[i] = new Vector4(l.soot.r, l.soot.g, l.soot.b, l.puffMode);
                lookHigh[i] = new Vector4(l.highColor.r, l.highColor.g, l.highColor.b, 0);
            }
            EnableDepth();
            GameEvents.OnCameraChange.Add(OnCameraChange);
            FlameSettings.Load();

            Shader.SetGlobalFloat("_CaelusCauli", SmokeSettings.cauliflower); Shader.SetGlobalFloat("_CaelusWarp", SmokeSettings.warp); Shader.SetGlobalFloat("_StepGrow", SmokeSettings.stepGrow); Shader.SetGlobalFloat("_CaelusPadValley", SmokeSettings.padValley); Shader.SetGlobalFloat("_CaelusCloseDetail", SmokeSettings.closeDetail); Shader.SetGlobalFloat("_CaelusShadeLift", SmokeSettings.shadeLift); Shader.SetGlobalFloat("_CaelusFlameThinAir", FlameSettings.thinAir); Shader.SetGlobalFloat("_CaelusFlameTailFlat", FlameSettings.tailFlat); Shader.SetGlobalFloat("_CaelusFlowLevel", FlameSettings.flowLevel);
            if (FlameSettings.enabled && SmokeAssets.flame != null) flames = new FlameSystem(SmokeAssets.flame, SmokeAssets.noise);
            reflections = new ReflectionSystem(SmokeAssets.foil, SmokeAssets.glass);
            VaporSettings.Load();
            if (VaporSettings.enabled && SmokeAssets.vapor != null) vapor = new VaporSystem(SmokeAssets.vapor, SmokeAssets.noise);
            FrostSettings.Load();
            if (FrostSettings.enabled && SmokeAssets.frost != null && SmokeAssets.frostVapour != null) frost = new FrostSystem(SmokeAssets.frost, SmokeAssets.frostVapour, SmokeAssets.noise);
            LunarDustSettings.Load();
            if (LunarDustSettings.enabled && SmokeAssets.lunarDust != null) dust = new LunarDustSystem(SmokeAssets.lunarDust, SmokeAssets.noise);
            CaelusUser.Apply(true);
            actuators = new ActuatorFollow();
            ivaShadows = new IvaShadows();
            cameraGuard = new CameraGuard();
            padSteam = PadSteamSystem.Create();
            if (padSteam != null) padSteam.RepairLeakedPause();
            GameEvents.onGameSceneLoadRequested.Add(OnSceneLoadRequested);
            padLook = Math.Min(7, SmokeSettings.looks.IndexOf(SmokeSettings.Named("padsteam") ?? SmokeSettings.Named("padcloud") ?? SmokeSettings.looks[0]));
            if (FlameSettings.enabled && SmokeAssets.bellGlow != null) { bells = new BellGlowSystem(SmokeAssets.bellGlow, SmokeAssets.noise); rcsPlumes = new RcsPlumeSystem(SmokeAssets.bellGlow, SmokeAssets.noise); }
        }

        void OnSceneLoadRequested(GameScenes s) { if (padSteam != null) padSteam.SetVgfx(true); }

        void OnDestroy()
        {
            GameEvents.OnCameraChange.Remove(OnCameraChange);
            GameEvents.onGameSceneLoadRequested.Remove(OnSceneLoadRequested);
            FossilBridge.ReleaseAll();
            if (cbCam00 != null && cb00 != null) cbCam00.RemoveCommandBuffer(ev00, cb00);
            if (cbCam01 != null && cb01 != null) cbCam01.RemoveCommandBuffer(ev01, cb01);
            if (cb00 != null) cb00.Release(); if (cb01 != null) cb01.Release();
            foreach (ComputeBuffer b in new[] { bSeg0, bSeg1, bSeg2, bSeg3, bSeg4, bChunk, bBlock, bSegB }) if (b != null) b.Release();
            if (mat != null) Destroy(mat);
            if (flames != null) { flames.Destroy(); flames = null; }
            if (bells != null) { bells.Destroy(); bells = null; }
            if (reflections != null) { reflections.Destroy(); reflections = null; }
            if (vapor != null) { vapor.Destroy(); vapor = null; }
            if (frost != null) { frost.Destroy(); frost = null; }
            if (dust != null) { dust.Destroy(); dust = null; }
            if (padSteam != null) { padSteam.Destroy(); padSteam = null; }
            if (cameraGuard != null) { cameraGuard.Destroy(); cameraGuard = null; }
            if (ivaShadows != null) { ivaShadows.Destroy(); ivaShadows = null; }
            if (rcsPlumes != null) { rcsPlumes.Destroy(); rcsPlumes = null; }
            if (tri != null) Destroy(tri);
        }

        void OnCameraChange(CameraManager.CameraMode m) { EnableDepth(); }

        float depthTimer;

        static void EnableDepth()
        {
            foreach (Camera c in Camera.allCameras)
            {
                if (c == null) continue;
                if (c.name == "Camera 00" || c.name == "Camera 01" || c == Camera.main)
                {
                    if ((c.depthTextureMode & DepthTextureMode.Depth) == 0) c.depthTextureMode |= DepthTextureMode.Depth;

                    if (!c.forceIntoRenderTexture) c.forceIntoRenderTexture = true;
                }
            }
        }

        void Alloc(int segs)
        {
            foreach (ComputeBuffer b in new[] { bSeg0, bSeg1, bSeg2, bSeg3, bSeg4, bChunk, bBlock, bSegB }) if (b != null) b.Release();
            bSeg0 = new ComputeBuffer(segs, 16); bSeg1 = new ComputeBuffer(segs, 16); bSeg2 = new ComputeBuffer(segs, 16); bSeg3 = new ComputeBuffer(segs, 16); bSeg4 = new ComputeBuffer(segs, 16); bSegB = new ComputeBuffer(segs, 16);
            bChunk = new ComputeBuffer(segs / 8, 16); bBlock = new ComputeBuffer(segs / 64, 16);
            aSeg0 = new Vector4[segs]; aSeg1 = new Vector4[segs]; aSeg2 = new Vector4[segs]; aSeg3 = new Vector4[segs]; aSeg4 = new Vector4[segs]; aSegB = new Vector4[segs]; aChunk = new Vector4[segs / 8]; aBlock = new Vector4[segs / 64];
        }

        readonly List<List<ModuleEngines>> emitGroups = new List<List<ModuleEngines>>(); readonly List<string> deadKeys = new List<string>(); RenderTargetIdentifier[] flameMrt;
        void UpdateEmitters(double ut, CelestialBody body)
        {
            foreach (SmokeEmitter e in emitters.Values) e.active = false;
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                if (v == null || v.packed || v.mainBody != body) continue;
                if (!CaelusParts.Vessel(v)) continue;

                var groups = emitGroups; groups.Clear();
                foreach (Part p in v.parts)
                    if (CaelusParts.Is(p))
                    foreach (PartModule pm in p.Modules)
                    {
                        var en = pm as ModuleEngines;
                        if (en == null || !en.EngineIgnited || en.flameout || en.finalThrust <= 0.5f || en.thrustTransforms.Count == 0) continue;
                        SmokeLook l = SmokeSettings.For(en); if (l == null) continue;
                        Vector3 pos = MeanPos(en);
                        List<ModuleEngines> g = null;
                        foreach (List<ModuleEngines> gg in groups)
                            if (SmokeSettings.For(gg[0]) == l && (MeanPos(gg[0]) - pos).magnitude < 12f + 0.05f * Mathf.Sqrt(en.finalThrust)) { g = gg; break; }
                        if (g == null) { g = new List<ModuleEngines>(); groups.Add(g); }
                        g.Add(en);
                    }
                foreach (List<ModuleEngines> g in groups)
                {
                    uint minId = uint.MaxValue; foreach (ModuleEngines en in g) minId = Math.Min(minId, en.part.flightID);
                    string key = v.id + ":" + minId;
                    SmokeEmitter e;
                    if (!emitters.TryGetValue(key, out e))
                    {
                        e = new SmokeEmitter { vessel = v, look = SmokeSettings.For(g[0]) };
                        e.lookIndex = Math.Min(7, SmokeSettings.looks.IndexOf(e.look));
                        emitters[key] = e;

                        var sbE = new System.Text.StringBuilder(); foreach (ModuleEngines en in g) sbE.Append(en.part.partInfo.name).Append('/').Append(en.engineID).Append(' ');
                        Debug.Log($"[CaelusSmoke] new trail emitter {key} look {e.look.name} at {v.altitude:F0} m: {sbE}");
                    }
                    e.engines = g; e.active = true; e.lastSeenUT = ut;
                    Emit(e, body, ut);
                }
            }

            var dead = deadKeys; dead.Clear();
            foreach (KeyValuePair<string, SmokeEmitter> kv in emitters)
                if (!kv.Value.active && ut - kv.Value.lastSeenUT > 0.5) { trail.Close(kv.Value); dead.Add(kv.Key); }
            foreach (string k in dead) emitters.Remove(k);

            denseVessels.Clear();
            if (CaelusUser.smoke) foreach (SmokeEmitter e in emitters.Values) if (e.look != null && e.look.opacity >= 1f) denseVessels.Add(e.vessel);
            FossilBridge.Sync(denseVessels);

            engineReach.Clear();
            foreach (SmokeEmitter e in emitters.Values) if (e.active) foreach (ModuleEngines en in e.engines) engineReach[en] = e.reachLen;
            Camera fc = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            disturb.Update(body, ut, engineReach, denseVessels, fc != null ? fc.transform.position : Vector3.zero);
            if (padSteam != null) padSteam.disturb = disturb;
        }
        readonly Dictionary<ModuleEngines, float> engineReach = new Dictionary<ModuleEngines, float>();
        readonly SmokeDisturb disturb = new SmokeDisturb();
        readonly HashSet<Vessel> denseVessels = new HashSet<Vessel>();

        static bool MeasurePlume(SmokeEmitter e, Vector3 c, Vector3 ax, float nozzleR, double ut)
        {
            if (ut - e.renderersUT > 2.0 || e.plumeRenderers.Count == 0)
            {
                e.plumeRenderers.Clear(); e.renderersUT = ut;
                var seen = new HashSet<Part>();
                foreach (ModuleEngines en in e.engines)
                {
                    if (!seen.Add(en.part)) continue;
                    foreach (Renderer r in en.part.GetComponentsInChildren<Renderer>(true))
                    {
                        Material m = r.sharedMaterial;
                        if (m == null || m.shader == null || !m.shader.name.StartsWith("Waterfall")) continue;
                        string nm = r.name.ToUpperInvariant();
                        if (nm.Contains("GLOW") || nm.Contains("CORE") || nm.Contains("LIGHT")) continue;
                        e.plumeRenderers.Add(r);
                    }
                }
            }
            float reach = 0, width = 0; int used = 0;
            Vector3 side = Vector3.Cross(ax, Mathf.Abs(ax.y) < 0.9f ? Vector3.up : Vector3.right).normalized, side2 = Vector3.Cross(ax, side);
            foreach (Renderer r in e.plumeRenderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                Bounds b = r.bounds;
                if (b.size.sqrMagnitude < 1f) continue;
                float far = 0, wid = 0;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                    Vector3 d = corner - c;
                    far = Mathf.Max(far, Vector3.Dot(d, ax));
                    wid = Mathf.Max(wid, Mathf.Max(Mathf.Abs(Vector3.Dot(d, side)), Mathf.Abs(Vector3.Dot(d, side2))));
                }
                if (far < nozzleR * 2f) continue;
                reach = Mathf.Max(reach, far); width = Mathf.Max(width, wid); used++;
            }
            if (used == 0) return false;

            e.measuredLen = reach * 0.8f; e.measuredRadius = width * 0.55f;
            return true;
        }

        static Vector3 MeanPos(ModuleEngines en) { Vector3 s = Vector3.zero; foreach (Transform t in en.thrustTransforms) s += t.position; return s / en.thrustTransforms.Count; }

        void Emit(SmokeEmitter e, CelestialBody body, double ut)
        {
            float F = 0, Fmax = 0; Vector3 c = Vector3.zero, ax = Vector3.zero; int nt = 0;
            foreach (ModuleEngines en in e.engines)
            {
                F += en.finalThrust; Fmax += en.maxThrust;
                float per = en.finalThrust / en.thrustTransforms.Count;
                foreach (Transform t in en.thrustTransforms) { c += t.position * per; ax += t.forward * per; nt++; }
            }
            if (F <= 0) return;
            c /= F; ax.Normalize();
            float spread = 0;
            foreach (ModuleEngines en in e.engines) foreach (Transform t in en.thrustTransforms) spread = Mathf.Max(spread, Vector3.ProjectOnPlane(t.position - c, ax).magnitude);
            float nozzleR = 0.02f * Mathf.Sqrt(F / Math.Max(1, nt));
            double airRel = e.vessel.atmDensity / Math.Max(1e-6, body.atmDensityASL);
            SmokeLook l = e.look;
            float flameLen = l.flameLengthK * Mathf.Sqrt(F) * l.flameLengthVsDensity.Evaluate((float)airRel);
            float clusterR = spread + nozzleR;
            float r0 = Mathf.Max(clusterR * 1.15f, l.radiusK * Mathf.Sqrt(F)) * Mathf.Pow((float)(1.0 / Math.Max(0.02, airRel)), 0.25f);

            float fLen, fR, fReach = 0, fJoinR = 0; e.ourFlame = false;
            if (flames != null && flames.TryGetSmoke(e.engines, out fLen, out fR, out fReach, out fJoinR))
            {
                e.ourFlame = true;
                flameLen = Mathf.Clamp(fLen, nozzleR * 4f, 3000f);
                r0 = Mathf.Clamp(fR, clusterR * 2.6f, clusterR * Mathf.Lerp(4f, 8f, Mathf.Clamp01(1f - (float)airRel * 3f)));
            }
            else if (MeasurePlume(e, c, ax, nozzleR, ut))
            {
                flameLen = Mathf.Clamp(e.measuredLen, nozzleR * 4f, 3000f);

                r0 = Mathf.Clamp(e.measuredRadius * 0.5f, clusterR * 2.6f, clusterR * Mathf.Lerp(4f, 8f, Mathf.Clamp01(1f - (float)airRel * 3f)));
            }
            e.nozzleWorld = c; e.axisWorld = ax; e.flameLen = flameLen; e.thrustKN = F;
            e.reachLen = e.ourFlame ? Mathf.Clamp(fReach, nozzleR * 4f, 3000f) : flameLen;
            e.tailWorld = c + ax * flameLen; e.nozzleRadius = clusterR; e.tailRadius = r0;
            if (e.ourFlame) e.tailRadius = Mathf.Clamp(fJoinR, clusterR * 1.1f, r0);
            float dens = Mathf.Clamp01(F / Mathf.Max(1, Fmax) * 1.2f) * l.altitudeFade.Evaluate((float)(e.vessel.altitude / 1000.0 / SmokeSettings.KmScale(e.vessel.mainBody)));
            if (airRel < 1e-4 || dens < 0.01f) { trail.Close(e); return; }
            Vector3d tail = (Vector3d)e.tailWorld;
            bool gap = e.chain < 0 || e.lastPushUT < 0 || ut - e.lastSeenUT > 0.5;
            double moved = e.lastPushUT < 0 ? 1e9 : (tail - body.GetWorldSurfacePosition(e.lastLat, e.lastLon, e.lastAlt)).magnitude;

            double gateDt = Math.Max(0.04, Math.Min(0.12, 1.5 * r0 / Math.Max(1.0, e.vessel.srfSpeed)));
            if (gap || (moved > Math.Max(1.2 * r0, 1.0) && ut - e.lastPushUT > gateDt) || ut - e.lastPushUT > 0.4)
            {

                float isp = 0; foreach (ModuleEngines en in e.engines) isp = Mathf.Max(isp, en.realIsp);
                Vector3d jetWorld = (Vector3d)(e.axisWorld * (e.look.jetSpeedK * isp * 9.80665f));
                trail.Push(body, tail, jetWorld, ut, e, r0, dens, gap);
                if (!anchored) { double alt; body.GetLatLonAlt(tail, out anchorLat, out anchorLon, out alt); anchored = true; }
            }
        }

        void UpdatePadCloud(double ut, CelestialBody body)
        {
            SmokeLook pl = SmokeSettings.Named("padcloud");
            if (pl == null) return;
            if ((outletScan -= Time.unscaledDeltaTime) < 0)
            {
                outletScan = 5f; outlets.Clear();
                foreach (Transform t in UnityEngine.Object.FindObjectsOfType<Transform>())
                    if (t.name.StartsWith("smokeEmitter") && t.gameObject.activeInHierarchy) outlets.Add(t);
            }
            Vessel v = FlightGlobals.ActiveVessel;
            float F = 0, len = 0; Vector3 c = Vector3.zero;
            foreach (SmokeEmitter e in emitters.Values) if (e.active && e.vessel == v) { F += e.thrustKN; len = Mathf.Max(len, e.reachLen); c = e.nozzleWorld; }
            foreach (Transform o in outlets)
            {
                SmokeEmitter pe;
                if (!padEmitters.TryGetValue(o, out pe)) { pe = new SmokeEmitter { look = pl, lookIndex = Math.Min(7, SmokeSettings.looks.IndexOf(pl)) }; padEmitters[o] = pe; }

                float reach = F > 0 && v != null ? Mathf.Clamp01(1.4f - (c - o.position).magnitude / Mathf.Max(30f, len * 1.2f)) : 0f;
                if (reach <= 0.02f) { if (pe.chain >= 0) trail.Close(pe); continue; }
                pe.vessel = v; pe.active = true; pe.lastSeenUT = ut;
                if (pe.lastPushUT >= 0 && ut - pe.lastPushUT < 0.12) continue;
                float sq = Mathf.Sqrt(F);
                float jet = Mathf.Clamp(0.45f * sq, 20f, 110f) * reach;
                float r0 = Mathf.Clamp(0.25f * sq, 12f, 60f);

                Vector3 up = (o.position - (Vector3)body.position).normalized;
                Vector3 dir = Vector3.ProjectOnPlane(o.position - c, up);
                dir = dir.sqrMagnitude > 1f ? dir.normalized : Vector3.ProjectOnPlane(o.forward, up).normalized;
                Vector3d vel = (Vector3d)(dir * jet + up * jet * 0.08f);
                trail.Push(body, (Vector3d)o.position, vel, ut, pe, r0, Mathf.Clamp01(reach * 1.1f), pe.chain < 0);
            }
        }

        static Vector4 SegBound(Vector4 a, Vector4 b) { if (a.w <= 0) return Vector4.zero; Vector3 c = ((Vector3)a + (Vector3)b) * 0.5f; return new Vector4(c.x, c.y, c.z, ((Vector3)b - (Vector3)a).magnitude * 0.5f + Mathf.Max(a.w, b.w)); }
        int AddSeg(ref int n, Vector3 a, float ra, Vector3 b, float rb, float da, float db, float heat, float lookSeed, Vector3 fa = default(Vector3), Vector3 fb = default(Vector3), float ua = 1f, float ub = 1f)
        {
            if (n >= aSeg0.Length) return n;
            aSeg0[n] = new Vector4(a.x, a.y, a.z, ra); aSeg1[n] = new Vector4(b.x, b.y, b.z, rb); aSeg2[n] = new Vector4(da, db, heat, lookSeed);
            aSeg3[n] = new Vector4(fa.x, fa.y, fa.z, ua); aSeg4[n] = new Vector4(fb.x, fb.y, fb.z, ub);
            return ++n;
        }

        double timAcc, timLoop; int timFrames, lastSegN; float timNext;
        readonly double[] timSec = new double[12];
        static readonly string[] timNames = { "flame", "bells", "refl", "rcs", "emitters", "padsteam", "trail", "segs+heads", "vapor", "dust", "frost", "upload+record" };
        long timT;
        void Lap(int i) { if (!SmokeSettings.timingLog) return; long now = System.Diagnostics.Stopwatch.GetTimestamp(); timSec[i] += now - timT; timT = now; }
        void LateUpdate()
        {
            if (!SmokeSettings.timingLog) { LateUpdateWork(); return; }
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            LateUpdateWork();
            timAcc += System.Diagnostics.Stopwatch.GetTimestamp() - t0; timFrames++;
            if (Time.realtimeSinceStartup > timNext)
            {
                timNext = Time.realtimeSinceStartup + 2f;
                double f = 1000.0 / System.Diagnostics.Stopwatch.Frequency / Math.Max(1, timFrames);
                string secs = ""; for (int i = 0; i < timSec.Length; i++) { secs += " " + timNames[i] + " " + (timSec[i] * f).ToString("F2"); timSec[i] = 0; }
                if (timFrames > 0) Debug.Log("[CaelusSmoke] timing: LateUpdate " + (timAcc * f).ToString("F2") + " ms/frame (segment loop " + (timLoop * f).ToString("F2") +
                    " ms;" + secs + ") over " + timFrames + " frames, segments " + lastSegN + ", chains " + trail.chains.Count + ", emitters " + emitters.Count +
                    ", pad puffs " + (padSteam != null ? padSteam.model.count : 0));
                timAcc = 0; timLoop = 0; timFrames = 0;
            }
        }

        void LateUpdateWork()
        {
            CelestialBody body = FlightGlobals.currentMainBody;
            ClearCbs();
            if (cameraGuard != null) cameraGuard.Update(Time.unscaledDeltaTime);
            if (actuators != null) actuators.Update(Time.unscaledDeltaTime);
            if (body == null || mat == null) return;
            double ut = Planetarium.GetUniversalTime();
            if ((depthTimer -= Time.unscaledDeltaTime) < 0) { depthTimer = 2f; EnableDepth(); }
            if (trailBody != body) { trail.chains.Clear(); emitters.Clear(); trailBody = body; anchored = false; }
            double dt = lastUT < 0 ? 0 : Math.Max(0, ut - lastUT); lastUT = ut;
            if (TimeWarp.CurrentRate > 4 && TimeWarp.WarpMode == TimeWarp.Modes.HIGH) { trail.Advance(body, ut, dt); return; }
            if (SmokeSettings.timingLog) timT = System.Diagnostics.Stopwatch.GetTimestamp();
            if (flames != null) flames.Update(ut, body);
            Lap(0);
            if (bells != null) bells.Update(Time.deltaTime);
            Lap(1);
            if (reflections != null) reflections.Update(Time.deltaTime);
            Lap(2);
            if (rcsPlumes != null) rcsPlumes.Update(Time.deltaTime);
            Lap(3);
            UpdateEmitters(ut, body);
            Lap(4);
            if (padSteam != null && CaelusUser.padSteam) padSteam.Update((float)dt, body);
            else if (padSteam != null) padSteam.SetVgfx(true);
            else if (SmokeSettings.padCloud && CaelusUser.padSteam) UpdatePadCloud(ut, body);
            bool padSteamLive = padSteam != null && padSteam.model.count > 0;
            Lap(5);
            trail.Advance(body, ut, dt);
            Lap(6);

            double airSL = Math.Max(1e-6, body.atmDensityASL);
            int n = 0;

            int colLimit = aSeg0.Length - 3 * emitters.Count - 16 - (padSteam != null && CaelusUser.padSteam ? padSteam.model.count : 0);
            Vector3 bmin = Vector3.one * float.MaxValue, bmax = Vector3.one * float.MinValue;
            long tLoop = SmokeSettings.timingLog ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            foreach (SmokeChain ch in trail.chains)
            {
                if (!CaelusUser.smoke) break;
                List<SmokeNode> ns = ch.nodes;
                Vector3 pPrev = Vector3.zero, fPrev = Vector3.zero; float rPrev = 0, dPrev = 0, hPrev = 0, uPrev = 0;
                for (int i = 0; i < ns.Count && n < colLimit; i++)
                {
                    SmokeNode nd = ns[i];
                    double age = ut - nd.born;
                    Vector3d pd = body.GetWorldSurfacePosition(nd.lat, nd.lon, nd.alt);
                    Vector3 p = (Vector3)pd;
                    Vector3 f = (Vector3)(pd - body.GetWorldSurfacePosition(nd.lat0, nd.lon0, nd.alt0));
                    float u = 1f - Mathf.Exp(-(float)age / 0.7f);
                    double air = Math.Max(0.0, body.GetDensity(body.GetPressure(nd.alt), body.GetTemperature(nd.alt))) / airSL;
                    float r = SmokeTrail.Radius(ref nd, age, air);
                    float d = SmokeTrail.Density(ref nd, age, r);
                    if (nd.alt < nd.ground + 12.0) d = 0f;

                    if (padSteamLive) d *= Mathf.Clamp01((float)((nd.alt0 - nd.ground - 15.0) / 30.0));
                    float h = 0.25f * Mathf.Exp(-(float)age / 0.12f) * Mathf.Clamp01((float)(air * 3.0));

                    float grow = 1f - Mathf.Exp(-(float)age / 0.5f);
                    float core = Mathf.Lerp(1f, 0.8f, grow);
                    SmokeLook lk = SmokeSettings.looks[nd.look];
                    float hiMix = Mathf.Clamp(Mathf.InverseLerp(lk.highAltKmLo, lk.highAltKmHi, (float)(nd.alt / 1000.0 / SmokeSettings.kmScaleNow)) * lk.highMix, 0f, 0.99f);

                    Vector3 fa = fPrev, fb = f;
                    if (i > 0)
                    {
                        float dfm = (f - fPrev).magnitude, rr = Mathf.Max(1f, Mathf.Min(rPrev, r));
                        float kk = Mathf.Clamp01((dfm - 0.3f * rr) / (0.5f * rr));
                        if (kk > 0f) { Vector3 fm = 0.5f * (fPrev + f); fa = Vector3.Lerp(fPrev, fm, kk); fb = Vector3.Lerp(f, fm, kk); }
                    }
                    if (i > 0) AddSeg(ref n, pPrev, rPrev * Mathf.Lerp(1f, 0.8f, 1f - Mathf.Exp(-(float)(ut - ns[i - 1].born) / 0.8f)), p, r * core, dPrev, d, Mathf.Max(h, hPrev), nd.look + hiMix, fa, fb, uPrev, u);

                    float spacing = i > 0 ? (p - pPrev).magnitude : r;
                    bool puff = ((nd.seed * 311.71f) % 1f) < Mathf.Clamp01(spacing / Mathf.Max(1f, 0.25f * r));

                    int nPuff = puff ? Mathf.Clamp(Mathf.RoundToInt(spacing / Mathf.Max(1f, 0.8f * r)), 1, 2) : 0;
                    for (int pk = 0; pk < nPuff && grow > 0.04f && d > 1e-4f; pk++)
                    {
                        Vector3 ax = i > 0 ? (p - pPrev) : (i + 1 < ns.Count ? ((Vector3)body.GetWorldSurfacePosition(ns[i + 1].lat, ns[i + 1].lon, ns[i + 1].alt) - p) : Vector3.up);
                        if (ax.sqrMagnitude < 1e-6f) ax = Vector3.up;
                        ax.Normalize();
                        Vector3 u0 = Vector3.Cross(ax, Mathf.Abs(ax.y) < 0.9f ? Vector3.up : Vector3.right).normalized, v0 = Vector3.Cross(ax, u0);
                        float sk = (nd.seed + pk * 0.618034f) % 1f;
                        float s1 = sk, s2 = (sk * 13.71f) % 1f, s3 = (sk * 71.37f) % 1f, s4 = (sk * 37.13f) % 1f;
                        Vector3 pc = (pk == 0 || i == 0) ? p : Vector3.Lerp(pPrev, p, (pk + s4 * 0.8f) / nPuff);
                        float ang = s1 * 6.2832f * 7f;
                        Vector3 off = (u0 * Mathf.Cos(ang) + v0 * Mathf.Sin(ang)) * (r * 0.45f * grow * (0.4f + 0.6f * s3)) + ax * (r * 0.35f * (s2 - 0.5f));
                        float pr = r * (0.35f + 0.3f * s2) * (0.4f + 0.6f * grow);
                        Vector3 fc = (pk == 0 || i == 0) ? fb : Vector3.Lerp(fa, fb, (pk + s4 * 0.8f) / nPuff);
                        AddSeg(ref n, pc + off, pr, pc + off, pr, d * 1.15f, d * 1.15f, 0f, nd.look + hiMix, fc, fc, u, u);
                    }
                    bmin = Vector3.Min(bmin, p - Vector3.one * r * 1.55f); bmax = Vector3.Max(bmax, p + Vector3.one * r * 1.55f);
                    pPrev = p; rPrev = r; dPrev = d; hPrev = h; fPrev = f; uPrev = u;
                }
            }
            if (SmokeSettings.timingLog) timLoop += System.Diagnostics.Stopwatch.GetTimestamp() - tLoop;
            int nf = 0;
            float flameDayScale = 1f;
            {
                Vector3 cp0 = FlightCamera.fetch != null && FlightCamera.fetch.mainCamera != null ? FlightCamera.fetch.mainCamera.transform.position : Vector3.zero;
                Vector3 sd0 = ((Vector3)(Planetarium.fetch.Sun.position - (Vector3d)cp0)).normalized, up0 = ((Vector3)((Vector3d)cp0 - body.position)).normalized;
                flameDayScale = Mathf.Lerp(1f, 0.75f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.1f, 0.25f, Vector3.Dot(up0, sd0))));
            }
            foreach (SmokeEmitter e in emitters.Values)
            {
                if (!e.active || e.chain < 0) continue;
                SmokeChain ch = null; foreach (SmokeChain c in trail.chains) if (c.id == e.chain) { ch = c; break; }
                if (ch == null || ch.nodes.Count == 0) continue;
                SmokeNode last = ch.nodes[ch.nodes.Count - 1];
                double ageL = ut - last.born;
                Vector3d pld = body.GetWorldSurfacePosition(last.lat, last.lon, last.alt);
                Vector3 pl = (Vector3)pld, fl = (Vector3)(pld - body.GetWorldSurfacePosition(last.lat0, last.lon0, last.alt0));
                float rl = SmokeTrail.Radius(ref last, ageL, e.vessel.atmDensity / airSL), dl = SmokeTrail.Density(ref last, ageL, rl);

                Vector3 s0 = e.nozzleWorld + e.axisWorld * e.flameLen * (e.ourFlame ? e.look.flameStart : e.look.startFraction);
                Vector3 sm = Vector3.Lerp(s0, e.tailWorld, 0.55f);
                float rh = e.ourFlame ? Mathf.Max(e.nozzleRadius * 1.1f, e.tailRadius * 0.85f) : e.nozzleRadius * 1.1f, dm = e.ourFlame ? e.look.flameMidDensity : 0.6f;
                float hAir = Mathf.Clamp01((float)(e.vessel.atmDensity / airSL * 3.0));
                float hiHead = Mathf.Clamp(Mathf.InverseLerp(e.look.highAltKmLo, e.look.highAltKmHi, (float)(e.vessel.altitude / 1000.0 / SmokeSettings.KmScale(e.vessel.mainBody))) * e.look.highMix, 0f, 0.99f);

                float headAir = Mathf.Lerp(0.3f, 1f, Mathf.Clamp01((float)(e.vessel.atmDensity / airSL) * 3f));
                dm *= headAir;
                if (CaelusUser.smoke)
                {
                    AddSeg(ref n, s0, rh, sm, Mathf.Lerp(rh, e.tailRadius, 0.7f), 0f, last.dens0 * dm, 0.35f * hAir, e.lookIndex + hiHead);
                    AddSeg(ref n, sm, Mathf.Lerp(rh, e.tailRadius, 0.7f), e.tailWorld, e.tailRadius, last.dens0 * dm, last.dens0 * 0.95f * headAir, 0.2f * hAir, e.lookIndex + hiHead);
                    AddSeg(ref n, e.tailWorld, e.tailRadius, pl, rl, last.dens0 * 0.95f * headAir, Mathf.Max(dl, last.dens0 * 0.8f * headAir), 0.1f * hAir, e.lookIndex + hiHead, Vector3.zero, fl, 0f, 1f - Mathf.Exp(-(float)ageL / 0.7f));
                    bmin = Vector3.Min(bmin, Vector3.Min(s0, e.tailWorld) - Vector3.one * e.tailRadius);
                    bmax = Vector3.Max(bmax, Vector3.Max(s0, e.tailWorld) + Vector3.one * e.tailRadius);
                }
                if (nf < 4)
                {
                    flamePos[nf] = (Vector4)(e.nozzleWorld + e.axisWorld * e.reachLen * 0.7f); float nightK = Mathf.InverseLerp(0.75f, 1f, flameDayScale);
                    flamePos[nf].w = Mathf.Min(e.reachLen * Mathf.Lerp(1.3f, 2.0f, nightK), Mathf.Lerp(300f, 450f, nightK));
                    float I = SmokeSettings.flameLight * Mathf.Clamp(Mathf.Sqrt(e.thrustKN) / 200f, 0.1f, 1.2f) * flameDayScale * Mathf.Lerp(1f, 1.2f, nightK);
                    flameCol[nf] = new Vector4(1.0f * I, 0.72f * I, 0.40f * I, 1);
                    nf++;
                }
            }

            if (padSteam != null && CaelusUser.padSteam && padSteam.model.count > 0 && padLook >= 0)
            {
                while ((n & 7) != 0 && n < aSeg0.Length) { aSeg0[n] = aSeg1[n] = aSeg2[n] = aSeg3[n] = aSeg4[n] = Vector4.zero; n++; }
                SteamPuff[] pf = padSteam.model.puffs; int[] ord = padSteam.model.RenderOrder();
                for (int k = 0; k < padSteam.model.count; k++)
                {
                    SteamPuff q = pf[ord[k]];
                    float d = PadSteamModel.Density(ref q); if (d < 0.002f) continue;
                    Vector3 p = padSteam.ToWorld(q.p), f = padSteam.ToWorldDir(q.p - q.p0);
                    float u = 1f - Mathf.Exp(-q.age / 0.7f);
                    AddSeg(ref n, p, q.r, p, q.r, d, d, PadSteamModel.Heat(ref q), padLook + PadSteamModel.Tint(ref q), f, f, u, u);
                    bmin = Vector3.Min(bmin, p - Vector3.one * q.r * 1.55f); bmax = Vector3.Max(bmax, p + Vector3.one * q.r * 1.55f);
                }
            }

            if (padSteam != null && CaelusUser.padSteam && padSteam.model.count > 0)
            {
                float nightK = Mathf.InverseLerp(0.75f, 1f, flameDayScale);
                float k = SmokeSettings.flameLight * Mathf.Clamp(Mathf.Sqrt(padSteam.lastThrustKN) / 200f, 0.1f, 1.2f) * flameDayScale * Mathf.Lerp(1f, 3.5f, nightK);
                nf = padSteam.AddGlows(flamePos, flameCol, nf, k, nightK);
            }

            Shader.SetGlobalVectorArray("_CaelusFlamePos", flamePos); Shader.SetGlobalVectorArray("_CaelusFlameCol", flameCol); Shader.SetGlobalFloat("_CaelusFlameCount", nf);
            Lap(7);
            bool drawFlame = CaelusUser.flame && flames != null && flames.clusterCount > 0;
            bool drawVapor = CaelusUser.vapour && vapor != null && CaelusParts.ActiveVessel() && vapor.Prepare(FlightGlobals.ActiveVessel, ut);
            Lap(8);
            bool drawDust = dust != null && dust.Prepare(ut, body, Time.deltaTime);
            Lap(9);
            bool drawFrost = frost != null && frost.Update(FlightGlobals.ActiveVessel, ut, vapor, CaelusUser.frost);
            Lap(10);
            if (n == 0 && !drawFlame && !drawVapor && !drawDust && !drawFrost) return;
            int nPad = (n + 63) / 64 * 64;
            for (int i = n; i < nPad && i < aSeg0.Length; i++) { aSeg0[i] = Vector4.zero; aSeg1[i] = Vector4.zero; aSeg2[i] = Vector4.zero; aSeg3[i] = Vector4.zero; aSeg4[i] = Vector4.zero; }
            nPad = Math.Min(nPad, aSeg0.Length);
            int nChunk = nPad / 8, nBlock = nPad / 64;
            for (int c = 0; c < nChunk; c++)
            {
                Vector3 mn = Vector3.one * float.MaxValue, mx = Vector3.one * float.MinValue; bool any = false;
                for (int k = 0; k < 8; k++)
                {
                    Vector4 a = aSeg0[c * 8 + k], b = aSeg1[c * 8 + k]; aSegB[c * 8 + k] = SegBound(a, b); if (a.w <= 0) continue; any = true;

                    mn = Vector3.Min(mn, Vector3.Min((Vector3)a - Vector3.one * a.w * 1.55f, (Vector3)b - Vector3.one * b.w * 1.55f));
                    mx = Vector3.Max(mx, Vector3.Max((Vector3)a + Vector3.one * a.w * 1.55f, (Vector3)b + Vector3.one * b.w * 1.55f));
                }
                aChunk[c] = any ? new Vector4((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, (mn.z + mx.z) / 2, (mx - mn).magnitude / 2) : Vector4.zero;

                if (any) { bmin = Vector3.Min(bmin, mn); bmax = Vector3.Max(bmax, mx); }
            }
            for (int b = 0; b < nBlock; b++)
            {
                Vector3 mn = Vector3.one * float.MaxValue, mx = Vector3.one * float.MinValue; bool any = false;
                for (int k = 0; k < 8; k++)
                {
                    Vector4 s = aChunk[b * 8 + k]; if (s.w <= 0) continue; any = true;
                    mn = Vector3.Min(mn, (Vector3)s - Vector3.one * s.w); mx = Vector3.Max(mx, (Vector3)s + Vector3.one * s.w);
                }
                aBlock[b] = any ? new Vector4((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, (mn.z + mx.z) / 2, (mx - mn).magnitude / 2) : Vector4.zero;
            }
            if (n > 0)
            {
                bSeg0.SetData(aSeg0, 0, 0, nPad); bSeg1.SetData(aSeg1, 0, 0, nPad); bSeg2.SetData(aSeg2, 0, 0, nPad); bSeg3.SetData(aSeg3, 0, 0, nPad); bSeg4.SetData(aSeg4, 0, 0, nPad); bSegB.SetData(aSegB, 0, 0, nPad);
                bChunk.SetData(aChunk, 0, 0, nChunk); bBlock.SetData(aBlock, 0, 0, nBlock);
            }

            Vector3 camPos = FlightCamera.fetch != null && FlightCamera.fetch.mainCamera != null ? FlightCamera.fetch.mainCamera.transform.position : Vector3.zero;
            Vector3 sunDir = ((Vector3)(Planetarium.fetch.Sun.position - (Vector3d)camPos)).normalized;
            Vector3 up = ((Vector3)((Vector3d)camPos - body.position)).normalized;
            float elev = Vector3.Dot(up, sunDir);
            float day = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.12f, 0.25f, elev));
            Color sunC = Color.Lerp(new Color(1.0f, 0.50f, 0.26f), new Color(1.0f, 0.96f, 0.90f), Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.25f, elev))) * SmokeSettings.sunIntensity;
            float dayAmb = day;
            if (SmokeSettings.sunsetLight > 0f)
            {

                sunC *= Mathf.Lerp(1f, Mathf.Lerp(0.3f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.0f, 0.2f, elev))), SmokeSettings.sunsetLight);
                dayAmb = Mathf.Lerp(day, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.17f, 0.10f, elev)), SmokeSettings.sunsetLight);
            }
            Vector3 sky = new Vector3(0.44f, 0.50f, 0.60f) * SmokeSettings.ambient * dayAmb + Vector3.one * 0.015f;
            Vector3 ground = new Vector3(0.30f, 0.27f, 0.22f) * SmokeSettings.ambient * day * 0.6f + Vector3.one * 0.01f;
            if (flames != null) flames.mat.SetVector("_FlameAmbient", sky);
            if (drawVapor) vapor.SetLight(sunDir, new Vector3(sunC.r, sunC.g, sunC.b), sky);
            if (drawFrost) frost.SetLight(sunDir, new Vector3(sunC.r, sunC.g, sunC.b), sky);
            if (drawDust) dust.SetLight(sunDir, SmokeSettings.sunIntensity * 1.6f);

            Vector3d anchorW = anchored ? body.GetWorldSurfacePosition(anchorLat, anchorLon, 0) : body.position;
            mpb.Clear();

            mat.SetBuffer("_Seg0", bSeg0); mat.SetBuffer("_Seg1", bSeg1); mat.SetBuffer("_Seg2", bSeg2); mat.SetBuffer("_Seg3", bSeg3); mat.SetBuffer("_Seg4", bSeg4); mat.SetBuffer("_SegB", bSegB); mat.SetBuffer("_Chunk", bChunk); mat.SetBuffer("_Block", bBlock);
            mpb.SetInt("_BlockCount", nBlock);
            mpb.SetInt("_CutCount", disturb.count); mpb.SetVectorArray("_CutA", disturb.A); mpb.SetVectorArray("_CutB", disturb.B); mpb.SetVectorArray("_CutS", disturb.S);
            mpb.SetVector("_BoxMin", bmin); mpb.SetVector("_BoxMax", bmax);
            mpb.SetTexture("_Noise", SmokeAssets.noise);
            mpb.SetFloat("_NoiseScale", SmokeSettings.noiseScale); mpb.SetFloat("_Erosion", SmokeSettings.erosion); mpb.SetFloat("_Sigma", sigma);
            mpb.SetFloat("_Time01", (float)(ut % 100000.0));
            mpb.SetVector("_NoiseOrigin", (Vector3)anchorW);
            {
                Vector3 upA = ((Vector3)(anchorW - body.position)).normalized;
                Vector3 northA = Vector3.ProjectOnPlane(body.transform.up, upA).normalized, eastA = Vector3.Cross(upA, northA);
                float hA = SmokeSettings.windHeadingDeg * Mathf.Deg2Rad;
                Vector3 windW = (eastA * Mathf.Sin(hA) + northA * Mathf.Cos(hA)) * SmokeSettings.windSpeed;
                mpb.SetVector("_WindBody", Quaternion.Inverse(body.transform.rotation) * windW);
            }

            float shK = padSteam != null ? padSteam.set.shadow : 0.6f;
            padShadow = n > 0 && shK > 0f && day > 0.05f && ((padSteam != null && CaelusUser.padSteam && padSteam.model.count > 0) || SmokeSettings.columnShadow > 0f);
            mpb.SetFloat("_ShadowK", shK * day); mpb.SetFloat("_ShadowColK", SmokeSettings.columnShadow);
            mpb.SetMatrix("_WorldToBodyRot", Matrix4x4.Rotate(Quaternion.Inverse(body.transform.rotation)));
            mpb.SetFloat("_Frame", Time.frameCount % 1024); mpb.SetInt("_Steps", SmokeSettings.marchSteps); mpb.SetInt("_LightSteps", SmokeSettings.lightSteps);
            mpb.SetInt("_EdgeRefine", SmokeSettings.edgeRefine ? 1 : 0);
            mpb.SetInt("_Debug", SmokeSettings.debugView);
            mpb.SetVector("_SunDir", sunDir); mpb.SetVector("_SunColor", new Vector3(sunC.r, sunC.g, sunC.b));
            mpb.SetVector("_AmbientSky", sky); mpb.SetVector("_AmbientGround", ground);
            mpb.SetVector("_BodyCenter", (Vector3)body.position); mpb.SetFloat("_BodyRadius", (float)body.Radius);
            mpb.SetFloat("_PhaseG", SmokeSettings.phaseG); mpb.SetFloat("_PhaseBack", SmokeSettings.phaseBackG);
            mat.SetVectorArray("_LookColor", lookCol); mat.SetVectorArray("_LookSoot", lookSoot); mat.SetVectorArray("_LookHigh", lookHigh);
            mat.SetVectorArray("_FlamePos", flamePos); mat.SetVectorArray("_FlameCol", flameCol); mpb.SetInt("_FlameCount", nf);

            Camera cam00 = null, cam01 = null;
            if (FlightCamera.fetch != null && FlightCamera.fetch.cameras != null)
                foreach (Camera c in FlightCamera.fetch.cameras)
                {
                    if (c == null || !c.enabled) continue;
                    if (c.name == "Camera 00") cam00 = c; else if (c.name == "Camera 01") cam01 = c;
                }
            PickSmokeDiv(cam00 != null ? cam00 : cam01, n);
            lastSegN = n;

            ScanForeignEngines();
            RecordCam(cam00, ref cb00, ref cbCam00, ref ev00, mpb, cam00 != null ? cam00.nearClipPlane : 0f, cam00 != null ? cam00.farClipPlane : 0f, n > 0, drawFlame, drawVapor, drawDust, drawFrost);
            RecordCam(cam01, ref cb01, ref cbCam01, ref ev01, mpb, cam01 == null ? 0f : (cam00 != null ? Mathf.Max(cam01.nearClipPlane, cam00.farClipPlane) : cam01.nearClipPlane), cam01 != null ? cam01.farClipPlane : 0f, n > 0, drawFlame, drawVapor, drawDust, drawFrost);
            Lap(11);
        }
    }
}
