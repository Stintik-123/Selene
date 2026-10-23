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
    // To collect the English phrases for a new language, make an empty file %TEMP%\selene-strings: every phrase shown is then
    // appended to %TEMP%\selene-strings.txt, once each.
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
                var folder = SelenePaths.ThemeFile("Localization");
                string file = null;
                if (!string.IsNullOrEmpty(Language) && Directory.Exists(folder))
                {
                    var exact = Path.Combine(folder, Language + ".txt");
                    var prefix = Path.Combine(folder, Language.Split('_', '-')[0] + ".txt");
                    file = File.Exists(exact) ? exact : File.Exists(prefix) ? prefix : null;
                }
                table = file != null ? Read(file) : null;
                if (table != null) HomeNav.Log("language: " + Language + ", " + table.Count + " phrases from " + Path.GetFileName(file));
                var marker = Path.Combine(Path.GetTempPath(), "selene-strings");
                if (File.Exists(marker)) { collectFile = marker + ".txt"; seen = new HashSet<string>(); HomeNav.Log("language: collecting phrases into " + collectFile); }
            }
            catch (Exception ex) { HomeNav.Log("language: " + ex.Message); }
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
                try { File.AppendAllText(collectFile, text.Replace("\r", " ").Replace("\n", " ") + " = " + Environment.NewLine); } catch { }
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
