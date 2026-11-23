using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using KerbalKonstructs.Core;
using UnityEngine;

namespace Caelus
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class CaelusGroundFit : MonoBehaviour
    {
        void Start() { DontDestroyOnLoad(this); StartCoroutine(Run()); }

        IEnumerator Run()
        {
            for (int i = 0; i < 120; i++)
            {
                if (Apply()) yield break;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            Debug.LogWarning("[CaelusGroundFit] launch site groups not found - site left as configured");
        }

        static bool WebTerrain(out string why)
        {
            bool web = false; why = "Mirage settings not found (base terrain assumed)";
            foreach (var n in GameDatabase.Instance.GetConfigNodes("Mirage"))
                if (n.HasValue("webStreaming"))
                {
                    web = string.Equals(n.GetValue("webStreaming").Trim(), "true", StringComparison.OrdinalIgnoreCase);
                    why = "Mirage webStreaming = " + n.GetValue("webStreaming").Trim();
                }
            return web;
        }

        static string SolScale()
        {
            foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("Sol_Configuration"))
            {
                string v = (n.GetValue("SystemScale") ?? "").Trim();
                if (v.Equals("Quarter", StringComparison.OrdinalIgnoreCase)) return "Quarter";
                if (v.Equals("Stock", StringComparison.OrdinalIgnoreCase)) return "Stock";
            }

            return GameDatabase.Instance.GetConfigNodes("Sol_Configuration").Length > 0 ? "Real" : null;
        }

        static float F(ConfigNode n, string key, float fallback)
        {
            float r;
            return n.HasValue(key) && float.TryParse(n.GetValue(key), NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : fallback;
        }

        static double D(ConfigNode n, string key, double fallback)
        {
            double r;
            return n.HasValue(key) && double.TryParse(n.GetValue(key), NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : fallback;
        }

        static bool Apply()
        {
            var groups = new Dictionary<string, ConfigNode>();
            double cLat = double.NaN, cLon = double.NaN;
            foreach (var root in GameDatabase.Instance.GetConfigNodes("CAELUS_GROUND_FIT"))
            {
                cLat = D(root, "centreLatitude", cLat); cLon = D(root, "centreLongitude", cLon);
                foreach (var g in root.GetNodes("GROUP"))
                    if (g.HasValue("name")) groups[g.GetValue("name").Trim()] = g;
            }
            if (groups.Count == 0) { Debug.Log("[CaelusGroundFit] no CAELUS_GROUND_FIT groups"); return true; }

            string why; bool web = WebTerrain(out why);
            string scale = SolScale();
            if (scale == null) { Debug.Log("[CaelusGroundFit] Sol not installed - site groups left as Kerbal Konstructs places them"); return true; }

            var found = new List<GroupCenter>();
            var f = typeof(StaticDatabase).GetField("allCenters", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            var all = f != null ? f.GetValue(null) as IDictionary : null;
            if (all == null) { Debug.LogWarning("[CaelusGroundFit] Kerbal Konstructs group list not readable (other KK version?) - site left as built"); return true; }
            foreach (var o in all.Values)
            {
                var gc = o as GroupCenter;
                if (gc != null && gc.CelestialBody != null && gc.CelestialBody.name == "Earth" && groups.ContainsKey(gc.Group)) found.Add(gc);
            }
            if (found.Count < groups.Count) return false;

            foreach (var gc in found)
            {
                ConfigNode g = groups[gc.Group];
                float rise = web ? 0f : F(g, "rise", 0f);
                if (scale == "Real")
                {
                    if (rise == 0f) continue;
                    gc.RadiusOffset += rise;
                    Refresh(gc, 0, 0, rise);
                    Debug.Log("[CaelusGroundFit] " + why + ", scale Real: " + gc.Group + " raised " + rise.ToString("F2", CultureInfo.InvariantCulture) + " m");
                    continue;
                }
                double k = scale == "Quarter" ? 4.0 : 9.0;
                bool hide = string.Equals((g.GetValue(scale == "Quarter" ? "hideQuarter" : "hideStock") ?? "").Trim(), "true", StringComparison.OrdinalIgnoreCase);
                if (hide) { Hide(gc); Debug.Log("[CaelusGroundFit] scale " + scale + ": " + gc.Group + " hidden (its spot is over the sea on this smaller Earth)"); continue; }
                float g0 = F(g, "ground", float.NaN);
                if (float.IsNaN(g0) || double.IsNaN(cLat) || double.IsNaN(cLon))
                { Debug.LogWarning("[CaelusGroundFit] " + gc.Group + ": no Real-scale ground / site centre in CAELUS_GROUND_FIT - left as built"); continue; }

                double lat0 = gc.RefLatitude, lon0 = gc.RefLongitude;
                double lat = cLat + k * (lat0 - cLat);
                double lon = cLon + k * (lon0 - cLon) * Math.Cos(lat0 * Math.PI / 180.0) / Math.Cos(lat * Math.PI / 180.0);

                float gk = F(g, scale == "Quarter" ? "groundQuarter" : "groundStock", g0);
                float above = gc.RadiusOffset - g0;
                float ro = (float)(gk / k) + above + (float)(rise / k);
                gc.RefLatitude = lat; gc.RefLongitude = lon; gc.RadiusOffset = ro;
                Refresh(gc, lat - lat0, lon - lon0, ro - (above + g0));
                Debug.Log("[CaelusGroundFit] " + why + ", scale " + scale + ": " + gc.Group + " moved from " + lat0.ToString("F5", CultureInfo.InvariantCulture) + "," +
                          lon0.ToString("F5", CultureInfo.InvariantCulture) + " to " + lat.ToString("F5", CultureInfo.InvariantCulture) + "," + lon.ToString("F5", CultureInfo.InvariantCulture) +
                          " (same layout in metres), height " + ro.ToString("F2", CultureInfo.InvariantCulture) + " m (ground " + (gk / k).ToString("F2", CultureInfo.InvariantCulture) + " m)");
            }
            return true;
        }

        static void Hide(GroupCenter gc)
        {
            try
            {
                gc.hidden = true;

                gc.RadiusOffset = -3000f;
                gc.Update();
                gc.SetInstancesEnabled(false);
                if (gc.launchsites == null) return;
                var un = typeof(LaunchSiteManager).GetMethod("UnregisterLaunchSite", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                foreach (var ls in gc.launchsites)
                {
                    if (ls == null) continue;
                    try { ls.LaunchSiteIsHidden = true; } catch (Exception) { }
                    if (un != null) un.Invoke(null, new object[] { ls });
                    Debug.Log("[CaelusGroundFit] launch site '" + ls.LaunchSiteName + "' removed from the list");
                }
            }
            catch (Exception e) { Debug.LogWarning("[CaelusGroundFit] " + gc.Group + " hide: " + e.Message); }
        }

        static void Refresh(GroupCenter gc, double dLat, double dLon, float dAlt)
        {
            try
            {
                bool moved = dLat != 0 || dLon != 0;
                if (moved && gc.CelestialBody != null)
                    gc.RadialPosition = (Vector3)(gc.CelestialBody.GetRelSurfaceNVector(gc.RefLatitude, gc.RefLongitude).normalized * gc.CelestialBody.Radius);
                gc.Update();
                if (gc.launchsites != null)
                    foreach (var ls in gc.launchsites)
                    {
                        if (ls == null) continue;
                        ls.refLat += (float)dLat; ls.refLon += (float)dLon; ls.refAlt += dAlt;
                    }
            }
            catch (Exception e) { Debug.LogWarning("[CaelusGroundFit] " + gc.Group + ": " + e.Message); }
        }
    }
}
