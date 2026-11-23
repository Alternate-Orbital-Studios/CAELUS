using System;
using System.Collections.Generic;
using UnityEngine;

namespace Caelus
{
    public class ModuleCoronaChar : PartModule
    {
        [KSPField(isPersistant = true)] public float charLevel = 0f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Heat marks"),
         UI_Toggle(enabledText = "Shown now", disabledText = "After re-entry only", scene = UI_Scene.Editor)]
        public bool heatMarksNow = false;
        bool appliedNow;

        const double FullCharDose = 2.0e12;
        readonly List<Material> mats = new List<Material>();
        readonly List<Renderer> charRenderers = new List<Renderer>();
        float applied = -1f, nextCheck; bool covered = true;

        public override void OnStart(StartState state)
        {
            mats.Clear(); charRenderers.Clear();
            foreach (var r in part.GetComponentsInChildren<Renderer>(true))
                if (r.name.StartsWith("CLD_Char", StringComparison.Ordinal)) { charRenderers.Add(r); mats.Add(r.material); }

            foreach (var r in part.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.StartsWith("CLD_Char", StringComparison.Ordinal)) continue;
                if (!r.name.StartsWith("CLD_Skin", StringComparison.Ordinal)) continue;
                foreach (var m in r.materials) if (m != null && m.renderQueue >= 2450) m.renderQueue = 3010;
            }
            foreach (var m in mats) if (m != null) m.renderQueue = 3020;
            Apply();
        }

        void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || charLevel >= 1f) return;
            double rho = vessel.atmDensity, v = vessel.srfSpeed;
            if (rho <= 0 || v < 1500) return;

            if (Time.time > nextCheck)
            {
                nextCheck = Time.time + 2f; covered = false;
                foreach (var p in vessel.parts) if (p.partInfo != null && p.partInfo.name.StartsWith("CaelusCorona.ServiceModule", StringComparison.Ordinal)) covered = true;
            }
            if (covered) return;
            charLevel = Mathf.Min(1f, charLevel + (float)(Math.Sqrt(rho) * v * v * v / FullCharDose * TimeWarp.fixedDeltaTime));
        }

        void Update() { if (Mathf.Abs(charLevel - applied) > 0.004f || heatMarksNow != appliedNow) Apply(); }

        void Apply()
        {
            applied = charLevel; appliedNow = heatMarksNow;

            float a = heatMarksNow ? 1f : Mathf.Clamp01(Mathf.Pow(charLevel, 0.6f));
            foreach (var m in mats) if (m != null && m.HasProperty("_Color")) { var c = m.color; c.a = a; m.color = c; }
            foreach (var r in charRenderers) if (r != null) r.enabled = a > 0.01f;
        }
    }
}
