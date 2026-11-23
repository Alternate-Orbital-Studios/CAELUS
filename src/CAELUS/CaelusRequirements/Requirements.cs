using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace CaelusRequirements
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class RequirementsCheck : MonoBehaviour
    {

        public static string Scale()
        {
            foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("Sol_Configuration"))
            {
                string s = (n.GetValue("SystemScale") ?? "").Trim();
                if (s.Equals("Quarter", StringComparison.OrdinalIgnoreCase)) return "Quarter";
                if (s.Equals("Stock", StringComparison.OrdinalIgnoreCase)) return "Kerbin";
                if (s.Length > 0) return "Real";
            }
            CelestialBody home = FlightGlobals.Bodies != null ? FlightGlobals.Bodies.FirstOrDefault(b => b != null && b.isHomeWorld) : null;
            if (home == null) return "Real";
            if (home.Radius > 4.5e6) return "Real";
            if (home.Radius > 1.2e6) return "Quarter";
            return "Kerbin";
        }

        void Start()
        {
            string me = "Requirements";
            try { me = GetType().Assembly.GetName().Name; Check(me); }
            catch (Exception e) { Debug.LogWarning("[" + me + "] add-on check skipped: " + e.Message); }
        }

        static string Entry(ConfigNode r)
        {
            return (r.GetValue("name") ?? "?") + "|" + (r.GetValue("why") ?? "");
        }

        static string Lines(List<string> entries)
        {
            return string.Join("\n\n", entries.Select(m =>
            {
                var p = m.Split(new[] { '|' }, 2); return "<b>" + p[0] + "</b>\n" + (p.Length > 1 ? p[1] : "");
            }).ToArray());
        }

        void Check(string me)
        {
            string title = "Missing add-ons", intro = "", outro = "", folder = "", scale = Scale(), note = "";
            string recIntro = "CAELUS works without these, but you may like them:";
            var missing = new List<string>();
            var recommended = new List<string>();
            foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("CAELUS_REQUIREMENTS"))
            {
                if (n.GetValue("checker") != me) continue;
                if (n.HasValue("title")) title = n.GetValue("title");
                if (n.HasValue("intro")) intro = n.GetValue("intro");
                if (n.HasValue("outro")) outro = n.GetValue("outro");
                if (n.HasValue("folder")) folder = n.GetValue("folder");
                if (n.HasValue("recommend_intro")) recIntro = n.GetValue("recommend_intro");
                foreach (ConfigNode r in n.GetNodes("REQUIRE"))
                    if (!Present(r)) missing.Add(Entry(r));
                foreach (ConfigNode r in n.GetNodes("RECOMMEND"))
                {
                    try { if (!Present(r)) recommended.Add(Entry(r)); }
                    catch (Exception e) { Debug.LogWarning("[" + me + "] recommended add-on check failed: " + e.Message); }
                }
                foreach (ConfigNode s in n.GetNodes("SCALE_NOTE"))
                    if (string.Equals(s.GetValue("scale"), scale, StringComparison.OrdinalIgnoreCase) && s.HasValue("text")) note = s.GetValue("text");
            }
            Debug.Log("[" + me + "] solar-system scale: " + scale + (note.Length > 0 ? " (note shown)" : ""));
            if (recommended.Count > 0) Debug.Log("[" + me + "] recommended (optional) add-ons not installed: " + string.Join(";", recommended.Select(m => m.Split('|')[0]).ToArray()));
            if (missing.Count == 0 && note.Length == 0 && recommended.Count == 0) { Debug.Log("[" + me + "] all required add-ons are installed"); return; }
            string key = string.Join(";", missing.Select(m => m.Split('|')[0]).ToArray()) + "|scale=" + scale;
            if (recommended.Count > 0) key += "|recommend=" + string.Join(";", recommended.Select(m => m.Split('|')[0]).ToArray());
            if (missing.Count > 0) Debug.LogWarning("[" + me + "] missing required add-ons: " + key);
            string flag = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", folder, "PluginData", me + "_dismissed.txt");
            try { if (File.Exists(flag) && File.ReadAllText(flag).Trim() == key) return; } catch (Exception) { }
            string body = "";
            if (missing.Count > 0)
                body = intro + "\n\n" + Lines(missing) + "\n\n" + outro;
            if (recommended.Count > 0)
                body = (body.Length > 0 ? body + "\n\n" : "") + "<b>Recommended (optional)</b>\n" + recIntro + "\n\n" + Lines(recommended);
            if (note.Length > 0) body = (body.Length > 0 ? body + "\n\n" : "") + "<b>Solar-system scale</b>\n" + note.Replace("\\n", "\n");
            if (missing.Count == 0) title = title.Replace("missing add-ons", "please note").Replace("Missing add-ons", "Please note");
            var dialog = new MultiOptionDialog(me, body, title, HighLogic.UISkin, 460f,
                new DialogGUIButton("OK", () => { }, true),
                new DialogGUIButton("Don't show again", () =>
                {
                    try { Directory.CreateDirectory(Path.GetDirectoryName(flag)); File.WriteAllText(flag, key); }
                    catch (Exception e) { Debug.LogWarning("[" + me + "] could not save the choice: " + e.Message); }
                }, true));
            PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, HighLogic.UISkin);
        }

        static bool Present(ConfigNode r)
        {
            foreach (string a in r.GetValues("assembly"))
                if (AssemblyLoader.loadedAssemblies.Any(x => x.name.Equals(a, StringComparison.OrdinalIgnoreCase))) return true;
            foreach (string f in r.GetValues("folder"))
                if (Directory.Exists(Path.Combine(Path.Combine(KSPUtil.ApplicationRootPath, "GameData"), f))) return true;
            foreach (string b in r.GetValues("body"))
                if (FlightGlobals.Bodies != null && FlightGlobals.Bodies.Any(c => c != null && c.bodyName == b)) return true;
            return false;
        }
    }
}
