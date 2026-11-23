using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace RomaAeterna
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class RomaAeternaLadderOwnerCompatibility : MonoBehaviour
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private const float ScanIntervalSeconds = .5f;
        private const int MaximumEvaPerPass = 16;
        private const int LadderLayer = 21;
        private readonly Collider[] overlapBuffer = new Collider[64];
        private readonly HashSet<int> warnedSensors = new HashSet<int>();
        private FieldInfo topField, bottomField, ladderColliderField, currentLadderField;
        private FieldInfo ownerField, fsmField;
        private PropertyInfo reachedProperty, onLadderProperty, startedProperty, currentStateProperty;
        private bool metadataReady;
        private float nextScanTime;

        private void Awake()
        {
            try
            {
                topField = RequireField(typeof(KerbalEVA), "topLadderEnd", typeof(LadderEndCheck));
                bottomField = RequireField(typeof(KerbalEVA), "bottomLadderEnd", typeof(LadderEndCheck));
                ladderColliderField = RequireField(typeof(KerbalEVA), "ladderCollider", typeof(Collider));
                currentLadderField = RequireField(typeof(KerbalEVA), "currentLadder", typeof(Collider));
                ownerField = RequireField(typeof(LadderEndCheck), "kerbalEVA", typeof(KerbalEVA));
                if (ownerField.IsInitOnly || ownerField.IsStatic)
                    throw new InvalidOperationException("Native owner field is not an ordinary instance field.");
                fsmField = RequireField(typeof(KerbalEVA), "fsm", typeof(KerbalFSM));
                reachedProperty = RequireProperty(typeof(LadderEndCheck), "Reached", typeof(bool));
                onLadderProperty = RequireProperty(typeof(KerbalEVA), "OnALadder", typeof(bool));
                startedProperty = RequireProperty(typeof(KerbalFSM), "Started", typeof(bool));
                currentStateProperty = RequireProperty(typeof(KerbalFSM), "CurrentState", typeof(KFSMState));
                metadataReady = true;
            }
            catch (Exception error)
            {

                metadataReady = false;
                Debug.LogError("[RomaAeterna] Native ladder owner compatibility disabled: " +
                    error.GetType().Name + ": " + error.Message);
            }
        }

        private static FieldInfo RequireField(Type type, string name, Type expectedType)
        {
            FieldInfo field = type.GetField(name, Fields);
            if (field == null || field.DeclaringType != type || field.FieldType != expectedType)
                throw new MissingFieldException(type.FullName, name + " of type " + expectedType.FullName);
            return field;
        }

        private static PropertyInfo RequireProperty(Type type, string name, Type expectedType)
        {
            PropertyInfo property = type.GetProperty(name, Fields);
            if (property == null || property.DeclaringType != type || property.PropertyType != expectedType ||
                property.GetGetMethod(true) == null || property.GetIndexParameters().Length != 0)
                throw new MissingMemberException(type.FullName, name + " of type " + expectedType.FullName);
            return property;
        }

        private void FixedUpdate()
        {
            if (!metadataReady || !HighLogic.LoadedSceneIsFlight || FlightGlobals.fetch == null ||
                Time.realtimeSinceStartup < nextScanTime)
                return;
            nextScanTime = Time.realtimeSinceStartup + ScanIntervalSeconds;
            List<Vessel> loadedVessels = FlightGlobals.VesselsLoaded;
            if (loadedVessels == null) return;
            int inspectedEva = 0;
            foreach (Vessel vessel in loadedVessels)
            {
                if (vessel == null || !vessel.loaded || vessel.packed || !vessel.isEVA)
                    continue;
                foreach (Part part in vessel.parts)
                {
                    if (part == null || !part.gameObject.activeInHierarchy || part.vessel != vessel)
                        continue;
                    KerbalEVA eva = null;
                    int count = 0;
                    foreach (PartModule module in part.Modules)
                    {
                        KerbalEVA candidate = module as KerbalEVA;
                        if (candidate == null) continue;
                        eva = candidate;
                        ++count;
                    }

                    if (count != 1 || eva == null || !eva.enabled || !eva.gameObject.activeInHierarchy || eva.part != part)
                        continue;
                    if (++inspectedEva > MaximumEvaPerPass) return;
                    try
                    {
                        Collider ownLadder = FindActualOwnLadderOverlap(eva);
                        if (ownLadder == null) continue;
                        RepairSensor(eva, topField.GetValue(eva) as LadderEndCheck, ownLadder);
                        RepairSensor(eva, bottomField.GetValue(eva) as LadderEndCheck, ownLadder);
                    }
                    catch (Exception error)
                    {
                        WarnOnce(eva.GetInstanceID(), "Skipping native ladder owner check for EVA " +
                            eva.GetInstanceID() + ": " + error.GetType().Name + ": " + error.Message);
                    }
                }
            }
        }

        private Collider FindActualOwnLadderOverlap(KerbalEVA eva)
        {
            Collider nativeCollider = ladderColliderField.GetValue(eva) as Collider;
            if (nativeCollider == null || !nativeCollider.enabled || !nativeCollider.gameObject.activeInHierarchy ||
                nativeCollider.GetComponentInParent<Part>() != eva.part)
                return null;
            Vector3 rawScale = nativeCollider.transform.lossyScale;
            Vector3 scale = new Vector3(Mathf.Abs(rawScale.x), Mathf.Abs(rawScale.y), Mathf.Abs(rawScale.z));
            if (scale.x <= 0 || scale.y <= 0 || scale.z <= 0) return null;
            int count;
            SphereCollider sphere = nativeCollider as SphereCollider;
            BoxCollider box = nativeCollider as BoxCollider;
            CapsuleCollider capsule = nativeCollider as CapsuleCollider;
            if (sphere != null)
            {
                count = Physics.OverlapSphereNonAlloc(sphere.transform.TransformPoint(sphere.center),
                    sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z)), overlapBuffer,
                    1 << LadderLayer, QueryTriggerInteraction.Collide);
            }
            else if (box != null)
            {
                count = Physics.OverlapBoxNonAlloc(box.transform.TransformPoint(box.center),
                    Vector3.Scale(box.size, scale) * .5f, overlapBuffer, box.transform.rotation,
                    1 << LadderLayer, QueryTriggerInteraction.Collide);
            }
            else if (capsule != null)
            {
                int axis = capsule.direction;
                Vector3 direction = axis == 0 ? capsule.transform.right :
                    (axis == 1 ? capsule.transform.up : capsule.transform.forward);
                float alongScale = axis == 0 ? scale.x : (axis == 1 ? scale.y : scale.z);
                float radialScale = axis == 0 ? Mathf.Max(scale.y, scale.z) :
                    (axis == 1 ? Mathf.Max(scale.x, scale.z) : Mathf.Max(scale.x, scale.y));
                float radius = capsule.radius * radialScale;
                float halfSegment = Mathf.Max(0, capsule.height * alongScale * .5f - radius);
                Vector3 center = capsule.transform.TransformPoint(capsule.center);
                count = Physics.OverlapCapsuleNonAlloc(center - direction * halfSegment,
                    center + direction * halfSegment, radius, overlapBuffer,
                    1 << LadderLayer, QueryTriggerInteraction.Collide);
            }
            else
            {

                return null;
            }
            if (count >= overlapBuffer.Length)
            {
                Array.Clear(overlapBuffer, 0, overlapBuffer.Length);
                WarnOnce(eva.GetInstanceID(), "Native ladder overlap buffer full; owner repair skipped.");
                return null;
            }
            Collider current = currentLadderField.GetValue(eva) as Collider;
            bool onLadder = (bool)onLadderProperty.GetValue(eva, null);
            Collider candidateLadder = null;
            for (int index = 0; index < count; ++index)
            {
                Collider hit = overlapBuffer[index];
                overlapBuffer[index] = null;
                if (!IsOwnAquilaLadder(hit)) continue;
                if (Physics.GetIgnoreLayerCollision(nativeCollider.gameObject.layer, hit.gameObject.layer) ||
                    Physics.GetIgnoreCollision(nativeCollider, hit)) continue;
                if (candidateLadder == null || (onLadder && hit == current)) candidateLadder = hit;
            }
            return candidateLadder;
        }

        private static bool IsOwnAquilaLadder(Collider collider)
        {
            if (collider == null || !(collider is BoxCollider) || !collider.enabled || !collider.isTrigger ||
                collider.gameObject.layer != LadderLayer || !collider.gameObject.activeInHierarchy ||
                !collider.CompareTag("Ladder")) return false;
            Part part = collider.GetComponentInParent<Part>();
            if (part == null || part.partInfo == null || part.vessel == null ||
                !part.vessel.loaded || part.vessel.packed || !part.gameObject.activeInHierarchy)
                return false;
            string name = part.partInfo.name.Replace('_', '.');
            if (name == "RomaAeterna.AquilaAscentVehicle")
                return collider.name == "RA_EVA_HatchSteps" || collider.name == "RA_EVA_UpperSteps";
            if (name == "RomaAeterna.AquilaDescentStage")
                return collider.name == "RA_EVA_DescentLadder";
            return false;
        }

        private void RepairSensor(KerbalEVA eva, LadderEndCheck sensor, Collider ladder)
        {
            if (sensor == null || sensor.GetType() != typeof(LadderEndCheck) ||
                sensor.GetComponentInParent<Part>() != eva.part ||
                sensor.GetComponentInParent<KerbalEVA>() != eva ||
                !sensor.gameObject.activeInHierarchy)
                return;
            object rawOwner = ownerField.GetValue(sensor);
            if (object.ReferenceEquals(rawOwner, eva)) return;
            object fsm = fsmField.GetValue(eva);
            if (fsm == null || !(bool)startedProperty.GetValue(fsm, null)) return;
            bool beforeReached = (bool)reachedProperty.GetValue(sensor, null);
            object beforeState = currentStateProperty.GetValue(fsm, null);
            Vector3 beforePosition = eva.part.transform.position;
            Quaternion beforeRotation = eva.part.transform.rotation;
            Part ladderPart = ladder.GetComponentInParent<Part>();
            string beforeProof = "field=" + ownerField.DeclaringType.FullName + "." + ownerField.Name +
                ",fieldType=" + ownerField.FieldType.FullName +
                ",assembly=" + ownerField.DeclaringType.Assembly.GetName().Name +
                ",rawClrNull=" + object.ReferenceEquals(rawOwner, null) +
                ",oldOwner=" + OwnerIdentity(rawOwner) +
                ",expectedEva=" + eva.GetInstanceID() +
                ",evaVessel=" + eva.vessel.id + ",evaPart=" + eva.part.flightID +
                ",sensor=" + sensor.name + ":" + sensor.GetInstanceID() +
                ",sensorActive=" + sensor.gameObject.activeInHierarchy + ",sensorEnabled=" + sensor.enabled +
                ",loaded=" + eva.vessel.loaded + ",packed=" + eva.vessel.packed +
                ",ownLadder=" + ladder.name + ":" + ladderPart.flightID +
                ",qualification=exactNativeLadderColliderPhysicsOverlap" +
                ",onALadder=" + onLadderProperty.GetValue(eva, null) +
                ",reached=" + beforeReached + ",fsm=" + beforeState +
                ",position=" + FormatVector(beforePosition);
            Debug.Log("[RomaAeterna] Native ladder owner repair BEFORE: " + beforeProof);

            ownerField.SetValue(sensor, eva);

            bool identityReadback = object.ReferenceEquals(ownerField.GetValue(sensor), eva);
            bool reachedUnchanged = beforeReached == (bool)reachedProperty.GetValue(sensor, null);
            bool stateUnchanged = object.ReferenceEquals(beforeState, currentStateProperty.GetValue(fsm, null));
            bool poseUnchanged = beforePosition.Equals(eva.part.transform.position) &&
                beforeRotation.Equals(eva.part.transform.rotation);
            Debug.Log("[RomaAeterna] Native ladder owner repair AFTER: sensor=" + sensor.GetInstanceID() +
                ",ownerMatches=" + identityReadback + ",reachedUnchanged=" + reachedUnchanged +
                ",fsmUnchanged=" + stateUnchanged + ",poseUnchanged=" + poseUnchanged +
                ",nativeReached=" + reachedProperty.GetValue(sensor, null) +
                ". No flag, FSM, joint, position, collider, collision-filter or input writes; " +
                "only subsequent native callbacks may clear Reached.");
            if (!identityReadback || !reachedUnchanged || !stateUnchanged || !poseUnchanged)
            {
                metadataReady = false;
                Debug.LogError("[RomaAeterna] Native ladder owner proof failed; further compatibility repairs disabled.");
            }
        }

        private string OwnerIdentity(object rawOwner)
        {
            if (object.ReferenceEquals(rawOwner, null)) return "CLR-null";
            KerbalEVA owner = rawOwner as KerbalEVA;
            if (owner == null) return "Unity-null-or-invalid:" + rawOwner.GetType().FullName;
            object fsm = fsmField.GetValue(owner);
            return owner.GetInstanceID() + ":unityNull=false:part=" +
                (owner.part == null ? "null" : owner.part.flightID.ToString(CultureInfo.InvariantCulture)) +
                ":fsmStarted=" + (fsm == null ? "null" : startedProperty.GetValue(fsm, null).ToString()) +
                ":onALadder=" + (fsm == null ? "unavailable" : onLadderProperty.GetValue(owner, null).ToString());
        }

        private static string FormatVector(Vector3 vector)
        {
            return vector.x.ToString("R", CultureInfo.InvariantCulture) + "/" +
                vector.y.ToString("R", CultureInfo.InvariantCulture) + "/" +
                vector.z.ToString("R", CultureInfo.InvariantCulture);
        }

        private void WarnOnce(int id, string message)
        {
            if (warnedSensors.Add(id)) Debug.LogWarning("[RomaAeterna] " + message);
        }
    }
}
