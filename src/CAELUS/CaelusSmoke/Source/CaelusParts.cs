using System.Collections.Generic;

namespace CaelusSmoke
{
    public static class CaelusParts
    {
        static readonly string[] prefixes = { "Caelus", "CaelusCorona", "RomaAeterna" };
        static readonly Dictionary<System.Guid, KeyValuePair<int, bool>> vesselCache = new Dictionary<System.Guid, KeyValuePair<int, bool>>();

        public static bool Is(Part p)
        {
            if (p == null || p.partInfo == null || p.partInfo.name == null) return false;
            string n = p.partInfo.name;
            foreach (string s in prefixes) if (n.StartsWith(s, System.StringComparison.Ordinal)) return true;
            return false;
        }

        public static bool Vessel(Vessel v)
        {
            if (v == null || v.parts == null) return false;
            KeyValuePair<int, bool> c;
            if (vesselCache.TryGetValue(v.id, out c) && c.Key == v.parts.Count) return c.Value;
            bool ours = false;
            foreach (Part p in v.parts) if (Is(p)) { ours = true; break; }
            vesselCache[v.id] = new KeyValuePair<int, bool>(v.parts.Count, ours);
            return ours;
        }

        public static bool ActiveVessel() { return Vessel(FlightGlobals.ActiveVessel); }

        public static bool Pad(UnityEngine.Transform t)
        {
            for (int i = 0; i < 12 && t != null; i++, t = t.parent) if (t.name.Contains("StatioAromata")) return true;
            return false;
        }
    }
}
