// WinZones - over-the-air updates from GitHub Releases.
//   check   : GET api.github.com/repos/<repo>/releases/latest, compare with AppInfo.Version
//   download: WinZones-x.y.z-win.zip from the release
//   verify  : SHA-256 must match BOTH the release's .sha256 file and GitHub's own asset digest,
//             and the extracted exe must report the expected version
//   install : rename the running exe to .old (Windows allows this), copy the new one in, restart
// MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace WinZones
{
    internal class UpdateInfo
    {
        public Version Version;
        public string Tag, Notes, PageUrl, ZipUrl, ShaUrl, ZipDigest;
    }

    internal static class Updater
    {
        public const string Repo = "Coursion-Studio/WinZones";
        /// WINZONES_UPDATE_URL overrides the feed (testing only, e.g. a file:// JSON in the GitHub release format).
        public static string ApiUrl = Environment.GetEnvironmentVariable("WINZONES_UPDATE_URL") ?? "https://api.github.com/repos/" + Repo + "/releases/latest";

        public static Version Current { get { return Parse(AppInfo.Version); } }

        /// "v1.2.0", "1.2.0-beta" -> 1.2.0 (null if unparseable). Always 3 parts, so versions compare cleanly.
        public static Version Parse(string s)
        {
            s = (s ?? "").Trim().TrimStart('v', 'V');
            int cut = s.IndexOfAny(new[] { '-', '+', ' ' });
            if (cut >= 0) s = s.Substring(0, cut);
            Version v;
            if (!Version.TryParse(s, out v)) return null;
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }

        private static WebClient Client()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2 for GitHub
            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = "WinZones-Updater/" + AppInfo.Version;
            wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            return wc;
        }

        /// The latest published release if it is newer than `current`, otherwise null. Throws on network errors.
        public static UpdateInfo CheckLatest(Version current)
        {
            string json;
            using (var wc = Client()) json = wc.DownloadString(ApiUrl);
            return ParseRelease(json, current);
        }

        public static UpdateInfo ParseRelease(string json, Version current)
        {
            var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            if (Flag(d, "draft") || Flag(d, "prerelease")) return null;
            var v = Parse(d.ContainsKey("tag_name") ? d["tag_name"] as string : null);
            if (v == null || current == null || v <= current) return null;

            var info = new UpdateInfo
            {
                Version = v,
                Tag = d["tag_name"] as string,
                Notes = d.ContainsKey("body") ? d["body"] as string : "",
                PageUrl = d.ContainsKey("html_url") ? d["html_url"] as string : "https://github.com/" + Repo + "/releases/latest",
            };
            var assets = d.ContainsKey("assets") ? d["assets"] as ArrayList : null;
            if (assets != null)
                foreach (var o in assets)
                {
                    var a = o as Dictionary<string, object>;
                    if (a == null) continue;
                    string name = a["name"] as string ?? "", url = a["browser_download_url"] as string;
                    if (name.EndsWith("-win.zip", StringComparison.OrdinalIgnoreCase))
                    {
                        info.ZipUrl = url;
                        var digest = a.ContainsKey("digest") ? a["digest"] as string : null;
                        if (digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) info.ZipDigest = digest.Substring(7);
                    }
                    else if (name.EndsWith("-win.zip.sha256", StringComparison.OrdinalIgnoreCase)) info.ShaUrl = url;
                }
            return info.ZipUrl != null && info.ShaUrl != null ? info : null;
        }

        private static bool Flag(Dictionary<string, object> d, string key)
        {
            object o;
            return d.TryGetValue(key, out o) && o is bool && (bool)o;
        }

        /// Downloads and verifies the update. Returns the path of the new WinZones.exe (in a temp folder).
        public static string Download(UpdateInfo info)
        {
            string dir = Path.Combine(Path.GetTempPath(), "WinZones-update-" + info.Version);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            string zip = Path.Combine(dir, "update.zip");
            string shaText;
            using (var wc = Client())
            {
                wc.DownloadFile(info.ZipUrl, zip);
                shaText = wc.DownloadString(info.ShaUrl);
            }
            VerifySha256(zip, shaText, info.ZipDigest);

            string exe = Path.Combine(dir, "WinZones.exe");
            using (var z = ZipFile.OpenRead(zip))
            {
                var entry = z.Entries.FirstOrDefault(e => string.Equals(e.FullName, "WinZones.exe", StringComparison.OrdinalIgnoreCase));
                if (entry == null) throw new InvalidDataException("The update package does not contain WinZones.exe.");
                entry.ExtractToFile(exe, true);
            }
            var fileVersion = Parse(FileVersionInfo.GetVersionInfo(exe).ProductVersion);
            if (fileVersion == null || fileVersion != info.Version)
                throw new InvalidDataException("The downloaded app reports version " + fileVersion + ", expected " + info.Version + ".");
            return exe;
        }

        /// Throws unless the file's SHA-256 matches the .sha256 text (and GitHub's digest, when known).
        public static void VerifySha256(string file, string shaText, string githubDigest)
        {
            string expected = (shaText ?? "").Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            string actual;
            using (var s = File.OpenRead(file))
            using (var h = SHA256.Create())
                actual = BitConverter.ToString(h.ComputeHash(s)).Replace("-", "");
            if (expected.Length != 64 || !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Checksum mismatch: the download does not match the release's .sha256 file.");
            if (!string.IsNullOrEmpty(githubDigest) && !string.Equals(githubDigest, actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Checksum mismatch: the download does not match GitHub's digest.");
        }

        /// Replaces `targetExe` (may be the running exe) with `newExe`. Restores the old file if anything fails.
        public static void Apply(string newExe, string targetExe)
        {
            string old = targetExe + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(targetExe, old);   // a running exe can be renamed, just not overwritten
            try { File.Copy(newExe, targetExe); }
            catch
            {
                try { if (File.Exists(targetExe)) File.Delete(targetExe); File.Move(old, targetExe); } catch { }
                throw;
            }
        }

        /// Starts the new version; it waits for this process to release the single-instance lock.
        public static void Restart(string exe, string fromVersion)
        {
            Process.Start(new ProcessStartInfo(exe, "--after-update " + fromVersion) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) });
        }

        /// Removes leftovers from a previous update (old exe, temp folders).
        public static void Cleanup()
        {
            try { string old = Application.ExecutablePath + ".old"; if (File.Exists(old)) File.Delete(old); } catch { }
            try
            {
                foreach (var d in Directory.GetDirectories(Path.GetTempPath(), "WinZones-update-*"))
                    try { Directory.Delete(d, true); } catch { }
            }
            catch { }
        }

        /// First few lines of the release notes as plain text (for the confirm dialog).
        public static string Summary(string notes, int maxLines = 5)
        {
            if (string.IsNullOrEmpty(notes)) return "";
            var lines = notes.Replace("\r", "").Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("<") && !l.StartsWith("#") && !l.StartsWith(">"))
                .Select(l => System.Text.RegularExpressions.Regex.Replace(l, @"\*\*|__|`|\[([^\]]*)\]\([^)]*\)", "$1"))
                .Take(maxLines);
            return string.Join("\n", lines);
        }
    }
}
