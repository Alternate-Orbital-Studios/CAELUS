using System.Collections.Generic;
using UnityEngine;

namespace CaelusSmoke
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class PetalHinge : MonoBehaviour
    {
        class Hinge { public Part parent; public Vector3 pointL, axisL, outL; }
        class Swing { public Part petal; public Hinge h; public float t, ang; }
        readonly Dictionary<uint, Hinge> attached = new Dictionary<uint, Hinge>();
        readonly List<Swing> swings = new List<Swing>();
        const float Rate = 36f, Ease = 0.45f, LetGo = 45f, Drift = 1.5f, Tumble = 0.8f;

        void FixedUpdate()
        {
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                if (v == null || v.packed) continue;
                foreach (Part p in v.parts)
                {
                    if (p == null || p.partInfo == null || !p.partInfo.name.StartsWith("Caelus.D08.Petal")) continue;
                    Hinge h;
                    if (p.parent != null) Remember(p);
                    else if (attached.TryGetValue(p.flightID, out h)) { attached.Remove(p.flightID); Begin(p, h); }
                }
            }
            for (int i = swings.Count - 1; i >= 0; i--) if (!Step(swings[i])) swings.RemoveAt(i);
        }

        void Remember(Part p)
        {
            Part par = p.parent; Transform pt = par.transform;
            Vector3 com = p.transform.TransformPoint(p.CoMOffset), basePt = p.transform.position;
            Vector3 up = pt.up; if (Vector3.Dot(com - basePt, up) < 0f) up = -up;
            Vector3 outW = Vector3.ProjectOnPlane(basePt - pt.position, up);
            if (outW.sqrMagnitude < 0.01f) outW = Vector3.ProjectOnPlane(com - pt.position, up);
            outW.Normalize();
            Vector3 axis = Vector3.Cross(up, outW).normalized;
            Hinge h;
            if (!attached.TryGetValue(p.flightID, out h)) attached[p.flightID] = h = new Hinge();
            h.parent = par; h.pointL = pt.InverseTransformPoint(basePt); h.axisL = pt.InverseTransformDirection(axis); h.outL = pt.InverseTransformDirection(outW);
        }

        void Begin(Part p, Hinge h)
        {
            if (h.parent == null || h.parent.vessel == null || p.rb == null) return;
            int n = 0; Vector3 hp = h.parent.transform.TransformPoint(h.pointL);
            var mine = p.GetComponentsInChildren<Collider>();
            foreach (Part q in h.parent.vessel.parts)
            {
                if (q == null || (q.transform.position - hp).sqrMagnitude > 400f) continue;
                foreach (var c in q.GetComponentsInChildren<Collider>())
                    foreach (var m in mine) if (c != null && m != null) { Physics.IgnoreCollision(c, m, true); n++; }
            }
            swings.Add(new Swing { petal = p, h = h });
            Debug.Log("[CaelusSmoke] petal hinge: " + p.partInfo.title + " swings open about its base (" + n + " collider pairs ignored, hinge " +
                      (p.rb.worldCenterOfMass - hp).magnitude.ToString("F1") + " m below its centre of mass)");
        }

        bool Step(Swing s)
        {
            Part p = s.petal, par = s.h.parent;
            if (p == null || p.rb == null || p.vessel == null || p.vessel.packed || par == null || par.rb == null || s.t > 6f) return false;
            float dt = Time.fixedDeltaTime; s.t += dt;
            float w = Rate * Mathf.SmoothStep(0f, 1f, s.t / Ease);
            Transform pt = par.transform;
            Vector3 H = pt.TransformPoint(s.h.pointL), ax = pt.TransformDirection(s.h.axisL);
            Vector3 spin = par.rb.angularVelocity + ax * (w * Mathf.Deg2Rad);
            p.rb.angularVelocity = spin;
            p.rb.velocity = par.rb.GetPointVelocity(H) + Vector3.Cross(spin, p.rb.worldCenterOfMass - H);
            s.ang += w * dt;
            if (s.ang < LetGo) return true;
            Vector3 outNow = Quaternion.AngleAxis(s.ang, ax) * pt.TransformDirection(s.h.outL);
            p.rb.velocity += outNow * Drift;
            p.rb.angularVelocity = par.rb.angularVelocity + ax * (w * Tumble * Mathf.Deg2Rad);
            Debug.Log("[CaelusSmoke] petal hinge: " + p.partInfo.title + " let go at " + s.ang.ToString("F0") + " deg after " + s.t.ToString("F2") + " s");
            return false;
        }
    }
}
