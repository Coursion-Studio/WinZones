// WinZones - "Start with Windows" for the Microsoft Store (MSIX) build.
// Packaged apps can't use the registry Run key; they declare a startup task in the manifest
// (windows.startupTask, TaskId "WinZonesStartup") and toggle it through the WinRT StartupTask API.
// Compiled only by store.bat (needs Windows.winmd from the Windows SDK).
// MIT License.

using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace WinZones
{
    internal static class StoreStartup
    {
        private const string TaskId = "WinZonesStartup";

        // WinRT calls run on a worker thread so the UI thread never blocks on a WinRT callback
        private static StartupTask Get()
        {
            return Task.Run(() => StartupTask.GetAsync(TaskId).AsTask()).Result;
        }

        public static bool IsEnabled()
        {
            try
            {
                var s = Get().State;
                return s == StartupTaskState.Enabled || s == StartupTaskState.EnabledByPolicy;
            }
            catch { return false; }
        }

        /// Returns false when Windows refused (e.g. the user turned it off in Settings > Apps > Startup).
        public static bool SetEnabled(bool on)
        {
            try
            {
                var task = Get();
                if (!on) { task.Disable(); return true; }
                var state = Task.Run(() => task.RequestEnableAsync().AsTask()).Result;
                return state == StartupTaskState.Enabled || state == StartupTaskState.EnabledByPolicy;
            }
            catch { return false; }
        }
    }
}
