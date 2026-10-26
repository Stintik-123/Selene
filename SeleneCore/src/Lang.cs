using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Controls;
using System.Windows.Data;

namespace Selene
{
    // Selene's words in Playnite's language. A translation is a plain text file in theme\Localization named after
    // Playnite's language setting (de_DE.txt, or just de.txt for every German), one line per phrase:
    //     Who's playing? = Wer spielt?
    // Every TextBlock Selene or the theme shows is looked up by its English text when it loads (HomeNav.OnTextLoaded), so a
    // translation needs no code. Texts made of numbers (play times, counts) stay as they are.
    // To collect the English phrases for a new language: Advanced > "Collect words to translate" (CollectIfAsked).
    public static class Lang
    {
        private static Dictionary<string, string> table;
        private static HashSet<string> seen;
        private static string collectFile;
        public static string Language { get; private set; }

        public static bool Active { get { return table != null && table.Count > 0; } }

        public static void Load(object api)
        {
            try
            {
                Language = PlayniteLanguage(api);
                if (string.IsNullOrEmpty(Language) || Language == "english") Language = System.Globalization.CultureInfo.CurrentUICulture.Name.Replace('-', '_');
                // in Selene Core's own data folder first (a theme update leaves it alone), then the theme's Localization folder
                var folders = new[] { Path.Combine(SelenePaths.DataFolder, "Localization"), SelenePaths.ThemeFile("Localization") };
                string file = null;
                var looked = new List<string>();
                if (!string.IsNullOrEmpty(Language))
                    foreach (var folder in folders)
                    {
                        if (file != null) break;
                        var exact = Path.Combine(folder, Language + ".txt");
                        var prefix = Path.Combine(folder, Language.Split('_', '-')[0] + ".txt");
                        looked.Add(exact); if (prefix != exact) looked.Add(prefix);
                        file = File.Exists(exact) ? exact : File.Exists(prefix) ? prefix : null;
                    }
                table = file != null ? Read(file) : null;
                if (table != null) HomeNav.Log("language: " + Language + ", " + table.Count + " phrases from " + file);
                else HomeNav.Log("language: " + Language + ": no translation (looked for " + string.Join(", ", looked.ToArray()) + ")");
            }
            catch (Exception ex) { HomeNav.Log("language: " + ex.Message); }
        }

        // ---- collecting the English phrases to translate: Advanced > "Collect words to translate" writes each phrase shown
        // to "Selene phrases.txt" on the Desktop, once each, " = " after it. (The old way, an empty "selene-strings" in
        // %TEMP%, still works as a file, a .txt or a folder: Explorer hides the .txt, and it failed silently.)
        public static void CollectIfAsked()
        {
            var temp = Path.GetTempPath();
            bool marker = File.Exists(Path.Combine(temp, "selene-strings")) || Directory.Exists(Path.Combine(temp, "selene-strings"))
                       || (File.Exists(Path.Combine(temp, "selene-strings.txt")) && new FileInfo(Path.Combine(temp, "selene-strings.txt")).Length == 0);
            Collect(Features.On("translatecollect") || marker);
        }

        // (the Desktop as the user sees it: with OneDrive it's the OneDrive one, not the folder under the profile)
        public static string PhrasesFile { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Selene phrases.txt"); } }
        private static int written;   // phrases written this session (the first and a failure go to the log)

        public static void Collect(bool on)
        {
            if (!on) { collectFile = null; return; }
            try
            {
                collectFile = PhrasesFile;
                seen = new HashSet<string>(StringComparer.Ordinal);
                if (File.Exists(collectFile))
                    foreach (var line in File.ReadAllLines(collectFile)) { int at = line.IndexOf(" = ", StringComparison.Ordinal); if (at > 0) seen.Add(line.Substring(0, at)); }
                else File.WriteAllText(collectFile, "# Selene's phrases: write your language after each \" = \", then save this as theme\\Localization\\<language>.txt" + Environment.NewLine);
                HomeNav.Log("language: collecting phrases into " + collectFile + " (" + seen.Count + " so far)");
            }
            catch (Exception ex) { collectFile = null; HomeNav.Log("language: can't collect phrases: " + ex.Message); }
        }

        // Playnite's own language setting (en_US, de_DE..., "english" for the default), read loosely so an SDK change can't break it
        private static string PlayniteLanguage(object api)
        {
            try
            {
                var settings = api.GetType().GetProperty("ApplicationSettings").GetValue(api, null);
                var lang = settings.GetType().GetProperty("Language").GetValue(settings, null) as string;
                return lang;
            }
            catch { return null; }
        }

        private static Dictionary<string, string> Read(string file)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var raw in File.ReadAllLines(file))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int at = line.IndexOf(" = ", StringComparison.Ordinal);
                if (at <= 0) continue;
                var key = line.Substring(0, at).Trim();
                var value = line.Substring(at + 3).Trim();
                if (key.Length > 0 && value.Length > 0) map[key] = value;
            }
            return map;
        }

        // a TextBlock as it loads: its English text swapped for the translation (texts that are bound stay Playnite's)
        public static void Apply(TextBlock t)
        {
            if (t == null || (table == null && collectFile == null)) return;
            var text = t.Text;
            if (string.IsNullOrEmpty(text) || text.Length > 400 || !HasLetters(text)) return;
            if (BindingOperations.GetBindingExpressionBase(t, TextBlock.TextProperty) != null) return;
            if (collectFile != null && seen.Add(text))
            {
                try
                {
                    File.AppendAllText(collectFile, text.Replace("\r", " ").Replace("\n", " ") + " = " + Environment.NewLine);
                    if (++written == 1) HomeNav.Log("language: phrases being written to " + collectFile);
                }
                catch (Exception ex) { if (written >= 0) { written = -1; HomeNav.Log("language: can't write a phrase to " + collectFile + ": " + ex.Message); } }
            }
            string translated;
            if (table != null && table.TryGetValue(text, out translated)) t.Text = translated;
        }

        private static bool HasLetters(string s)
        {
            foreach (var c in s) if (char.IsLetter(c)) return true;
            return false;
        }
    }
}
