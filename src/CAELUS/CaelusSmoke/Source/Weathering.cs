using System;
using System.Collections.Generic;
using UnityEngine;

namespace CaelusSmoke
{
    public class ModuleCaelusWeathering : PartModule
    {
        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Finish")]
        [UI_ChooseOption(options = new[] { "New", "Used", "Plain" }, display = new[] { "New (real materials)", "Used (wear, stains)", "Plain" }, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.Editor)]
        public string finish = "New";
        [KSPField] public string newPrefix = "Caelus_Surface/Textures/MAT_";

        [KSPField] public string dirtyPrefix = "Caelus_Surface/Textures/RKT_";
        [KSPField] public string dirtySuffix = "_COL";
        [KSPField] public string cleanFolder = "CaelusFX_UpperStages/Models/";

        class Slot { public Material mat; public Texture dirty, clean, fresh; public float mottle; }
        readonly List<Slot> slots = new List<Slot>();
        readonly List<Material> owned = new List<Material>();
        static readonly Dictionary<string, float> mottleOf = new Dictionary<string, float>();
        bool built;

        public override void OnStart(StartState state)
        {
            BaseField f = Fields["finish"];
            if (f != null && f.uiControlEditor != null)
            {
                f.uiControlEditor.onFieldChanged = (a, b) => Apply();
                f.uiControlEditor.onSymmetryFieldChanged = (a, b) => Apply();
            }
            Apply();
        }

        void Build()
        {
            built = true;
            foreach (Renderer r in part.FindModelComponents<Renderer>())
            {
                if (r == null) continue;
                Material[] shared = r.sharedMaterials; bool any = false;
                foreach (Material m in shared) if (m != null && BaseName(m) != null) { any = true; break; }
                if (!any) continue;
                Material[] inst = r.materials;
                owned.AddRange(inst);
                foreach (Material m in inst)
                {
                    if (m == null) continue;
                    bool original;
                    string x = BaseName(m, out original);
                    if (x == null) continue;
                    Texture clean = GameDatabase.Instance.GetTexture(cleanFolder + x, false);
                    Texture dirty = original ? m.mainTexture : GameDatabase.Instance.GetTexture(dirtyPrefix + x + dirtySuffix, false);
                    if (clean == null || dirty == null) continue;
                    Texture fresh = GameDatabase.Instance.GetTexture(newPrefix + x + dirtySuffix, false);
                    float mottle;
                    if (original) { mottle = m.HasProperty("_HexTile") ? m.GetVector("_HexTile").y : 0f; mottleOf[x] = mottle; }
                    else if (!mottleOf.TryGetValue(x, out mottle)) mottle = 0f;
                    slots.Add(new Slot { mat = m, dirty = dirty, clean = clean, fresh = fresh != null ? fresh : clean, mottle = mottle });
                }
            }
        }

        string BaseName(Material m) { bool o; return BaseName(m, out o); }

        string BaseName(Material m, out bool original)
        {
            original = false;
            Texture t = m.HasProperty("_MainTex") ? m.mainTexture : null;
            if (t == null) return null;
            string x;
            if (Strip(t.name, dirtyPrefix, dirtySuffix, out x)) { original = true; return x; }
            if (Strip(t.name, newPrefix, dirtySuffix, out x) || Strip(t.name, cleanFolder, "", out x)) return x;
            return null;
        }

        static bool Strip(string n, string prefix, string suffix, out string x)
        {
            x = null;
            if (n.Length <= prefix.Length + suffix.Length || !n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return false;
            x = n.Substring(prefix.Length, n.Length - prefix.Length - suffix.Length);
            return true;
        }

        void Apply()
        {
            if (!built) Build();
            foreach (Slot s in slots)
            {
                if (s.mat == null) continue;
                bool used = finish == "Used", plain = finish == "Plain";
                s.mat.mainTexture = used ? s.dirty : (plain ? s.clean : s.fresh);
                if (s.mat.HasProperty("_HexTile")) { Vector4 h = s.mat.GetVector("_HexTile"); h.y = used ? s.mottle : 0f; s.mat.SetVector("_HexTile", h); }
            }
        }

        public override string GetInfo() { return "Finish: right-click in the editor - New (real material detail: brushed and machined metal, castings, paint, fabric), Used (scratches, stains, wear) or Plain."; }

        void OnDestroy() { foreach (Material m in owned) if (m != null) Destroy(m); owned.Clear(); slots.Clear(); }
    }
}
