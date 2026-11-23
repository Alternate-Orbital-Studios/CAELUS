using System;
using System.Reflection;
using UnityEngine;
namespace RomaAeterna
{
    public sealed class RomaAeternaDockingProbe : PartModule
    {
        [KSPField(guiActive=true, guiName="Capture probe extension", guiFormat="F2", guiUnits=" m")]
        public float probeExtension;
        [KSPField(isPersistant=true, guiActiveEditor=true, guiName="Probe inspection position")]
        [UI_Toggle(enabledText="Ready / extended",disabledText="Hard dock / retracted")]
        public bool editorExtended=true;
        private Transform middle,tip;
        private Vector3 middleZero,tipZero;
        private ModuleDockingNode node;
        private float stroke=1;
        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            middle=part.FindModelTransform("RA_Port_IntermediateSlide");
            tip=part.FindModelTransform("RA_Port_ProbeSlide");
            node=part.FindModuleImplementing<ModuleDockingNode>();
            if(middle==null || tip==null || node==null) {
                Debug.LogError("[RomaAeterna] RA-09 active probe missing required transform or docking module.");return;
            }
            middleZero=middle.localPosition;tipZero=tip.localPosition;
            stroke=Desired();Apply();
        }
        private static object Read(object o,string key)
        {
            if(o==null)return null;
            BindingFlags f=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
            FieldInfo field=o.GetType().GetField(key,f);
            if(field!=null)return field.GetValue(o);
            PropertyInfo prop=o.GetType().GetProperty(key,f);
            return prop==null ? null : prop.GetValue(o,null);
        }
        private float Desired()
        {
            if(HighLogic.LoadedSceneIsEditor)return editorExtended ? 1 : 0;
            if(node==null)return 1;
            string state=Convert.ToString(Read(node,"state"));
            if(state.StartsWith("Docked",StringComparison.OrdinalIgnoreCase))return 0;
            ModuleDockingNode other=Read(node,"otherNode") as ModuleDockingNode;
            Transform a=Read(node,"nodeTransform") as Transform;
            Transform b=Read(other,"nodeTransform") as Transform;
            if(other!=null && a!=null && b!=null &&
                (state=="Acquire" || state=="Acquired" || state=="Captured"))
                return Mathf.Clamp01(Vector3.Distance(a.position,b.position)/.31f);
            return 1;
        }
        public void LateUpdate()
        {
            if(middle==null || tip==null)return;

            stroke=Desired();Apply();
        }
        private void Apply()
        {
            middle.localPosition=middleZero+Vector3.up*(.17f*stroke);
            tip.localPosition=tipZero+Vector3.up*(.31f*stroke);
            probeExtension=.31f*stroke;
        }
    }
}
