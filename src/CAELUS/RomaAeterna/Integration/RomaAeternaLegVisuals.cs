using System;
using System.Collections.Generic;
using UnityEngine;
using ModuleWheels;

namespace RomaAeterna
{
    public class RomaAeternaLegVisuals : PartModule
    {
        private class Leg
        {
            public Transform host, primary, slider, foot;
            public Transform[] braces = new Transform[2];
            public Transform[] braceSliders = new Transform[2];
            public Vector3 bindDirection;
            public Vector3[] braceDirections = new Vector3[2];
            public ModuleWheelDeployment deployment;
            public ModuleWheelBase wheelBase;
        }
        private readonly List<Leg> legs = new List<Leg>();
        private bool initialized;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            try
            {
                var deployments = part.FindModulesImplementing<ModuleWheelDeployment>();
                for (int i = 1; i <= 4; ++i)
                {
                    string p = "RA_Leg_" + i + "_";
                    var leg = new Leg();
                    leg.host = part.FindModelTransform(p + "Animation");
                    leg.primary = part.FindModelTransform(p + "PrimaryPivot");
                    leg.slider = part.FindModelTransform(p + "PrimarySlider");
                    leg.foot = part.FindModelTransform(p + "FootPivot");
                    if (!leg.host || !leg.primary || !leg.slider || !leg.foot)
                        throw new Exception("Missing kinematic transform for leg " + i);
                    int sx = (i == 1 || i == 3) ? -1 : 1;
                    int sy = (i <= 2) ? -1 : 1;

                    leg.bindDirection = new Vector3(sx * .76f, -2.71f, sy * .76f).normalized;
                    Vector3 anchor = new Vector3(sx * 1.94f, 2.99f, sy * 1.94f);
                    Vector3 foot = new Vector3(sx * 2.70f, .28f, sy * 2.70f);
                    for (int j = 0; j < 2; ++j)
                    {
                        leg.braces[j] = part.FindModelTransform(p + "Brace" + (j + 1) + "Pivot");
                        leg.braceSliders[j] = part.FindModelTransform(p + "Brace" + (j + 1) + "Slider");
                        if (!leg.braces[j] || !leg.braceSliders[j])
                            throw new Exception("Missing drag brace for leg " + i);
                        Vector3 b = (j == 0)
                            ? new Vector3(sx * 2.10f, 1.90f, sy * 1.55f)
                            : new Vector3(sx * 1.55f, 1.90f, sy * 2.10f);
                        leg.braceDirections[j] = (foot - b).normalized;
                    }
                    foreach (var d in deployments)
                        if (d.animationStateName == "RA_LegsDeploy_" + i) leg.deployment = d;
                    if (leg.deployment == null) throw new Exception("Missing stock deployment for leg " + i);
                    leg.wheelBase = part.Modules[leg.deployment.baseModuleIndex] as ModuleWheelBase;
                    if (!leg.wheelBase) throw new Exception("Missing stock contact base for leg " + i);
                    legs.Add(leg);
                }
                initialized = true;
                Debug.Log("[RomaAeterna] Four stock-contact leg visual linkages initialized.");
            }
            catch (Exception e)
            {
                initialized = false;
                Debug.LogError("[RomaAeterna] Leg visual setup failed: " + e);
            }
        }

        public void LateUpdate()
        {
            if (!initialized || !HighLogic.LoadedSceneIsFlight || part.packed) return;
            foreach (var leg in legs)
            {

                if (leg.deployment.Position < .999f) continue;
                Vector3 target = leg.host.InverseTransformPoint(leg.foot.position);
                Vector3 delta = target - leg.primary.localPosition;
                leg.primary.localRotation = Quaternion.FromToRotation(leg.bindDirection, delta.normalized);
                leg.slider.localPosition = leg.bindDirection * delta.magnitude;
                for (int j = 0; j < 2; ++j)
                {
                    Vector3 bd = target - leg.braces[j].localPosition;
                    leg.braces[j].localRotation = Quaternion.FromToRotation(leg.braceDirections[j], bd.normalized);
                    leg.braceSliders[j].localPosition = leg.braceDirections[j] * bd.magnitude;
                }

                Quaternion desired = Quaternion.identity;
                if (leg.wheelBase.Wheel && leg.wheelBase.Wheel.IsGrounded)
                {
                    Vector3 normal = leg.host.InverseTransformDirection(leg.wheelBase.Wheel.currentState.hit.normal);
                    if (normal.sqrMagnitude > .01f)
                        desired = Quaternion.RotateTowards(Quaternion.identity,
                            Quaternion.FromToRotation(Vector3.up, normal.normalized), 15f);
                }
                leg.foot.localRotation = desired;
            }
        }
    }
}
