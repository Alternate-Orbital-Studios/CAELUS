using System.Linq;
using UnityEngine;
namespace RomaAeterna
{
    public sealed class RomaAeternaAscentAttitudeArm : PartModule
    {
        [KSPField(isPersistant=true, guiActive=true, guiActiveEditor=true,
            guiName="Ascent ignition: arm attitude control")]
        [UI_Toggle(enabledText="Automatic", disabledText="Manual")]
        public bool automaticArm = true;
        private bool engineWasIgnited;
        public void FixedUpdate()
        {
            if(!HighLogic.LoadedSceneIsFlight || vessel==null || vessel.packed) return;
            ModuleEngines engine=vessel.parts.Where(p=>p.partInfo!=null &&
                p.partInfo.name.Replace('_','.')=="RomaAeterna.AquilaAscentEngine")
                .SelectMany(p=>p.Modules.OfType<ModuleEngines>()).FirstOrDefault();
            bool ignited=engine!=null && engine.EngineIgnited;
            if(ignited && !engineWasIgnited && automaticArm) {
                vessel.ActionGroups.SetGroup(KSPActionGroup.RCS,true);
                if(!vessel.ActionGroups[KSPActionGroup.SAS]) {
                    vessel.Autopilot.SetMode(VesselAutopilot.AutopilotMode.StabilityAssist);
                    vessel.ActionGroups.SetGroup(KSPActionGroup.SAS,true);
                }
                Debug.Log("[RomaAeterna] Aquila ascent ignition armed native RCS/SAS; pilot may override or select Manual.");
            }
            engineWasIgnited=ignited;
        }
    }
}
