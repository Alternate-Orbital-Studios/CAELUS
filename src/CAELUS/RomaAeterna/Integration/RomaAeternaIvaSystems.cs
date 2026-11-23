using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace RomaAeterna {
    public class RomaAeternaIvaSystems : InternalModule {
        readonly Dictionary<string,Transform> needles=new Dictionary<string,Transform>();
        readonly Dictionary<string,Quaternion> initial=new Dictionary<string,Quaternion>();
        readonly Dictionary<string,double> values=new Dictionary<string,double>();
        Transform globe;Quaternion globeInitial;bool bound;float next;
        public bool RadarValid {get;private set;}
        public bool TargetValid {get;private set;}
        public int BoundNeedles {get{return needles.Count;}}
        public Dictionary<string,double> Readings {get{return new Dictionary<string,double>(values);}}
        public override void OnAwake(){base.OnAwake();}
        bool Bind(){
            if(internalModel==null)return false;
            foreach(string name in new[]{"Height","Sink","Speed","Chamber","MainFuel","RCS","Range","Closing","Pitch","Yaw","BusA","BusB","Current"}){
                Transform t=internalModel.FindModelTransform("AQIVA_Needle_"+name);
                if(t!=null){needles[name]=t;initial[name]=t.localRotation;}
            }
            globe=internalModel.FindModelTransform("IVA2_Command_Physical_Attitude_Globe");
            if(globe!=null)globeInitial=globe.localRotation;
            foreach(string n in new[]{"AQIVA_Light_Command","AQIVA_Light_Systems","AQIVA_Light_Service"}){
                var t=internalModel.FindModelTransform(n);if(t==null)continue;
                var light=t.gameObject.GetComponent<Light>()??t.gameObject.AddComponent<Light>();
                light.type=LightType.Point;light.cullingMask=1<<16;light.range=2.7f;
                light.color=new Color(1,.90f,.76f);light.intensity=n.EndsWith("Service")?.45f:.8f;
                light.shadows=LightShadows.None;
            }
            return needles.Count>0;
        }
        static double Fraction(Vessel v,params string[] names){
            double result=1;bool found=false;
            foreach(string name in names){double amount=0,capacity=0;
                foreach(Part p in v.parts){var r=p.Resources[name];if(r!=null){amount+=r.amount;capacity+=r.maxAmount;}}
                if(capacity>0){found=true;result=Math.Min(result,amount/capacity);}
            }
            return found?result:0;
        }
        void Dial(string key,double value,double lo,double hi){
            values[key]=value;Transform t;if(!needles.TryGetValue(key,out t))return;
            float fraction=(float)Math.Max(0,Math.Min(1,(value-lo)/(hi-lo)));

            Quaternion wanted=initial[key]*Quaternion.AngleAxis(-270*fraction,Vector3.forward);
            t.localRotation=Quaternion.Slerp(t.localRotation,wanted,1-Mathf.Exp(-Time.deltaTime*9));
        }
        void Warning(string name,bool on){
            var t=internalModel.FindModelTransform("AQIVA_Warning_"+name);if(t==null)return;
            var r=t.GetComponent<Renderer>();if(r==null)return;
            var b=new MaterialPropertyBlock();r.GetPropertyBlock(b);
            Color colour=on?new Color(.95f,.27f,.045f,1):new Color(.11f,.06f,.025f,1);
            b.SetColor("_Color",colour);b.SetColor("_EmissiveColor",on?colour:Color.black);r.SetPropertyBlock(b);
        }
        public override void OnUpdate(){
            if(!HighLogic.LoadedSceneIsFlight||vessel==null||internalModel==null)return;
            var mask=internalModel.FindModelTransform("AQIVA_ExternalDepthMask");
            if(mask!=null){var mode=CameraManager.Instance.currentCameraMode;bool inside=mode==CameraManager.CameraMode.IVA||mode==CameraManager.CameraMode.Internal;mask.gameObject.SetActive(!inside);}
            if(!bound){if(Time.realtimeSinceStartup<next)return;next=Time.realtimeSinceStartup+1;bound=Bind();if(!bound)return;}
            Vessel v=vessel;
            var engines=v.parts.Where(p=>p.partInfo.name.StartsWith("RomaAeterna")).SelectMany(p=>p.FindModulesImplementing<ModuleEnginesFX>()).ToArray();
            var active=engines.FirstOrDefault(e=>e.EngineIgnited);
            bool descent=v.parts.Any(p=>p.Resources.Contains("RomaAeterna_DescentMMH"));
            double main=descent?Fraction(v,"RomaAeterna_DescentMMH","RomaAeterna_DescentNTO"):Fraction(v,"RomaAeterna_AscentMMH","RomaAeterna_AscentNTO");
            double rcs=Fraction(v,"Hydrazine"),electric=Fraction(v,"ElectricCharge");
            RadarValid=v.radarAltitude>=0&&v.radarAltitude<=1000;
            Dial("Height",Math.Max(0,v.radarAltitude)/100,0,10);
            Dial("Sink",v.verticalSpeed,-20,20);Dial("Speed",v.horizontalSrfSpeed,0,20);
            Dial("Chamber",active==null||active.maxThrust<=0?0:20*active.finalThrust/active.maxThrust,0,20);
            Dial("MainFuel",main*100,0,100);Dial("RCS",rcs*100,0,100);

            Dial("BusA",electric*100,0,100);Dial("BusB",electric*100,0,100);
            Dial("Current",v.ctrlState.mainThrottle*100,0,100);
            var target=v.targetObject;TargetValid=target!=null;
            double range=0,closing=0,pitch=0,yaw=0;
            if(target!=null){Vector3d delta=target.GetTransform().position-v.GetWorldPos3D();range=delta.magnitude;
                Vector3d relative=v.obt_velocity-target.GetObtVelocity();if(range>0)closing=Vector3d.Dot(relative,delta/range);
                Vector3 local=v.ReferenceTransform.InverseTransformDirection((Vector3)delta.normalized);
                pitch=Math.Atan2(local.z,local.y)*Mathf.Rad2Deg;yaw=Math.Atan2(local.x,local.y)*Mathf.Rad2Deg;
            }
            Dial("Range",range/1000,0,20);Dial("Closing",closing,-20,20);Dial("Pitch",pitch,-20,20);Dial("Yaw",yaw,-20,20);
            Warning("RADAR",!RadarValid);Warning("TARGET",!TargetValid);Warning("FUEL",main<.15);Warning("RCS",rcs<.15);Warning("BUS",electric<.15);Warning("ENGINE",active!=null&&active.flameout);
            if(globe!=null)globe.localRotation=FlightGlobals.ship_orientation*globeInitial;
        }
    }
}
