// WinZones unit tests: plain C#, no framework. Run tests\run-tests.bat (builds WinZones.exe first if needed).
// Copyright (c) 2026 the WinZones authors. MIT License (see LICENSE).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Web.Script.Serialization;
using WinZones;

[assembly: System.Reflection.AssemblyTitle("WinZones.Tests")]

internal static class Tests
{
    private static int passed, failed;

    private static void Check(bool ok, string name)
    {
        if (ok) passed++;
        else { failed++; Console.WriteLine("FAIL  " + name); }
    }

    private static void Eq<T>(T actual, T expected, string name)
    {
        bool ok = EqualityComparer<T>.Default.Equals(actual, expected);
        if (!ok) Console.WriteLine("      expected " + expected + ", got " + actual);
        Check(ok, name);
    }

    private static Monitor Mon(int x, int y, int w, int h, bool primary = false)
    {
        return new Monitor { Bounds = new Rectangle(x, y, w, h), Work = new Rectangle(x, y, w, h - 48), Primary = primary, Device = "M" + x };
    }

    private static int Main()
    {
        GameModeTests();
        AppRuleTests();
        WorkspaceTests();
        ConfigTests();
        UpdaterTests();
        Eq(Program.CliCommand(new[] { "--workspace", "Coding" }), "workspace:Coding", "--workspace restores a workspace");
        Eq(Program.CliCommand(new[] { "--ARRANGE" }), "arrange", "switches are case-insensitive");
        Eq(Program.CliCommand(new[] { "--workspace" }), null, "--workspace needs a name");
        Eq(Program.CliCommand(new[] { "--after-update", "1.2.0" }), null, "the updater switch is not a command");
        Console.WriteLine();
        Console.WriteLine(passed + " passed, " + failed + " failed");
        return failed == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ game mode
    private static void GameModeTests()
    {
        var mon = new Rectangle(0, 0, 2560, 1440);
        int caption = N.WS_CAPTION, popup = unchecked((int)0x80000000);
        Check(GameDetector.IsFullscreen(new Rectangle(0, 0, 2560, 1440), mon, popup), "borderless window covering the monitor is full screen");
        Check(GameDetector.IsFullscreen(new Rectangle(-8, -8, 2576, 1456), mon, popup), "slightly larger borderless window is full screen");
        Check(!GameDetector.IsFullscreen(new Rectangle(0, 0, 2560, 1440), mon, caption), "window with a title bar is not full screen (maximized app)");
        Check(!GameDetector.IsFullscreen(new Rectangle(0, 0, 2560, 1392), mon, popup), "window above the taskbar is not full screen");
        Check(!GameDetector.IsFullscreen(new Rectangle(100, 0, 2460, 1440), mon, popup), "window not reaching the left edge is not full screen");

        var cfg = new Config();
        Check(cfg.GameMode && cfg.GameModeAnyFullscreen, "game mode is on by default");
        cfg.GameMode = false;
        Eq(GameDetector.Check(cfg), null, "game mode off never pauses");
    }

    // ------------------------------------------------------------------ app rules
    private static void AppRuleTests()
    {
        var r = new AppRule { Process = "Spotify.exe", Place = "Zone 3" };
        Check(r.Matches("spotify", "Spotify Premium"), "rule matches process name, any case, with .exe");
        Check(!r.Matches("spotifyd", "x"), "rule does not match a different process");
        r.TitleContains = "premium";
        Check(r.Matches("Spotify", "Spotify Premium"), "title filter matches case-insensitively");
        Check(!r.Matches("Spotify", "Spotify Free"), "title filter rejects other titles");
        r.Enabled = false;
        Check(!r.Matches("Spotify", "Spotify Premium"), "disabled rule never matches");
        Check(!new AppRule().Matches("", ""), "empty rule never matches");

        var cfg = new Config();
        cfg.AppRules.Add(new AppRule { Process = "code", TitleContains = "notes", Place = "Zone 1" });
        cfg.AppRules.Add(new AppRule { Process = "code", Place = "Zone 2" });
        Eq(AppRules.Find(cfg, "Code", "my notes - VS Code").Place, "Zone 1", "first matching rule wins");
        Eq(AppRules.Find(cfg, "Code", "project - VS Code").Place, "Zone 2", "falls through to the next rule");
        Eq(AppRules.Find(cfg, "notepad", "x"), null, "no rule for other apps");

        var m = Mon(0, 0, 1000, 548);   // work area 1000 x 500
        var layout = new Layout("Halves", new Zone(0, 0, 50, 100), new Zone(50, 0, 50, 100));
        Eq(AppRules.TargetRect("Zone 2", m, layout, 0).Value, new Rectangle(500, 0, 500, 500), "zone 2 of halves");
        Eq(AppRules.TargetRect("Zone 7", m, layout, 0).Value, new Rectangle(500, 0, 500, 500), "zone past the end uses the last zone");
        Eq(AppRules.TargetRect("Fill", m, layout, 0).Value, new Rectangle(0, 0, 1000, 500), "fill");
        Eq(AppRules.TargetRect("Fill", m, layout, 10).Value, new Rectangle(10, 10, 980, 480), "fill with a gap");
        Check(AppRules.TargetRect("Maximize", m, layout, 0) == null, "maximize has no rectangle");
        var c = AppRules.TargetRect("Center", m, layout, 0).Value;
        Check(Math.Abs((c.Left - m.Work.Left) - (m.Work.Right - c.Right)) <= 1, "center is centered");

        var mons = new List<Monitor> { Mon(0, 0, 1920, 1080), Mon(1920, 0, 2560, 1440) };
        Eq(AppRules.TargetMonitor(new AppRule { Monitor = 2 }, IntPtr.Zero, mons).Bounds.X, 1920, "monitor 2 is the second from the left");
        Eq(AppRules.TargetMonitor(new AppRule { Monitor = 5 }, IntPtr.Zero, mons).Bounds.X, 1920, "missing monitor falls back to the last one");
        Eq(AppRules.Normalize(" Discord.EXE "), "Discord", "process names are normalized");
    }

    // ------------------------------------------------------------------ workspaces
    private static void WorkspaceTests()
    {
        var mons = new List<Monitor> { Mon(0, 0, 1920, 1080, true), Mon(1920, 0, 2560, 1440) };
        Eq(Workspaces.MonitorSetup(mons), "1920x1080@0,0|2560x1440@1920,0", "monitor setup signature");

        var s = new WorkspaceWindow { Process = "code", Monitor = 2, X = 50, Y = 0, W = 50, H = 100 };
        Eq(Workspaces.TargetRect(s, mons), new Rectangle(1920 + 1280, 0, 1280, 1392), "saved window goes back to monitor 2");
        var single = new List<Monitor> { Mon(0, 0, 1920, 1080, true) };
        var r1 = Workspaces.TargetRect(s, single);
        Check(single[0].Work.Contains(r1), "saved window is kept on screen when its monitor is gone");
        var off = new WorkspaceWindow { Monitor = 1, X = 90, Y = 90, W = 50, H = 50 };
        Check(single[0].Work.Contains(Workspaces.TargetRect(off, single)), "window hanging off the edge is pulled back on screen");

        Check(Workspaces.Score(s, "chrome", "x", "y") == 0, "different app never matches");
        var saved = new List<WorkspaceWindow>
        {
            new WorkspaceWindow { Process = "chrome", Title = "Docs - Google Chrome" },
            new WorkspaceWindow { Process = "chrome", Title = "YouTube - Google Chrome" },
            new WorkspaceWindow { Process = "code", Title = "app.cs - WinZones - Visual Studio Code" },
            new WorkspaceWindow { Process = "slack", Title = "Slack" },
        };
        var open = new List<Win>
        {
            new Win { Process = "chrome", Title = "YouTube - Google Chrome" },
            new Win { Process = "code", Title = "other.cs - WinZones - Visual Studio Code" },
            new Win { Process = "chrome", Title = "Gmail - Google Chrome" },
            new Win { Process = "notepad", Title = "x" },
        };
        var m = Workspaces.Match(saved, open).ToDictionary(p => p.Key, p => p.Value);
        Eq(m.Count, 3, "three saved windows found");
        Eq(m[1], 0, "same title is matched exactly");
        Eq(m[0], 2, "the other chrome window takes the remaining saved chrome slot");
        Eq(m[2], 1, "editor matched by similar title");
        Check(!m.ContainsKey(3), "closed app is not matched");
        Check(m.Values.Distinct().Count() == m.Count, "each open window is used once");

        // JSON round trip of a workspace
        var cfg = new Config();
        cfg.Workspaces.Add(new Workspace { Name = "Coding", AutoRestore = true, MonitorSetup = "a", Windows = saved });
        var back = new JavaScriptSerializer().Deserialize<Config>(new JavaScriptSerializer().Serialize(cfg));
        Eq(back.Workspaces[0].Windows.Count, 4, "workspace survives a save and load");
        Check(back.Workspaces[0].AutoRestore, "workspace options survive a save and load");
    }

    // ------------------------------------------------------------------ settings file
    private static void ConfigTests()
    {
        // a 1.2 settings file: no game mode, app rules, workspaces or new shortcuts
        string old = "{\"Gap\":8,\"Hotkeys\":{\"AutoArrange\":\"Win+Alt+A\",\"Fill\":\"Win+Alt+P\"},\"Layouts\":[{\"Name\":\"Halves\",\"Zones\":[{\"X\":0,\"Y\":0,\"W\":50,\"H\":100}]}]}";
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wz-test-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        string file = System.IO.Path.Combine(dir, "winzones.json");
        System.IO.File.WriteAllText(file, old);
        var field = typeof(ConfigStore).GetField("path", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        object prev = field.GetValue(null);
        field.SetValue(null, file);
        try
        {
            string err;
            var c = ConfigStore.Load(out err);
            Eq(err, null, "old settings file loads");
            Check(c.GameMode, "game mode defaults to on for old settings");
            Check(c.AppRules != null && c.Workspaces != null && c.GameApps != null, "new lists exist for old settings");
            Eq(c.Hotkeys["TogglePause"], "", "new default shortcut is skipped when the combination is already used");
            Check(c.Hotkeys.ContainsKey("SaveWorkspace"), "new shortcut slots are added");
            Eq(c.Hotkeys["Fill"], "Win+Alt+P", "existing shortcuts are kept");
        }
        finally
        {
            field.SetValue(null, prev);
            try { System.IO.Directory.Delete(dir, true); } catch { }
        }
    }

    // ------------------------------------------------------------------ updates
    private static void UpdaterTests()
    {
        Eq(Updater.Parse("v1.3.0"), new Version(1, 3, 0), "tag parses");
        Eq(Updater.Parse("1.3"), new Version(1, 3, 0), "two-part version parses");
        Check(Updater.Parse("nope") == null, "junk version is rejected");
        string json = "{\"tag_name\":\"v9.0.0\",\"assets\":[" +
                      "{\"name\":\"WinZonesSetup-9.0.0.exe\",\"browser_download_url\":\"u0\"}," +
                      "{\"name\":\"WinZones-9.0.0-win.zip\",\"browser_download_url\":\"u1\",\"digest\":\"sha256:ab\"}," +
                      "{\"name\":\"WinZones-9.0.0-win.zip.sha256\",\"browser_download_url\":\"u2\"}]}";
        var info = Updater.ParseRelease(json, new Version(1, 3, 0));
        Check(info != null && info.ZipUrl == "u1" && info.ShaUrl == "u2" && info.ZipDigest == "ab", "release with an installer asset still updates from the zip");
        Check(Updater.ParseRelease(json, new Version(9, 0, 0)) == null, "same version is not an update");
    }
}
