using System;
using UnityEngine;

namespace CaelusSiteLights
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class CaelusPadSaveGuard : MonoBehaviour
    {

        static readonly double[,] Sites = { { 10.4370, 51.3940 }, { 10.4164, 51.3873 }, { 10.4064, 51.3809 },
                                            { 10.4015, 51.3768 }, { 10.3969, 51.3736 }, { 10.3929, 51.3686 } };
        const double Radius = 0.012;
        const string Body = "Earth";

        void Awake()
        {
            DontDestroyOnLoad(this);
            GameEvents.onProtoVesselSave.Add(OnSave);
            GameEvents.onProtoVesselLoad.Add(OnLoad);
        }

        void OnDestroy()
        {
            GameEvents.onProtoVesselSave.Remove(OnSave);
            GameEvents.onProtoVesselLoad.Remove(OnLoad);
        }

        static bool AtOurPads(ProtoVessel pv)
        {
            if (pv == null) return false;
            if (pv.situation != Vessel.Situations.LANDED && pv.situation != Vessel.Situations.PRELAUNCH) return false;
            if (pv.orbitSnapShot == null || FlightGlobals.Bodies == null) return false;
            int bi = pv.orbitSnapShot.ReferenceBodyIndex;
            if (bi < 0 || bi >= FlightGlobals.Bodies.Count || FlightGlobals.Bodies[bi].bodyName != Body) return false;
            for (int i = 0; i < Sites.GetLength(0); i++)
            {
                double dLat = pv.latitude - Sites[i, 0], dLon = pv.longitude - Sites[i, 1];
                if (dLat * dLat + dLon * dLon < Radius * Radius) return true;
            }
            return false;
        }

        void OnSave(GameEvents.FromToAction<ProtoVessel, ConfigNode> e)
        {
            try
            {
                if (e.to == null || !AtOurPads(e.from)) return;
                e.from.skipGroundPositioning = true;
                e.to.SetValue("skipGroundPositioning", "True", true);
            }
            catch (Exception ex) { Debug.LogWarning("[CaelusPadSaveGuard] save: " + ex.Message); }
        }

        void OnLoad(GameEvents.FromToAction<ProtoVessel, ConfigNode> e)
        {
            try
            {
                if (!AtOurPads(e.from) || e.from.skipGroundPositioning) return;
                e.from.skipGroundPositioning = true;
                Debug.Log("[CaelusPadSaveGuard] " + e.from.vesselName + " stands at the Statio Caelestis Aromata pads: loaded at its saved height");
            }
            catch (Exception ex) { Debug.LogWarning("[CaelusPadSaveGuard] load: " + ex.Message); }
        }
    }
}
