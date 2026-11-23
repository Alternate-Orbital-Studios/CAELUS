using System;
using UnityEngine;

namespace Caelus
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class CaelusEntryGlow : MonoBehaviour
    {
        GameObject lead, wake; Light lLead, lWake; AerodynamicsFX aero; float nextAeroLookup;
        float maxLead = 6f, maxWake = 2.5f, rangeLead = 40f, rangeWake = 60f;

        void Start()
        {
            ReadSettings();
            lead = new GameObject("CaelusEntryGlowLead"); lLead = lead.AddComponent<Light>(); lLead.type = LightType.Point; lLead.color = new Color(1.0f, 0.62f, 0.30f); lLead.range = rangeLead; lLead.intensity = 0f; lLead.shadows = LightShadows.None; lLead.enabled = false; lLead.cullingMask = ~((1 << 16) | (1 << 20));
            wake = new GameObject("CaelusEntryGlowWake"); lWake = wake.AddComponent<Light>(); lWake.type = LightType.Point; lWake.color = new Color(1.0f, 0.42f, 0.22f); lWake.range = rangeWake; lWake.intensity = 0f; lWake.shadows = LightShadows.None; lWake.enabled = false; lWake.cullingMask = ~((1 << 16) | (1 << 20));
            aero = FindObjectOfType<AerodynamicsFX>();

            bool firefly = false;
            foreach (var a in AssemblyLoader.loadedAssemblies) if (a != null && a.name == "Firefly") { firefly = true; break; }
            Debug.Log("[CaelusEntryGlow] Firefly " + (firefly ? "installed" : "not installed") + ", stock AERO_FX_QUALITY " + GameSettings.AERO_FX_QUALITY
                + (firefly && GameSettings.AERO_FX_QUALITY > 0 ? " - stock re-entry flames will mix with Firefly (set Aero FX quality to minimal)" : ""));
        }

        void ReadSettings()
        {
            const string rel = "GameData/Caelus_EntryGlow/PluginData/glow.txt";
            string p = KSPUtil.ApplicationRootPath + rel;
            if (!System.IO.File.Exists(p)) return;
            var f = System.IO.File.ReadAllText(p).Split(',');
            var v = new float[4];
            for (int i = 0; i < 4; i++)
                if (i >= f.Length || !float.TryParse(f[i].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i]))
                {
                    Debug.LogWarning("[CaelusEntryGlow] " + rel + " unreadable (expected 'leadIntensity, wakeIntensity, leadRange, wakeRange') - using 6, 2.5, 40, 60");
                    return;
                }
            maxLead = v[0]; maxWake = v[1]; rangeLead = v[2]; rangeWake = v[3];
        }

        static bool IsCaelusPart(Part p)
        {
            if (p == null || p.partInfo == null || p.partInfo.name == null) return false;
            string n = p.partInfo.name;
            return n.StartsWith("Caelus", StringComparison.Ordinal) || n.StartsWith("CaelusCorona", StringComparison.Ordinal) || n.StartsWith("RomaAeterna", StringComparison.Ordinal);
        }
        static Vessel scopeV; static int scopeN = -1; static bool scopeR;
        static bool IsCaelusVessel(Vessel v)
        {
            if (v == null || v.parts == null) return false;
            if (v == scopeV && v.parts.Count == scopeN) return scopeR;
            scopeV = v; scopeN = v.parts.Count; scopeR = false;
            for (int i = 0; i < v.parts.Count; i++) if (IsCaelusPart(v.parts[i])) { scopeR = true; break; }
            return scopeR;
        }

        void LateUpdate()
        {
            var v = FlightGlobals.ActiveVessel;
            if (!IsCaelusVessel(v)) { Off(); return; }
            if (v == null || v.mainBody == null || !v.mainBody.atmosphere || v.altitude > v.mainBody.atmosphereDepth) { Off(); return; }
            double rho = v.atmDensity, spd = v.srfSpeed;

            float fx = Mathf.Clamp01((float)((spd - 2500.0) / 3500.0)) * Mathf.Clamp01((float)(rho / 1.5e-4));
            if (aero == null && Time.realtimeSinceStartup > nextAeroLookup) { nextAeroLookup = Time.realtimeSinceStartup + 2f; aero = FindObjectOfType<AerodynamicsFX>(); }
            if (aero != null) fx = Mathf.Max(fx * 0.5f, Mathf.Clamp01(aero.FxScalar) * Mathf.Clamp01(aero.state));
            if (fx < 0.02f) { Off(); return; }
            Vector3 dir = v.srf_velocity.normalized; Vector3 com = v.CoM;
            float size = Mathf.Max(2f, v.vesselSize.magnitude * 0.5f);
            lead.transform.position = com + dir * (size * 0.6f + 1.5f);
            wake.transform.position = com - dir * (size * 0.8f + 6f);
            lLead.intensity = maxLead * fx; lWake.intensity = maxWake * fx; lLead.enabled = lWake.enabled = true;
        }

        void Off() { if (lLead != null && lLead.enabled) { lLead.enabled = false; lWake.enabled = false; } }
        void OnDestroy() { if (lead) Destroy(lead); if (wake) Destroy(wake); }
    }
}
