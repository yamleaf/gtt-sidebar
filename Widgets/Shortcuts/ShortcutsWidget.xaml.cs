using gtt_sidebar.Core.Interfaces;
using gtt_sidebar.Core.Settings;
using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace gtt_sidebar.Widgets.Shortcuts
{
    public partial class ShortcutsWidget : UserControl, IWidget
    {
        private ShortcutsData _shortcutsData;
        private int _iconsPerRow = 3; // Default, will be calculated
        private int _visibleShortcuts = 0;
        private const int ICON_SIZE = 18; // Same as weather forecast icons
        private const int ICON_SPACING = 6; // Padding between icons

        public new string Name => "Shortcuts";

        public ShortcutsWidget()
        {
            InitializeComponent();
        }

        public UserControl GetControl() => this;

        public async Task InitializeAsync()
        {
            try
            {
                // Load shortcuts data
                System.Diagnostics.Debug.WriteLine("ShortcutsWidget: Loading shortcuts...");
                _shortcutsData = ShortcutsStorage.LoadShortcuts();

                if (_shortcutsData?.Shortcuts == null)
                {
                    System.Diagnostics.Debug.WriteLine("ShortcutsWidget: No shortcuts data found");
                    _shortcutsData = ShortcutsData.CreateDefault();
                }

                // Calculate layout
                CalculateLayout();

                // Create the shortcuts grid
                CreateShortcutsGrid();

                System.Diagnostics.Debug.WriteLine($"ShortcutsWidget: Initialized with {_shortcutsData.Shortcuts.Count} shortcuts");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ShortcutsWidget InitializeAsync error: {ex.Message}");
            }
        }



        /// <summary>
        /// Calculate how many icons per row based on widget width
        /// </summary>
        private void CalculateLayout()
        {
            try
            {
                // 组件可用宽度（宽度 122px 减去左右内边距）
                var availableWidth = 113.0;

                // 每行图标数量：5 列能让默认 8 个快捷方式排成整齐的两行
                _iconsPerRow = 5;

                var totalIconWidth = _iconsPerRow * ICON_SIZE;
                var totalSpacing = availableWidth - totalIconWidth;
                var spacingPerIcon = Math.Max(2, totalSpacing / (_iconsPerRow + 1)); // +1 for edges

                // 可用高度：头部约 20px，其余留给图标
                var availableHeight = 122.0;
                var iconWithVerticalSpacing = ICON_SIZE + spacingPerIcon;
                var maxRows = Math.Max(1, (int)Math.Floor(availableHeight / iconWithVerticalSpacing));

                _visibleShortcuts = _iconsPerRow * maxRows;

                System.Diagnostics.Debug.WriteLine($"Layout: {_iconsPerRow} icons per row, {maxRows} rows, {_visibleShortcuts} visible shortcuts");
                System.Diagnostics.Debug.WriteLine($"Icon size: {ICON_SIZE}px, Spacing: {spacingPerIcon:F1}px");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error calculating layout: {ex.Message}");
                _iconsPerRow = 5;
                _visibleShortcuts = 20; // 5x4 grid
            }
        }

        /// <summary>
        /// Create the dynamic grid of shortcut icons
        /// </summary>
        private void CreateShortcutsGrid()
        {
            try
            {
                ShortcutsGrid.Children.Clear();
                ShortcutsGrid.RowDefinitions.Clear();
                ShortcutsGrid.ColumnDefinitions.Clear();

                if (_shortcutsData?.Shortcuts == null || _shortcutsData.Shortcuts.Count == 0)
                {
                    // Show empty state
                    var emptyText = new TextBlock
                    {
                        Text = "暂无快捷方式\n点击 + 添加",
                        Foreground = new SolidColorBrush(Color.FromRgb(153, 153, 153)),
                        FontSize = 9,
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    ShortcutsGrid.Children.Add(emptyText);
                    UpdateHeaderText();
                    return;
                }

                // Get shortcuts in order
                var orderedShortcuts = _shortcutsData.Shortcuts.OrderBy(s => s.Order).ToList();

                // Calculate grid dimensions
                var visibleCount = Math.Min(orderedShortcuts.Count, _visibleShortcuts);
                var rows = (int)Math.Ceiling((double)visibleCount / _iconsPerRow);

                // Create grid structure with equal spacing
                for (int i = 0; i < rows; i++)
                {
                    ShortcutsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                }

                for (int i = 0; i < _iconsPerRow; i++)
                {
                    ShortcutsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                }

                // Add shortcut icons
                for (int i = 0; i < visibleCount; i++)
                {
                    var shortcut = orderedShortcuts[i];
                    var button = CreateShortcutButton(shortcut);

                    var row = i / _iconsPerRow;
                    var col = i % _iconsPerRow;

                    Grid.SetRow(button, row);
                    Grid.SetColumn(button, col);
                    ShortcutsGrid.Children.Add(button);
                }

                UpdateHeaderText();
                System.Diagnostics.Debug.WriteLine($"Created grid: {rows} rows x {_iconsPerRow} cols, showing {visibleCount} shortcuts");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating shortcuts grid: {ex.Message}");

                // Show error state
                var errorText = new TextBlock
                {
                    Text = "加载快捷方式失败",
                    Foreground = new SolidColorBrush(Color.FromRgb(220, 53, 69)),
                    FontSize = 9,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                ShortcutsGrid.Children.Add(errorText);
            }
        }


        /// <summary>
        /// Create a clickable button for a shortcut - UPDATED WITH PNG SUPPORT
        /// </summary>
        private Button CreateShortcutButton(ShortcutItem shortcut)
        {
            var button = new Button
            {
                Width = ICON_SIZE,
                Height = ICON_SIZE,
                Margin = new Thickness(2),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = shortcut.Label,
                Tag = shortcut,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(0)
            };

            // Set content based on icon type
            if (shortcut.IconType == "custom")
            {
                // Show custom image
                var image = new Image
                {
                    Width = ICON_SIZE,
                    Height = ICON_SIZE
                };

                var iconPath = ShortcutsStorage.GetCustomIconPath(shortcut.IconValue);
                if (!string.IsNullOrEmpty(iconPath) && System.IO.File.Exists(iconPath))
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(iconPath);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        image.Source = bitmap;
                        button.Content = image;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error loading custom icon: {ex.Message}");
                        // Fallback to default icon
                        button.Content = "📁";
                        button.FontSize = ICON_SIZE - 4;
                    }
                }
                else
                {
                    // Fallback if custom icon file is missing
                    button.Content = "📁";
                    button.FontSize = ICON_SIZE - 4;
                }
            }
            else
            {
                // Try to load PNG for built-in icon
                var iconPath = GetBuiltinIconPath(shortcut.IconValue);
                if (!string.IsNullOrEmpty(iconPath))
                {
                    var image = new Image
                    {
                        Width = ICON_SIZE,
                        Height = ICON_SIZE
                    };

                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(iconPath);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        image.Source = bitmap;
                        button.Content = image;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error loading builtin icon PNG {shortcut.IconValue}: {ex.Message}");
                        // Fallback to text
                        button.Content = shortcut.IconValue;
                        button.FontSize = ICON_SIZE - 4;
                    }
                }
                else
                {
                    // Fallback to text if PNG not found
                    button.Content = shortcut.IconValue;
                    button.FontSize = ICON_SIZE - 4;
                }
            }

            // Apply custom style to remove hover effects
            var style = new Style(typeof(Button));
            var template = new ControlTemplate(typeof(Button));
            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            template.VisualTree = contentPresenter;
            style.Setters.Add(new Setter(Button.TemplateProperty, template));
            button.Style = style;

            button.Click += ShortcutButton_Click;

            return button;
        }

        /// <summary>
        /// Get path to built-in Phosphor icon PNG - copied from IconPicker
        /// </summary>
        private string GetBuiltinIconPath(string iconName)
        {
            try
            {
                // Method 1: Try embedded resource first
                var resourcePath = $"pack://application:,,,/Core/Icons/DefaultIcons/{iconName}.png";
                try
                {
                    var resourceStream = System.Windows.Application.GetResourceStream(new Uri(resourcePath));
                    if (resourceStream != null)
                    {
                        resourceStream.Stream.Close();
                        return resourcePath;
                    }
                }
                catch (Exception)
                {
                    // Not an embedded resource, try file system
                }

                // Method 2: Try file system locations
                var possiblePaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Core", "Icons", "DefaultIcons", $"{iconName}.png"),
                    Path.Combine(gtt_sidebar.Core.AppPaths.DefaultIconsPath, $"{iconName}.png"),
                    Path.Combine("Core", "Icons", "DefaultIcons", $"{iconName}.png"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DefaultIcons", $"{iconName}.png")
                };

                foreach (var path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting builtin icon path for {iconName}: {ex.Message}");
                return null;
            }
        }

        

        /// <summary>
        /// Handle shortcut button click - launch the application/URL
        /// </summary>
        private void ShortcutButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var shortcut = button?.Tag as ShortcutItem;

            if (shortcut == null)
            {
                System.Diagnostics.Debug.WriteLine("ShortcutButton_Click: No shortcut data found");
                return;
            }

            try
            {
                LaunchShortcut(shortcut);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error launching shortcut '{shortcut.Label}': {ex.Message}");
                MessageBox.Show($"启动“{shortcut.Label}”失败：{ex.Message}",
                               "启动失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Launch a shortcut based on its type with enhanced error handling
        /// </summary>
        private void LaunchShortcut(ShortcutItem shortcut)
        {
            System.Diagnostics.Debug.WriteLine($"Launching shortcut: {shortcut.Label} -> {shortcut.Path} (Type: {shortcut.Type})");

            // Validate shortcut data first
            if (string.IsNullOrWhiteSpace(shortcut.Path))
            {
                ShowLaunchError(shortcut.Label, "快捷方式路径为空，请在设置中修改。");
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    UseShellExecute = true,
                    WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                };

                switch (shortcut.Type)
                {
                    case ShortcutType.Executable:
                        // Handle command line arguments
                        var executablePath = ExtractExecutablePath(shortcut.Path);
                        var arguments = ExtractArguments(shortcut.Path);
                        if (!File.Exists(executablePath))
                        {
                            ShowLaunchError(shortcut.Label, $"未找到可执行文件：{Path.GetFileName(executablePath)}");
                            return;
                        }
                        startInfo.FileName = executablePath;
                        if (!string.IsNullOrEmpty(arguments))
                        {
                            startInfo.Arguments = arguments;
                        }
                        break;
                    case ShortcutType.URL:
                        if (!IsValidUrl(shortcut.Path))
                        {
                            ShowLaunchError(shortcut.Label, "网址格式无效，请检查地址。");
                            return;
                        }
                        startInfo.FileName = shortcut.Path;
                        break;

                    case ShortcutType.WindowsShortcut:
                        if (!File.Exists(shortcut.Path))
                        {
                            ShowLaunchError(shortcut.Label,
                                $"未找到快捷方式文件：{Path.GetFileName(shortcut.Path)}");
                            return;
                        }
                        startInfo.FileName = shortcut.Path;
                        break;

                    case ShortcutType.Command:
                        if (!IsValidCommand(shortcut.Path))
                        {
                            ShowLaunchError(shortcut.Label,
                                $"无法识别 Windows 命令“{shortcut.Path}”。\n\n请改用可执行文件的完整路径。");
                            return;
                        }
                        startInfo.FileName = shortcut.Path;
                        break;

                    default:
                        startInfo.FileName = shortcut.Path;
                        break;
                }

                var process = Process.Start(startInfo);
                System.Diagnostics.Debug.WriteLine($"Successfully launched: {shortcut.Label}");

                // Optional: Close the process handle to free resources for simple launches
                if (process != null && !process.HasExited)
                {
                    process.Close();
                }
            }
            catch (System.ComponentModel.Win32Exception win32Ex)
            {
                // Handle specific Windows errors
                string errorMessage = GetFriendlyWin32Error(win32Ex.NativeErrorCode, shortcut);
                ShowLaunchError(shortcut.Label, errorMessage);
                System.Diagnostics.Debug.WriteLine($"Win32 error launching {shortcut.Label}: {win32Ex.NativeErrorCode} - {win32Ex.Message}");
            }
            catch (FileNotFoundException)
            {
                ShowLaunchError(shortcut.Label,
                    $"未找到文件：{Path.GetFileName(shortcut.Path)}\n\n该程序可能已被移动或卸载。");
                System.Diagnostics.Debug.WriteLine($"File not found: {shortcut.Path}");
            }
            catch (UnauthorizedAccessException)
            {
                ShowLaunchError(shortcut.Label,
                    "访问被拒绝，你没有运行该程序的权限。\n\n请尝试以管理员身份运行，或检查文件权限。");
                System.Diagnostics.Debug.WriteLine($"Access denied launching: {shortcut.Path}");
            }
            catch (Exception ex)
            {
                ShowLaunchError(shortcut.Label,
                    $"发生异常：{ex.Message}\n\n请在设置中检查快捷方式路径。");
                System.Diagnostics.Debug.WriteLine($"Unexpected error launching {shortcut.Label}: {ex.Message}");
            }
        }

        private string ExtractExecutablePath(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine)) return commandLine;
            var trimmed = commandLine.Trim();

            // Handle quoted paths: "C:\Program Files\App\app.exe" --arguments
            if (trimmed.StartsWith("\""))
            {
                var endQuoteIndex = trimmed.IndexOf("\"", 1);
                if (endQuoteIndex > 0)
                    return trimmed.Substring(1, endQuoteIndex - 1);
            }

            // Handle unquoted paths: C:\Path\app.exe --arguments
            var spaceIndex = trimmed.IndexOf(" ");
            if (spaceIndex > 0)
            {
                var potentialPath = trimmed.Substring(0, spaceIndex);
                if (potentialPath.Contains("\\") && File.Exists(potentialPath))
                    return potentialPath;
            }

            return trimmed;
        }

        /// <summary>
        /// Extract arguments from a command line that might contain arguments
        /// </summary>
        private string ExtractArguments(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine))
                return "";

            var trimmed = commandLine.Trim();

            // handle quoted paths
            if (trimmed.StartsWith("\""))
            {
                var endQuoteIndex = trimmed.IndexOf("\"", 1);
                if (endQuoteIndex > 0 && endQuoteIndex + 1 < trimmed.Length)
                {
                    return trimmed.Substring(endQuoteIndex + 1).Trim();
                }
            }

            // handle unquoted paths
            var spaceIndex = trimmed.IndexOf(" ");
            if (spaceIndex > 0)
            {
                var potentialPath = trimmed.Substring(0, spaceIndex);
                // check if this looks like a file path and exists
                if (potentialPath.Contains("\\") && File.Exists(potentialPath))
                {
                    return trimmed.Substring(spaceIndex + 1).Trim();
                }
            }

            // if no arguments detected, return empty
            return "";
        }

        /// <summary>
        /// Validate URL format
        /// </summary>
        private bool IsValidUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                   url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                   url.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) ||
                   url.StartsWith("file://", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Validate Windows command
        /// </summary>
        private bool IsValidCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return false;

            // List of known valid Windows commands
            var knownCommands = new[] {
        "calc", "calculator", "notepad", "mspaint", "paint", "cmd", "command",
        "taskmgr", "taskmanager", "explorer", "control", "controlpanel",
        "msconfig", "regedit", "winver", "charmap", "magnify", "narrator",
        "osk", "snippingtool", "wordpad", "write"
    };

            var cleanCommand = command.ToLowerInvariant().Trim();

            // Check if it's a known command
            if (knownCommands.Contains(cleanCommand))
                return true;

            // Check if it looks like an executable path
            if (cleanCommand.Contains("\\") || cleanCommand.Contains("/") || cleanCommand.EndsWith(".exe"))
                return true;

            // For unknown commands, assume they might be valid (let Windows decide)
            return true;
        }

        /// <summary>
        /// Get user-friendly error message for Win32 errors
        /// </summary>
        private string GetFriendlyWin32Error(int errorCode, ShortcutItem shortcut)
        {
            switch (errorCode)
            {
                case 2: // File not found
                    return $"未找到程序：{Path.GetFileName(shortcut.Path)}\n\n该程序可能已被移动、重命名或卸载。";

                case 3: // Path not found
                    return $"未找到路径：{Path.GetDirectoryName(shortcut.Path)}\n\n该程序所在的文件夹已不存在。";

                case 5: // Access denied
                    return "访问被拒绝，你没有运行该程序的权限。\n\n请尝试以管理员身份运行。";

                case 8: // Not enough memory
                    return "内存不足，无法启动该程序。\n\n请关闭其他程序后重试。";

                case 193: // Not a valid Win32 application
                    return $"{Path.GetFileName(shortcut.Path)} 不是有效的 Windows 应用程序。\n\n文件可能已损坏或不兼容。";

                case 1155: // No application associated
                    return $"该文件类型没有关联的应用程序。\n\n系统不知道如何打开 {Path.GetExtension(shortcut.Path)} 文件。";

                default:
                    return $"Windows 错误 {errorCode}：无法启动 {shortcut.Label}\n\n请在设置中检查快捷方式路径。";
            }
        }

        /// <summary>
        /// Show a user-friendly error message
        /// </summary>
        private void ShowLaunchError(string shortcutName, string errorMessage)
        {
            var fullMessage = $"启动“{shortcutName}”失败\n\n{errorMessage}";

            MessageBox.Show(fullMessage, "快捷方式启动失败",
                           MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        /// <summary>
        /// Update the header text to show hidden shortcuts count
        /// </summary>
        private void UpdateHeaderText()
        {
            try
            {
                if (_shortcutsData?.Shortcuts == null)
                {
                    HeaderText.Text = "快捷方式";
                    return;
                }

                var totalShortcuts = _shortcutsData.Shortcuts.Count;
                var hiddenCount = Math.Max(0, totalShortcuts - _visibleShortcuts);

                if (hiddenCount > 0)
                {
                    HeaderText.Text = $"快捷方式（{hiddenCount} 个已隐藏）";
                }
                else
                {
                    HeaderText.Text = "快捷方式";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating header text: {ex.Message}");
                HeaderText.Text = "快捷方式";
            }
        }

        

        /// <summary>
        /// Handle add button click - open Settings to Shortcuts section
        /// </summary>
        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("AddButton_Click: Opening settings...");

                // Get the main window
                var mainWindow = Application.Current.MainWindow as gtt_sidebar.Core.Application.MainWindow;
                if (mainWindow == null)
                {
                    System.Diagnostics.Debug.WriteLine("AddButton_Click: Could not find main window");
                    return;
                }

                // Use reflection to call the SettingsButton_Click method
                var settingsMethod = mainWindow.GetType().GetMethod("SettingsButton_Click",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (settingsMethod != null)
                {
                    settingsMethod.Invoke(mainWindow, new object[] { null, null });
                    System.Diagnostics.Debug.WriteLine("AddButton_Click: Settings opened successfully");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("AddButton_Click: Could not find SettingsButton_Click method");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Refresh the widget (useful when shortcuts are modified in settings)
        /// </summary>
        public void RefreshShortcuts()
        {
            try
            {
                
                _shortcutsData = ShortcutsStorage.LoadShortcuts();
                
                CreateShortcutsGrid();
                
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[REFRESH-ERROR] Error refreshing shortcuts: {ex.Message}");
            }
        }

        public void Dispose()
        {
            try
            {
                // Clean up any resources if needed
                _shortcutsData = null;
                System.Diagnostics.Debug.WriteLine("ShortcutsWidget: Disposed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing ShortcutsWidget: {ex.Message}");
            }
        }
    }
}