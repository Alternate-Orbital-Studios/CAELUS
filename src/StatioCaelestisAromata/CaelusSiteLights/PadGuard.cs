using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CaelusSiteLights
{
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class CaelusPadGuard : MonoBehaviour
    {
        static readonly string[] OurSites = { "Statio Caelestis Aromata", "Ordo Missilium" };
        Button.ButtonClickedEvent original;

        IEnumerator Start()
        {
            for (int i = 0; i < 300 && (EditorLogic.fetch == null || EditorLogic.fetch.launchBtn == null); i++) yield return null;
            if (EditorLogic.fetch == null || EditorLogic.fetch.launchBtn == null) { Debug.Log("[CaelusPadGuard] no launch button found"); yield break; }
            yield return null;

            if (EditorLogic.fetch == null || EditorLogic.fetch.launchBtn == null) yield break;
            Button b = EditorLogic.fetch.launchBtn;
            original = b.onClick;
            b.onClick = new Button.ButtonClickedEvent();
            b.onClick.AddListener(OnLaunch);
        }

        static bool Ours(string site)
        {
            if (string.IsNullOrEmpty(site)) return false;
            foreach (string s in OurSites) if (site.StartsWith(s)) return true;
            return false;
        }

        static List<ProtoVessel> OnPad(string site)
        {
            var r = new List<ProtoVessel>();
            if (HighLogic.CurrentGame == null || HighLogic.CurrentGame.flightState == null) return r;
            foreach (ProtoVessel pv in HighLogic.CurrentGame.flightState.protoVessels)
            {
                if (pv == null || pv.vesselType == VesselType.SpaceObject) continue;
                if (pv.situation != Vessel.Situations.PRELAUNCH && pv.situation != Vessel.Situations.LANDED && pv.situation != Vessel.Situations.SPLASHED) continue;
                if (pv.landedAt == site) r.Add(pv);
            }
            return r;
        }

        void OnLaunch()
        {
            string site = EditorLogic.fetch != null ? EditorLogic.fetch.launchSiteName : null;
            if (string.IsNullOrEmpty(site)) site = EditorDriver.SelectedLaunchSiteName;
            List<ProtoVessel> there = Ours(site) ? OnPad(site) : new List<ProtoVessel>();
            if (there.Count == 0) { original.Invoke(); return; }

            string names = "";
            foreach (ProtoVessel pv in there) names += "\n  - " + pv.vesselName + " (" + pv.vesselType + ")";
            Debug.Log("[CaelusPadGuard] launch held: " + site + " has " + there.Count + " vessel(s):" + names.Replace("\n", " "));
            string msg = site + " is not clear. Still standing there:" + names
                       + "\n\nA new rocket would be placed inside it and both would be destroyed.";
            PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new MultiOptionDialog("CaelusPadGuard", msg, "Launch site not clear", HighLogic.UISkin,
                    new DialogGUIButton("Recover and launch", () => RecoverAndLaunch(site), true),
                    new DialogGUIButton("Cancel", () => { }, true)),
                false, HighLogic.UISkin);
        }

        void RecoverAndLaunch(string site)
        {
            foreach (ProtoVessel pv in OnPad(site))
            {
                Debug.Log("[CaelusPadGuard] recovering " + pv.vesselName + " from " + site);
                ShipConstruction.RecoverVesselFromFlight(pv, HighLogic.CurrentGame.flightState, true);
            }
            if (OnPad(site).Count == 0) original.Invoke();
            else ScreenMessages.PostScreenMessage(site + " could not be cleared - recover the vessels in the Tracking Station.", 6f, ScreenMessageStyle.UPPER_CENTER);
        }
    }
}
