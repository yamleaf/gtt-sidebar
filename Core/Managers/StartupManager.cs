using Microsoft.Win32;
using System;
using System.IO;
using System.Reflection;

namespace gtt_sidebar.Core.Managers
{
    /// <summary>
    /// Manages Windows startup registration for the application
    /// </summary>
    public static class StartupManager
    {
        private const string REGISTRY_KEY = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string APP_NAME = "DesktopSidebar";

        /// <summary>
        /// 旧版使用的启动项名称：检测到残留时清理，避免重名残留导致开机启动重复
        /// </summary>
        private const string LEGACY_APP_NAME = "gtt-sidebar";

        /// <summary>
        /// 清理旧版本（gtt-sidebar）遗留的开机启动项
        /// </summary>
        private static void CleanupLegacyStartupEntry(RegistryKey key)
        {
            try
            {
                if (key.GetValue(LEGACY_APP_NAME) != null)
                {
                    key.DeleteValue(LEGACY_APP_NAME, false);
                    System.Diagnostics.Debug.WriteLine("已清理旧版开机启动项：gtt-sidebar");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"清理旧版启动项失败：{ex.Message}");
            }
        }

        /// <summary>
        /// Checks if the application is currently set to run at startup
        /// </summary>
        public static bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(REGISTRY_KEY, false))
                {
                    if (key == null)
                        return false;

                    var value = key.GetValue(APP_NAME) as string;
                    var currentPath = GetExecutablePath();

                    // Check if the registry entry exists and points to the current executable
                    return !string.IsNullOrEmpty(value) &&
                           string.Equals(value.Trim('"'), currentPath, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking startup status: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Enables or disables running the application at Windows startup
        /// </summary>
        public static bool SetStartupEnabled(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(REGISTRY_KEY, true))
                {
                    if (key == null)
                    {
                        System.Diagnostics.Debug.WriteLine("Could not open registry key for startup management");
                        return false;
                    }

                    // 顺带清理旧版 gtt-sidebar 的启动项，避免重名残留
                    CleanupLegacyStartupEntry(key);

                    if (enabled)
                    {
                        // Add to startup
                        var executablePath = GetExecutablePath();
                        key.SetValue(APP_NAME, $"\"{executablePath}\"", RegistryValueKind.String);
                        System.Diagnostics.Debug.WriteLine($"Added to startup: {executablePath}");
                    }
                    else
                    {
                        // Remove from startup
                        if (key.GetValue(APP_NAME) != null)
                        {
                            key.DeleteValue(APP_NAME, false);
                            System.Diagnostics.Debug.WriteLine("Removed from startup");
                        }
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting startup status: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets the full path to the current executable
        /// </summary>
        private static string GetExecutablePath()
        {
            return Assembly.GetExecutingAssembly().Location;
        }

        /// <summary>
        /// Gets a user-friendly description of the startup status
        /// </summary>
        public static string GetStartupStatusDescription()
        {
            if (IsStartupEnabled())
            {
                return "Application will start automatically with Windows";
            }
            else
            {
                return "Application will not start automatically";
            }
        }
    }
}