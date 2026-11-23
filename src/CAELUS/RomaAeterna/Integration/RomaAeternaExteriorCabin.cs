using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace RomaAeterna {
    public sealed class RomaAeternaExteriorCabin : PartModule {
        sealed class Link { public Transform source, view; }
        sealed class Draw { public Renderer source, view; public readonly MaterialPropertyBlock block=new MaterialPropertyBlock(); }
        readonly List<Link> links=new List<Link>();
        readonly List<Draw> draws=new List<Draw>();
        readonly Dictionary<Transform,Transform> map=new Dictionary<Transform,Transform>();
        GameObject root; InternalModel sourceModel; int crewCount=-1; float nextBind;
        public int ExteriorRendererCount {get{return draws.Count;}}
        public bool ExteriorVisible {get{return root!=null&&root.activeSelf;}}
        Transform CopyTransform(Transform source) {
            if(source==sourceModel.transform)return root.transform;
            Transform result;if(map.TryGetValue(source,out result))return result;
            result=new GameObject("AQExterior_"+source.name).transform;
            result.SetParent(CopyTransform(source.parent),false);
            result.gameObject.layer=part.gameObject.layer;
            map.Add(source,result);links.Add(new Link{source=source,view=result});return result;
        }
        static bool Relevant(Renderer r) {
            if(r.name.Contains("DepthMask")||r.name.StartsWith("AQIVA_Window"))return false;
            return r.sharedMaterials.Length>0 && !r.sharedMaterials.Any(m=>m!=null&&m.shader!=null&&m.shader.name.Contains("DepthMask"));
        }
        void Clear() {
            if(root!=null)Destroy(root);root=null;links.Clear();draws.Clear();map.Clear();sourceModel=null;
        }
        void Bind(InternalModel model) {
            Clear();sourceModel=model;crewCount=part.protoModuleCrew.Count;
            root=new GameObject("AquilaExteriorCabin");root.transform.SetParent(part.transform,false);

            root.transform.localRotation=new Quaternion(0,.70710678f,-.70710678f,0);
            foreach(Renderer src in model.GetComponentsInChildren<Renderer>(true).Where(Relevant)) {
                Transform t=CopyTransform(src.transform);Renderer dst;
                var skin=src as SkinnedMeshRenderer;
                if(skin!=null) {
                    var s=t.gameObject.AddComponent<SkinnedMeshRenderer>();s.sharedMesh=skin.sharedMesh;
                    s.bones=skin.bones.Select(b=>b==null?null:CopyTransform(b)).ToArray();
                    s.rootBone=skin.rootBone==null?null:CopyTransform(skin.rootBone);
                    s.localBounds=skin.localBounds;s.updateWhenOffscreen=false;dst=s;
                } else {
                    MeshFilter filter=src.GetComponent<MeshFilter>();if(filter==null)continue;
                    t.gameObject.AddComponent<MeshFilter>().sharedMesh=filter.sharedMesh;
                    dst=t.gameObject.AddComponent<MeshRenderer>();
                }
                dst.sharedMaterials=src.sharedMaterials;dst.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                dst.receiveShadows=true;draws.Add(new Draw{source=src,view=dst});
            }

            foreach(Light src in model.GetComponentsInChildren<Light>(true).Where(l=>l.name.StartsWith("AQIVA_Light"))) {
                Light l=CopyTransform(src.transform).gameObject.AddComponent<Light>();
                l.type=src.type;l.color=src.color;l.range=src.range;l.intensity=src.intensity;
                l.cullingMask=1<<part.gameObject.layer;l.shadows=LightShadows.None;
            }
            Debug.Log("[RomaAeterna] Exterior cabin bound: "+draws.Count+" shared-mesh renderers; no extra simulated parts.");
        }
        void LateUpdate() {
            if(!HighLogic.LoadedSceneIsFlight||part==null||part.vessel==null||CameraManager.Instance==null)return;
            bool exterior=CameraManager.Instance.currentCameraMode==CameraManager.CameraMode.Flight;
            bool nearby=FlightCamera.fetch!=null&&(FlightCamera.fetch.transform.position-part.transform.position).sqrMagnitude<35*35;
            if(!exterior||!nearby){if(root!=null)root.SetActive(false);return;}
            if(Time.realtimeSinceStartup>=nextBind) {
                nextBind=Time.realtimeSinceStartup+1;
                if(part.internalModel==null)part.CreateInternalModel();
                if(part.internalModel!=null&&(part.internalModel!=sourceModel||crewCount!=part.protoModuleCrew.Count))Bind(part.internalModel);
            }
            if(root==null)return;root.SetActive(true);
            var systems=sourceModel.GetComponent<RomaAeternaIvaSystems>();if(systems!=null)systems.OnUpdate();
            var transfer=sourceModel.GetComponent<RomaAeternaCabinTransfer>();if(transfer!=null)transfer.OnUpdate();
            foreach(Link link in links) {
                if(link.source==null||link.view==null)continue;
                link.view.localPosition=link.source.localPosition;link.view.localRotation=link.source.localRotation;link.view.localScale=link.source.localScale;
                link.view.gameObject.SetActive(link.source.gameObject.activeSelf);
            }
            foreach(Draw draw in draws) {
                if(draw.source==null||draw.view==null)continue;
                draw.source.GetPropertyBlock(draw.block);draw.view.SetPropertyBlock(draw.block);
                var skin=draw.source as SkinnedMeshRenderer;var projected=draw.view as SkinnedMeshRenderer;
                if(skin!=null&&projected!=null&&skin.sharedMesh!=null)
                    for(int i=0;i<skin.sharedMesh.blendShapeCount;i++)projected.SetBlendShapeWeight(i,skin.GetBlendShapeWeight(i));
            }
        }
        void OnDestroy(){Clear();}
    }
}
