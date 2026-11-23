using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RomaAeterna {
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class RomaAeternaInteriorBounds : MonoBehaviour {
        readonly Dictionary<int,int> completed = new Dictionary<int,int>();
        float nextScan;
        static Bounds ColliderBounds(InternalModel frame, MeshCollider[] colliders) {
            Bounds result=new Bounds();bool first=true;
            foreach(MeshCollider collider in colliders) {
                if(collider.sharedMesh==null)continue;
                Bounds mesh=collider.sharedMesh.bounds;
                Matrix4x4 toInternal=frame.transform.worldToLocalMatrix * collider.transform.localToWorldMatrix;
                for(int corner=0;corner<8;corner++) {
                    Vector3 p=mesh.center+Vector3.Scale(mesh.extents,new Vector3(
                        (corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1));
                    p=toInternal.MultiplyPoint3x4(p);
                    if(first){result=new Bounds(p,Vector3.zero);first=false;}else result.Encapsulate(p);
                }
            }
            return result;
        }
        void Update() {
            if (!HighLogic.LoadedSceneIsFlight || FlightGlobals.fetch == null ||
                Time.realtimeSinceStartup < nextScan) return;
            nextScan=Time.realtimeSinceStartup+.25f;
            foreach (Vessel vessel in FlightGlobals.VesselsLoaded) {
                if(vessel==null || !vessel.loaded || vessel.parts==null)continue;
                foreach(Part part in vessel.parts) {
                    if(part==null || part.partInfo==null || part.internalModel==null)continue;
                    string name=part.partInfo.name.Replace('.','_');
                    bool cabin=name=="RomaAeterna_AquilaAscentVehicle";
                    bool port=name=="RomaAeterna_DockingActive" || name=="RomaAeterna_DockingPassive";
                    if(!cabin && !port)continue;
                    InternalModel model=part.internalModel;
                    Part attached=cabin ? (part.FindAttachNode("top")==null ? null : part.FindAttachNode("top").attachedPart) : null;
                    bool ownPort=attached!=null && attached.partInfo!=null &&
                        new[]{"RomaAeterna_DockingActive","RomaAeterna_DockingPassive"}.Contains(attached.partInfo.name.Replace('.','_'));
                    if(ownPort && attached.internalModel==null)continue;
                    int signature=ownPort ? attached.internalModel.GetInstanceID() : 0;
                    int id=model.GetInstanceID(),previous;
                    if(completed.TryGetValue(id,out previous) && previous==signature)continue;

                    var module=FreeIvaBridge.InteriorModule(model);
                    if(module==null || !FreeIvaBridge.HasShellBounds)continue;
                    string[] shells=cabin ? new[]{"AQIVA_FreeShell_1","AQIVA_FreeShell_6",
                        "AQIVA_FreeShell_7","AQIVA_FreeShell_8","AQIVA_FreeShell_NeckGap"} :
                        new[]{"RA_Port_InteriorShell"};

                    Transform[] found=shells.Select(n=>model.GetComponentsInChildren<Transform>(true)
                        .FirstOrDefault(t=>t.name==n)).ToArray();
                    if(found.Any(t=>t==null))continue;
                    var physical=found.SelectMany(t=>t.GetComponentsInChildren<MeshCollider>(true)).Where(c=>c.sharedMesh!=null).ToArray();
                    Bounds local=ColliderBounds(model,physical);
                    if(physical.Length==0 || local.size.sqrMagnitude<.01f)continue;
                    float overlap=0;
                    if(ownPort) {
                        Transform portShell=attached.internalModel.GetComponentsInChildren<Transform>(true)
                            .FirstOrDefault(t=>t.name=="RA_Port_InteriorShell");
                        if(portShell==null)continue;

                        Bounds sleeve=ColliderBounds(model,portShell.GetComponentsInChildren<MeshCollider>(true));
                        float boundary=sleeve.max.z;
                        if(boundary>local.min.z && boundary<local.center.z &&
                            Math.Abs(sleeve.center.x)<.01f && Math.Abs(sleeve.center.y)<.01f) {
                            overlap=boundary-local.min.z;
                            Vector3 minimum=local.min;minimum.z=boundary;local.SetMinMax(minimum,local.max);
                        }
                    }
                    Bounds before=FreeIvaBridge.GetShellBounds(module);
                    FreeIvaBridge.SetShellBounds(module,local);
                    completed[id]=signature;
                    Debug.Log("[RomaAeterna] Interior local bounds: part="+part.flightID+
                        " internal="+model.internalName+" before="+before.ToString("F5")+
                        " derived="+local.ToString("F5")+" portOwnedOverlap_m="+overlap.ToString("F5")+" physicalCollidersUnchanged=true");
                }
            }
        }
    }
}
