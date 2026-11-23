using UnityEngine;
using UnityEngine.Rendering;

namespace CaelusSmoke
{
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class CaelusEditorLook : MonoBehaviour
    {
        ReflectionSystem foil; RenderTexture cube; ReflectionProbe probe; Camera cam; GameObject root;
        int face, frame, size = 128, framesPerFace = 3; float scan; bool logged, layersLogged;
        bool ours;

        void Start()
        {
            bool on = true;
            foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("CAELUS_EDITOR_LOOK"))
            { n.TryGetValue("enabled", ref on); n.TryGetValue("size", ref size); n.TryGetValue("framesPerFace", ref framesPerFace); }
            if (!on || !SmokeAssets.Load()) { enabled = false; return; }
            foil = new ReflectionSystem(SmokeAssets.foil, SmokeAssets.glass);
            root = new GameObject("CaelusEditorReflections");
            cube = new RenderTexture(size, size, 16, RenderTextureFormat.ARGBHalf) { dimension = TextureDimension.Cube, useMipMap = true, autoGenerateMips = true, name = "CaelusEditorCube" };
            cube.Create();
            probe = root.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Custom; probe.customBakedTexture = cube;
            probe.size = new Vector3(2000f, 2000f, 2000f); probe.importance = 100; probe.intensity = 1f; probe.boxProjection = false;
            var g = new GameObject("CaelusEditorReflCam"); g.transform.SetParent(root.transform, false);
            cam = g.AddComponent<Camera>(); cam.enabled = false; cam.nearClipPlane = 0.5f; cam.farClipPlane = 3000f;
            cam.clearFlags = CameraClearFlags.Skybox;
            Debug.Log("[CaelusSmoke] editor look: foil/glass swap + editor reflection cube " + size + " px");
        }

        void LateUpdate()
        {
            if (foil == null || EditorLogic.fetch == null) return;
            ShipConstruct ship = EditorLogic.fetch.ship;
            Part root0 = EditorLogic.RootPart;
            if ((scan -= Time.unscaledDeltaTime) < 0f)
            {
                scan = 0.5f;
                ours = false;
                if (ship != null) foreach (Part p in ship.parts) { foil.ApplyFoil(p); if (!ours && CaelusParts.Is(p)) ours = true; }
                Part sel = EditorLogic.SelectedPart;
                if (sel != null) foreach (Part p in sel.GetComponentsInChildren<Part>(true)) { foil.ApplyFoil(p); if (!ours && CaelusParts.Is(p)) ours = true; }
                foil.ForgetDead();
                if (probe.enabled != ours) probe.enabled = ours;
            }
            if (!ours) return;

            Vector3 c;
            if (ship != null && ship.parts.Count > 0)
            {
                Bounds b = new Bounds(ship.parts[0].transform.position, Vector3.zero);
                foreach (Part p in ship.parts) if (p != null) b.Encapsulate(p.transform.position);
                c = b.center;
            }
            else c = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            root.transform.position = c;
            int partLayer = root0 != null ? root0.gameObject.layer : 0;
            cam.cullingMask = ~((1 << partLayer) | (1 << 5) | (1 << 2) | (1 << 12) | (1 << 16) | (1 << 20) | (1 << 31));
            if (!layersLogged && ship != null && ship.parts.Count > 0)
            {
                layersLogged = true;
                var count = new int[32];
                foreach (Renderer r in Object.FindObjectsOfType<Renderer>()) if (r != null && r.enabled) count[r.gameObject.layer]++;
                string s = ""; for (int i = 0; i < 32; i++) if (count[i] > 0) s += " L" + i + ":" + count[i];
                Debug.Log("[CaelusSmoke] editor look: part layer " + partLayer + ", renderers by layer" + s);
            }
            cam.transform.position = c;
            if (++frame >= framesPerFace)
            {
                frame = 0;
                var shadowsWas = QualitySettings.shadows; QualitySettings.shadows = ShadowQuality.Disable;
                try { cam.RenderToCubemap(cube, 1 << face); }
                finally { QualitySettings.shadows = shadowsWas; }
                face = (face + 1) % 6;
                if (!logged && face == 0) { logged = true; Debug.Log("[CaelusSmoke] editor look: first editor cube done at " + c); }
            }
            if (probe.size.x != 2000f) probe.size = new Vector3(2000f, 2000f, 2000f);
        }

        void OnDestroy()
        {
            if (foil != null) foil.Destroy();
            if (cube != null) { cube.Release(); Destroy(cube); }
            if (root != null) Destroy(root);
        }
    }

    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class CaelusIconFix : MonoBehaviour
    {
        const float IconMetal = 0.15f, IconSmooth = 0.45f;

        static readonly string[] HiddenInIcon = { "CLD_Char", "GLOW_", "CLD_Canopy", "CLD_Chute", "CLD_Lines", "CLD_Drogue" };
        void Start()
        {
            int parts = 0, mats = 0, hidden = 0;
            try
            {
                foreach (AvailablePart ap in PartLoader.LoadedPartsList)
                {
                    if (ap == null || ap.iconPrefab == null || ap.name == null || !ap.name.StartsWith("Caelus")) continue;
                    bool any = false;
                    foreach (Renderer r in ap.iconPrefab.GetComponentsInChildren<Renderer>(true))
                    {
                        bool hide = false; foreach (string h in HiddenInIcon) if (r.name.StartsWith(h)) { hide = true; break; }
                        if (hide) { if (r.enabled) { r.enabled = false; hidden++; any = true; } continue; }
                        Material[] ms = r.sharedMaterials; bool ch = false;
                        for (int i = 0; i < ms.Length; i++)
                        {
                            Material m = ms[i];
                            if (m == null || m.shader == null || m.shader.name.IndexOf("Metallic", System.StringComparison.Ordinal) < 0 || !m.HasProperty("_Metal")) continue;
                            if (m.GetFloat("_Metal") <= IconMetal) continue;
                            Material c = new Material(m); c.SetFloat("_Metal", IconMetal);
                            if (c.HasProperty("_Smoothness")) c.SetFloat("_Smoothness", Mathf.Min(c.GetFloat("_Smoothness"), IconSmooth));
                            ms[i] = c; ch = true; mats++;
                        }
                        if (ch) { r.sharedMaterials = ms; any = true; }
                    }
                    if (any) parts++;
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[CaelusSmoke] icon fix failed: " + e.Message); }
            Debug.Log("[CaelusSmoke] part icons: metal turned down on " + mats + " materials, " + hidden + " hidden overlays, " + parts + " CAELUS parts");
        }
    }
}
