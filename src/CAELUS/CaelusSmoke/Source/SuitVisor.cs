using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace CaelusSmoke
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class SuitVisor : MonoBehaviour
    {
        readonly List<string> combos = new List<string>();
        bool allKerbals = false, enabledCfg = false, logged = false;
        Color colour = new Color(0.86f, 0.88f, 0.92f, 1f);
        float smoothness = 0.97f, opacity = 0.96f, nextScan;
        readonly HashSet<int> done = new HashSet<int>();
        static readonly FieldInfo visorMatField = typeof(KerbalEVA).GetField("visorMaterial", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        static readonly FieldInfo visorRendField = typeof(KerbalEVA).GetField("VisorRenderer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        static readonly PropertyInfo visorRendProp = typeof(KerbalEVA).GetProperty("VisorRenderer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        void Start()
        {
            foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("CAELUS_VISOR"))
            {
                enabledCfg = true;
                foreach (string s in n.GetValues("suitCombo")) if (!string.IsNullOrEmpty(s.Trim())) combos.Add(s.Trim());
                n.TryGetValue("allKerbals", ref allKerbals); n.TryGetValue("colour", ref colour);
                n.TryGetValue("smoothness", ref smoothness); n.TryGetValue("opacity", ref opacity);
            }
            if (enabledCfg && !SmokeAssets.Load() && SmokeAssets.visor == null) enabledCfg = false;
        }

        void Update()
        {
            if (!enabledCfg || SmokeAssets.visor == null || Time.time < nextScan) return;
            nextScan = Time.time + 0.5f;
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                if (v == null || !v.isEVA) continue;
                KerbalEVA eva = v.evaController != null ? v.evaController : v.GetComponentInChildren<KerbalEVA>();
                if (eva == null) continue;
                ProtoCrewMember pcm = v.GetVesselCrew().Count > 0 ? v.GetVesselCrew()[0] : null;
                if (!Wanted(pcm)) continue;
                Apply(eva, pcm);
                Material vm = visorMatField != null ? visorMatField.GetValue(eva) as Material : null;
                if (vm != null && vm.shader == SmokeAssets.visor) vm.SetFloat("_SheenLevel", v.directSunlight ? 1f : 0.15f);
            }
        }

        bool Wanted(ProtoCrewMember pcm)
        {
            if (allKerbals) return true;
            if (pcm == null || string.IsNullOrEmpty(pcm.ComboId)) return false;
            foreach (string c in combos) if (pcm.ComboId.StartsWith(c, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        void Apply(KerbalEVA eva, ProtoCrewMember pcm)
        {

            Material m = visorMatField != null ? visorMatField.GetValue(eva) as Material : null;
            Renderer r = null;
            if (visorRendField != null) r = visorRendField.GetValue(eva) as Renderer;
            else if (visorRendProp != null) r = visorRendProp.GetValue(eva, null) as Renderer;
            if (m == null && r != null) m = r.sharedMaterial;
            if (m == null) return;
            if (m.shader == SmokeAssets.visor) return;
            string before = m.shader.name;
            m.shader = SmokeAssets.visor;
            m.SetColor("_Color", colour); m.SetFloat("_Glossiness", smoothness); m.SetFloat("_Opacity", opacity);
            if (r != null) r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes;
            if (!logged || done.Add(eva.GetInstanceID()))
            {
                logged = true;
                Debug.Log("[CaelusSmoke] silver visor on " + (pcm != null ? pcm.name + " (" + pcm.ComboId + ")" : eva.name) + ": material " + m.name + " was " + before
                    + ", renderer " + (r != null ? r.name : "-") + ", offset " + m.GetTextureOffset("_MainTex"));
            }
        }
    }
}
