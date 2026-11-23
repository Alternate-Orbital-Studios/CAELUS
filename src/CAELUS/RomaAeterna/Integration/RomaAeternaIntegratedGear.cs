using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using ModuleWheels;
using VehiclePhysics;

namespace RomaAeterna
{
    public class RomaAeternaIntegratedGear : PartModule
    {
        private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo BaseWheel = RequiredField(typeof(ModuleWheelBase), "wheel");
        private static readonly FieldInfo BaseHost = RequiredField(typeof(ModuleWheelBase), "wheelHost");
        private static readonly FieldInfo BaseColliderHost = RequiredField(typeof(ModuleWheelBase), "wheelColliderHost");
        private static readonly FieldInfo SubWheel = RequiredField(typeof(ModuleWheelSubmodule), "wheel");
        private static readonly FieldInfo SubBase = RequiredField(typeof(ModuleWheelSubmodule), "wheelBase");
        private static readonly FieldInfo SubReady = RequiredField(typeof(ModuleWheelSubmodule), "baseSetup");
        private static readonly FieldInfo BalancedSuspensions = RequiredField(typeof(SuspensionLoadBalancer), "suspensionModules");
        private static readonly FieldInfo ControllerBody = RequiredField(typeof(KSPWheelController), "cachedRigidbody");
        private static readonly MethodInfo InopUpdate = RequiredMethod(typeof(ModuleWheelBase), "InopUpdate");
        private static readonly MethodInfo GeeBias = RequiredMethod(typeof(ModuleWheelBase), "ApplyGeeBias");
        private static readonly MethodInfo ToggleDeployment = RequiredMethod(typeof(ModuleWheelDeployment), "ToggleDeployment");
        private static readonly MethodInfo DeploymentStateChanged = RequiredMethod(typeof(ModuleWheelDeployment), "OnFSMStateChange");
        private static readonly FieldInfo DeploymentAnimation = RequiredField(typeof(ModuleWheelDeployment), "animationState");
        private static readonly MethodInfo IgnoreForces = RequiredMethod(typeof(ModuleWheelBase), "IgnoreForcesOnSameVesselContact");
        private readonly List<ModuleWheelBase> bases = new List<ModuleWheelBase>();
        private readonly List<KSPWheelController> controllers = new List<KSPWheelController>();
        private readonly List<GameObject> hosts = new List<GameObject>();
        private readonly List<Transform> animationBases = new List<Transform>();
        private readonly List<ModuleWheelSuspension> balanceAdditions = new List<ModuleWheelSuspension>();
        private SuspensionLoadBalancer loadBalancer;
        private bool started, bound, failed;
        private Rigidbody body;

        public int ControllerCount { get { return controllers.Count; } }
        public bool BindingsValid { get { return bound && ValidateBindings(); } }
        public int RegisteredSuspensionCount
        {
            get
            {
                if (!loadBalancer) return 0;
                var list = (List<ModuleWheelSuspension>)BalancedSuspensions.GetValue(loadBalancer);
                if (list == null) return 0;
                int count = 0;
                foreach (var suspension in part.FindModulesImplementing<ModuleWheelSuspension>())
                    if (bases.Contains((ModuleWheelBase)SubBase.GetValue(suspension)) && list.Contains(suspension)) ++count;
                return count;
            }
        }

        private static FieldInfo RequiredField(Type type, string name)
        {
            var result = type.GetField(name, InstanceFields);
            if (result == null) throw new MissingFieldException(type.FullName, name);
            return result;
        }
        private static MethodInfo RequiredMethod(Type type, string name)
        {
            var result = type.GetMethod(name, InstanceFields);
            if (result == null) throw new MissingMethodException(type.FullName, name);
            return result;
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (!HighLogic.LoadedSceneIsFlight) return;
            foreach (var module in part.FindModulesImplementing<ModuleWheelBase>())
                if (module.wheelColliderTransformName.StartsWith("RA_Leg_", StringComparison.Ordinal)) bases.Add(module);
            started = bases.Count == 4;
            if (!started)
            {
                failed = true;
                Debug.LogError("[RomaAeterna] Integrated gear requires exactly four named stock wheel bases.");
            }
        }

        public void LateUpdate()
        {
            if (!started || failed || !HighLogic.LoadedSceneIsFlight || !part || part.packed || !part.Rigidbody) return;
            try
            {
                if (!bound)
                {
                    foreach (var wheelBase in bases) if (!wheelBase.Wheel) return;
                    BindFourControllers();
                }
                else if (body != part.Rigidbody)
                {
                    body = part.Rigidbody;
                    for (int i = 0; i < controllers.Count; ++i)
                    {
                        bool enabled = controllers[i].enabled;
                        controllers[i].enabled = false;
                        MatchAnimationBasis(hosts[i].transform, animationBases[i]);
                        ControllerBody.SetValue(controllers[i], body);
                        controllers[i].SetRigidbodyTarget(body);
                        controllers[i].enabled = enabled;
                    }
                }
                if (bound) EnsureSuspensionsRegistered();
            }
            catch (Exception e)
            {
                failed = true;
                Debug.LogError("[RomaAeterna] Independent landing contact initialization failed: " + e);
                foreach (var controller in controllers) if (controller) controller.enabled = false;
            }
        }

        private static void CopyScalarSettings(object source, object target, Type declaredType)
        {
            foreach (var field in declaredType.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                Type t = field.FieldType;
                if (t.IsPrimitive || t.IsEnum || t == typeof(Vector2) || t == typeof(Vector3))
                    field.SetValue(target, field.GetValue(source));
            }
        }

        private void RemoveBalanceAdditions()
        {
            if (loadBalancer)
            {
                var list = (List<ModuleWheelSuspension>)BalancedSuspensions.GetValue(loadBalancer);
                if (list != null) foreach (var added in balanceAdditions) list.Remove(added);
            }
            balanceAdditions.Clear();
        }

        private void EnsureSuspensionsRegistered()
        {

            var current = part.vessel.FindVesselModuleImplementing<SuspensionLoadBalancer>();
            if (loadBalancer != current)
            {
                RemoveBalanceAdditions();
                loadBalancer = current;
            }
            if (!loadBalancer) return;
            var list = (List<ModuleWheelSuspension>)BalancedSuspensions.GetValue(loadBalancer);
            if (list == null) return;
            bool changed = false;
            foreach (var suspension in part.FindModulesImplementing<ModuleWheelSuspension>())
            {
                if (!bases.Contains((ModuleWheelBase)SubBase.GetValue(suspension)) || list.Contains(suspension)) continue;
                list.Add(suspension);
                if (!balanceAdditions.Contains(suspension)) balanceAdditions.Add(suspension);
                changed = true;
            }

            if (changed) Debug.Log("[RomaAeterna] Registered " + RegisteredSuspensionCount +
                " integrated leg suspensions with the existing stock vessel load balancer.");
        }

        private void MatchAnimationBasis(Transform host, Transform animationBasis)
        {
            if (!animationBasis || !body) throw new Exception("Missing controller animation coordinate basis.");

            host.SetParent(animationBasis.parent, false);
            host.localPosition = animationBasis.localPosition;
            host.localRotation = animationBasis.localRotation;
            host.localScale = animationBasis.localScale;
            host.SetParent(body.transform, true);
        }

        private void BindFourControllers()
        {
            body = part.Rigidbody;
            var oldControllers = new HashSet<KSPWheelController>();
            var wheelObjects = new List<Transform>();
            var sourceSettings = bases[0].Wheel;
            foreach (var wheelBase in bases)
            {
                oldControllers.Add(wheelBase.Wheel);
                Transform wheel = (Transform)BaseColliderHost.GetValue(wheelBase);
                if (!wheel || !wheel.GetComponent<VPWheelCollider>())
                    throw new Exception("Missing stock-created VP contact for " + wheelBase.wheelColliderTransformName);
                wheelObjects.Add(wheel);
            }

            foreach (var old in oldControllers) if (old) old.enabled = false;
            for (int i = 0; i < bases.Count; ++i)
            {
                var host = new GameObject("RomaAeterna_LegController_" + (i + 1));
                Transform animationBasis = part.FindModelTransform("RA_Leg_" + (i + 1) + "_Animation");
                MatchAnimationBasis(host.transform, animationBasis);
                var contact = wheelObjects[i].GetComponent<VPWheelCollider>();
                bool contactEnabled = contact.enabled;

                contact.enabled = false;

                wheelObjects[i].SetParent(host.transform, true);
                var controller = KSPWheelController.Create(body, host, wheelObjects[i].gameObject);
                controller.enabled = false;
                CopyScalarSettings(sourceSettings, controller, typeof(VehicleBase));
                CopyScalarSettings(sourceSettings, controller, typeof(KSPWheelController));
                ControllerBody.SetValue(controller, body);
                controller.SetRigidbodyTarget(body);
                controller.gravity.Value = part.vessel.gravityForPos;
                controller.OnShouldIgnoreForces = (Func<VehicleBase.WheelState, bool>)Delegate.CreateDelegate(
                    typeof(Func<VehicleBase.WheelState, bool>), bases[i], IgnoreForces);
                controller.tireFriction.model = (TireFriction.Model)1;
                controller.Initialize();
                contact.enabled = contactEnabled;
                hosts.Add(host); controllers.Add(controller); animationBases.Add(animationBasis);
                BaseWheel.SetValue(bases[i], controller);
                BaseHost.SetValue(bases[i], host);
            }

            foreach (var submodule in part.FindModulesImplementing<ModuleWheelSubmodule>())
            {
                int index = bases.IndexOf((ModuleWheelBase)SubBase.GetValue(submodule));
                if (index < 0) continue;
                SubWheel.SetValue(submodule, controllers[index]);
                SubReady.SetValue(submodule, true);
            }
            foreach (var submodule in part.FindModulesImplementing<ModuleWheelSubmodule>())
            {
                if (bases.IndexOf((ModuleWheelBase)SubBase.GetValue(submodule)) < 0) continue;
                RequiredMethod(submodule.GetType(), "OnWheelSetup").Invoke(submodule, null);
            }
            foreach (var suspension in part.FindModulesImplementing<ModuleWheelSuspension>())
            {
                int index = bases.IndexOf((ModuleWheelBase)SubBase.GetValue(suspension));
                if (index < 0) continue;
                var contact = controllers[index].wheelCollider;

                suspension.suspensionOffset = -contact.groundPenetration;
                contact.suspensionOffset = suspension.suspensionOffset;
            }
            for (int i = 0; i < bases.Count; ++i)
            {
                GeeBias.Invoke(bases[i], new object[] { (float)part.vessel.mainBody.GeeASL });
                InopUpdate.Invoke(bases[i], new object[] { true });
                controllers[i].SetRigidbodyTarget(body);
            }
            bound = ValidateBindings();
            if (!bound) throw new Exception("Contact/controller/submodule identity check failed.");
            Debug.Log("[RomaAeterna] Four independent stock landing contacts bound to one descent-stage Rigidbody. " + Diagnostic());
        }

        private bool ValidateBindings()
        {
            if (controllers.Count != 4 || !body) return false;
            var ids = new HashSet<int>();
            var contactIds = new HashSet<int>();
            for (int i = 0; i < 4; ++i)
            {
                var c = controllers[i];
                if (!c || bases[i].Wheel != c || c.rb != body || c.RbTgt != body || !c.wheelCollider) return false;
                if (c.OnShouldIgnoreForces == null || !System.Object.ReferenceEquals(c.OnShouldIgnoreForces.Target, bases[i])) return false;
                if (c.wheelCollider.enabled && c.wheelCollider.gameObject.activeInHierarchy && c.wheelCollider.vehicle != c)
                    return false;
                if (!animationBases[i] || (hosts[i].transform.position - animationBases[i].position).sqrMagnitude > 1e-6f ||
                    Quaternion.Angle(hosts[i].transform.rotation, animationBases[i].rotation) > .01f ||
                    (hosts[i].transform.lossyScale - animationBases[i].lossyScale).sqrMagnitude > 1e-6f)
                    return false;
                ids.Add(c.GetInstanceID()); contactIds.Add(c.wheelCollider.GetInstanceID());
            }
            foreach (var submodule in part.FindModulesImplementing<ModuleWheelSubmodule>())
            {
                int index = bases.IndexOf((ModuleWheelBase)SubBase.GetValue(submodule));
                if (index >= 0 && (KSPWheelController)SubWheel.GetValue(submodule) != controllers[index]) return false;
            }
            return ids.Count == 4 && contactIds.Count == 4;
        }

        public string Diagnostic()
        {
            var rows = new List<string>();
            for (int i = 0; i < controllers.Count; ++i)
            {
                var c = controllers[i];
                rows.Add("leg=" + (i + 1) + ",controller=" + c.GetInstanceID() + ",contact=" + c.wheelCollider.GetInstanceID()
                    + ",body=" + (c.rb ? c.rb.GetInstanceID().ToString() : "missing") + ",grounded=" + c.IsGrounded
                    + ",basisError=" + (hosts[i].transform.position - animationBases[i].position).magnitude
                    + ",visualOffset=" + c.wheelCollider.suspensionOffset
                    + ",stockBalanceCount=" + RegisteredSuspensionCount);
            }
            return string.Join("; ", rows.ToArray());
        }

        public void SetLegsDeployed(bool deploy)
        {
            Debug.Log("[RomaAeterna] Whole-leg " + (deploy ? "deploy" : "retract") +
                " requested in " + HighLogic.LoadedScene + ".");
            if (HighLogic.LoadedSceneIsEditor)
            {
                SetEditorLegPose(deploy);
                return;
            }
            foreach (var d in part.FindModulesImplementing<ModuleWheelDeployment>())
            {
                if (d.fsm == null)
                {
                    Debug.LogWarning("[RomaAeterna] Landing-leg command deferred: stock deployment FSM is not initialized.");
                    continue;
                }

                var current = d.fsm.CurrentState;
                bool alreadyRequested = deploy ?
                    current == d.st_deployed || current == d.st_deploying :
                    current == d.st_retracted || current == d.st_retracting;
                if (!alreadyRequested) ToggleDeployment.Invoke(d, new object[] { false });
            }
        }

        private void SetEditorLegPose(bool deploy)
        {

            int count = 0;
            try
            {
                foreach (var d in part.FindModulesImplementing<ModuleWheelDeployment>())
                {
                    if (!d.animationTrfName.StartsWith("RA_Leg_", StringComparison.Ordinal)) continue;
                    var state = (AnimationState)DeploymentAnimation.GetValue(d);
                    var host = part.FindModelTransform(d.animationTrfName);
                    var animation = host ? host.GetComponent<Animation>() : null;
                    var target = deploy ? d.st_deployed : d.st_retracted;
                    if (d.fsm == null || target == null || state == null || !animation)
                        throw new Exception("Editor landing animation/FSM is not initialized: " + d.animationTrfName);
                    var previous = d.fsm.CurrentState;
                    d.fsm.StartFSM(target);
                    DeploymentStateChanged.Invoke(d, new object[] {
                        previous, target, deploy ? d.on_deployed : d.on_retracted });
                    state.enabled = true;
                    state.weight = 1f;
                    animation.Sample();
                    float endpoint = deploy ? d.deployedPosition : d.retractedPosition;
                    if (Mathf.Abs(state.normalizedTime - endpoint) > .0001f ||
                        Mathf.Abs(d.Position - endpoint) > .0001f)
                        throw new Exception("Editor landing endpoint did not apply: " + d.animationTrfName);
                    ++count;
                }
                if (count != 4) throw new Exception("Editor preview requires all four Aquila landing clips; found " + count + ".");
                if (EditorLogic.fetch && EditorLogic.fetch.ship != null)
                    GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
                Debug.Log("[RomaAeterna] Editor preview sampled all four stock leg clips at " +
                    (deploy ? "deployed" : "retracted") + " craft-save endpoints.");
            }
            catch (Exception e)
            {
                Debug.LogError("[RomaAeterna] Editor landing-leg preview failed: " + e);
            }
        }

        [KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "Deploy all landing legs")]
        public void DeployAll() { SetLegsDeployed(true); }
        [KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "Retract all landing legs")]
        public void RetractAll() { SetLegsDeployed(false); }

        public void OnDestroy()
        {
            RemoveBalanceAdditions();
            foreach (var host in hosts) if (host) UnityEngine.Object.Destroy(host);
        }
    }
}
