using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace gtt_sidebar.Core.Settings
{
    /// <summary>
    /// Represents a single shortcut item
    /// </summary>
    public class ShortcutItem
    {
        public string Id { get; set; }           // Unique identifier
        public string Label { get; set; }        // Tooltip text
        public string Path { get; set; }         // File path, URL, or command
        public string IconType { get; set; }     // "builtin" or "custom"
        public string IconValue { get; set; }    // Emoji/icon name or file path
        public ShortcutType Type { get; set; }   // Executable, URL, Command
        public int Order { get; set; }           // Display order

        /// <summary>
        /// Validates that this shortcut item has valid data
        /// </summary>
        public bool IsValid()
        {
            if (string.IsNullOrWhiteSpace(Id) ||
                string.IsNullOrWhiteSpace(Label) ||
                string.IsNullOrWhiteSpace(Path))
            {
                return false;
            }

            // Additional validation based on type
            if (Type == ShortcutType.Executable || Type == ShortcutType.WindowsShortcut)
            {
                // extract executable path from command line (handle arguments)
                var executablePath = ExtractExecutablePath(Path);

                // for executables, check if file exists or if it's a known Windows command
                if (!File.Exists(executablePath) && !IsWindowsCommand(executablePath))
                {
                    return false;
                }
            }
            else if (Type == ShortcutType.URL)
            {
                // Basic URL validation
                if (!Path.StartsWith("http://") && !Path.StartsWith("https://") && !Path.StartsWith("ftp://"))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Checks if the path is a known Windows command
        /// </summary>
        private bool IsWindowsCommand(string command)
        {
            var knownCommands = new[] {
                "calc", "calculator",
                "taskmgr", "taskmanager",
                "explorer",
                "mspaint", "paint",
                "cmd", "command",
                "control", "controlpanel",
                "notepad",
                "msconfig",
                "regedit",
                "winver"
            };

            return knownCommands.Contains(command.ToLowerInvariant());
        }

        /// <summary>
        /// Extract the executable path from a command line that might contain arguments
        /// </summary>
        private string ExtractExecutablePath(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine))
                return commandLine;

            var trimmed = commandLine.Trim();

            // handle quoted paths: "C:\Program Files\App\app.exe" --arguments
            if (trimmed.StartsWith("\""))
            {
                var endQuoteIndex = trimmed.IndexOf("\"", 1);
                if (endQuoteIndex > 0)
                {
                    return trimmed.Substring(1, endQuoteIndex - 1);
                }
            }

            // handle unquoted paths: C:\Users\name\App\app.exe --arguments
            var spaceIndex = trimmed.IndexOf(" ");
            if (spaceIndex > 0)
            {
                var potentialPath = trimmed.Substring(0, spaceIndex);
                // check if this looks like a file path and exists
                if (potentialPath.Contains("\\") && File.Exists(potentialPath))
                {
                    return potentialPath;
                }
            }

            // if no arguments detected, return as-is
            return trimmed;
        }

        /// <summary>
        /// Auto-detects the shortcut type based on the path
        /// </summary>
        public static ShortcutType DetectType(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return ShortcutType.Executable;

            // URL detection
            if (path.StartsWith("http://") || path.StartsWith("https://") || path.StartsWith("ftp://"))
                return ShortcutType.URL;

            // Windows shortcut file
            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                return ShortcutType.WindowsShortcut;

            // Check if it's a file path
            if (path.Contains("\\") || path.Contains("/") || File.Exists(path))
                return ShortcutType.Executable;

            // Assume it's a Windows command
            return ShortcutType.Command;
        }
    }

    /// <summary>
    /// Types of shortcuts supported
    /// </summary>
    public enum ShortcutType
    {
        Executable,      // .exe, .bat, .ps1 files
        URL,            // Web pages
        Command,        // Windows Run commands (calc, notepad, etc.)
        WindowsShortcut // .lnk files
    }

    /// <summary>
    /// Main container for all shortcuts data
    /// </summary>
    public class ShortcutsData
    {
        public List<ShortcutItem> Shortcuts { get; set; } = new List<ShortcutItem>();
        public string Version { get; set; } = "1.0.0";

        /// <summary>
        /// Ensures shortcuts have valid order numbers and no duplicates
        /// </summary>
        public void ValidateAndCleanup()
        {
            if (Shortcuts == null)
            {
                Shortcuts = new List<ShortcutItem>();
                return;
            }

            // Remove any invalid shortcuts
            var validShortcuts = Shortcuts.Where(s => s != null && s.IsValid()).ToList();

            // Remove shortcuts with duplicate IDs (keep first occurrence)
            var uniqueShortcuts = validShortcuts
                .GroupBy(s => s.Id)
                .Select(g => g.First())
                .ToList();

            // Ensure proper ordering (0, 1, 2, 3...)
            for (int i = 0; i < uniqueShortcuts.Count; i++)
            {
                uniqueShortcuts[i].Order = i;
            }

            Shortcuts = uniqueShortcuts;

            System.Diagnostics.Debug.WriteLine($"ShortcutsData: Validated {Shortcuts.Count} shortcuts");
        }

        /// <summary>
        /// Adds a new shortcut with proper order assignment
        /// </summary>
        public ShortcutItem AddShortcut(string label, string path, string iconType = "builtin", string iconValue = "📁")
        {
            var shortcut = new ShortcutItem
            {
                Id = Guid.NewGuid().ToString(),
                Label = label,
                Path = path,
                IconType = iconType,
                IconValue = iconValue,
                Type = ShortcutItem.DetectType(path),
                Order = Shortcuts.Count
            };

            Shortcuts.Add(shortcut);
            System.Diagnostics.Debug.WriteLine($"ShortcutsData: Added shortcut '{label}' with ID {shortcut.Id}");
            return shortcut;
        }

        /// <summary>
        /// Removes a shortcut by ID and reorders remaining shortcuts
        /// </summary>
        public bool RemoveShortcut(string shortcutId)
        {
            var shortcut = Shortcuts.FirstOrDefault(s => s.Id == shortcutId);
            if (shortcut == null)
            {
                System.Diagnostics.Debug.WriteLine($"ShortcutsData: Shortcut with ID {shortcutId} not found");
                return false;
            }

            Shortcuts.Remove(shortcut);

            // Reorder remaining shortcuts
            for (int i = 0; i < Shortcuts.Count; i++)
            {
                Shortcuts[i].Order = i;
            }

            System.Diagnostics.Debug.WriteLine($"ShortcutsData: Removed shortcut '{shortcut.Label}', {Shortcuts.Count} shortcuts remaining");
            return true;
        }

        /// <summary>
        /// Reorders shortcuts based on new order list
        /// </summary>
        public void ReorderShortcuts(List<string> newOrderIds)
        {
            if (newOrderIds == null || newOrderIds.Count != Shortcuts.Count)
            {
                System.Diagnostics.Debug.WriteLine("ShortcutsData: Invalid reorder request");
                return;
            }

            var reorderedShortcuts = new List<ShortcutItem>();

            for (int i = 0; i < newOrderIds.Count; i++)
            {
                var shortcut = Shortcuts.FirstOrDefault(s => s.Id == newOrderIds[i]);
                if (shortcut != null)
                {
                    shortcut.Order = i;
                    reorderedShortcuts.Add(shortcut);
                }
            }

            Shortcuts = reorderedShortcuts;
            System.Diagnostics.Debug.WriteLine($"ShortcutsData: Reordered {Shortcuts.Count} shortcuts");
        }

        /// <summary>
        /// 把旧版英文默认快捷方式迁移为中文名称与内置 PNG 图标。
        /// 只处理“名称匹配旧默认项”的记录，用户自己改名过的快捷方式不受影响。
        /// </summary>
        /// <returns>是否发生了改动（调用方据此决定是否回写文件）</returns>
        public bool LocalizeLegacyDefaults()
        {
            if (Shortcuts == null) return false;

            // 旧默认项：英文名称 → (中文名称, 内置图标名)
            var legacyMap = new Dictionary<string, Tuple<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "Calculator",      Tuple.Create("计算器",         "calculator") },
                { "File Explorer",   Tuple.Create("文件资源管理器", "folder-open") },
                { "Paint",           Tuple.Create("画图",           "palette") },
                { "Command Prompt",  Tuple.Create("命令提示符",     "terminal-window") },
                { "Notepad",         Tuple.Create("记事本",         "note-pencil") },
                { "Task Manager",    Tuple.Create("任务管理器",     "pulse") },
                { "Control Panel",   Tuple.Create("控制面板",       "wrench") },
                { "Settings",        Tuple.Create("系统设置",       "wrench") }
            };

            var changed = false;

            foreach (var shortcut in Shortcuts)
            {
                if (shortcut == null) continue;
                if (string.IsNullOrWhiteSpace(shortcut.Label)) continue;

                Tuple<string, string> mapped;
                if (!legacyMap.TryGetValue(shortcut.Label.Trim(), out mapped)) continue;

                shortcut.Label = mapped.Item1;
                shortcut.IconType = "builtin";
                shortcut.IconValue = mapped.Item2;
                changed = true;

                System.Diagnostics.Debug.WriteLine($"ShortcutsData: 迁移旧默认快捷方式 → {mapped.Item1}");
            }

            if (changed)
            {
                System.Diagnostics.Debug.WriteLine("ShortcutsData: 旧默认快捷方式已完成中文本地化");
            }

            return changed;
        }

        /// <summary>
        /// Creates default shortcuts for first-time users
        /// </summary>
        public static ShortcutsData CreateDefault()
        {
            var data = new ShortcutsData();

            // 默认快捷方式：计算器、文件资源管理器、画图、命令提示符、
            // 任务管理器、系统设置、记事本、任务计划/控制面板
            data.AddShortcut("计算器", "calc", "builtin", "calculator");
            data.AddShortcut("文件资源管理器", "explorer", "builtin", "folder-open");
            data.AddShortcut("画图", "mspaint", "builtin", "palette");
            data.AddShortcut("命令提示符", "cmd", "builtin", "terminal-window");
            data.AddShortcut("任务管理器", "taskmgr", "builtin", "pulse");
            data.AddShortcut("系统设置", "control", "builtin", "wrench");
            data.AddShortcut("记事本", "notepad", "builtin", "note-pencil");
            data.AddShortcut("终端 (PowerShell)", "powershell", "builtin", "brackets-curly");

            System.Diagnostics.Debug.WriteLine("ShortcutsData: Created default shortcuts");
            return data;
        }
    }
}