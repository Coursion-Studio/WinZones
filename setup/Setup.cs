// WinZones Setup - a small per-user installer: no admin rights, nothing outside your profile.
// Copyright (c) 2026 the WinZones authors. MIT License (see LICENSE).
//
//   WinZonesSetup.exe                    install or update (window)
//   WinZonesSetup.exe /S                 silent install (winget, scripts); add /nostartup, /launch, /dir="<folder>"
//   Uninstall.exe /uninstall [/S] [/purge]   remove it again; /purge also deletes settings
//
// Exit codes: 0 done, 1 failed, 2 cancelled.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using WinKit;

[assembly: AssemblyTitle("WinZones Setup")]
[assembly: AssemblyProduct("WinZones")]
[assembly: AssemblyDescription("Installs WinZones for the current user")]
[assembly: AssemblyCompany("Coursion Studio")]
[assembly: AssemblyCopyright("Copyright (c) 2026 kuraydev - MIT License")]

namespace WinZonesSetup
{
    internal static class Installer
    {
        public const string AppName = "WinZones";
        public const string Publisher = "Coursion Studio";
        public const string Url = "https://github.com/Coursion-Studio/WinZones";
        public const string ExeName = "WinZones.exe";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\WinZones";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "WinZones";   // same name and format the app's own "Start with Windows" uses
        private static readonly string[] Files = { "WinZones.exe", "LICENSE.txt" };

        public static string Version { get { return Build.Version; } }
        public static readonly string DefaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);
        public static string Dir = DefaultDir;

        public static string ExePath { get { return Path.Combine(Dir, ExeName); } }
        private static string UninstallerPath { get { return Path.Combine(Dir, "Uninstall.exe"); } }
        private static string ShortcutPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk"); } }
        private static string DataDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName); } }

        /// Folder of an existing install (from Installed apps), or null.
        public static string InstalledDir
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                {
                    string d = k == null ? null : k.GetValue("InstallLocation") as string;
                    return d != null && File.Exists(Path.Combine(d, ExeName)) ? d : null;
                }
            }
        }

        public static string InstalledVersion
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                    return k == null ? null : k.GetValue("DisplayVersion") as string;
            }
        }

        public static bool StartupEnabled
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(RunValue) != null;
            }
        }

        // ------------------------------------------------------------ install / update
        public static void Install(bool startup, Action<double, string> progress)
        {
            progress(0.08, "Closing WinZones...");
            StopApp();

            progress(0.25, "Copying files...");
            Directory.CreateDirectory(Dir);
            foreach (var f in Files) WriteWithRetry(Path.Combine(Dir, f), Payload(f));
            string self = Assembly.GetExecutingAssembly().Location;
            if (!SamePath(self, UninstallerPath)) WriteWithRetry(UninstallerPath, File.ReadAllBytes(self));
            TryDelete(Path.Combine(Dir, "WinZones.exe.old"));   // left over by an in-app update

            progress(0.6, "Adding WinZones to the Start menu...");
            CreateShortcut();

            progress(0.8, "Registering with Windows...");
            Register();
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (startup) k.SetValue(RunValue, "\"" + ExePath + "\"");
                else if (PointsHere(k.GetValue(RunValue) as string)) k.DeleteValue(RunValue, false);
            }
            progress(1, "Done");
        }

        public static void Launch()
        {
            Process.Start(new ProcessStartInfo(ExePath) { UseShellExecute = true, WorkingDirectory = Dir });
        }

        // ------------------------------------------------------------ uninstall
        public static void Uninstall(bool removeData, Action<double, string> progress)
        {
            progress(0.1, "Closing WinZones...");
            StopApp();

            progress(0.35, "Removing the shortcut and startup entry...");
            TryDelete(ShortcutPath);
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                if (k != null && PointsHere(k.GetValue(RunValue) as string)) k.DeleteValue(RunValue, false);
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);

            progress(0.65, "Removing files...");
            foreach (var f in Files) TryDelete(Path.Combine(Dir, f));
            TryDelete(Path.Combine(Dir, "WinZones.exe.old"));
            if (removeData) { try { if (Directory.Exists(DataDir)) Directory.Delete(DataDir, true); } catch { } }

            string self = Assembly.GetExecutingAssembly().Location;
            if (self.StartsWith(Dir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            {
                // we're running from the install folder: let cmd remove it once we've exited
                Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + Dir + "\"")
                { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
            }
            else
            {
                TryDelete(UninstallerPath);
                try { if (Directory.Exists(Dir) && !Directory.EnumerateFileSystemEntries(Dir).Any()) Directory.Delete(Dir); } catch { }
            }
            progress(1, "Done");
        }

        // ------------------------------------------------------------ helpers
        private static bool SamePath(string a, string b)
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }

        private static bool PointsHere(string runValue)
        {
            return runValue != null && runValue.IndexOf(Dir, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// Asks a running WinZones to quit (it listens for this signal), then makes sure it's gone.
        private static void StopApp()
        {
            var running = Process.GetProcessesByName("WinZones");
            if (running.Length == 0) return;
            try
            {
                EventWaitHandle quit;
                if (EventWaitHandle.TryOpenExisting("WinZones.Quit", out quit)) using (quit) quit.Set();
            }
            catch { }
            foreach (var p in running)
            {
                try { if (!p.WaitForExit(4000)) { p.Kill(); p.WaitForExit(4000); } } catch { }
            }
        }

        public static byte[] Payload(string name)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload." + name))
            {
                if (s == null) throw new InvalidOperationException("Setup is missing " + name + ".");
                var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        private static void WriteWithRetry(string path, byte[] data)
        {
            for (int i = 0; ; i++)
            {
                try { File.WriteAllBytes(path, data); return; }
                catch (IOException) { if (i >= 20) throw; Thread.Sleep(250); }
                catch (UnauthorizedAccessException) { if (i >= 20) throw; Thread.Sleep(250); }
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void CreateShortcut()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ShortcutPath));
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(t);
            try
            {
                object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { ShortcutPath });
                Type lt = lnk.GetType();
                lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { ExePath });
                lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Dir });
                lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "Zones, hotkeys and smart auto-arrange for Windows" });
                lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { ExePath + ",0" });
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
                Marshal.FinalReleaseComObject(lnk);
            }
            finally { Marshal.FinalReleaseComObject(shell); }
        }

        private static void Register()
        {
            long bytes = 0;
            foreach (var f in Directory.GetFiles(Dir)) bytes += new FileInfo(f).Length;
            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", AppName);
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", Publisher);
                k.SetValue("DisplayIcon", ExePath + ",0");
                k.SetValue("InstallLocation", Dir);
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("UninstallString", "\"" + UninstallerPath + "\" /uninstall");
                k.SetValue("QuietUninstallString", "\"" + UninstallerPath + "\" /uninstall /S");
                k.SetValue("URLInfoAbout", Url);
                k.SetValue("HelpLink", Url + "/issues");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(bytes / 1024), RegistryValueKind.DWord);
            }
        }
    }

    // =====================================================================================
    //  Entry point
    // =====================================================================================
    internal static class Program
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);

        [STAThread]
        private static int Main(string[] args)
        {
            var flags = new HashSet<string>(args.Select(a => a.ToLowerInvariant()));
            bool silent = flags.Contains("/s") || flags.Contains("/silent") || flags.Contains("/verysilent");
            bool uninstall = flags.Contains("/uninstall");
            string dir = args.Where(a => a.StartsWith("/dir=", StringComparison.OrdinalIgnoreCase)).Select(a => a.Substring(5).Trim('"')).FirstOrDefault();

            string self = Assembly.GetExecutingAssembly().Location;
            bool isUninstaller = string.Equals(Path.GetFileName(self), "Uninstall.exe", StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(dir)) Installer.Dir = Path.GetFullPath(dir);
            else if (isUninstaller) Installer.Dir = Path.GetDirectoryName(self);
            else if (Installer.InstalledDir != null) Installer.Dir = Installer.InstalledDir;   // update in place
            if (isUninstaller) uninstall = true;

            using (var mutex = new Mutex(false, "WinZones.Setup"))
            {
                bool owned;
                try { owned = mutex.WaitOne(0, false); } catch (AbandonedMutexException) { owned = true; }
                if (!owned) return 2;

                if (silent)
                {
                    try
                    {
                        if (uninstall) Installer.Uninstall(flags.Contains("/purge"), (p, s) => { });
                        else
                        {
                            Installer.Install(!flags.Contains("/nostartup"), (p, s) => { });
                            if (flags.Contains("/launch")) Installer.Launch();
                        }
                        return 0;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(ex.Message);
                        return 1;
                    }
                }

                try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                var form = new SetupForm(uninstall);
                Application.Run(form);
                return form.ExitCode;
            }
        }
    }

    // =====================================================================================
    //  The setup window
    // =====================================================================================
    internal class SetupForm : Form
    {
        public int ExitCode = 2;
        private readonly bool uninstall;
        private readonly Label heading = new Label(), body = new Label(), status = new Label();
        private readonly KitToggle optA = new KitToggle(), optB = new KitToggle();
        private readonly Label optALabel = new Label(), optBLabel = new Label();
        private readonly KitButton primary, cancel;
        private readonly ProgressLine progress = new ProgressLine();
        private bool busy, done;

        public SetupForm(bool uninstall)
        {
            this.uninstall = uninstall;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(540, 340);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui(9.75f);
            Text = uninstall ? "Uninstall WinZones" : "WinZones Setup";
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("setup.icon.ico"))
                if (s != null) Icon = new Icon(s);

            var logo = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(64, 64), Location = new Point(32, 32), BackColor = Theme.Bg };
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("setup.icon.ico"))
                if (s != null) using (var ico = new Icon(s, 256, 256)) logo.Image = ico.ToBitmap();

            Style(heading, 15f, FontStyle.Bold, Theme.Text);
            heading.Location = new Point(114, 34);
            Style(body, 9.5f, FontStyle.Regular, Theme.TextSub);
            body.Location = new Point(116, 72);
            body.MaximumSize = new Size(390, 0);

            optA.Location = new Point(116, 150);
            optB.Location = new Point(116, 188);
            Style(optALabel, 9.75f, FontStyle.Regular, Theme.Text);
            Style(optBLabel, 9.75f, FontStyle.Regular, Theme.Text);
            optALabel.Location = new Point(166, 152);
            optBLabel.Location = new Point(166, 190);
            optALabel.Click += (s, e) => optA.Checked = !optA.Checked;
            optBLabel.Click += (s, e) => optB.Checked = !optB.Checked;

            progress.Location = new Point(116, 236);
            progress.Size = new Size(390, 6);
            progress.Visible = false;
            Style(status, 9f, FontStyle.Regular, Theme.TextMuted);
            status.Location = new Point(114, 250);

            primary = new KitButton("", uninstall ? ButtonStyle.Danger : ButtonStyle.Primary, null);
            cancel = new KitButton("Cancel", ButtonStyle.Secondary, null);
            primary.Click += (s, e) => OnPrimary();
            cancel.Click += (s, e) => Close();

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 64, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 14, 24, 0), BackColor = Theme.Sidebar };
            primary.Margin = new Padding(8, 0, 0, 0);
            buttons.Controls.Add(primary);
            buttons.Controls.Add(cancel);

            Controls.AddRange(new Control[] { logo, heading, body, optA, optB, optALabel, optBLabel, progress, status, buttons });
            AcceptButton = primary;

            if (uninstall)
            {
                heading.Text = "Uninstall WinZones?";
                body.Text = "WinZones will be closed and removed from this PC, along with its Start menu shortcut and startup entry.";
                optALabel.Text = "Also delete my settings, layouts and workspaces";
                optA.SetInitial(false);
                optB.Visible = optBLabel.Visible = false;
                primary.Text = "Uninstall";
            }
            else
            {
                string installed = Installer.InstalledDir != null ? Installer.InstalledVersion : null;
                bool same = installed == Installer.Version;
                heading.Text = installed == null ? "Install WinZones " + Installer.Version
                             : same ? "Reinstall WinZones " + Installer.Version
                             : "Update WinZones to " + Installer.Version;
                body.Text = (installed != null && !same ? "You have version " + installed + ". Your settings, layouts and workspaces are kept.\r\n\r\n" : "") +
                            "Installs just for you, no admin rights needed:\r\n" + Installer.Dir;
                optALabel.Text = "Start with Windows";
                optA.SetInitial(installed == null || Installer.StartupEnabled);
                optBLabel.Text = "Open WinZones when setup finishes";
                optB.SetInitial(true);
                primary.Text = installed == null ? "Install" : same ? "Reinstall" : "Update";
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { Native.StyleWindow(Handle, Theme.Sidebar); } catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy) { e.Cancel = true; return; }   // never stop half way
            base.OnFormClosing(e);
        }

        private static void Style(Label l, float pt, FontStyle fs, Color c)
        {
            l.AutoSize = true;
            l.Font = Theme.Ui(pt, fs);
            l.ForeColor = c;
            l.BackColor = Color.Transparent;
        }

        private void OnPrimary()
        {
            if (done) { Close(); return; }
            if (busy) return;
            busy = true;
            primary.Enabled = cancel.Enabled = optA.Enabled = optB.Enabled = false;
            progress.Visible = true;
            bool a = optA.Checked, b = optB.Checked;
            var ui = SynchronizationContext.Current;
            Action<double, string> report = (p, s) => ui.Post(_ => { progress.Value = (float)p; status.Text = s; }, null);
            var t = new Thread(() =>
            {
                Exception error = null;
                try
                {
                    if (uninstall) Installer.Uninstall(a, report);
                    else Installer.Install(a, report);
                }
                catch (Exception ex) { error = ex; }
                ui.Post(_ => Finish(error, b), null);
            });
            t.IsBackground = true;
            t.Start();
        }

        private void Finish(Exception error, bool launch)
        {
            busy = false;
            if (error != null)
            {
                ExitCode = 1;
                progress.Visible = false;
                status.ForeColor = Theme.Danger;
                status.Text = "Something went wrong: " + error.Message;
                primary.Enabled = cancel.Enabled = true;
                primary.Text = "Try again";
                return;
            }
            ExitCode = 0;
            done = true;
            optA.Visible = optB.Visible = optALabel.Visible = optBLabel.Visible = false;
            cancel.Visible = false;
            primary.Enabled = true;
            primary.Text = "Close";
            status.Text = "";
            progress.Visible = false;
            if (uninstall)
            {
                heading.Text = "WinZones was removed";
                body.Text = "Thanks for trying it. If something was missing, tell us at\r\n" + Installer.Url + "/issues";
            }
            else
            {
                heading.Text = "WinZones is ready";
                body.Text = "Hold Shift while dragging a window to snap it into a zone. Press Win+Alt+A to arrange everything.\r\n\r\n" +
                            "Find WinZones in the Start menu and in the tray, next to the clock.";
                if (launch) { try { Installer.Launch(); } catch { } }
            }
        }
    }

    /// A thin rounded progress bar.
    internal class ProgressLine : Control
    {
        private float value;
        public float Value { get { return value; } set { this.value = Math.Max(0, Math.Min(1, value)); Invalidate(); } }

        public ProgressLine()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Theme.Bg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            Gfx.FillRound(g, r, r.Height / 2, Theme.Control);
            if (value <= 0) return;
            var f = new RectangleF(0, 0, Math.Max(r.Height, r.Width * value), r.Height);
            using (var b = new LinearGradientBrush(new RectangleF(0, 0, Width, Height), Theme.Accent2, Theme.Accent, 0f))
            using (var path = Gfx.Round(f, f.Height / 2))
                g.FillPath(b, path);
        }
    }
}
