// WinZones - translations. English is written in the code; other languages are JSON files in lang\
// ("English text": "translation"), compiled into the exe, so it stays a single file.
// Copyright (c) 2026 the WinZones authors. MIT License (see LICENSE).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace WinZones
{
    internal static class L
    {
        /// Languages WinZones ships with: code and the language's own name (shown in the picker).
        public static readonly string[][] Languages =
        {
            new[] { "en", "English" },
            new[] { "de", "Deutsch" },
            new[] { "es", "Español" },
            new[] { "fr", "Français" },
            new[] { "it", "Italiano" },
            new[] { "nl", "Nederlands" },
            new[] { "pl", "Polski" },
            new[] { "pt-BR", "Português (Brasil)" },
            new[] { "tr", "Türkçe" },
            new[] { "ru", "Русский" },
            new[] { "ja", "日本語" },
            new[] { "ko", "한국어" },
            new[] { "zh-Hans", "简体中文" },
        };

        private static Dictionary<string, string> map = new Dictionary<string, string>();
        private static string code = "en";

        /// The language in use right now ("en", "de", "pt-BR", ...).
        public static string Code { get { return code; } }

        /// Culture of the language in use (for upper-casing headers the right way, e.g. Turkish i -> İ).
        public static CultureInfo Culture
        {
            get { try { return CultureInfo.GetCultureInfo(code); } catch { return CultureInfo.InvariantCulture; } }
        }

        /// Switches the language. `preference` is a language code, or "" / null to follow Windows.
        public static void Use(string preference)
        {
            code = Resolve(preference, CultureInfo.CurrentUICulture.Name);
            map = code == "en" ? new Dictionary<string, string>() : Load(code);
            WinKit.KitText.Tr = T;
        }

        /// Picks a shipped language for a preference ("" = Windows' display language `uiCulture`).
        public static string Resolve(string preference, string uiCulture)
        {
            string want = string.IsNullOrWhiteSpace(preference) ? uiCulture ?? "" : preference.Trim();
            foreach (var l in Languages) if (string.Equals(l[0], want, StringComparison.OrdinalIgnoreCase)) return l[0];
            string lower = want.ToLowerInvariant();
            if (lower.StartsWith("zh")) return lower.Contains("hant") || lower.EndsWith("-tw") || lower.EndsWith("-hk") || lower.EndsWith("-mo") ? "en" : "zh-Hans";
            if (lower.StartsWith("pt")) return "pt-BR";
            string two = lower.Split('-')[0];
            foreach (var l in Languages) if (l[0] == two) return l[0];
            return "en";
        }

        /// Native name of a language code ("de" -> "Deutsch").
        public static string NameOf(string languageCode)
        {
            foreach (var l in Languages) if (l[0] == languageCode) return l[1];
            return languageCode;
        }

        /// Translates an English UI string. Unknown strings are shown in English.
        public static string T(string english)
        {
            if (english == null) return null;
            string v;
            return map.TryGetValue(english, out v) && !string.IsNullOrEmpty(v) ? v : english;
        }

        /// Translates a format string, then fills in {0}, {1}, ...
        public static string F(string english, params object[] args)
        {
            try { return string.Format(T(english), args); }
            catch (FormatException) { return string.Format(english, args); }
        }

        /// Marks a string for translation without translating it (for keys kept in English, translated where shown).
        public static string N(string english) { return english; }

        /// Reads the translations compiled into the exe for a language (empty if there are none).
        public static Dictionary<string, string> Load(string languageCode)
        {
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("WinZones.lang." + languageCode + ".json"))
                {
                    if (s == null) return new Dictionary<string, string>();
                    using (var r = new StreamReader(s, Encoding.UTF8)) return Parse(r.ReadToEnd());
                }
            }
            catch { return new Dictionary<string, string>(); }
        }

        public static Dictionary<string, string> Parse(string json)
        {
            var d = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(json);
            var result = new Dictionary<string, string>();
            foreach (var kv in d)
                if (!kv.Key.StartsWith("@")) result[kv.Key] = kv.Value as string;   // "@..." keys are notes for translators
            return result;
        }

        /// Text of a file compiled into the exe (e.g. "CHANGELOG.md"), or null.
        public static string Resource(string name)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("WinZones." + name))
            {
                if (s == null) return null;
                using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
            }
        }
    }
}
