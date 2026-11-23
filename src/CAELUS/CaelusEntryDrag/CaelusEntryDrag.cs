using System;
using System.Reflection;
using UnityEngine;

namespace Caelus
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class CaelusEntryDrag : MonoBehaviour
    {
        const double CdHyper = 1.3, CdSub = 0.9, RefArea = 11.26, RefDiam = 3.79, PitchDamp = 1.5, DampRate = 4.0, MainDiam = 40f, DrogueDiam = 5f,
                     ChuteFull = 750, ChuteSemi = 60, LoverD = 0.3, VsTarget = -80, VsBand = 60;

        const double MainChuteDrag = 1280.0;
        static bool? farPresent;
        readonly System.Collections.Generic.Dictionary<PartModule, float> rcOpenT = new System.Collections.Generic.Dictionary<PartModule, float>();
        readonly System.Collections.Generic.HashSet<uint> chuteSynced = new System.Collections.Generic.HashSet<uint>();
        readonly System.Collections.Generic.Dictionary<Guid, double> peakG = new System.Collections.Generic.Dictionary<Guid, double>();

        static bool FarPresent()
        {
            if (farPresent == null)
            {
                farPresent = false;
                foreach (AssemblyLoader.LoadedAssembly a in AssemblyLoader.loadedAssemblies) if (a.name == "FerramAerospaceResearch") { farPresent = true; break; }
                Debug.Log("[CaelusEntryDrag] FAR " + (farPresent.Value ? "installed: Corona capsule entry model active" : "not installed: stock aerodynamics fly the Corona capsule"));
            }
            return farPresent.Value;
        }

        void SyncChute(Part cap)
        {
            if (chuteSynced.Contains(cap.flightID)) return;
            chuteSynced.Add(cap.flightID);
            var pre = cap.partInfo != null && cap.partInfo.partPrefab != null ? cap.partInfo.partPrefab.FindModuleImplementing<ModuleParachute>() : null;
            var pc = cap.FindModuleImplementing<ModuleParachute>();
            if (pre == null || pc == null) return;
            if (Math.Abs(pc.minAirPressureToOpen - pre.minAirPressureToOpen) > 1e-4 || Math.Abs(pc.deployAltitude - pre.deployAltitude) > 0.5f)
            {
                Debug.Log("[CaelusEntryDrag] Corona chute settings from the save (" + pc.minAirPressureToOpen.ToString("F2") + " atm, " + pc.deployAltitude.ToString("F0") + " m) reset to the part config (" + pre.minAirPressureToOpen.ToString("F2") + " atm, " + pre.deployAltitude.ToString("F0") + " m)");
                pc.minAirPressureToOpen = pre.minAirPressureToOpen; pc.deployAltitude = pre.deployAltitude;
            }
        }

        readonly System.Collections.Generic.HashSet<uint> chuteLabelled = new System.Collections.Generic.HashSet<uint>();
        readonly System.Collections.Generic.Dictionary<uint, float> mainsOpenT = new System.Collections.Generic.Dictionary<uint, float>();

        static string ChuteCap(PartModule m)
        {
            var pc = m as ModuleParachute; if (pc != null) return pc.capName;
            return m.moduleName == "RealChuteFAR" ? m.Fields.GetValue("capName") as string : null;
        }

        static bool ChuteOpen(PartModule m)
        {
            var pc = m as ModuleParachute;
            if (pc != null) return pc.deploymentState == ModuleParachute.deploymentStates.SEMIDEPLOYED || pc.deploymentState == ModuleParachute.deploymentStates.DEPLOYED;
            string st = m.Fields.GetValue("depState") as string ?? "";
            return st == "PREDEPLOYED" || st == "DEPLOYED";
        }

        static void ChuteCut(PartModule m)
        {
            var pc = m as ModuleParachute;
            if (pc != null) { pc.CutParachute(); return; }
            BaseEvent e = m.Events["GUICut"]; if (e != null) e.Invoke();
        }

        void ChuteHelp(Part cap, Vessel v)
        {
            if (!chuteLabelled.Contains(cap.flightID))
            {
                chuteLabelled.Add(cap.flightID); int n = 0;
                foreach (PartModule m in cap.Modules)
                {
                    string c = ChuteCap(m); string pre = c == "DrogueCap" ? "Drogues: " : c == "ChuteCap" ? "Mains: " : null;
                    if (pre == null) continue;
                    foreach (BaseEvent e in m.Events) if (e.guiName != null && !e.guiName.StartsWith(pre)) { e.guiName = pre + e.guiName; n++; }
                    foreach (BaseAction a in m.Actions) if (a.guiName != null && !a.guiName.StartsWith(pre)) { a.guiName = pre + a.guiName; n++; }
                }
                if (n > 0) Debug.Log("[CaelusEntryDrag] Corona chutes: " + n + " menu entries and actions named Mains / Drogues");
            }
            if (v.LandedOrSplashed) return;
            PartModule mains = null, drogues = null;
            foreach (PartModule m in cap.Modules) { string c = ChuteCap(m); if (c == "ChuteCap") mains = m; else if (c == "DrogueCap") drogues = m; }
            if (mains == null || drogues == null) return;
            if (!ChuteOpen(mains)) { mainsOpenT.Remove(cap.flightID); return; }
            float t0; if (!mainsOpenT.TryGetValue(cap.flightID, out t0)) { mainsOpenT[cap.flightID] = Time.time; return; }
            if (Time.time - t0 < 4f || !ChuteOpen(drogues)) return;
            try { ChuteCut(drogues); Debug.Log("[CaelusEntryDrag] Corona: mains open - drogues cut away at " + v.altitude.ToString("F0") + " m (Apollo sequence)"); }
            catch (Exception ex) { Debug.LogWarning("[CaelusEntryDrag] drogue cut failed: " + ex.Message); }
            mainsOpenT[cap.flightID] = float.MaxValue / 2f;
        }

        static FieldInfo farForceField, farTorqueField; static PropertyInfo farForceProp; static bool reflDone, farWarned;

        static void FarLookup(PartModule m)
        {
            if (reflDone) return;
            reflDone = true;
            var t = m.GetType(); const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            farForceField = t.GetField("worldSpaceAeroForce", F);
            if (farForceField == null) farForceProp = t.GetProperty("worldSpaceAeroForce", F);
            farTorqueField = t.GetField("worldSpaceTorque", F);
            if ((farForceField == null && farForceProp == null) || farTorqueField == null)
                FarWarn("FARAeroPartModule.worldSpaceAeroForce/worldSpaceTorque not found (another FAR version?)");
        }

        static void FarWarn(string why)
        {
            if (farWarned) return;
            farWarned = true;
            Debug.LogWarning("[CaelusEntryDrag] " + why + " - FAR's forces on the Corona capsule are not cancelled; its entry may differ");
        }

        static Vector3 FarVector(Part p, bool torque)
        {
            foreach (PartModule m in p.Modules)
            {
                if (m.moduleName != "FARAeroPartModule") continue;
                FarLookup(m);
                try
                {
                    object o = torque ? (farTorqueField != null ? farTorqueField.GetValue(m) : null)
                                      : farForceField != null ? farForceField.GetValue(m) : farForceProp != null ? farForceProp.GetValue(m, null) : null;
                    if (o is Vector3) return (Vector3)o;
                }
                catch (Exception e) { FarWarn("reading FAR's " + (torque ? "torque" : "force") + " failed (" + e.GetType().Name + ": " + e.Message + ")"); }
            }
            return Vector3.zero;
        }

        static void SpinCut(Vessel v, Vector3 cut)
        {
            Vector3 c = Vector3.zero; float m = 0;
            foreach (var p in v.parts) if (p.rb != null) { c += p.rb.worldCenterOfMass * p.rb.mass; m += p.rb.mass; }
            if (m <= 0) return; c /= m;
            foreach (var p in v.parts)
            {
                if (p.rb == null) continue;
                p.rb.angularVelocity -= cut;
                p.rb.velocity -= Vector3.Cross(cut, p.rb.worldCenterOfMass - c);
            }
        }

        static float RcFloat(PartModule m, string f)
        {
            object o = m.Fields.GetValue(f);
            return o is float ? (float)o : o is double ? (float)(double)o : float.NaN;
        }
        static bool rcWarned;

        void Start() { TimingManager.FixedUpdateAdd(TimingManager.TimingStage.BetterLateThanNever, Step); }
        void OnDestroy() { TimingManager.FixedUpdateRemove(TimingManager.TimingStage.BetterLateThanNever, Step); }

        float nextSlowLog;
        void Step()
        {
            long tA = System.Diagnostics.Stopwatch.GetTimestamp();
            StepAll();
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - tA) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (ms > 20.0 && Time.realtimeSinceStartup > nextSlowLog) { nextSlowLog = Time.realtimeSinceStartup + 30f; Debug.Log("[CaelusEntryDrag] slow physics step " + ms.ToString("F0") + " ms"); }
        }
        void StepAll()
        {
            foreach (var v in FlightGlobals.VesselsLoaded)
            {
                if (v == null || v.packed) continue;
                Part cap = null; bool hasSM = false;
                foreach (var p in v.parts)
                {
                    if (p.partInfo == null) continue;
                    string n = p.partInfo.name;
                    if (n.StartsWith("CaelusCorona.Capsule", StringComparison.Ordinal)) cap = p;
                    else if (n.StartsWith("CaelusCorona.ServiceModule", StringComparison.Ordinal)) hasSM = true;
                }
                if (cap != null) { SyncChute(cap); ChuteHelp(cap, v); }
                double peak;
                if (cap != null && v.LandedOrSplashed && peakG.TryGetValue(v.id, out peak))
                {
                    peakG.Remove(v.id);
                    Debug.Log("[CaelusEntryDrag] " + v.vesselName + " " + (v.Splashed ? "splashed down" : "landed") + ", entry peak " + peak.ToString("F1") + " g");
                }
                if (v.atmDensity <= 0 || cap == null || hasSM || cap.rb == null || !FarPresent()) continue;
                Vector3d vel = v.srf_velocity; double sp = vel.magnitude; if (sp < 1) continue;
                Vector3 dir = (Vector3)(vel / sp);
                double q = 0.5 * v.atmDensity * sp * sp / 1000.0;
                double cd = v.mach >= 3 ? CdHyper : v.mach <= 1 ? CdSub : CdSub + (CdHyper - CdSub) * (v.mach - 1) / 2;
                double chute = 0;
                foreach (PartModule m in cap.Modules)
                {
                    var pc = m as ModuleParachute; if (pc == null) continue;
                    double k = Math.Max(0.0, pc.fullyDeployedDrag / MainChuteDrag);
                    if (pc.deploymentState == ModuleParachute.deploymentStates.DEPLOYED) chute += ChuteFull * k;
                    else if (pc.deploymentState == ModuleParachute.deploymentStates.SEMIDEPLOYED) chute += ChuteSemi * k;
                }

                foreach (PartModule m in cap.Modules)
                {
                    if (m.moduleName != "RealChuteFAR") continue;
                    string st = m.Fields.GetValue("depState") as string ?? "?";
                    bool open = st == "PREDEPLOYED" || st == "DEPLOYED";
                    float t0; if (!rcOpenT.TryGetValue(m, out t0)) t0 = -1;
                    if (open && t0 < 0) { t0 = Time.time; rcOpenT[m] = t0; }
                    if (!open) { rcOpenT.Remove(m); continue; }
                    float cur = RcFloat(m, "currentArea");
                    if (float.IsNaN(cur))
                    {
                        if (!rcWarned) { rcWarned = true; Debug.LogWarning("[CaelusEntryDrag] RealChuteFAR field 'currentArea' not found (another FAR version?) - Corona canopy top-up skipped"); }
                        continue;
                    }
                    string capN = m.Fields.GetValue("capName") as string ?? "";
                    float d = capN == "ChuteCap" ? (float)MainDiam : capN == "DrogueCap" ? (float)DrogueDiam : RcFloat(m, st == "DEPLOYED" ? "deployedDiameter" : "preDeployedDiameter");
                    float speed = RcFloat(m, "semiDeploymentSpeed");
                    if (float.IsNaN(d) || float.IsNaN(speed)) continue;
                    float ramp = Mathf.Clamp01((Time.time - t0) * Math.Max(0.05f, speed));
                    double target = Math.PI / 4 * d * d * ramp * ramp;
                    chute += Math.Max(0, target - cur);
                }

                double pk;
                if (peakG.TryGetValue(v.id, out pk)) peakG[v.id] = Math.Max(pk, v.geeForce);
                else if (!v.LandedOrSplashed && v.altitude > 300) { peakG[v.id] = v.geeForce; Debug.Log("[CaelusEntryDrag] " + v.vesselName + ": Corona capsule entry model active"); }
                double want = q * (cd * RefArea + chute);

                foreach (var p in v.parts)
                {
                    if (p.rb == null) continue;
                    Vector3 f = FarVector(p, false), tq = FarVector(p, true);
                    if (f != Vector3.zero) p.rb.AddForce(-f, ForceMode.Force);
                    if (tq != Vector3.zero) p.rb.AddTorque(-tq, ForceMode.Force);
                }
                double addBody = q * cd * RefArea, addChute = q * chute;

                if (addBody > 0) cap.rb.AddForceAtPosition(-dir * (float)addBody, cap.rb.worldCenterOfMass + cap.transform.up * 0.8f, ForceMode.Force);

                Vector3 w = cap.rb.angularVelocity, ax = cap.transform.up;
                Vector3 wPerp = w - ax * Vector3.Dot(w, ax);
                double cq = PitchDamp * q * RefArea * RefDiam * RefDiam / (2 * sp);
                cap.rb.AddTorque(-wPerp * (float)cq, ForceMode.Force);

                float kd = (float)(DampRate * Math.Min(1.0, q / 1.0));
                if (kd > 0) SpinCut(v, wPerp * (1f - Mathf.Exp(-kd * TimeWarp.fixedDeltaTime)));
                if (addChute > 0)
                {
                    Vector3 top = cap.transform.position + cap.transform.up * 3.0f;
                    cap.rb.AddForceAtPosition(-dir * (float)addChute, top, ForceMode.Force);
                    SpinCut(v, cap.rb.angularVelocity * (1f - (float)Math.Exp(-TimeWarp.fixedDeltaTime / 3.0)));
                }

                if (chute == 0 && v.mach > 2 && v.altitude < 90000)
                {
                    double fade = Math.Min(1, v.mach - 2);
                    Vector3 up = (Vector3)(v.CoMD - v.mainBody.position).normalized;
                    Vector3 lv = up - dir * Vector3.Dot(up, dir);
                    if (lv.sqrMagnitude > 1e-6)
                    {
                        lv.Normalize();
                        double s = Math.Max(-1, Math.Min(1, (VsTarget - v.verticalSpeed) / VsBand));
                        cap.rb.AddForce(lv * (float)(LoverD * want * fade * s), ForceMode.Force);
                    }
                }
            }
        }
    }
}
