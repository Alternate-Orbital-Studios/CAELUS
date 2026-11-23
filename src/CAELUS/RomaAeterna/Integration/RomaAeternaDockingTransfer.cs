using System;
using System.Linq;
using UnityEngine;

namespace RomaAeterna {
    public sealed class RomaAeternaDockingTransfer : PartModule {
        [KSPField(isPersistant=true)] public float transferProgress;
        [KSPField(isPersistant=true)] public bool transferRequested;
        [KSPField(isPersistant=true)] public bool equalisationConfirmed;
        [KSPField(guiActive=true,guiName="Transfer hardware")] public string transferStatus="Capture ready";
        [KSPField] public bool passive;
        Transform kit; Vector3 kitZero; Quaternion kitRotation;
        Transform[] arms; Quaternion[] armZero; Transform drive;Quaternion driveZero;
        ModuleDockingNode node; bool undockBlocked;
        readonly System.Collections.Generic.Dictionary<string,bool> eventStates=new System.Collections.Generic.Dictionary<string,bool>();
        public Part Cabin {get {return part==null?null:part.attachNodes.Select(n=>n.attachedPart).FirstOrDefault(p=>p!=null&&p.partInfo.name.Replace('.','_')=="RomaAeterna_AquilaAscentVehicle");}}
        public bool HardDocked {get{return node!=null&&node.state!=null&&node.state.StartsWith("Docked",StringComparison.OrdinalIgnoreCase);}}
        [KSPEvent(guiActive=true,guiName="Confirm tunnel equalisation")]
        public void ConfirmEqualisation(){if(!HardDocked){Message("Hard docking is required before tunnel equalisation.");return;}equalisationConfirmed=true;Message("Crew checklist: tunnel equalisation confirmed.");}
        [KSPEvent(guiActive=true,guiName="Prepare crew-transfer passage")]
        public void PrepareTransfer(){
            if(!HardDocked||!equalisationConfirmed){Message("Hard dock and confirm tunnel equalisation first.");return;}
            if(Cabin==null){Message("This installation needs an Aquila transfer/stowage cabin behind this port.");return;}
            string activeName;
            bool unbuckled=FreeIvaBridge.UnbuckledKerbal(out activeName);
            var commandCrew=Cabin.protoModuleCrew.FirstOrDefault();
            if(!unbuckled||commandCrew==null||activeName!=commandCrew.name){
                Message(FreeIvaBridge.Available?"The command crew member must unbuckle and clear the command station before hatch stowage.":"The crew-transfer passage needs the FreeIva mod (walk-in interiors). Stock crew transfer works without it.");return;}
            transferRequested=true;
        }
        [KSPEvent(guiActive=true,guiName="Restore hatch and capture hardware")]
        public void RestoreCapture(){transferRequested=false;}
        static void Message(string text){ScreenMessages.PostScreenMessage(text,5,ScreenMessageStyle.UPPER_CENTER);}
        public override void OnStart(StartState state){base.OnStart(state);
            node=part.FindModuleImplementing<ModuleDockingNode>();string code=passive?"Passive":"Active";
            kit=part.FindModelTransform("RA_"+code+"_CaptureKit");
            if(kit==null){Debug.LogError("[RomaAeterna] Transfer capture-kit transform missing: "+code);return;}
            kitZero=kit.localPosition;kitRotation=kit.localRotation;
            arms=Enumerable.Range(0,3).Select(i=>part.FindModelTransform("RA_"+code+"_FoldArm_"+i)).ToArray();
            armZero=arms.Select(a=>a==null?Quaternion.identity:a.localRotation).ToArray();
            drive=part.FindModelTransform("RA_Active_FoldDrive");if(drive!=null)driveZero=drive.localRotation;
        }
        public void LateUpdate(){
            if(kit==null||!HighLogic.LoadedSceneIsFlight)return;
            if(!HardDocked&&transferProgress>0){transferRequested=false;equalisationConfirmed=false;}
            transferProgress=Mathf.MoveTowards(transferProgress,transferRequested?1:0,Time.deltaTime/45f);
            ApplyKit();transferStatus=transferProgress>=.999f?"Passage clear / kit restrained":transferProgress<=.001f?"Capture ready":"Handling hardware";

            if(part.internalModel!=null)foreach(var interior in part.internalModel.GetComponentsInChildren<RomaAeternaPortInterior>(true))interior.Synchronize();
            Part adjacent=Cabin;
            if(adjacent!=null&&adjacent.internalModel!=null)foreach(var interior in adjacent.internalModel.GetComponentsInChildren<RomaAeternaCabinTransfer>(true))interior.Synchronize();

            if(node!=null){
                if(transferProgress>.001f){
                    foreach(string name in new[]{"Undock","Decouple"})if(node.Events.Contains(name)){
                        if(!undockBlocked)eventStates[name]=node.Events[name].active;
                        node.Events[name].active=false;
                    }
                    undockBlocked=true;
                }else if(undockBlocked){
                    foreach(var original in eventStates)if(node.Events.Contains(original.Key))node.Events[original.Key].active=original.Value;
                    undockBlocked=false;eventStates.Clear();
                }
            }
        }
        void ApplyKit(){
            float fold=Ramp(transferProgress,.55f,.64f);
            for(int i=0;i<3;i++)if(arms[i]!=null){float a=i*2*Mathf.PI/3;
                Vector3 axis=new Vector3(-Mathf.Sin(a),0,Mathf.Cos(a));
                arms[i].localRotation=armZero[i]*Quaternion.AngleAxis((passive?90:Mathf.Atan2(.252f,.190f)*Mathf.Rad2Deg)*fold,axis);}
            if(drive!=null)drive.localRotation=driveZero*Quaternion.AngleAxis(90*fold,Vector3.up);
            Part cabin=Cabin;if(cabin==null){kit.localPosition=kitZero;kit.localRotation=kitRotation;return;}
            float withdrawal=Ramp(transferProgress,.64f,1);Vector3 centre=new Vector3(0,0,passive?.1515f:.18300845f);
            Vector3 start=CabinR5(cabin,kit.parent.TransformPoint(kitZero+kitRotation*new Vector3(centre.x,centre.z,centre.y)));
            Vector3[] path=passive?new[]{start,new Vector3(0,0,5.45f),new Vector3(0,-.52f,5.22f),new Vector3(0,-.60f,4.51f),new Vector3(.39f,-.60f,4.51f),new Vector3(.39f,-.04f,4.51f)}:
                new[]{start,new Vector3(0,0,5.35f),new Vector3(0,-.53f,5.10f),new Vector3(0,-.60f,4.85f),new Vector3(0,-.60f,4.85f),new Vector3(0,-.60f,4.51f),new Vector3(-.39f,-.60f,4.51f),new Vector3(-.39f,-.04f,4.51f)};
            float step=withdrawal*(path.Length-1);int index=Mathf.Min((int)step,path.Length-2);float f=step-index;
            Vector3 desired=Vector3.Lerp(path[index],path[index+1],Mathf.SmoothStep(0,1,f));
            float turn=passive?0:90*Ramp(step,3,4);
            Quaternion relative=Quaternion.AngleAxis(turn,Vector3.forward);
            kit.localRotation=kitRotation*relative;
            Vector3 target=cabin.transform.TransformPoint(new Vector3(desired.x,desired.z-3.7f,desired.y));
            kit.localPosition=kit.parent.InverseTransformPoint(target)-kit.localRotation*new Vector3(centre.x,centre.z,centre.y);
        }
        public static float Ramp(float t,float lo,float hi){return Mathf.Clamp01((t-lo)/(hi-lo));}
        public static Vector3 CabinR5(Part cabin,Vector3 world){Vector3 p=cabin.transform.InverseTransformPoint(world);return new Vector3(p.x,p.z,p.y+3.7f);}
    }

    public sealed class RomaAeternaPortInterior : InternalModule {
        Transform basis; System.Collections.Generic.List<Transform[]> pairs;
        public override void OnAwake(){base.OnAwake();basis=internalModel.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="RA_Port_InteriorBasis");}
        public override void OnUpdate(){base.OnUpdate();Synchronize();}
        public void Synchronize(){if(part==null||basis==null||!HighLogic.LoadedSceneIsFlight)return;
            if(pairs==null){pairs=new System.Collections.Generic.List<Transform[]>();
                foreach(Transform dest in basis.GetComponentsInChildren<Transform>(true)){
                    if(dest==basis||dest.name=="model")continue;
                    Transform source=part.FindModelTransform(dest.name);
                    if(source!=null)pairs.Add(new[]{source,dest});
                }
            }
            foreach(Transform[] p in pairs)if(p[0]!=null&&p[1]!=null){p[1].localPosition=p[0].localPosition;p[1].localRotation=p[0].localRotation;p[1].localScale=p[0].localScale;}
            var own=part.FindModuleImplementing<RomaAeternaDockingTransfer>();
            var docking=part.FindModuleImplementing<ModuleDockingNode>();

            Part peerPart=docking==null?null:part.vessel.parts.FirstOrDefault(p=>p.flightID==docking.dockedPartUId&&p!=part);
            if(peerPart==null&&docking!=null&&docking.otherNode!=null&&docking.otherNode.part!=part)peerPart=docking.otherNode.part;
            var peer=peerPart==null?null:peerPart.FindModuleImplementing<RomaAeternaDockingTransfer>();
            bool clear=own!=null&&peer!=null&&own.HardDocked&&own.transferProgress>=.999f&&peer.transferProgress>=.999f;
            FreeIvaBridge.SetHatches(internalModel,"RA_portNode",null,clear);
        }
    }
    public sealed class RomaAeternaCabinTransfer : InternalModule {
        Transform plug,command,systems;Vector3 commandZero,systemsZero;Quaternion commandRot,systemsRot,plugZero;
        Transform[] dogs;Quaternion[] dogZero;Transform[] slingA,slingB;Quaternion[] slingAZero,slingBZero;Vector3[] slingStowOrigin;float nextBind;RomaAeternaDockingTransfer port;
        public override void OnAwake(){base.OnAwake();
            plug=internalModel.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="AQIVA_Transfer_Hatch_ROOT");
            if(plug!=null)plugZero=plug.localRotation;
            command=Find("IVA2_Command_Console_Swing_Hinge");systems=Find("IVA2_Systems_Console_Swing_Hinge");
            if(command!=null){commandZero=command.localPosition;commandRot=command.localRotation;}
            if(systems!=null){systemsZero=systems.localPosition;systemsRot=systems.localRotation;}
            dogs=Enumerable.Range(0,6).Select(i=>Find("AQIVA_Dog_"+i+"_Hatch_ROOT")).ToArray();dogZero=dogs.Select(t=>t==null?Quaternion.identity:t.localRotation).ToArray();
            slingA=Enumerable.Range(0,2).Select(i=>Find("AQIVA_Sling_"+i+"_A_Hatch_ROOT")).ToArray();slingB=Enumerable.Range(0,2).Select(i=>Find("AQIVA_Sling_"+i+"_B_Hatch_ROOT")).ToArray();
            slingAZero=slingA.Select(s=>s==null?Quaternion.identity:s.localRotation).ToArray();slingBZero=slingB.Select(s=>s==null?Quaternion.identity:s.localRotation).ToArray();
            slingStowOrigin=slingA.Select(s=>s==null?Vector3.zero:s.localPosition).ToArray();
        }
        Transform Find(string n){return internalModel.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==n);}
        public override void OnUpdate(){base.OnUpdate();Synchronize();}
        public void Synchronize(){if(part==null||!HighLogic.LoadedSceneIsFlight)return;
            if(Time.realtimeSinceStartup>nextBind){nextBind=Time.realtimeSinceStartup+1;port=part.vessel.parts.Select(p=>p.FindModuleImplementing<RomaAeternaDockingTransfer>()).FirstOrDefault(m=>m!=null&&m.Cabin==part);}
            float t=port==null?0:port.transferProgress;
            float slide=RomaAeternaDockingTransfer.Ramp(t,0,.07f),swing=RomaAeternaDockingTransfer.Ramp(t,.07f,.20f);
            if(command!=null){command.localPosition=commandZero+Vector3.left*.47f*slide;command.localRotation=commandRot*Quaternion.AngleAxis(-90*swing,Vector3.up);}
            if(systems!=null){systems.localPosition=systemsZero+Vector3.right*.47f*slide;systems.localRotation=systemsRot*Quaternion.AngleAxis(90*swing,Vector3.up);}
            float unlock=RomaAeternaDockingTransfer.Ramp(t,.20f,.25f);
            for(int i=0;i<dogs.Length;i++)if(dogs[i]!=null)dogs[i].localRotation=dogZero[i]*Quaternion.AngleAxis(90*unlock,Vector3.up);

            float retention=Mathf.SmoothStep(0,1,RomaAeternaDockingTransfer.Ramp(t,.53f,.58f));
            for(int i=0;i<2;i++){
                if(slingA[i]!=null){
                    slingA[i].localPosition=slingStowOrigin[i]+Vector3.forward*.36f*(1-retention);
                    slingA[i].localRotation=slingAZero[i]*Quaternion.AngleAxis(-90*(1-retention),Vector3.left);
                }
                if(slingB[i]!=null)slingB[i].localRotation=slingBZero[i]*Quaternion.AngleAxis(180*(1-retention),Vector3.left);
            }
            if(plug!=null){Vector3[] p={new Vector3(0,0,5.766f),new Vector3(0,0,5.42f),new Vector3(0,-.50f,5.15f),new Vector3(0,-.50f,5.15f),new Vector3(0,-.60f,4.90f),new Vector3(-.50f,-.60f,4.90f)};
                float f=RomaAeternaDockingTransfer.Ramp(t,.25f,.55f)*(p.Length-1);int i=Mathf.Min((int)f,p.Length-2);Vector3 r=Vector3.Lerp(p[i],p[i+1],Mathf.SmoothStep(0,1,f-i));
                plug.localPosition=new Vector3(-r.x,-r.y,3.7f-r.z);

                plug.localRotation=Quaternion.AngleAxis(-90*Mathf.SmoothStep(0,1,RomaAeternaDockingTransfer.Ramp(f,2,3)),Vector3.up)*plugZero;
            }
            FreeIvaBridge.SetHatches(internalModel,null,"top",t>=.999f&&port!=null&&port.HardDocked);
        }
    }
}
