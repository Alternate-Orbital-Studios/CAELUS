using System;
using System.Collections.Generic;
using UnityEngine;

namespace RomaAeterna
{
    public sealed class RomaAeternaNozzleHeat : PartModule
    {
        [KSPField] public string engineID = "";
        [KSPField] public string nozzleMesh = "";
        [KSPField] public string emissionMap = "";
        [KSPField] public float warmupSeconds = 24f;
        [KSPField] public float cooldownSeconds = 42f;
        [KSPField] public float peakVisualKelvin = 1200f;
        [KSPField] public float peakEmission = .16f;
        [KSPField(isPersistant = true)] public double visualHeat;
        [KSPField(isPersistant = true)] public double lastVisualUT = -1;

        public double VisualKelvin { get { return 293.15 + (peakVisualKelvin - 293.15) * visualHeat; } }
        public float ActualOutputFraction { get; private set; }
        public float EmissionStrength { get; private set; }
        public Color EmissionColour { get; private set; }
        public int BoundMaterialCount { get { return materials.Count; } }
        public string BoundShader { get { return materials.Count == 0 ? "" : materials[0].shader.name; } }
        public bool MapBound { get; private set; }
        private ModuleEngines engine;
        private readonly List<Material> materials = new List<Material>();
        private int bindFrames;
        private bool bindErrorReported;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            foreach (PartModule module in part.Modules)
            {
                ModuleEngines candidate = module as ModuleEngines;
                if (candidate != null && candidate.engineID == engineID) { engine = candidate; break; }
            }
            if (engine == null) Debug.LogError("[RomaAeterna] Nozzle heat missing engine " + engineID);
            visualHeat = Math.Max(0, Math.Min(1, visualHeat));
            double now = Planetarium.GetUniversalTime();

            if (lastVisualUT >= 0 && now > lastVisualUT)
                visualHeat *= Math.Exp(-(now - lastVisualUT) / Math.Max(1, cooldownSeconds));
            lastVisualUT = now;
            if (HighLogic.LoadedSceneIsEditor) visualHeat = 0;
        }

        private bool Owns(Component component)
        {
            for (Transform t = component.transform; t != null; t = t.parent)
            {
                Part owner = t.GetComponent<Part>();
                if (owner != null) return owner == part;
            }
            return false;
        }

        private void BindMaterial()
        {
            if (materials.Count > 0 || part == null) return;
            Texture2D mask = GameDatabase.Instance.GetTexture(emissionMap, false);
            if (mask == null) return;
            foreach (Renderer renderer in part.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name != nozzleMesh || !Owns(renderer)) continue;
                Material[] shared = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < shared.Length; i++)
                {
                    Material source = shared[i];
                    if (source == null || source.shader == null || source.shader.name != "TU/Metallic" ||
                        !source.HasProperty("_Emissive") || !source.HasProperty("_EmissiveColor")) continue;

                    Material owned = new Material(source);
                    owned.name = source.name + "_AquilaNozzleHeat";
                    owned.SetTexture("_Emissive", mask);
                    owned.SetColor("_EmissiveColor", new Color(0, 0, 0, 1));
                    shared[i] = owned; materials.Add(owned); changed = true;
                }
                if (changed) renderer.sharedMaterials = shared;
            }
            MapBound = materials.Count > 0;
            if (MapBound) Debug.Log("[RomaAeterna] Localized nozzle afterglow bound: " + engineID +
                ", mesh=" + nozzleMesh + ", materials=" + materials.Count + ", shader=" + BoundShader);
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null) return;
            double now = Planetarium.GetUniversalTime();
            double dt = lastVisualUT < 0 ? 0 : Math.Max(0, now - lastVisualUT);
            lastVisualUT = now;
            ActualOutputFraction = engine != null && engine.EngineIgnited && !engine.flameout && !vessel.packed &&
                engine.maxThrust > 0 ? Mathf.Clamp01(engine.finalThrust / engine.maxThrust) : 0;

            double target = ActualOutputFraction > 0 ? Math.Pow(ActualOutputFraction, .25) : 0;
            double tau = target > visualHeat ? warmupSeconds : cooldownSeconds;
            if (dt > 0) visualHeat = target + (visualHeat - target) * Math.Exp(-dt / Math.Max(1, tau));
            visualHeat = Math.Max(0, Math.Min(1, visualHeat));
        }

        public void LateUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight && !HighLogic.LoadedSceneIsEditor) return;
            if (materials.Count == 0)
            {

                if (++bindFrames % 15 == 1) BindMaterial();
                if (bindFrames > 900 && !MapBound && !bindErrorReported)
                {
                    bindErrorReported = true;
                    Debug.LogError("[RomaAeterna] Nozzle afterglow could not bind approved TU material: " + engineID);
                }
            }
            double temperature = HighLogic.LoadedSceneIsEditor ? 293.15 : VisualKelvin;

            double radiance = temperature > 750 ?
                (Math.Exp(22135.0 / peakVisualKelvin) - 1) / (Math.Exp(22135.0 / temperature) - 1) : 0;
            EmissionStrength = (float)Math.Min(peakEmission, Math.Max(0, radiance * peakEmission));
            float warmth = Mathf.Clamp01((float)((temperature - 800) / 500));
            EmissionColour = new Color(EmissionStrength, EmissionStrength * (.025f + .085f * warmth),
                EmissionStrength * .002f * warmth, 1);
            foreach (Material material in materials)
                if (material != null) material.SetColor("_EmissiveColor", EmissionColour);
        }

        public void OnDestroy()
        {
            foreach (Material material in materials) if (material != null) Destroy(material);
            materials.Clear();
        }
    }
}
