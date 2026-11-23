using System.Collections.Generic;
using UnityEngine;

namespace CaelusSiteLights
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class CaelusPadSpawnGuard : MonoBehaviour
    {
        static readonly string[] OurSites = { "Statio Caelestis Aromata", "Ordo Missilium" };
        const float MaxDrop = 6f, WatchAfterPhysics = 6f;
        class Watch { public double refAlt; public float physicsAt = -1f; public int fixes; }
        readonly Dictionary<Vessel, Watch> watched = new Dictionary<Vessel, Watch>();

        static bool OurSite(Vessel v)
        {
            if (v == null || string.IsNullOrEmpty(v.landedAt)) return false;
            foreach (string s in OurSites) if (v.landedAt.Contains(s)) return true;
            return false;
        }

        void Update() { Check(); }
        void FixedUpdate() { Check(); }

        void Check()
        {
            try
            {
                foreach (Vessel v in FlightGlobals.VesselsLoaded)
                {
                    if (v == null || v.mainBody == null) continue;
                    Watch w;
                    if (!watched.TryGetValue(v, out w))
                    {
                        if (v.situation != Vessel.Situations.PRELAUNCH || !OurSite(v)) continue;
                        w = new Watch { refAlt = v.mainBody.GetAltitude(v.transform.position) };
                        watched[v] = w;
                        Debug.Log("[CaelusPadSpawnGuard] watching " + v.vesselName + " on " + v.landedAt + " (placed at altitude " + w.refAlt.ToString("F2") + " m)");
                        continue;
                    }
                    if (w.physicsAt < 0f && !v.packed && v.situation == Vessel.Situations.PRELAUNCH && FlightGlobals.ready) w.physicsAt = Time.time;
                    if (w.physicsAt >= 0f && Time.time - w.physicsAt > WatchAfterPhysics) continue;
                    if (v.situation != Vessel.Situations.PRELAUNCH) continue;
                    double alt = v.mainBody.GetAltitude(v.transform.position);
                    double drop = w.refAlt - alt;
                    if (drop <= MaxDrop || w.fixes >= 3) continue;
                    Vector3 up = (v.transform.position - (Vector3)v.mainBody.position).normalized;
                    Vector3 off = up * (float)drop;
                    if (v.packed)
                    {
                        v.SetPosition(v.transform.position + off);
                        v.altitude += drop;
                    }
                    else
                    {
                        v.SetPosition(v.transform.position + off);
                        foreach (Part p in v.parts)
                            if (p.Rigidbody != null) { p.Rigidbody.velocity = Vector3.zero; p.Rigidbody.angularVelocity = Vector3.zero; }
                    }
                    w.fixes++;
                    Debug.Log("[CaelusPadSpawnGuard] " + v.vesselName + " was moved " + drop.ToString("F1") + " m below where it was placed on " + v.landedAt
                              + " (KSP's ground check missed the pad deck) - put back" + (v.packed ? " (packed)" : " (unpacked)"));
                }
                if (watched.Count > 0)
                {
                    var gone = new List<Vessel>();
                    foreach (var kv in watched) if (kv.Key == null || !kv.Key.loaded) gone.Add(kv.Key);
                    foreach (var g in gone) watched.Remove(g);
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[CaelusPadSpawnGuard] check failed: " + e.Message); }
        }
    }
}
