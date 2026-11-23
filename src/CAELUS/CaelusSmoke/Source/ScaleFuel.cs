using System;
using UnityEngine;

namespace CaelusSmoke
{
    public class ModuleCaelusScaleFuel : PartModule
    {
        [KSPField] public string resources = "";

        public override void OnStart(StartState state)
        {
            if (HighLogic.LoadedSceneIsEditor || (vessel != null && vessel.situation == Vessel.Situations.PRELAUNCH)) Fix();
        }

        void Fix()
        {
            Part prefab = part.partInfo != null ? part.partInfo.partPrefab : null;
            if (prefab == null || string.IsNullOrEmpty(resources)) return;
            foreach (string raw in resources.Split(','))
            {
                string n = raw.Trim(); if (n.Length == 0) continue;
                PartResource r = part.Resources[n], p = prefab.Resources[n];
                if (r == null || p == null) continue;
                if (Math.Abs(r.maxAmount - p.maxAmount) <= 1e-3 * Math.Max(1.0, p.maxAmount)) continue;
                bool full = r.amount >= r.maxAmount * 0.999;
                double was = r.maxAmount;
                r.maxAmount = p.maxAmount;
                r.amount = full ? p.maxAmount : Math.Min(r.amount, p.maxAmount);
                Debug.Log("[CaelusSmoke] scale profile: " + part.partInfo.name + " " + n + " capacity " + was.ToString("F0") + " -> " + p.maxAmount.ToString("F0"));
            }
        }
    }
}
