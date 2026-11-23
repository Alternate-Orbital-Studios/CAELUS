using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CaelusLegLanded
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class CaelusRptBridge : MonoBehaviour
    {
        const string Id = "caelus.rptbridge";
        static readonly string[] Prefixes = { "Caelus", "CaelusCorona", "RomaAeterna" };
        static Harmony harmony; static bool off;
        static FieldInfo fParts, fEngines, fProfiles, fFx, fPartId;

        void Start()
        {
            if (harmony != null) return;
            try
            {
                Type t = AccessTools.TypeByName("RaymarchedPlumeTrails.EngineClusterUtils");
                if (t == null) { Debug.Log("[CaelusRptBridge] Raymarched Plume Trails not installed - nothing to do"); return; }
                MethodInfo mOf = AccessTools.Method(t, "EnginesOf", new[] { typeof(Vessel) });
                MethodInfo mGather = AccessTools.Method(t, "GatherEngineSamples", new[] { typeof(Vessel), typeof(bool) });
                Type tVe = mOf != null ? mOf.ReturnType : null, tSample = AccessTools.TypeByName("RaymarchedPlumeTrails.EngineSample");
                if (tVe != null) { fParts = AccessTools.Field(tVe, "parts"); fEngines = AccessTools.Field(tVe, "engines"); fProfiles = AccessTools.Field(tVe, "profiles"); fFx = AccessTools.Field(tVe, "fx"); }
                if (tSample != null) fPartId = AccessTools.Field(tSample, "partId");
                if (mOf == null || mGather == null || fParts == null || fEngines == null || fPartId == null)
                { Debug.Log("[CaelusRptBridge] RPT found but not the version this was made for (engine query not found) - RPT left as it is"); return; }
                harmony = new Harmony(Id);
                harmony.Patch(mOf, postfix: new HarmonyMethod(typeof(CaelusRptBridge).GetMethod(nameof(EnginesOfPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                harmony.Patch(mGather, postfix: new HarmonyMethod(typeof(CaelusRptBridge).GetMethod(nameof(GatherPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                Debug.Log("[CaelusRptBridge] Raymarched Plume Trails found: it skips CAELUS engines (CAELUS smoke there), all other engines unchanged");
            }
            catch (Exception e) { Off("setup", e); }
        }

        static bool Ours(Part p)
        {
            if (p == null || p.partInfo == null || p.partInfo.name == null) return false;
            string n = p.partInfo.name;
            for (int i = 0; i < Prefixes.Length; i++) if (n.StartsWith(Prefixes[i], StringComparison.Ordinal)) return true;
            return false;
        }

        static void Off(string where, Exception e)
        {
            if (off) return; off = true;
            Debug.LogWarning("[CaelusRptBridge] switched off (" + where + ": " + e.Message + ") - RPT draws on CAELUS engines as before");
            try { if (harmony != null) harmony.UnpatchAll(Id); } catch (Exception) { }
        }

        static void EnginesOfPostfix(object __result)
        {
            if (off || __result == null) return;
            try
            {
                var parts = fParts.GetValue(__result) as IList; if (parts == null || parts.Count == 0) return;
                IList engines = fEngines.GetValue(__result) as IList, profiles = fProfiles != null ? fProfiles.GetValue(__result) as IList : null, fx = fFx != null ? fFx.GetValue(__result) as IList : null;
                for (int i = parts.Count - 1; i >= 0; i--)
                {
                    if (!Ours(parts[i] as Part)) continue;
                    parts.RemoveAt(i);
                    if (engines != null && i < engines.Count) engines.RemoveAt(i);
                    if (profiles != null && i < profiles.Count) profiles.RemoveAt(i);
                    if (fx != null && i < fx.Count) fx.RemoveAt(i);
                }
            }
            catch (Exception e) { Off("engine list", e); }
        }

        static readonly Dictionary<Guid, KeyValuePair<int, HashSet<uint>>> ids = new Dictionary<Guid, KeyValuePair<int, HashSet<uint>>>();
        static void GatherPostfix(Vessel vessel, object __result)
        {
            if (off || vessel == null || vessel.parts == null) return;
            try
            {
                var list = __result as IList; if (list == null || list.Count == 0) return;
                KeyValuePair<int, HashSet<uint>> c;
                if (!ids.TryGetValue(vessel.id, out c) || c.Key != vessel.parts.Count)
                {
                    var s = new HashSet<uint>();
                    foreach (Part p in vessel.parts) if (Ours(p)) { s.Add(p.flightID); s.Add(p.persistentId); }
                    ids[vessel.id] = c = new KeyValuePair<int, HashSet<uint>>(vessel.parts.Count, s);
                }
                if (c.Value.Count == 0) return;
                for (int i = list.Count - 1; i >= 0; i--)
                    if (c.Value.Contains((uint)fPartId.GetValue(list[i]))) list.RemoveAt(i);
            }
            catch (Exception e) { Off("engine samples", e); }
        }
    }
}
