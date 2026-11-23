using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace CaelusLegLanded
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class Loader : MonoBehaviour
    {
        void Awake()
        {
            var h = new Harmony("caelus.leglanded");
            h.PatchAll(typeof(Loader).Assembly);
            Debug.Log("[CaelusLegLanded] patched Vessel.checkLanded");
            LegAxisFix.Apply(h);
        }
    }

    [HarmonyPatch(typeof(Vessel), "checkLanded")]
    static class CheckLandedPatch
    {
        static readonly HashSet<uint> active = new HashSet<uint>();
        static readonly Dictionary<uint, KeyValuePair<int, bool>> hasStage = new Dictionary<uint, KeyValuePair<int, bool>>();

        static void Postfix(Vessel __instance, ref bool __result)
        {
            if (__instance == null) return;
            if (!__instance.loaded || __instance.packed) { active.Remove(__instance.persistentId); return; }
            if (__result) return;

            int pc = __instance.parts.Count; KeyValuePair<int, bool> c;
            if (!hasStage.TryGetValue(__instance.persistentId, out c) || c.Key != pc)
            {
                bool h = false; for (int i = 0; i < pc && !h; i++) h = IsAquilaDescentStage(__instance.parts[i]);
                hasStage[__instance.persistentId] = c = new KeyValuePair<int, bool>(pc, h);
            }
            if (!c.Value) { Note(__instance, false); return; }
            var parts = __instance.parts;
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];

                if (!IsAquilaDescentStage(p)) continue;
                int n = 0; bool grounded = false;
                var mods = p.Modules;
                for (int k = 0; k < mods.Count; k++)
                {
                    var w = mods[k] as ModuleWheelBase;
                    if (w == null) continue;
                    n++;
                    if (w.isGrounded) grounded = true;
                }
                if (n >= 2 && grounded)
                {
                    __result = true;
                    __instance.Landed = true;
                    Note(__instance, true);
                    return;
                }
            }
            Note(__instance, false);
        }

        static bool IsAquilaDescentStage(Part p)
        {
            if (p == null || p.partInfo == null || p.partInfo.name == null) return false;
            string n = p.partInfo.name;
            return n.StartsWith("RomaAeterna", System.StringComparison.Ordinal) && n.Contains("AquilaDescentStage");
        }

        static void Note(Vessel v, bool on)
        {
            if (v == null) return;
            if (on && active.Add(v.persistentId)) Debug.Log("[CaelusLegLanded] " + v.vesselName + ": multi-leg part grounded -> LANDED");
            else if (!on && active.Remove(v.persistentId)) Debug.Log("[CaelusLegLanded] " + v.vesselName + ": legs no longer grounded");
        }
    }

    public class ModuleCaelusContactProbes : PartModule
    {
        [KSPField] public string model = "Caelus_Payload/Models/AquilaContactProbe";
        [KSPField] public string footName = "RA_Leg_#_FootPivot";
        [KSPField] public string legs = "1,2,3,4";
        [KSPField] public float drop = 0.05f;
        readonly List<Transform> feet = new List<Transform>();
        readonly List<GameObject> probes = new List<GameObject>();
        List<ModuleWheels.ModuleWheelDeployment> deploy;

        public override void OnStart(StartState state)
        {
            if (!HighLogic.LoadedSceneIsFlight && !HighLogic.LoadedSceneIsEditor) return;
            var src = GameDatabase.Instance.GetModelPrefab(model);
            if (src == null) { Debug.LogWarning("[CaelusLegLanded] ContactProbes: model " + model + " not found - no probes on " + part.partInfo.title); return; }
            foreach (var l in legs.Split(','))
            {
                var f = part.FindModelTransform(footName.Replace("#", l.Trim()));
                if (f == null) { Debug.LogWarning("[CaelusLegLanded] ContactProbes: transform " + footName.Replace("#", l.Trim()) + " not found on " + part.partInfo.title); continue; }
                var g = Instantiate(src); g.SetActive(true);
                g.transform.SetParent(f, false);
                foreach (var c in g.GetComponentsInChildren<Collider>(true)) Destroy(c);
                feet.Add(f); probes.Add(g);
            }
            deploy = part.FindModulesImplementing<ModuleWheels.ModuleWheelDeployment>();
        }

        float Deployed()
        {
            if (deploy == null || deploy.Count == 0) return 1f;
            float min = 1f;
            foreach (var m in deploy) min = Mathf.Min(min, m.position);
            return min;
        }

        void LateUpdate()
        {
            if (probes.Count == 0) return;
            bool show = Deployed() > 0.95f;
            Vector3 down = -part.transform.up;
            for (int i = 0; i < probes.Count; i++)
            {
                var g = probes[i]; if (g == null) continue;
                if (g.activeSelf != show) g.SetActive(show);
                if (!show) continue;
                g.transform.position = feet[i].position + down * drop;
                g.transform.rotation = Quaternion.FromToRotation(Vector3.up, -down) ;
            }
        }
    }

    public class ModuleCaelusNavLights : PartModule
    {
        [KSPField] public bool running = true;
        [KSPField] public float heightFraction = 0.6f;
        [KSPField] public bool tracking = true;
        [KSPField] public float flashPeriod = 1.2f, flashOn = 0.15f;
        [KSPField] public float range = 3f, intensity = 0.5f, bead = 0.05f;
        class NavLamp { public GameObject go; public Light light; public Material lens; public Color colour; public bool flashing, on; }
        readonly List<NavLamp> lamps = new List<NavLamp>();

        NavLamp Lamp(string name, Vector3 localPos, Color c, bool flashing)
        {
            var g = new GameObject(name); g.transform.SetParent(part.transform, false); g.transform.localPosition = localPos;
            var l = g.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.range = range; l.intensity = intensity; l.enabled = false;
            l.cullingMask = (1 << 0) | (1 << 15);
            var b = GameObject.CreatePrimitive(PrimitiveType.Sphere); var col = b.GetComponent<Collider>(); if (col) Destroy(col);
            b.transform.SetParent(g.transform, false); b.transform.localScale = Vector3.one * bead; b.layer = 0;
            var sh = Shader.Find("KSP/Emissive/Diffuse") ?? Shader.Find("Unlit/Color");
            var m = new Material(sh); m.color = Color.Lerp(c, Color.gray, 0.5f) * 0.6f; if (m.HasProperty("_EmissiveColor")) m.SetColor("_EmissiveColor", Color.black);
            b.GetComponent<Renderer>().sharedMaterial = m;
            return new NavLamp { go = g, light = l, lens = m, colour = c, flashing = flashing };
        }

        public override void OnStart(StartState state)
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            var inv = part.transform.worldToLocalMatrix; bool any = false; Vector3 mn = Vector3.zero, mx = Vector3.zero; hull.Clear();
            foreach (var r in part.GetComponentsInChildren<MeshRenderer>(false))
            {
                var b = r.bounds;

                if (b.extents.magnitude > 15f || r.gameObject.layer != 0 || r.GetComponent<MeshFilter>() == null) continue;

                if (r.GetComponentInParent<Part>() != part) continue;
                string rn = r.name.ToLowerInvariant();
                if (rn.Contains("plume") || rn.Contains("fx") || rn.Contains("flame") || rn.Contains("waterfall")) continue;
                if (Wing(rn)) continue;
                hull.Add(r);
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var lp = inv.MultiplyPoint3x4(c);
                    if (!any) { mn = mx = lp; any = true; } else { mn = Vector3.Min(mn, lp); mx = Vector3.Max(mx, lp); }
                }
            }
            if (!any) return;
            float y = Mathf.Lerp(mn.y, mx.y, heightFraction), zc = (mn.z + mx.z) * 0.5f, xc = (mn.x + mx.x) * 0.5f;
            if (running)
            {
                lamps.Add(Lamp("CaelusNav_Port", OnHull(new Vector3(mn.x, y, zc), new Vector3(xc, y, zc)), new Color(1f, 0.15f, 0.1f), false));
                lamps.Add(Lamp("CaelusNav_Starboard", OnHull(new Vector3(mx.x, y, zc), new Vector3(xc, y, zc)), new Color(0.2f, 1f, 0.3f), false));
            }
            if (tracking) lamps.Add(Lamp("CaelusNav_Tracking", OnHull(new Vector3(xc, mx.y, zc), new Vector3(xc, (mn.y + mx.y) * 0.5f, zc)), Color.white, true));
            foreach (GameObject g in temp) if (g != null) DestroyImmediate(g);
            temp.Clear(); hull.Clear();
            foreach (NavLamp n in lamps) Debug.Log("[CaelusNavLights] " + part.partInfo.name + " " + n.go.name + " at " + n.go.transform.localPosition.ToString("F2"));
        }

        readonly List<MeshRenderer> hull = new List<MeshRenderer>(); readonly List<GameObject> temp = new List<GameObject>();
        static bool Wing(string rn) { return rn.Contains("_wing") || rn.Contains("photoelectric") || rn.Contains("solar") || rn.Contains("arrayhinge"); }

        Vector3 OnHull(Vector3 outer, Vector3 centre)
        {
            Vector3 wo = part.transform.TransformPoint(outer), wc = part.transform.TransformPoint(centre);
            Vector3 d = (wc - wo); float len = d.magnitude; if (len < 0.01f) return outer; d /= len;
            Vector3 start = wo - d * 1f;
            try
            {
                if (temp.Count == 0)
                {
                    foreach (MeshRenderer r in hull)
                    {
                        var mf = r != null ? r.GetComponent<MeshFilter>() : null;
                        if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                        var g = new GameObject("CaelusNavProbe"); g.layer = 0; g.transform.SetParent(r.transform, false);
                        var mc = g.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; temp.Add(g);
                    }
                    Physics.SyncTransforms();
                }
                RaycastHit best = new RaycastHit(); float bd = float.MaxValue;
                foreach (GameObject g in temp)
                {
                    RaycastHit h; var mc = g.GetComponent<MeshCollider>();
                    if (mc.Raycast(new Ray(start, d), out h, len + 1f) && h.distance < bd) { bd = h.distance; best = h; }
                }
                if (bd < float.MaxValue) return part.transform.InverseTransformPoint(best.point - d * 0.05f);
            }
            catch (System.Exception e) { Debug.LogWarning("[CaelusNavLights] hull probe failed: " + e.Message); }
            foreach (var h in Physics.RaycastAll(start, d, len + 1f, ~0).OrderBy(h => h.distance))
                if (h.collider != null && h.collider.GetComponentInParent<Part>() == part)
                    return part.transform.InverseTransformPoint(h.point - d * 0.05f);
            return Vector3.Lerp(outer, centre, 0.2f);
        }

        void Update()
        {
            if (lamps.Count == 0 || vessel == null) return;
            bool lightsOn = vessel.ActionGroups[KSPActionGroup.Light];
            bool flash = (Time.time % flashPeriod) < flashOn;
            foreach (NavLamp n in lamps)
            {
                bool on = lightsOn && (!n.flashing || flash);
                if (on == n.on || n.light == null) continue;
                n.on = on; n.light.enabled = on;
                if (n.lens.HasProperty("_EmissiveColor")) n.lens.SetColor("_EmissiveColor", on ? n.colour : Color.black);
            }
        }

        void OnDestroy() { foreach (NavLamp n in lamps) { if (n.go) Destroy(n.go); if (n.lens) Destroy(n.lens); } lamps.Clear(); }
    }

    public class ModuleCaelusRockScan : PartModule
    {
        [KSPField(guiActive = true, guiName = "rockblocked", groupName = "caelusRockScan", groupDisplayName = "Landing rock scan (kOS)", groupStartCollapsed = true)] public float rockBlocked = 0f;
        [KSPField(guiActive = true, guiName = "clearn", groupName = "caelusRockScan", groupDisplayName = "Landing rock scan (kOS)", groupStartCollapsed = true)] public float clearN = 0f;
        [KSPField(guiActive = true, guiName = "cleare", groupName = "caelusRockScan", groupDisplayName = "Landing rock scan (kOS)", groupStartCollapsed = true)] public float clearE = 0f;
        [KSPField(guiActive = true, guiName = "rocksnear", groupName = "caelusRockScan", groupDisplayName = "Landing rock scan (kOS)", groupStartCollapsed = true)] public float rocksNear = 0f;
        [KSPField(guiActive = true, guiName = "aheadblocked", groupName = "caelusRockScan", groupDisplayName = "Landing rock scan (kOS)", groupStartCollapsed = true)] public float aheadBlocked = 0f;
        [KSPField(guiActive = true, guiName = "aheadn", groupName = "caelusRockScan", groupDisplayName = "Landing rock scan (kOS)", groupStartCollapsed = true)] public float aheadN = 0f;
        [KSPField(guiActive = true, guiName = "aheade", groupName = "caelusRockScan", groupDisplayName = "Landing rock scan (kOS)", groupStartCollapsed = true)] public float aheadE = 0f;
        [KSPField(guiActive = true, guiName = "predn", groupName = "caelusRockScan", groupDisplayName = "Landing rock scan (kOS)", groupStartCollapsed = true)] public float predN = 0f;
        [KSPField(guiActive = true, guiName = "prede", groupName = "caelusRockScan", groupDisplayName = "Landing rock scan (kOS)", groupStartCollapsed = true)] public float predE = 0f;
        [KSPField(guiActive = true, guiName = "slopedeg", groupName = "caelusRockScan", groupDisplayName = "Landing rock scan (kOS)", groupStartCollapsed = true)] public float slopeDeg = 0f;
        [KSPField] public float scanAltitude = 150f, footRadius = 4.2f, interval = 0.5f, maxRange = 30f, rockHeight = 0.35f, maxSlope = 10f;
        [KSPField] public float probeRadius = 0.85f;
        float next;
        static readonly float[] Rings = { 4f, 8f, 12f, 16f, 20f, 25f, 30f };
        static readonly RaycastHit[] castHits = new RaycastHit[256];

        bool Probe(Vector3 p, Vector3 up, out double groundAlt, out bool rock)
        {
            groundAlt = 0; rock = false;

            int n = Physics.SphereCastNonAlloc(p + up * 60f, probeRadius, -up, castHits, 120f, ~0, QueryTriggerInteraction.Ignore);
            int best = -1;
            for (int i = 0; i < n; i++)
            {
                var h = castHits[i];

                if (h.collider == null || h.distance <= 0f || h.collider.GetComponentInParent<Part>() != null) continue;
                if (best < 0 || h.distance < castHits[best].distance) best = i;
            }
            if (best < 0) return false;
            var body = vessel.mainBody; var hb = castHits[best];
            double lat = body.GetLatitude(hb.point), lon = body.GetLongitude(hb.point);
            double hitAlt = body.GetAltitude(hb.point), terr = body.TerrainAltitude(lat, lon, true);

            rock = hitAlt > terr + rockHeight; groundAlt = rock ? terr : hitAlt;
            return true;
        }

        bool Blocked(Vector3 groundPoint, Vector3 up, Vector3 north, Vector3 east, ref int hits, out float slope)
        {
            slope = 0f; double lo = double.MaxValue, hi = double.MinValue; bool blocked = false;
            for (int k = 0; k < 25; k++)
            {
                float r = k == 0 ? 0f : (k <= 8 ? 0.5f * footRadius : footRadius);
                float a = k <= 8 ? (k - 1) * Mathf.PI / 4f : (k - 9) * Mathf.PI / 8f;
                Vector3 off = (north * Mathf.Cos(a) + east * Mathf.Sin(a)) * r;
                if (!Probe(groundPoint + off, up, out double g, out bool rock)) continue;
                if (rock) { hits++; blocked = true; }
                if (k >= 9) { lo = System.Math.Min(lo, g); hi = System.Math.Max(hi, g); }
            }
            if (hi > lo) slope = Mathf.Atan2((float)(hi - lo), 2f * footRadius) * Mathf.Rad2Deg;
            return blocked || slope > maxSlope;
        }

        bool FindClear(Vector3 p, Vector3 up, Vector3 north, Vector3 east, ref int hits, out Vector3 bestOff)
        {
            bestOff = Vector3.zero;
            Vector3 drift = Vector3.ProjectOnPlane((Vector3)vessel.srf_velocity, up);
            foreach (var r in Rings)
            {
                if (r > maxRange) break;
                float best = float.MaxValue; bool found = false;
                for (int d = 0; d < 12; d++)
                {
                    float a = d * Mathf.PI / 6f;
                    Vector3 off = (north * Mathf.Cos(a) + east * Mathf.Sin(a)) * r;
                    if (!Blocked(p + off, up, north, east, ref hits, out float _))
                    {
                        float score = r - Vector3.Dot(off.normalized, drift) * 0.5f;
                        if (score < best) { best = score; bestOff = off; found = true; }
                    }
                }
                if (found) return true;
            }
            return false;
        }

        void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || !vessel.loaded || vessel.packed) return;
            if (Time.time < next) return; next = Time.time + interval;
            if (vessel.radarAltitude > scanAltitude || vessel.LandedOrSplashed) { rockBlocked = aheadBlocked = 0; clearN = clearE = aheadN = aheadE = predN = predE = slopeDeg = rocksNear = 0; return; }
            Vector3 up = (vessel.CoMD - vessel.mainBody.position).normalized;
            Vector3 north = Vector3.ProjectOnPlane(vessel.mainBody.transform.up, up).normalized;
            Vector3 east = Vector3.Cross(up, north).normalized;
            Vector3 ground = (Vector3)vessel.CoMD - up * (float)vessel.radarAltitude;
            int hits = 0;
            bool here = Blocked(ground, up, north, east, ref hits, out slopeDeg);
            rockBlocked = here ? 1f : 0f; clearN = clearE = 0f;
            if (here && FindClear(ground, up, north, east, ref hits, out Vector3 off)) { clearN = Vector3.Dot(off, north); clearE = Vector3.Dot(off, east); }

            Vector3 vH = Vector3.ProjectOnPlane((Vector3)vessel.srf_velocity, up);
            float tGo = Mathf.Clamp((float)vessel.radarAltitude / Mathf.Max(1f, (float)-vessel.verticalSpeed), 0f, 25f);
            Vector3 pred = Vector3.ClampMagnitude(vH * tGo * 0.5f, 120f);
            predN = Vector3.Dot(pred, north); predE = Vector3.Dot(pred, east);
            aheadBlocked = 0f; aheadN = aheadE = 0f;
            if (pred.magnitude > 3f && Blocked(ground + pred, up, north, east, ref hits, out float _))
            {
                aheadBlocked = 1f;
                if (FindClear(ground + pred, up, north, east, ref hits, out Vector3 aoff)) { aheadN = Vector3.Dot(aoff, north); aheadE = Vector3.Dot(aoff, east); }
            }
            rocksNear = hits;
        }
    }

    public class ModuleCaelusParkingBrake : PartModule
    {
        [KSPField] public float groundAlt = 5f, maxSpeed = 0.6f, thrustRelease = 0.2f, settle = 1f, release = 0.3f;

        [KSPField] public float pinRate = 2f, pinMax = 0.02f, reanchor = 0.5f;

        [KSPField] public float vDamp = 0.15f, releaseSpeed = 2.5f;

        [KSPField] public float turnRate = 1f, turnMax = 0.01f;

        [KSPField] public float moveRelease = 0.15f;
        float onFor = 0f, offFor = 0f, lastLog = -999f; bool held = false; Vector3d anchorLocal; Quaternion attLocal; Part anchorRoot;

        void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || !vessel.loaded || vessel.packed || vessel.mainBody == null) return;

            if (!held && onFor == 0f && (vessel.Splashed || vessel.radarAltitude >= groundAlt)) { offFor = release; return; }
            double g = FlightGlobals.getGeeForceAtPosition(vessel.CoMD).magnitude;
            double thrust = 0;
            foreach (var p in vessel.parts) foreach (var e in p.FindModulesImplementing<ModuleEngines>()) thrust += e.finalThrust;
            bool push = thrust > thrustRelease * vessel.totalMass * g;
            bool slow = vessel.srfSpeed < (held ? releaseSpeed : maxSpeed);
            if (held && !slow && (vessel.mainBody.transform.TransformPoint(anchorLocal) - vessel.CoMD).magnitude < moveRelease) slow = true;
            bool on = !vessel.Splashed && vessel.radarAltitude < groundAlt && slow && !push;
            float dt = TimeWarp.fixedDeltaTime;
            if (push) { onFor = 0f; offFor = release; }
            else if (on) { onFor += dt; offFor = 0f; }
            else { offFor += dt; if (offFor >= release) onFor = 0f; }
            bool hold = onFor >= settle && offFor < release;
            if (hold != held)
            {
                held = hold;
                if (held)
                {
                    anchorLocal = vessel.mainBody.transform.InverseTransformPoint(vessel.CoMD);
                    attLocal = Quaternion.Inverse(vessel.mainBody.transform.rotation) * vessel.transform.rotation; anchorRoot = vessel.rootPart;
                }

                if (push || Time.time - lastLog > 30f) Debug.Log("[CaelusLegLanded] " + vessel.vesselName + ": parking brake " + (held ? "ON" : "off") + " (radar " + vessel.radarAltitude.ToString("F2") + " m, srf " + vessel.srfSpeed.ToString("F2") + " m/s, thrust " + thrust.ToString("F1") + " kN)");
                lastLog = Time.time;
            }
            if (!held) return;
            Vector3 up = (vessel.CoMD - vessel.mainBody.position).normalized;
            Vector3 vH = Vector3.ProjectOnPlane((Vector3)vessel.srf_velocity, up);
            Vector3 errH = Vector3.ProjectOnPlane((Vector3)(vessel.mainBody.transform.TransformPoint(anchorLocal) - vessel.CoMD), up);
            if (errH.magnitude > reanchor) { anchorLocal = vessel.mainBody.transform.InverseTransformPoint(vessel.CoMD); errH = Vector3.zero; Debug.Log("[CaelusLegLanded] " + vessel.vesselName + ": parking brake re-anchored"); }
            Vector3 dv = Vector3.ClampMagnitude(errH * pinRate, pinMax) - vH;
            Quaternion err = vessel.mainBody.transform.rotation * attLocal * Quaternion.Inverse(vessel.transform.rotation);
            err.ToAngleAxis(out float ang, out Vector3 axis);
            if (ang > 180f) ang -= 360f;
            if (float.IsNaN(axis.x) || Mathf.Abs(ang) < 1e-4f) { ang = 0f; axis = up; }
            if (vessel.rootPart != anchorRoot)
            {
                attLocal = Quaternion.Inverse(vessel.mainBody.transform.rotation) * vessel.transform.rotation; anchorRoot = vessel.rootPart; ang = 0f;
            }
            Vector3 wTurn = Vector3.ClampMagnitude(axis * (ang * Mathf.Deg2Rad * turnRate), turnMax);
            Vector3 com = vessel.CoM;
            foreach (var p in vessel.parts)
            {
                var rb = p.Rigidbody; if (rb == null) continue;
                Vector3 w = wTurn;
                rb.velocity += dv - up * ((float)vessel.verticalSpeed * vDamp) + Vector3.Cross(w - rb.angularVelocity, rb.worldCenterOfMass - com);
                rb.angularVelocity = w;
            }
        }
    }

    static class LegAxisFix
    {
        public static System.Reflection.MethodInfo legLate;
        public static System.Type legType;

        public static void Apply(Harmony h)
        {
            legType = AccessTools.TypeByName("RomaAeterna.RomaAeternaLegVisuals");
            legLate = legType != null ? AccessTools.Method(legType, "LateUpdate") : null;
            if (legLate == null) { Debug.LogWarning("[CaelusLegLanded] RomaAeternaLegVisuals.LateUpdate not found - leg axis fix off"); return; }
            var onStart = AccessTools.Method(legType, "OnStart");
            if (onStart == null) { Debug.LogWarning("[CaelusLegLanded] RomaAeternaLegVisuals.OnStart not found - leg axis fix off"); return; }
            h.Patch(onStart, postfix: new HarmonyMethod(typeof(LegAxisFix), nameof(OnStartPostfix)));
            Debug.Log("[CaelusLegLanded] leg axis fix armed on RomaAeternaLegVisuals");
        }

        static void OnStartPostfix(PartModule __instance)
        {
            if (!HighLogic.LoadedSceneIsFlight || __instance == null || __instance.part == null) return;
            var go = __instance.part.gameObject;
            var f = go.GetComponent<LegAxisLate>() ?? go.AddComponent<LegAxisLate>();
            f.vis = __instance;
        }
    }

    [DefaultExecutionOrder(32000)]
    public class LegAxisLate : MonoBehaviour
    {
        public PartModule vis;
        Transform[] sus;

        readonly Transform[] proxy = new Transform[4];
        readonly Transform[] foot = new Transform[4];
        readonly float[] lift = new float[4];
        static bool liftLogged;
        void Detach(int i, Transform s)
        {
            var pivot = vis.part.FindModelTransform("RA_Leg_" + (i + 1) + "_FootPivot");
            if (pivot == null || s.parent == null) return;
            var go = new GameObject("RA_Leg_" + (i + 1) + "_SuspensionAxis");
            var px = go.transform;
            px.SetParent(s.parent, false);
            px.localRotation = s.localRotation; px.localScale = s.localScale;
            px.localPosition = new Vector3(0f, s.localPosition.y, 0f);
            pivot.SetParent(px, false);
            proxy[i] = px; foot[i] = pivot;
            if (s.localPosition.x * s.localPosition.x + s.localPosition.z * s.localPosition.z > 0.25f) Debug.Log("[CaelusLegLanded] leg " + (i + 1) + " foot moved onto its own axis proxy (stock suspension was at " + s.localPosition.ToString("F2") + ")");
        }
        static bool logged;

        static readonly List<LegAxisLate> all = new List<LegAxisLate>();
        static bool hooked;

        void OnEnable() { all.Add(this); if (!hooked) { hooked = true; Camera.onPreCull += PreCull; Camera.onPreRender += PreRender; } }
        void OnDisable() { all.Remove(this); }

        static void PreCull(Camera c) { for (int i = 0; i < all.Count; i++) if (all[i] != null) all[i].Fix(true); }
        static void PreRender(Camera c) { for (int i = 0; i < all.Count; i++) if (all[i] != null) all[i].Fix(true); }
        void LateUpdate() { Fix(false); }

        void Fix(bool preCull)
        {
            if (vis == null || vis.part == null || vis.part.packed) return;
            if (sus == null)
            {
                sus = new Transform[4];
                for (int i = 0; i < 4; i++) sus[i] = vis.part.FindModelTransform("RA_Leg_" + (i + 1) + "_Suspension");
            }
            bool moved = false;
            for (int i = 0; i < 4; i++)
            {
                var s = sus[i];
                if (s == null) continue;
                var lp = s.localPosition;
                if (lp.x * lp.x + lp.z * lp.z > 0.25f)
                {
                    if (!logged) { logged = true; Debug.Log("[CaelusLegLanded] leg " + (i + 1) + " suspension off its axis " + lp.ToString("F3") + " - kept on the carrier axis"); }
                }
                if (proxy[i] == null) Detach(i, s);
                if (proxy[i] == null) continue;

                if (!preCull)
                {
                    float target = 0f;
                    if (foot[i] != null)
                    {
                        Vector3 up = s.parent.up;
                        Vector3 bottom = foot[i].position - foot[i].up * 0.28f;
                        if (Physics.Raycast(bottom + up * 1.5f, -up, out RaycastHit hit, 3f, 1 << 15))
                            target = Mathf.Clamp(lift[i] + (1.5f - hit.distance) - 0.02f, 0f, 0.6f);
                        else target = lift[i];
                    }
                    lift[i] = Mathf.MoveTowards(lift[i], target, Time.deltaTime * 1.0f);
                    if (!liftLogged && lift[i] > 0.05f) { liftLogged = true; Debug.Log("[CaelusLegLanded] pad lift active: leg " + (i + 1) + " target " + target.ToString("F2") + " m"); }
                }
                proxy[i].localPosition = new Vector3(0f, s.localPosition.y, 0f) + proxy[i].parent.InverseTransformDirection(s.parent.up) * lift[i];
                moved = true;
            }
            if (moved)
            {
                try { LegAxisFix.legLate.Invoke(vis, null); }
                catch (System.Exception e) { Debug.LogWarning("[CaelusLegLanded] leg axis fix: RomaAeternaLegVisuals.LateUpdate failed (" + (e.InnerException ?? e) + ") - fix off for " + vis.part.partInfo.title); enabled = false; }
            }
        }
    }

    public class ModuleCaelusCrewTransfer : PartModule
    {
        [KSPField] public int moved = 0;

        [KSPEvent(guiActive = true, guiName = "Transfer Aquila crew", active = true)]
        public void TransferAquilaCrew()
        {
            if (vessel == null) return;
            int n = 0;
            foreach (var p in vessel.parts.ToList())
            {

                if (p == part || p.partInfo == null || !p.partInfo.name.StartsWith("RomaAeterna", System.StringComparison.Ordinal) || !p.partInfo.name.Contains("Aquila")) continue;
                foreach (var c in p.protoModuleCrew.ToList())
                {
                    if (part.protoModuleCrew.Count >= part.CrewCapacity) break;
                    p.RemoveCrewmember(c);
                    part.AddCrewmember(c);
                    n++;
                }
            }
            moved += n;
            if (n > 0)
            {
                vessel.CrewListSetDirty();
                vessel.DespawnCrew();
                StartCoroutine(Respawn());
            }
            Debug.Log("[CaelusLegLanded] crew transfer: " + n + " kerbal(s) moved into " + part.partInfo.title + ", crew now " + part.protoModuleCrew.Count);
        }

        System.Collections.IEnumerator Respawn()
        {
            yield return null;
            if (vessel == null) yield break;
            vessel.SpawnCrew();
            GameEvents.onVesselCrewWasModified.Fire(vessel);
        }
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class SepClean : MonoBehaviour
    {
        readonly Dictionary<ulong, List<KeyValuePair<Collider, Collider>>> ignored = new Dictionary<ulong, List<KeyValuePair<Collider, Collider>>>();
        readonly List<Part> bells = new List<Part>(), necks = new List<Part>();
        static bool IsBell(Part p) => p.partInfo != null && p.partInfo.name.StartsWith("CaelusCorona.Engine", System.StringComparison.Ordinal);
        static bool IsNeck(Part p) => p.partInfo != null && (p.partInfo.name == "RomaAeterna.AquilaAscentVehicle" || p.partInfo.name == "RomaAeterna.DockingPassive");
        void Update()
        {
            bells.Clear(); necks.Clear();
            foreach (var v in FlightGlobals.VesselsLoaded) foreach (var p in v.parts) { if (p == null) continue; if (IsBell(p)) bells.Add(p); else if (IsNeck(p)) necks.Add(p); }
            foreach (var b in bells)
                foreach (var n in necks)
                {
                    ulong key = ((ulong)b.flightID << 32) | n.flightID;
                    bool apart = b.vessel != n.vessel;
                    float d = (b.transform.position - n.transform.position).magnitude;
                    bool has = ignored.ContainsKey(key);
                    if (apart && d < 8f && !has)
                    {
                        var pairs = new List<KeyValuePair<Collider, Collider>>();
                        foreach (var c1 in b.GetComponentsInChildren<Collider>(true))
                            foreach (var c2 in n.GetComponentsInChildren<Collider>(true))
                                if (c1 != null && c2 != null && !c1.isTrigger && !c2.isTrigger) { Physics.IgnoreCollision(c1, c2, true); pairs.Add(new KeyValuePair<Collider, Collider>(c1, c2)); }
                        ignored[key] = pairs;
                        Debug.Log("[CaelusLegLanded] separation: " + b.partInfo.title + " x " + n.partInfo.title + " collisions ignored while within 8 m (" + pairs.Count + " collider pairs)");
                    }
                    else if (has && (!apart || d > 8f))
                    {
                        foreach (var kv in ignored[key]) if (kv.Key != null && kv.Value != null) Physics.IgnoreCollision(kv.Key, kv.Value, false);
                        ignored.Remove(key);
                        Debug.Log("[CaelusLegLanded] separation: " + b.partInfo.title + " x " + n.partInfo.title + " collisions restored");
                    }
                }
        }
    }

    public class ModuleCaelusHoldDown : PartModule
    {
        [KSPField] public string modelRoot = "Caelus_HoldDown";
        [KSPField] public string vesselPartPrefix = "Caelus_D01";
        [KSPField] public float footTop = 0.95f;
        [KSPField(isPersistant = true)] public bool caelus = false;
        Transform root, column, foot;
        static readonly RaycastHit[] rayHits = new RaycastHit[32];

        static Transform Find(Transform t, string n)
        {
            if (t.name == n) return t;
            foreach (Transform c in t) { var r = Find(c, n); if (r != null) return r; }
            return null;
        }

        public override void OnStart(StartState state)
        {
            column = part.FindModelTransform("CLD_HD_Column");
            root = column != null ? column.parent : null;
            if (root == null) { Debug.LogWarning("[CaelusLegLanded] HoldDown: transform CLD_HD_Column not found on " + part.partInfo.name + " - stock clamp kept"); return; }
            foot = Find(root, "CLD_HD_Foot");
            List<Part> ps = HighLogic.LoadedSceneIsEditor && EditorLogic.fetch != null && EditorLogic.fetch.ship != null ? EditorLogic.fetch.ship.parts : (vessel != null ? vessel.parts : null);
            if (!caelus && ps != null) caelus = ps.Any(p => p.partInfo != null && p.partInfo.name.Replace('.', '_').StartsWith(vesselPartPrefix, System.StringComparison.Ordinal));
            Apply();
        }

        void Apply()
        {
            foreach (var r in part.GetComponentsInChildren<Renderer>(true))
            {
                bool ours = r.transform.IsChildOf(root);
                r.enabled = caelus ? ours : !ours;
            }
        }

        void LateUpdate()
        {
            if (!caelus || root == null || column == null || foot == null || !HighLogic.LoadedSceneIsFlight) return;
            Vector3 up = root.up, top = column.position;
            float best = 60f; Vector3 hitP = top - up * 3f;
            int n = Physics.RaycastNonAlloc(top - up * 0.05f, -up, rayHits, 60f, (1 << 0) | (1 << 15) | (1 << 28));
            for (int i = 0; i < n; i++)
            {
                var h = rayHits[i];
                if (h.collider == null || h.collider.GetComponentInParent<Part>() != null) continue;
                if (h.distance < best) { best = h.distance; hitP = h.point; }
            }
            foot.position = hitP; foot.rotation = root.rotation;
            float len = Mathf.Max(0.05f, Vector3.Dot(top - hitP, up) - footTop);
            column.localScale = new Vector3(1f, len, 1f);
        }
    }
}
