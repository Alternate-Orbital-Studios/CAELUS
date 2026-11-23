using System;
using System.Collections.Generic;
using UnityEngine;

namespace Caelus
{
    public class ModuleCaelusLaunchTower : PartModule
    {
        const string PivotName = "SERVICE_ARM_R3_SWING_PIVOT_CONTROL_ART_ZERO", ArmPrefix = "CLD_R9_SERVICE_ARM_COPY_";
        [KSPField] public float rocketDistance = 13f;
        [KSPField] public float swingDeg = 80f, swingSeconds = 4f, stagger = 0.25f, latch = 0.4f;
        [KSPField] public string craneName = "CLD_R6_ROOF_HAMMERHEAD_CRANE_ART";
        [KSPField] public float craneDeg = 90f, craneSeconds = 9f, cranePivotZ = 7f;
        [KSPField(isPersistant = true)] public bool retracted = false;
        [KSPField(isPersistant = true)] public bool autoRetract = true;

        class Arm { public Transform pivot; public Quaternion rest; public float delay; }
        readonly List<Arm> arms = new List<Arm>(); Transform cranePivot; Quaternion craneRest;
        float f, fc; bool ready;

        public bool ArmsSwinging(float clearAt)
        {
            if (!ready || arms.Count == 0) return false;
            if (!retracted && autoRetract) Retract();
            return retracted && f < clearAt;
        }

        public override void OnStart(StartState state)
        {
            try { Setup(); } catch (Exception e) { Debug.LogWarning("[CaelusLaunchTower] setup failed: " + e); }
            f = fc = retracted ? 1f : 0f; Apply(); UpdateEvents();
        }

        void Setup()
        {
            Transform model = part.transform.Find("model"); if (model == null) return;
            var markers = new List<Transform>();
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                if (t.name == PivotName && t.parent != null && t.parent.name.StartsWith(ArmPrefix)) markers.Add(t);

            var byArm = new Dictionary<Transform, Transform>();
            foreach (Transform mk in markers) if (!byArm.ContainsKey(mk.parent)) byArm[mk.parent] = mk;
            var list = new List<Transform>(byArm.Values);
            list.Sort((a, b) => part.transform.InverseTransformPoint(a.position).y.CompareTo(part.transform.InverseTransformPoint(b.position).y));
            var filters = new List<MeshFilter>();
            foreach (MeshFilter mf in model.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null && mf.sharedMesh.isReadable && mf.GetComponent<MeshRenderer>() != null && !mf.name.StartsWith(craneName) && !mf.name.StartsWith("HWARM")) filters.Add(mf);

            var named = new List<Transform>();
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("HWARM")) named.Add(t);
            int k = 0, idx = -1;
            foreach (Transform mk in list)
            {
                idx++;
                Vector3 p = part.transform.InverseTransformPoint(mk.position);
                float dist = p.z + rocketDistance; if (dist < 3f) continue;
                Func<Vector3, bool> inside = q =>
                {
                    float along = p.z - q.z, side = q.x - p.x;
                    return along > 0.3f && along < dist - 4f && Mathf.Abs(side) < 3f && q.y > p.y - 6f && q.y < p.y + 4f;
                };
                var go = new GameObject("CaelusTowerArmPivot_" + k); go.transform.SetParent(model, false);
                go.transform.position = mk.position; go.transform.rotation = part.transform.rotation;
                int tris = 0; foreach (MeshFilter mf in filters) tris += Cut(mf, inside, go.transform);
                int hung = 0; string pre = "HWARM" + idx + "_";
                foreach (Transform t in named) if (t != null && t.name.StartsWith(pre)) { t.SetParent(go.transform, true); hung++; }
                if (tris < 100 && hung == 0) { Destroy(go); continue; }
                arms.Add(new Arm { pivot = go.transform, rest = go.transform.localRotation, delay = k++ * stagger });
                Debug.Log("[CaelusLaunchTower] arm " + k + " at " + p.y.ToString("F1") + " m: " + tris + " triangles, " + hung + " hardware nodes");
            }
            Transform crane = null;
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true)) if (t.name == craneName) { crane = t; break; }
            if (crane != null)
            {
                var cp = new GameObject("CaelusTowerCranePivot"); cp.transform.SetParent(model, false);
                Vector3 cl = part.transform.InverseTransformPoint(crane.position);
                cp.transform.position = part.transform.TransformPoint(new Vector3(0f, cl.y, cranePivotZ)); cp.transform.rotation = part.transform.rotation;
                crane.SetParent(cp.transform, true); cranePivot = cp.transform; craneRest = cp.transform.localRotation;
            }
            ready = true;
            Debug.Log("[CaelusLaunchTower] " + arms.Count + " arms, crane " + (cranePivot != null));
        }

        int Cut(MeshFilter mf, Func<Vector3, bool> inside, Transform pivot)
        {
            Mesh m = mf.sharedMesh; Vector3[] v = m.vertices; bool[] inV = new bool[v.Length]; bool any = false;
            Matrix4x4 toPart = part.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            for (int i = 0; i < v.Length; i++) { inV[i] = inside(toPart.MultiplyPoint3x4(v[i])); any |= inV[i]; }
            if (!any) return 0;
            var keepSubs = new List<int[]>(); var cutSubs = new List<int[]>(); int cut = 0;
            for (int s = 0; s < m.subMeshCount; s++)
            {
                int[] t = m.GetTriangles(s); var keep = new List<int>(t.Length); var take = new List<int>();
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    if (inV[t[i]] && inV[t[i + 1]] && inV[t[i + 2]]) { take.Add(t[i]); take.Add(t[i + 1]); take.Add(t[i + 2]); }
                    else { keep.Add(t[i]); keep.Add(t[i + 1]); keep.Add(t[i + 2]); }
                }
                keepSubs.Add(keep.ToArray()); cutSubs.Add(take.ToArray()); cut += take.Count / 3;
            }
            if (cut == 0) return 0;
            Mesh rest = Instantiate(m); rest.name = m.name + "_noarms";
            for (int s = 0; s < keepSubs.Count; s++) rest.SetTriangles(keepSubs[s], s);
            mf.sharedMesh = rest;
            Mesh piece = Instantiate(m); piece.name = m.name + "_arm";
            for (int s = 0; s < cutSubs.Count; s++) piece.SetTriangles(cutSubs[s], s);
            var go = new GameObject(mf.name + "_arm"); go.layer = mf.gameObject.layer;
            go.transform.SetParent(pivot, false);
            go.transform.position = mf.transform.position; go.transform.rotation = mf.transform.rotation;
            Vector3 ls = mf.transform.lossyScale, ps = pivot.lossyScale;
            go.transform.localScale = new Vector3(ls.x / ps.x, ls.y / ps.y, ls.z / ps.z);
            go.AddComponent<MeshFilter>().sharedMesh = piece;
            var src = mf.GetComponent<MeshRenderer>(); var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = src.sharedMaterials; mr.shadowCastingMode = src.shadowCastingMode; mr.receiveShadows = src.receiveShadows;
            return cut;
        }

        static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * t * (t * (t * 6f - 15f) + 10f); }

        void Apply()
        {
            float total = swingSeconds + latch + stagger * Mathf.Max(0, arms.Count - 1);
            for (int i = 0; i < arms.Count; i++)
            {
                Arm a = arms[i]; if (a.pivot == null) continue;
                float t = (f * total - latch - a.delay) / swingSeconds;
                a.pivot.localRotation = a.rest * Quaternion.AngleAxis(swingDeg * Ease(t), Vector3.up);
            }
            if (cranePivot != null) cranePivot.localRotation = craneRest * Quaternion.AngleAxis(craneDeg * Ease(fc), Vector3.up);
        }

        public void Update()
        {
            if (!ready) return;
            if (HighLogic.LoadedSceneIsFlight && autoRetract && !retracted && vessel != null && Time.frameCount % 10 == 0)
            {
                foreach (Part p in vessel.parts)
                {
                    foreach (PartModule pm in p.Modules)
                    {
                        var en = pm as ModuleEngines;
                        if (en != null && en.EngineIgnited && en.finalThrust > 1f) { Retract(); break; }
                    }
                    if (retracted) break;
                }
            }
            float total = swingSeconds + latch + stagger * Mathf.Max(0, arms.Count - 1);
            float target = retracted ? 1f : 0f, dt = Time.deltaTime;
            bool moved = false;
            if (f != target) { f = Mathf.MoveTowards(f, target, dt / total); moved = true; }
            if (fc != target) { fc = Mathf.MoveTowards(fc, target, dt / craneSeconds); moved = true; }
            if (moved) Apply();
        }

        void UpdateEvents()
        {
            Events["Retract"].active = !retracted; Events["Extend"].active = retracted;
            Events["ToggleAuto"].guiName = "Auto-retract at ignition: " + (autoRetract ? "ON" : "OFF");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "Retract service arms")]
        public void Retract() { retracted = true; UpdateEvents(); }

        [KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "Extend service arms")]
        public void Extend() { retracted = false; UpdateEvents(); }

        [KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "Auto-retract at ignition: ON")]
        public void ToggleAuto() { autoRetract = !autoRetract; UpdateEvents(); }

        [KSPAction("Retract service arms")] public void RetractAction(KSPActionParam p) { Retract(); }
        [KSPAction("Extend service arms")] public void ExtendAction(KSPActionParam p) { Extend(); }
        [KSPAction("Toggle service arms")] public void ToggleAction(KSPActionParam p) { if (retracted) Extend(); else Retract(); }

        public override string GetInfo()
        {
            return "Service tower with six swing arms (the top one is the crew access arm) and a roof crane. Fits the CAELUS launch mount's tower node, so it always stands in "
                 + "the right place. The arms swing back when the engines ignite (or use the part menu / action groups); preview it in the VAB.";
        }
    }
}

namespace Caelus
{

    public class ModuleCaelusLaunchMount : PartModule
    {
        [KSPField] public float releaseTWR = 1.05f;
        [KSPField(isPersistant = true)] public bool armed = false;
        [KSPField(guiActive = true, guiName = "Hold-down")] public string holdStatus = "holding";
        [KSPField] public float armsClearAt = 0.85f, armHoldMax = 6f;
        bool released; FixedJoint anchor; float unpackedAt = -1f, liftAt = -1f; bool warpTested, contactLogged, settled, liftChecked;
        const bool EnableSettle = false;
        [KSPField] public bool selfTestWarp = false;

        public override bool IsStageable() { return true; }
        public override bool StagingEnabled() { return true; }
        public override void OnActive() { if (!released) { armed = true; holdStatus = "armed - releases at TWR " + releaseTWR.ToString("F2"); Debug.Log("[CaelusLaunchMount] release armed"); } }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || vessel.packed || part.Rigidbody == null) return;

            if (unpackedAt < 0f) unpackedAt = Time.time;
            if (anchor == null)
            {

                if (!settled) { settled = true; if (EnableSettle && vessel.situation == Vessel.Situations.PRELAUNCH) SettleOnGround(); }
                bool onGround = vessel.situation == Vessel.Situations.PRELAUNCH || vessel.situation == Vessel.Situations.LANDED;

                if (onGround && vessel.srfSpeed < 0.2)
                {
                    if (!liftChecked) { liftChecked = true; LiftOutOfDeck(); }
                    anchor = part.gameObject.AddComponent<FixedJoint>();
                    anchor.breakForce = float.PositiveInfinity; anchor.breakTorque = float.PositiveInfinity;
                    Debug.Log("[CaelusLaunchMount] pinned to the pad (" + vessel.situation + ", contact " + part.GroundContact + ")");
                }
            }

            if (!contactLogged && anchor != null && Time.time - unpackedAt > 6f) { contactLogged = true; Debug.Log("[CaelusLaunchMount] real ground contact after 6 s: " + part.GroundContact + ", vessel landed " + vessel.Landed); }
            if (anchor != null && (vessel.situation == Vessel.Situations.PRELAUNCH || vessel.situation == Vessel.Situations.LANDED))
            { part.GroundContact = true; vessel.Landed = true; }
            if (selfTestWarp && !warpTested && anchor != null && Time.time - unpackedAt > 12f && vessel == FlightGlobals.ActiveVessel)
            {
                warpTested = true; TimeWarp.SetRate(1, false, true);
                Debug.Log("[CaelusLaunchMount] warp self-test: asked for rails warp x" + TimeWarp.fetch.warpRates[1] + ", landed " + vessel.Landed + " " + vessel.situation);
                StartCoroutine(WarpReport());
            }
            if (!armed || released || part.parent == null) return;

            double mass = 0, thrust = 0; Vector3d up = (vessel.CoMD - vessel.mainBody.position).normalized;
            foreach (Part p in vessel.parts)
            {
                if (p == part || p.parent == part) continue;
                mass += p.mass + p.GetResourceMass();
                foreach (PartModule pm in p.Modules)
                {
                    var e = pm as ModuleEngines; if (e == null || !e.EngineIgnited) continue;
                    foreach (Transform t in e.thrustTransforms) thrust += Vector3d.Dot(-t.forward, up) * e.finalThrust / Mathf.Max(1, e.thrustTransforms.Count);
                }
            }
            double g = vessel.mainBody.gravParameter / (vessel.CoMD - vessel.mainBody.position).sqrMagnitude;
            double twr = mass > 0 ? thrust / (mass * g) : 0;
            holdStatus = "armed - TWR " + twr.ToString("F2") + " / " + releaseTWR.ToString("F2");
            if (twr < releaseTWR) { liftAt = -1f; return; }

            if (liftAt < 0f) liftAt = Time.time;
            if (Time.time - liftAt < armHoldMax)
                foreach (Part p in part.children)
                {
                    var tw = p.FindModuleImplementing<ModuleCaelusLaunchTower>();
                    if (tw != null && tw.ArmsSwinging(armsClearAt)) { holdStatus = "armed - holding until the service arms are clear"; return; }
                }
            Release("TWR " + twr.ToString("F2"));
        }

        void OnDestroy() { GameEvents.onVesselGoOffRails.Remove(OnOffRails); GameEvents.onVesselGoOnRails.Remove(OnOnRails); }
        void OnOffRails(Vessel v) { if (v == vessel) unpackedAt = Time.time; }

        void SettleOnGround()
        {
            try
            {
                Vector3 up = (part.transform.position - (Vector3)vessel.mainBody.position).normalized;
                var lows = new System.Collections.Generic.List<Vector3>(); float lowest = float.MaxValue;
                foreach (Collider c in part.GetComponentsInChildren<Collider>(true))
                {
                    if (c == null || c.isTrigger || !c.enabled) continue;
                    Vector3 lp = Vector3.zero; float ld = float.MaxValue;
                    MeshCollider mc = c as MeshCollider; BoxCollider bc = c as BoxCollider;
                    if (mc != null && mc.sharedMesh != null)
                    {
                        foreach (Vector3 v in mc.sharedMesh.vertices) { Vector3 w = c.transform.TransformPoint(v); float d = Vector3.Dot(w, up); if (d < ld) { ld = d; lp = w; } }
                    }
                    else if (bc != null)
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            Vector3 o = new Vector3((i & 1) == 0 ? -0.5f : 0.5f, (i & 2) == 0 ? -0.5f : 0.5f, (i & 4) == 0 ? -0.5f : 0.5f);
                            Vector3 w = c.transform.TransformPoint(bc.center + Vector3.Scale(o, bc.size)); float d = Vector3.Dot(w, up); if (d < ld) { ld = d; lp = w; }
                        }
                    }
                    else continue;
                    lows.Add(lp); if (ld < lowest) lowest = ld;
                }
                float gap = float.MaxValue; int hits = 0;
                foreach (Vector3 lp in lows)
                {
                    if (Vector3.Dot(lp, up) > lowest + 0.3f) continue;
                    foreach (RaycastHit h in Physics.RaycastAll(lp + up * 0.3f, -up, 6.3f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        Part hp = h.collider.GetComponentInParent<Part>();
                        if (hp != null && hp.vessel == vessel) continue;
                        float g = h.distance - 0.3f; hits++; if (g < gap) gap = g;
                    }
                }
                if (hits > 0 && gap > 0.03f && gap < 6f)
                {
                    Vector3 off = -up * (gap - 0.005f);
                    foreach (Part p in vessel.parts)
                    {
                        p.transform.position += off;
                        if (p.Rigidbody != null) { p.Rigidbody.position += off; p.Rigidbody.velocity = Vector3.zero; p.Rigidbody.angularVelocity = Vector3.zero; }
                    }
                    Debug.Log("[CaelusLaunchMount] settled on the pad: lowered the vessel " + gap.ToString("F2") + " m onto the ground under the feet (" + lows.Count + " colliders)");
                }
                else Debug.Log("[CaelusLaunchMount] settle check: gap " + (hits > 0 ? gap.ToString("F2") + " m" : "none found") + " - not moved");
            }
            catch (System.Exception e) { Debug.LogWarning("[CaelusLaunchMount] settle check failed: " + e.Message); }
        }

        void LiftOutOfDeck()
        {
            try
            {
                Vector3 up = (part.transform.position - (Vector3)vessel.mainBody.position).normalized;
                const float above = 4f;
                var lows = new System.Collections.Generic.List<Vector3>(); float lowest = float.MaxValue;
                foreach (Collider c in part.GetComponentsInChildren<Collider>(true))
                {
                    if (c == null || c.isTrigger || !c.enabled) continue;
                    Vector3 lp = Vector3.zero; float ld = float.MaxValue;
                    MeshCollider mc = c as MeshCollider; BoxCollider bc = c as BoxCollider;
                    if (mc != null && mc.sharedMesh != null)
                    {
                        foreach (Vector3 v in mc.sharedMesh.vertices) { Vector3 w = c.transform.TransformPoint(v); float d = Vector3.Dot(w, up); if (d < ld) { ld = d; lp = w; } }
                    }
                    else if (bc != null)
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            Vector3 o = new Vector3((i & 1) == 0 ? -0.5f : 0.5f, (i & 2) == 0 ? -0.5f : 0.5f, (i & 4) == 0 ? -0.5f : 0.5f);
                            Vector3 w = c.transform.TransformPoint(bc.center + Vector3.Scale(o, bc.size)); float d = Vector3.Dot(w, up); if (d < ld) { ld = d; lp = w; }
                        }
                    }
                    else continue;
                    lows.Add(lp); if (ld < lowest) lowest = ld;
                }
                float lift = 0f; int hits = 0;
                foreach (Vector3 lp0 in lows)
                {
                    if (Vector3.Dot(lp0, up) > lowest + 0.3f) continue;
                    Vector3 lp = lp0 + up * (lowest - Vector3.Dot(lp0, up));
                    RaycastHit best = new RaycastHit(); float bd = float.MaxValue;
                    foreach (RaycastHit h in Physics.RaycastAll(lp + up * above, -up, above + 0.3f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        Part hp = h.collider.GetComponentInParent<Part>();
                        if (hp != null && hp.vessel == vessel) continue;
                        if (h.distance < bd) { bd = h.distance; best = h; }
                    }
                    if (bd < float.MaxValue) { hits++; lift = Mathf.Max(lift, above - bd); }
                }
                if (hits > 0 && lift > 0.03f && lift < 3.5f)
                {
                    Vector3 off = up * (lift + 0.005f);
                    foreach (Part p in vessel.parts)
                    {
                        p.transform.position += off;
                        if (p.Rigidbody != null) { p.Rigidbody.position += off; p.Rigidbody.velocity = Vector3.zero; p.Rigidbody.angularVelocity = Vector3.zero; }
                    }
                    Debug.Log("[CaelusLaunchMount] the table stood " + lift.ToString("F2") + " m inside the pad deck (loaded from a launch save): lifted the vessel onto it");
                }
                else Debug.Log("[CaelusLaunchMount] deck check: " + (hits > 0 ? "inside by " + lift.ToString("F2") + " m" : "no surface found") + " - not moved");
            }
            catch (System.Exception e) { Debug.LogWarning("[CaelusLaunchMount] deck check failed: " + e.Message); }
        }

        void OnOnRails(Vessel v)
        {
            if (v != vessel || anchor == null) return;
            Destroy(anchor); anchor = null; if (!vessel.packed) unpackedAt = Time.time;
        }

        System.Collections.IEnumerator WarpReport()
        {
            yield return new WaitForSeconds(3f);
            Debug.Log("[CaelusLaunchMount] warp self-test result: rate index " + TimeWarp.CurrentRateIndex + " mode " + TimeWarp.WarpMode + " (1 = rails warp works)");
            TimeWarp.SetRate(0, true);
        }

        void Release(string why)
        {
            if (released || part.parent == null) return;
            released = true; armed = false; holdStatus = "released";
            Debug.Log("[CaelusLaunchMount] release: " + why);
            part.decouple(0f);
        }

        [KSPEvent(guiActive = true, guiName = "Release now")] public void ReleaseNow() { Release("manual"); }
        [KSPAction("Release the rocket")] public void ReleaseAction(KSPActionParam p) { Release("action group"); }

        public override void OnStart(StartState state)
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (part.parent == null) { released = true; holdStatus = "released"; }
            GameEvents.onVesselGoOffRails.Add(OnOffRails); GameEvents.onVesselGoOnRails.Add(OnOnRails);
        }

        public override string GetInfo()
        {
            return "Launch table: put your rocket on its top node and hang the Launch Tower on its side node. Stands on the pad under its own weight. "
                 + "Its stage arms the hold-down: the rocket is let go once its engines lift more than its weight (no drop into the trench "
                 + "if the throttle is still down). Part menu: Release now.";
        }
    }
}
