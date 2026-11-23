using UnityEngine;

namespace CaelusSmoke
{
    public class IvaShadows
    {
        public float distance = 12f, bias = 0.12f, normalBias = 0.7f;

        public float nightAmbient = 0.08f;
        bool active, logged, ambLogged; float savedDist; Camera ivaCam;
        Color sAmb, sSky, sEq, sGround; float sAmbI;
        Light sun; float sunBias, sunNBias;

        public IvaShadows()
        {
            Camera.onPreCull += Pre;
            Camera.onPostRender += Post;
        }

        static Part cabinPart;
        static bool OurCabin()
        {
            CameraManager cm = CameraManager.Instance;
            if (cm == null || (cm.currentCameraMode != CameraManager.CameraMode.IVA && cm.currentCameraMode != CameraManager.CameraMode.Internal)) return false;
            Kerbal k = cm.IVACameraActiveKerbal;
            Part p = k != null ? k.InPart : null;
            cabinPart = p;
            return p != null && CaelusParts.Is(p);
        }

        void Pre(Camera c)
        {
            if (active || c == null || !HighLogic.LoadedSceneIsFlight) return;
            if (ivaCam == null) { InternalCamera ic = InternalCamera.Instance; if (ic != null) ivaCam = ic.GetComponent<Camera>(); }
            if (c != ivaCam || !OurCabin()) return;
            savedDist = QualitySettings.shadowDistance;
            QualitySettings.shadowDistance = Mathf.Min(savedDist, distance);
            sun = null;
            foreach (Light l in Light.GetLights(LightType.Directional, 16))
                if (l != null && (l.cullingMask & 1) == 0 && l.shadows != LightShadows.None) { sun = l; break; }
            if (sun != null)
            {
                sunBias = sun.shadowBias; sunNBias = sun.shadowNormalBias;
                sun.shadowBias = Mathf.Max(sunBias, bias); sun.shadowNormalBias = Mathf.Max(sunNBias, normalBias);
            }
            sAmb = RenderSettings.ambientLight; sSky = RenderSettings.ambientSkyColor; sEq = RenderSettings.ambientEquatorColor;
            sGround = RenderSettings.ambientGroundColor; sAmbI = RenderSettings.ambientIntensity;
            float k = 1f;
            try { if (cabinPart != null && cabinPart.vessel != null) k = Mathf.Lerp(nightAmbient, 1f, IvaGlow.Sunlight(cabinPart.vessel, cabinPart.transform.position)); } catch { }
            if (k < 0.999f)
            {
                RenderSettings.ambientLight = sAmb * k; RenderSettings.ambientSkyColor = sSky * k; RenderSettings.ambientEquatorColor = sEq * k;
                RenderSettings.ambientGroundColor = sGround * k; RenderSettings.ambientIntensity = sAmbI * k;
                if (!ambLogged) { ambLogged = true; Debug.Log("[CaelusSmoke] IVA ambient: CAELUS cabin in the dark - ambient x" + k.ToString("F2") + " while the IVA camera renders (" + RenderSettings.ambientMode + ")"); }
            }
            active = true;
            if (!logged) { logged = true; Debug.Log("[CaelusSmoke] IVA shadows: CAELUS cabin - shadow distance " + savedDist + " -> " + QualitySettings.shadowDistance + " m while the IVA camera renders" + (sun != null ? ", IVA sun " + sun.name + " bias " + sunBias + " -> " + sun.shadowBias : "")); }
        }

        void Post(Camera c)
        {
            if (!active || c != ivaCam) return;
            QualitySettings.shadowDistance = savedDist;
            if (sun != null) { sun.shadowBias = sunBias; sun.shadowNormalBias = sunNBias; }
            RestoreAmbient();
            active = false;
        }

        void RestoreAmbient()
        {
            RenderSettings.ambientLight = sAmb; RenderSettings.ambientSkyColor = sSky; RenderSettings.ambientEquatorColor = sEq;
            RenderSettings.ambientGroundColor = sGround; RenderSettings.ambientIntensity = sAmbI;
        }

        public void Destroy()
        {
            if (active) { QualitySettings.shadowDistance = savedDist; if (sun != null) { sun.shadowBias = sunBias; sun.shadowNormalBias = sunNBias; } RestoreAmbient(); active = false; }
            Camera.onPreCull -= Pre; Camera.onPostRender -= Post;
        }
    }
}
