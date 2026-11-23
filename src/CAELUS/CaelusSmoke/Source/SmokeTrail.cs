using System;
using System.Collections.Generic;
using UnityEngine;

namespace CaelusSmoke
{

    public struct SmokeNode
    {
        public double lat, lon, alt;
        public double lat0, lon0, alt0;
        public float rBirth;
        public double born;
        public float r0;
        public float rMax;
        public float dens0;
        public float tau;
        public float life;
        public float buoy;
        public double vE, vN, vU;
        public float jetTau;
        public float growExp;
        public float expandK;
        public double ground;
        public float seed;
        public int chain;
        public int look;
    }

    public class SmokeEmitter
    {
        public Vessel vessel;
        public List<ModuleEngines> engines = new List<ModuleEngines>();
        public SmokeLook look; public int lookIndex;
        public int chain = -1;
        public double lastPushUT = -1, lastSeenUT = -1;
        public double lastLat, lastLon, lastAlt;

        public bool active;
        public Vector3 nozzleWorld, tailWorld, axisWorld;
        public float nozzleRadius, tailRadius, thrustKN, flameLen;
        public float reachLen;
        public bool ourFlame;

        public float measuredLen = -1, measuredRadius = -1;
        public readonly List<Renderer> plumeRenderers = new List<Renderer>(); public double renderersUT = -1;
    }

    public class SmokeChain
    {
        public int id;
        public readonly List<SmokeNode> nodes = new List<SmokeNode>();
        public bool open;
    }

    public class SmokeTrail
    {
        public readonly List<SmokeChain> chains = new List<SmokeChain>();
        static int nextChain = 1;
        readonly System.Random rng = new System.Random(1969);
        double lastThinUT = -1;

        public int NodeCount { get { int c = 0; foreach (SmokeChain ch in chains) c += ch.nodes.Count; return c; } }

        public static float Radius(ref SmokeNode n, double age, double airRel)
        {
            double g = Math.Pow(1.0 + Math.Max(0, age) / Math.Max(0.5, n.tau), n.growExp);
            double thin = Math.Pow(1.0 / Math.Max(0.02, airRel), 1.0 / 6.0);

            double expand = 1.0 + n.expandK * (0.8 + 2.4 * (1.0 - Math.Min(1.0, airRel * 2.0))) * (1.0 - Math.Exp(-Math.Max(0, age) / Math.Max(0.1, SmokeSettings.headExpandS)));

            double lump = 1.0 + 0.0 * (n.seed - 0.5)
                         * (1.0 - Math.Exp(-Math.Max(0, age) / 4.0));

            double taper = n.rBirth > 0 && n.rBirth < n.r0 ? n.rBirth / n.r0 + (1.0 - n.rBirth / n.r0) * (1.0 - Math.Exp(-Math.Max(0, age) / Math.Max(0.1, SmokeSettings.headTaperS))) : 1.0;

            return (float)Math.Min(Math.Min(n.r0 * expand * g * thin * lump * taper, n.rMax * 1.6), SmokeSettings.maxRadiusM);
        }

        public static float Density(ref SmokeNode n, double age, float r)
        {
            float spread = Mathf.Pow(n.r0 / Mathf.Max(0.1f, r), 1.0f);
            float a = (float)(age / Math.Max(1, n.life));
            float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, a));
            float fadeIn = Mathf.Clamp01((float)age / 0.35f);
            return n.dens0 * Mathf.Max(spread, 0.04f) * fade * Mathf.Lerp(0.85f, 1f, fadeIn);
        }

        public void Push(CelestialBody body, Vector3d world, Vector3d velBody, double ut, SmokeEmitter e, float r0, float dens, bool newChain)
        {
            SmokeChain ch = null;
            if (!newChain && e.chain >= 0) foreach (SmokeChain c in chains) if (c.id == e.chain) { ch = c; break; }
            if (ch == null) { ch = new SmokeChain { id = nextChain++, open = true }; chains.Add(ch); e.chain = ch.id; }
            var n = new SmokeNode();
            body.GetLatLonAlt(world, out n.lat, out n.lon, out n.alt);
            n.lat0 = n.lat; n.lon0 = n.lon; n.alt0 = n.alt;
            n.rBirth = e.ourFlame && e.tailRadius > 0 ? Math.Min(e.tailRadius, r0) : r0;
            n.ground = body.pqsController != null ? Math.Max(body.ocean ? 0.0 : double.MinValue, body.TerrainAltitude(n.lat, n.lon, true)) : double.MinValue;
            n.born = ut; n.r0 = r0; n.rMax = r0 * e.look.maxRadiusFactor; n.dens0 = dens; n.tau = e.look.growTauS; n.life = e.look.lifeS;
            n.buoy = e.look.buoyancy; n.seed = (float)rng.NextDouble(); n.chain = ch.id; n.look = e.lookIndex;
            n.jetTau = e.look.jetTauS; n.growExp = e.look.growExponent; n.expandK = e.look.expandK;

            Vector3d up = (world - body.position).normalized;
            Vector3d north = Vector3d.Exclude(up, body.transform.up).normalized;
            Vector3d east = Vector3d.Cross(up, north);
            n.vE = Vector3d.Dot(velBody, east); n.vN = Vector3d.Dot(velBody, north); n.vU = Vector3d.Dot(velBody, up);
            ch.nodes.Add(n);
            e.lastPushUT = ut; e.lastLat = n.lat; e.lastLon = n.lon; e.lastAlt = n.alt;
        }

        public void Close(SmokeEmitter e)
        {
            foreach (SmokeChain c in chains) if (c.id == e.chain) c.open = false;
            e.chain = -1;
        }

        public void Advance(CelestialBody body, double ut, double dt)
        {
            double hdg = SmokeSettings.windHeadingDeg * Math.PI / 180.0;
            double R = body.Radius;
            double kmScale = SmokeSettings.KmScale(body); SmokeSettings.kmScaleNow = kmScale;
            for (int c = chains.Count - 1; c >= 0; c--)
            {
                List<SmokeNode> ns = chains[c].nodes;
                for (int i = ns.Count - 1; i >= 0; i--)
                {
                    SmokeNode n = ns[i];
                    double age = ut - n.born;
                    if (age > n.life) { ns.RemoveAt(i); continue; }
                    if (dt <= 0) continue;

                    double km = n.alt / 1000.0 / kmScale;
                    double wind = SmokeSettings.windSpeed * (1.0 + 0.35 * Math.Min(km, 11.0)) * (km > 25 ? Math.Max(0, 1 - (km - 25) / 15) : 1);
                    double h = hdg + SmokeSettings.windVeerDegPerKm * Math.Min(km, 11.0) * Math.PI / 180.0;

                    double jet = Math.Exp(-age / Math.Max(0.3, n.jetTau));

                    n.lat0 += n.vN * jet * dt / (R + n.alt0) * (180.0 / Math.PI);
                    n.lon0 += n.vE * jet * dt / ((R + n.alt0) * Math.Max(0.05, Math.Cos(n.lat0 * Math.PI / 180.0))) * (180.0 / Math.PI);
                    n.alt0 += n.vU * jet * dt;
                    double east = wind * Math.Sin(h) + n.vE * jet, north = wind * Math.Cos(h) + n.vN * jet;
                    double rr = R + n.alt;
                    n.lat += north * dt / rr * (180.0 / Math.PI);
                    n.lon += east * dt / (rr * Math.Max(0.05, Math.Cos(n.lat * Math.PI / 180.0))) * (180.0 / Math.PI);

                    double cool = 12.0 + 0.4 * Math.Min(n.rMax, 120.0);
                    n.alt += (n.vU * jet + n.buoy * Math.Exp(-age / cool)) * dt;
                    if (n.alt < n.ground + 3) { n.alt = n.ground + 3; if (n.vU < 0) n.vU = 0; }
                    ns[i] = n;
                }
                if (ns.Count == 0 && !chains[c].open) chains.RemoveAt(c);
            }

            if (NodeCount > SmokeSettings.maxSegments / 2 - 16 && ut - lastThinUT > 1.0)
            {
                lastThinUT = ut;
                foreach (SmokeChain ch in chains)
                {
                    List<SmokeNode> ns = ch.nodes; if (ns.Count < 12) continue;
                    var keep = new List<SmokeNode>(ns.Count); int half = ns.Count / 2;
                    for (int i = 0; i < ns.Count; i++) if (i >= half || i == 0 || (i % 3) != 1) keep.Add(ns[i]);
                    ns.Clear(); ns.AddRange(keep);
                }
            }
        }
    }
}
