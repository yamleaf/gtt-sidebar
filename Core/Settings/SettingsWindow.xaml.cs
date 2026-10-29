using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using gtt_sidebar.Core.Settings;
using gtt_sidebar.Core.Managers;

namespace gtt_sidebar.Core.Settings
{
    public partial class SettingsWindow : Window
    {
        public event Action ShortcutsChanged;
        public event Action SystemMonitorSettingsChanged;

        /// <summary>
        /// 组件启用状态或顺序变更（即时生效，由主窗口重建组件区）
        /// </summary>
        public event Action<WidgetSettings> WidgetsChanged;
        private SettingsData _originalSettings;
        private SettingsData _currentSettings;
        private ShortcutsData _shortcutsData;
        private Dictionary<string, Border> _shortcutCards = new Dictionary<string, Border>();
        private bool _isDragging = false;
        // Add these fields to the existing private fields in SettingsWindow class
        private Border _draggedCard = null;
        private Point _dragStartPoint;
        private int _draggedIndex = -1;
        private int _dropIndex = -1;

        public event Action<SettingsData> SettingsApplied;

        public SettingsWindow(SettingsData currentSettings)
        {
            InitializeComponent();

            _originalSettings = currentSettings;
            _currentSettings = CloneSettings(currentSettings);

            LoadSettingsToUI();
            LoadShortcutsData();
        }

        // Add these methods to SettingsWindow.xaml.cs

        /// <summary>
        /// Initialize shortcuts data when settings window opens
        /// </summary>
        private void LoadShortcutsData()
        {
            try
            {
                _shortcutsData = ShortcutsStorage.LoadShortcuts();
                LoadShortcutsToUI();
                //System.Diagnostics.Debug.WriteLine($"Loaded {_shortcutsData.Shortcuts.Count} shortcuts to settings");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading shortcuts data: {ex.Message}");
                _shortcutsData = ShortcutsData.CreateDefault();
                LoadShortcutsToUI();
            }
        }

        /// <summary>
        /// Validate shortcut data before saving
        /// </summary>
        private bool ValidateShortcutData(ShortcutItem shortcut, out string errorMessage)
        {
            errorMessage = "";

            // Check label
            if (string.IsNullOrWhiteSpace(shortcut.Label))
            {
                errorMessage = "名称不能为空。";
                return false;
            }

            // Check path
            if (string.IsNullOrWhiteSpace(shortcut.Path))
            {
                errorMessage = "路径不能为空。";
                return false;
            }

            // Type-specific validation
            switch (shortcut.Type)
            {
                case ShortcutType.URL:
                    if (!shortcut.Path.StartsWith("http://") && !shortcut.Path.StartsWith("https://") &&
                        !shortcut.Path.StartsWith("ftp://") && !shortcut.Path.StartsWith("file://"))
                    {
                        errorMessage = "网址必须以 http://、https://、ftp:// 或 file:// 开头";
                        return false;
                    }
                    break;

                case ShortcutType.Executable:
                case ShortcutType.WindowsShortcut:
                    // extract executable path from command line (handle arguments)
                    var executablePath = ExtractExecutablePath(shortcut.Path);
                    if (!File.Exists(executablePath))
                    {
                        errorMessage = $"未找到可执行文件：{executablePath}\n\n请检查路径。";
                        return false;
                    }
                    break;
            }

            return true;
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
        /// Creates UI cards for all shortcuts
        /// </summary>
        private void LoadShortcutsToUI()
        {
            ShortcutsContainer.Children.Clear();
            _shortcutCards.Clear();

            if (_shortcutsData?.Shortcuts == null)
                return;

            foreach (var shortcut in _shortcutsData.Shortcuts.OrderBy(s => s.Order))
            {
                var card = CreateShortcutCard(shortcut);
                ShortcutsContainer.Children.Add(card);
                _shortcutCards[shortcut.Id] = card;
            }

            //System.Diagnostics.Debug.WriteLine($"Created UI for {_shortcutCards.Count} shortcuts");
        }

        /// <summary>
        /// Create icon display for shortcut card - UPDATED WITH PNG SUPPORT
        /// </summary>
        private UIElement CreateIconDisplay(ShortcutItem shortcut)
        {
            if (shortcut.IconType == "custom")
            {
                // Show custom image
                var image = new Image
                {
                    Width = 18,
                    Height = 18,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false
                };

                var iconPath = ShortcutsStorage.GetCustomIconPath(shortcut.IconValue);
                if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(iconPath);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        image.Source = bitmap;
                        return image;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error loading custom icon in settings: {ex.Message}");
                    }
                }

                // Fallback for custom icons
                return new TextBlock
                {
                    Text = "📁",
                    FontSize = 18,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false
                };
            }
            else
            {
                // Try to load PNG for built-in icon
                var iconPath = GetBuiltinIconPath(shortcut.IconValue);
                if (!string.IsNullOrEmpty(iconPath))
                {
                    var image = new Image
                    {
                        Width = 18,
                        Height = 18,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        IsHitTestVisible = false
                    };

                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(iconPath);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        image.Source = bitmap;
                        return image;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error loading builtin icon PNG in settings {shortcut.IconValue}: {ex.Message}");
                    }
                }

                // Fallback to text
                return new TextBlock
                {
                    Text = shortcut.IconValue,
                    FontSize = 18,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0)
                };
            }
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
        /// Creates a UI card for a single shortcut with up/down reordering buttons
        /// </summary>
        /// <summary>
        /// 创建设置页中的单个快捷方式编辑卡片。
        ///
        /// 视觉规范与主界面一致：浅灰底内嵌卡片 + 细描边 + 圆角，
        /// 输入框无边框（靠底色区分），操作按钮用极简图标而非 emoji。
        /// </summary>
        private Border CreateShortcutCard(ShortcutItem shortcut)
        {
            var subtle = new SolidColorBrush(Color.FromRgb(0xF7, 0xF8, 0xFA));
            var borderSubtle = new SolidColorBrush(Color.FromRgb(0xE8, 0xEB, 0xF0));
            var textPrimary = new SolidColorBrush(Color.FromRgb(0x1A, 0x1D, 0x23));
            var textTertiary = new SolidColorBrush(Color.FromRgb(0x90, 0x99, 0xA8));

            var card = new Border
            {
                Background = subtle,
                BorderBrush = borderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Margin = new Thickness(0, 0, 0, 7),
                Padding = new Thickness(9, 8, 8, 8),
                Tag = shortcut.Id
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });         // 图标
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 内容
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });         // 操作

            // ── 图标预览（可点击换图标） ──
            var iconBorder = new Border
            {
                Width = 30,
                Height = 30,
                BorderBrush = borderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Margin = new Thickness(0, 0, 9, 0),
                Cursor = Cursors.Hand,
                Tag = shortcut.Id,
                Background = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };

            var iconDisplay = CreateIconDisplay(shortcut);
            iconBorder.Child = iconDisplay;
            iconBorder.PreviewMouseLeftButtonDown += IconBorder_Click;

            // ── 名称与路径：上下两行，每行是「标签 + 无边框输入框」 ──
            var contentPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            contentPanel.Children.Add(new TextBlock
            {
                Text = "名称",
                FontSize = 8.5,
                Foreground = textTertiary,
                Margin = new Thickness(1, 0, 0, 1)
            });

            var labelTextBox = new TextBox
            {
                Text = shortcut.Label,
                FontSize = 10,
                Height = 22,
                Padding = new Thickness(5, 0, 5, 0),
                Foreground = textPrimary,
                Background = Brushes.White,
                BorderThickness = new Thickness(1),
                BorderBrush = borderSubtle,
                VerticalContentAlignment = VerticalAlignment.Center,
                Tag = shortcut.Id,
                Margin = new Thickness(0, 0, 0, 5)
            };
            labelTextBox.TextChanged += ShortcutField_TextChanged;
            contentPanel.Children.Add(labelTextBox);

            contentPanel.Children.Add(new TextBlock
            {
                Text = "路径",
                FontSize = 8.5,
                Foreground = textTertiary,
                Margin = new Thickness(1, 0, 0, 1)
            });

            var pathTextBox = new TextBox
            {
                Text = shortcut.Path,
                FontSize = 9.5,
                Height = 22,
                Padding = new Thickness(5, 0, 5, 0),
                Foreground = textPrimary,
                Background = Brushes.White,
                BorderThickness = new Thickness(1),
                BorderBrush = borderSubtle,
                VerticalContentAlignment = VerticalAlignment.Center,
                Tag = shortcut.Id
            };
            pathTextBox.TextChanged += ShortcutField_TextChanged;
            contentPanel.Children.Add(pathTextBox);

            // ── 操作按钮：竖排两行，用几何图标 ──
            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(9, 0, 0, 0)
            };

            var topRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            var bottomRow = new StackPanel { Orientation = Orientation.Horizontal };

            Button MakeIconButton(string pathData, string tooltip, System.Windows.Media.Brush fg, double w = 24)
            {
                var b = new Button
                {
                    Width = w,
                    Height = 22,
                    Margin = new Thickness(0, 0, 4, 0),
                    Background = Brushes.White,
                    BorderThickness = new Thickness(1),
                    BorderBrush = borderSubtle,
                    Cursor = Cursors.Hand,
                    ToolTip = tooltip,
                    Tag = shortcut.Id,
                    Padding = new Thickness(0)
                };

                // 极简模板：圆角 + 悬停浅底
                //
                // 注意：模板字符串必须是合法 XHTML，`{TemplateBinding X}` 里的 X
                // 需要命名空间解析，因此必须声明 xmlns:x（否则 XamlReader 抛 XmlException
                // "找不到命名空间"）。这里统一显式声明两个命名空间。
                var tpl =
                    "<ControlTemplate TargetType=\"Button\" " +
                    "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                    "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                    "<Border x:Name=\"B\" CornerRadius=\"6\" " +
                    "Background=\"{TemplateBinding Background}\" " +
                    "BorderBrush=\"{TemplateBinding BorderBrush}\" BorderThickness=\"1\" " +
                    "SnapsToDevicePixels=\"True\">" +
                    "<Path x:Name=\"P\" Data=\"" + pathData + "\" Stroke=\"" + ToHex(fg) + "\" " +
                    "StrokeThickness=\"1.3\" StrokeStartLineCap=\"Round\" StrokeEndLineCap=\"Round\" " +
                    "StrokeLineJoin=\"Round\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" " +
                    "IsHitTestVisible=\"False\"/>" +
                    "</Border>" +
                    "<ControlTemplate.Triggers>" +
                    "<Trigger Property=\"IsMouseOver\" Value=\"True\">" +
                    "<Setter TargetName=\"B\" Property=\"Background\" Value=\"#EEF1F5\"/>" +
                    "</Trigger>" +
                    "<Trigger Property=\"IsEnabled\" Value=\"False\">" +
                    "<Setter TargetName=\"B\" Property=\"Opacity\" Value=\"0.35\"/>" +
                    "</Trigger>" +
                    "</ControlTemplate.Triggers></ControlTemplate>";

                try
                {
                    b.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(tpl);
                }
                catch (Exception ex)
                {
                    // 模板解析失败不应让整个设置窗口崩溃，退化为默认按钮外观
                    System.Diagnostics.Debug.WriteLine($"快捷方式按钮模板解析失败: {ex.Message}");
                    b.Content = "·";
                }
                return b;
            }

            // 上移 / 下移
            var upButton = MakeIconButton("M 2,5 L 5,2 L 8,5", "上移",
                new SolidColorBrush(Color.FromRgb(0x5A, 0x62, 0x70)));
            upButton.Click += MoveUp_Click;

            var downButton = MakeIconButton("M 2,2 L 5,5 L 8,2", "下移",
                new SolidColorBrush(Color.FromRgb(0x5A, 0x62, 0x70)));
            downButton.Click += MoveDown_Click;

            // 保存（对勾）
            var saveButton = MakeIconButton("M 2,5 L 4.5,7.5 L 8.5,2", "保存修改",
                new SolidColorBrush(Color.FromRgb(0x34, 0xC7, 0x59)));
            saveButton.IsEnabled = false;
            saveButton.Click += SaveShortcut_Click;

            // 删除（叉）
            var deleteButton = MakeIconButton("M 2,2 L 8,8 M 8,2 L 2,8", "删除快捷方式",
                new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x4F)));
            deleteButton.Click += DeleteShortcut_Click;
            deleteButton.Margin = new Thickness(0);

            topRow.Children.Add(upButton);
            topRow.Children.Add(downButton);
            bottomRow.Children.Add(saveButton);
            bottomRow.Children.Add(deleteButton);

            buttonsPanel.Children.Add(topRow);
            buttonsPanel.Children.Add(bottomRow);

            // ── 组装 ──
            Grid.SetColumn(iconBorder, 0);
            Grid.SetColumn(contentPanel, 1);
            Grid.SetColumn(buttonsPanel, 2);
            grid.Children.Add(iconBorder);
            grid.Children.Add(contentPanel);
            grid.Children.Add(buttonsPanel);

            card.Child = grid;

            // 图标框悬停高亮
            iconBorder.MouseEnter += (s, e) =>
                iconBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xE2));
            iconBorder.MouseLeave += (s, e) => iconBorder.BorderBrush = borderSubtle;

            return card;
        }

        /// <summary>把 Brush 转成 #RRGGBB 字符串（供 XamlReader 模板使用）</summary>
        private static string ToHex(System.Windows.Media.Brush brush)
        {
            var c = (brush as SolidColorBrush)?.Color ?? Colors.Black;
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        private async void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            var shortcutId = ((Button)sender).Tag.ToString();
            var shortcut = _shortcutsData.Shortcuts.FirstOrDefault(s => s.Id == shortcutId);
            var index = _shortcutsData.Shortcuts.IndexOf(shortcut);

            if (index > 0)
            {
                _shortcutsData.Shortcuts.RemoveAt(index);
                _shortcutsData.Shortcuts.Insert(index - 1, shortcut);
                UpdateOrders();
                await ShortcutsStorage.SaveShortcutsAsync(_shortcutsData);  // IMMEDIATE SAVE
                LoadShortcutsToUI();
                ShortcutsChanged?.Invoke();
            }
        }

        private async void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            var shortcutId = ((Button)sender).Tag.ToString();
            var shortcut = _shortcutsData.Shortcuts.FirstOrDefault(s => s.Id == shortcutId);
            var index = _shortcutsData.Shortcuts.IndexOf(shortcut);

            if (index < _shortcutsData.Shortcuts.Count - 1)
            {
                _shortcutsData.Shortcuts.RemoveAt(index);
                _shortcutsData.Shortcuts.Insert(index + 1, shortcut);
                UpdateOrders();
                await ShortcutsStorage.SaveShortcutsAsync(_shortcutsData);  // IMMEDIATE SAVE
                LoadShortcutsToUI();
                ShortcutsChanged?.Invoke();
            }
        }

        private void UpdateOrders()
        {
            for (int i = 0; i < _shortcutsData.Shortcuts.Count; i++)
                _shortcutsData.Shortcuts[i].Order = i;
        }

        // Add these event handlers to SettingsWindow.xaml.cs

        /// <summary>
        /// Handle shortcuts section collapse/expand
        /// </summary>
        private void ShortcutsHeader_Click(object sender, MouseButtonEventArgs e)
        {
            if (ShortcutsContent.Visibility == Visibility.Visible)
            {
                ShortcutsContent.Visibility = Visibility.Collapsed;
                ShortcutsArrow.Text = "▶";
            }
            else
            {
                ShortcutsContent.Visibility = Visibility.Visible;
                ShortcutsArrow.Text = "▼";
            }
        }

        #region 组件（插件）管理

        /// <summary>
        /// 当前组件列表在界面中的顺序（与 _widgetSettings 同步维护）
        /// </summary>
        private readonly List<WidgetDescriptor> _orderedDescriptors = new List<WidgetDescriptor>();

        private void WidgetsHeader_Click(object sender, MouseButtonEventArgs e)
        {
            if (WidgetsContent.Visibility == Visibility.Visible)
            {
                WidgetsContent.Visibility = Visibility.Collapsed;
                WidgetsArrow.Text = "▶";
            }
            else
            {
                WidgetsContent.Visibility = Visibility.Visible;
                WidgetsArrow.Text = "▼";
            }
        }

        /// <summary>
        /// 刷新组件管理列表：按当前配置的启用状态与顺序重建条目
        /// </summary>
        private void RefreshWidgetsList()
        {
            if (WidgetsList == null) return;

            WidgetsList.Children.Clear();
            _orderedDescriptors.Clear();

            var manager = WidgetRegistry.Instance;
            var all = manager.AvailableWidgets;
            if (all == null || all.Count == 0) return;

            var setting = _currentSettings?.Widgets;

            // 排序：用户自定义顺序在前，其余按内置默认顺序兜底
            _orderedDescriptors.AddRange(all
                .OrderBy(d =>
                {
                    var custom = setting?.GetOrder(d.Name) ?? int.MaxValue;
                    return custom == int.MaxValue ? d.DefaultOrder : custom;
                })
                .ThenBy(d => d.DefaultOrder));

            for (int i = 0; i < _orderedDescriptors.Count; i++)
            {
                WidgetsList.Children.Add(CreateWidgetRow(_orderedDescriptors[i], i));
            }
        }

        /// <summary>
        /// 生成单行组件条目：勾选框 + 名称/说明 + 上移/下移
        /// </summary>
        private Border CreateWidgetRow(WidgetDescriptor descriptor, int index)
        {
            var subtle = new SolidColorBrush(Color.FromRgb(0xF7, 0xF8, 0xFA));
            var borderSubtle = new SolidColorBrush(Color.FromRgb(0xE8, 0xEB, 0xF0));
            var textTertiary = new SolidColorBrush(Color.FromRgb(0x90, 0x99, 0xA8));

            var row = new Border
            {
                Background = subtle,
                BorderBrush = borderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Margin = new Thickness(0, 0, 0, 7),
                Padding = new Thickness(9, 8, 7, 8)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 勾选框：绑定启用状态
            var check = new CheckBox
            {
                Style = (Style)FindResource("ModernCheckBox"),
                IsChecked = _currentSettings?.Widgets?.IsEnabled(descriptor.Name) ?? true,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = descriptor.Name,
                Margin = new Thickness(0, 0, 9, 0),
                ToolTip = "勾选后显示该组件"
            };
            check.Checked += WidgetEnabled_Changed;
            check.Unchecked += WidgetEnabled_Changed;

            // 名称 + 说明
            var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textPanel.Children.Add(new TextBlock
            {
                Text = descriptor.DisplayName,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1D, 0x23)),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            textPanel.Children.Add(new TextBlock
            {
                Text = descriptor.Description,
                FontSize = 8.5,
                Foreground = textTertiary,
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });

            // 上移 / 下移：自绘几何箭头，与快捷方式卡片的按钮视觉统一
            var arrows = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };

            var up = MakeWidgetArrow("M 2,5 L 5,2 L 8,5", "上移", descriptor.Name, index > 0);
            up.Click += WidgetMoveUp_Click;

            var down = MakeWidgetArrow("M 2,2 L 5,5 L 8,2", "下移", descriptor.Name,
                index < _orderedDescriptors.Count - 1);
            down.Click += WidgetMoveDown_Click;

            arrows.Children.Add(up);
            arrows.Children.Add(down);

            Grid.SetColumn(check, 0);
            Grid.SetColumn(textPanel, 1);
            Grid.SetColumn(arrows, 2);
            grid.Children.Add(check);
            grid.Children.Add(textPanel);
            grid.Children.Add(arrows);

            row.Child = grid;
            return row;
        }

        /// <summary>
        /// 生成组件的上移/下移小按钮（与快捷方式卡片的箭头同款视觉）。
        ///
        /// 自绘 ControlTemplate 用 XamlReader.Parse 构建：
        /// 必须同时声明 xmlns 与 xmlns:x，否则 {TemplateBinding X} 解析失败会崩进程。
        /// </summary>
        private Button MakeWidgetArrow(string pathData, string tooltip, string widgetName, bool enabled)
        {
            var b = new Button
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(0, 0, 4, 0),
                Background = Brushes.White,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xEB, 0xF0)),
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                Tag = widgetName,
                IsEnabled = enabled,
                Padding = new Thickness(0)
            };

            var tpl =
                "<ControlTemplate TargetType=\"Button\" " +
                "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                "<Border x:Name=\"B\" CornerRadius=\"6\" Background=\"#FFFFFF\" " +
                "BorderBrush=\"#E8EBF0\" BorderThickness=\"1\" SnapsToDevicePixels=\"True\">" +
                "<Path Data=\"" + pathData + "\" Stroke=\"#5A6270\" StrokeThickness=\"1.3\" " +
                "StrokeStartLineCap=\"Round\" StrokeEndLineCap=\"Round\" StrokeLineJoin=\"Round\" " +
                "HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"/>" +
                "</Border>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property=\"IsMouseOver\" Value=\"True\">" +
                "<Setter TargetName=\"B\" Property=\"Background\" Value=\"#EEF1F5\"/>" +
                "</Trigger>" +
                "<Trigger Property=\"IsEnabled\" Value=\"False\">" +
                "<Setter TargetName=\"B\" Property=\"Opacity\" Value=\"0.3\"/>" +
                "</Trigger>" +
                "</ControlTemplate.Triggers></ControlTemplate>";

            try
            {
                b.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(tpl);
            }
            catch (Exception ex)
            {
                // 模板失败只影响外观，不影响功能
                System.Diagnostics.Debug.WriteLine($"组件箭头模板解析失败: {ex.Message}");
            }

            return b;
        }

        private void WidgetEnabled_Changed(object sender, RoutedEventArgs e)
        {
            var name = (string)((CheckBox)sender).Tag;
            var enabled = ((CheckBox)sender).IsChecked == true;

            _currentSettings.Widgets.SetEnabled(name, enabled);

            // 即时生效：立刻保存并通知主窗口重建组件区
            ApplyWidgetsImmediately();
        }

        private void WidgetMoveUp_Click(object sender, RoutedEventArgs e)
        {
            var name = (string)((Button)sender).Tag;
            MoveWidget(name, -1);
        }

        private void WidgetMoveDown_Click(object sender, RoutedEventArgs e)
        {
            var name = (string)((Button)sender).Tag;
            MoveWidget(name, +1);
        }

        private void MoveWidget(string widgetName, int delta)
        {
            try
            {
                var index = _orderedDescriptors.FindIndex(d => d.Name == widgetName);

                if (index < 0) return;

                var target = index + delta;
                if (target < 0 || target >= _orderedDescriptors.Count) return;

                var moved = _orderedDescriptors[index];
                _orderedDescriptors.RemoveAt(index);
                _orderedDescriptors.Insert(target, moved);

                _currentSettings.Widgets.ApplyOrder(_orderedDescriptors.Select(d => d.Name).ToList());

                // 注意顺序：先通知主窗口重建并落盘，再刷新本列表。
                // 若先 RefreshWidgetsList()，它会按旧的 _currentSettings 重排，可能覆盖刚才的顺序。
                ApplyWidgetsImmediately();
                RefreshWidgetsList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MoveWidget failed: {ex.Message}");
            }
        }
        /// <summary>
        /// 组件设置即时生效：持久化后通知主窗口重新装载。
        ///
        /// 注意：这里把 WidgetSettings 对象**直接传给主窗口**，
        /// 而不是让主窗口回读磁盘。因为 SaveSettings 内部用
        /// Task.Run(...).Result 同步等待异步写入，在 UI 线程上可能尚未落盘，
        /// 回读会拿到旧数据导致"改了没反应"（实测踩过）。
        /// </summary>
        private void ApplyWidgetsImmediately()
        {
            try
            {
                SettingsStorage.SaveSettings(_currentSettings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存组件设置失败: {ex.Message}");
            }

            // 无论落盘是否完成，界面都应立刻反映当前内存中的配置
            try
            {
                WidgetsChanged?.Invoke(_currentSettings.Widgets);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"应用组件设置失败: {ex.Message}");
            }
        }

        #endregion

        /// <summary>
        /// Handle text changes in shortcut fields
        /// </summary>
        private void ShortcutField_TextChanged(object sender, TextChangedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox?.Tag == null) return;

            var shortcutId = textBox.Tag.ToString();

            // Find the save button for this shortcut and enable it
            var card = _shortcutCards.ContainsKey(shortcutId) ? _shortcutCards[shortcutId] : null;

            if (card != null)
            {
                // Use the existing helper method that finds by content (💾)
                var saveButton = FindChildByContent<Button>(card, "💾");
                if (saveButton != null)
                {
                    saveButton.IsEnabled = true;
                    saveButton.Background = new SolidColorBrush(Color.FromRgb(40, 167, 69)); // Green when enabled
                }
            }
        }
        /// <summary>
        /// Handle icon border click - show icon picker (with drag prevention)
        /// </summary>
        private void IconBorder_Click(object sender, MouseButtonEventArgs e)
        {
            // Stop the event from bubbling up to drag handler
            e.Handled = true;
            e.Handled = true; // Sometimes needed twice for WPF

            var border = sender as Border;
            if (border?.Tag == null) return;

            var shortcutId = border.Tag.ToString();
            var shortcut = _shortcutsData.Shortcuts.FirstOrDefault(s => s.Id == shortcutId);
            if (shortcut == null) return;

            try
            {
                // Get screen position of the clicked icon
                var screenPoint = border.PointToScreen(new Point(border.ActualWidth / 2, 0));

                // Create and show icon picker
                var iconPicker = new IconPicker();
                iconPicker.IconSelected += (iconType, iconValue) => OnIconSelected(shortcutId, iconType, iconValue);
                iconPicker.PositionNear(screenPoint);
                iconPicker.Show();

                System.Diagnostics.Debug.WriteLine($"Opened icon picker for shortcut: {shortcut.Label}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening icon picker: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle icon selection from picker
        /// </summary>
        private void OnIconSelected(string shortcutId, string iconType, string iconValue)
        {
            try
            {
                var shortcut = _shortcutsData.Shortcuts.FirstOrDefault(s => s.Id == shortcutId);
                if (shortcut == null) return;

                System.Diagnostics.Debug.WriteLine($"OnIconSelected: {iconType} = {iconValue} for {shortcut.Label}");

                // Update shortcut data
                shortcut.IconType = iconType;
                shortcut.IconValue = iconValue;

                // Update the visual immediately
                if (_shortcutCards.ContainsKey(shortcutId))
                {
                    var card = _shortcutCards[shortcutId];

                    // Find the icon border by walking the visual tree
                    var iconBorder = FindIconBorderInCard(card);
                    if (iconBorder != null)
                    {
                        // COMPLETELY RECREATE THE ICON DISPLAY
                        UIElement newIconDisplay;

                        if (iconType == "custom")
                        {
                            // Create new image for custom icon
                            var image = new Image
                            {
                                Width = 18,
                                Height = 18,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center,
                                IsHitTestVisible = false
                            };

                            // Load the custom icon
                            var iconPath = ShortcutsStorage.GetCustomIconPath(iconValue);
                            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                            {
                                try
                                {
                                    var bitmap = new BitmapImage();
                                    bitmap.BeginInit();
                                    bitmap.UriSource = new Uri(iconPath);
                                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                    bitmap.EndInit();
                                    image.Source = bitmap;
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Error loading updated custom icon: {ex.Message}");
                                }
                            }

                            newIconDisplay = image;
                        }
                        else
                        {
                            // Create new TextBlock for built-in icon
                            var iconText = new TextBlock
                            {
                                Text = iconValue,
                                FontSize = 18,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center,
                                IsHitTestVisible = false,
                                Padding = new Thickness(0),
                                Margin = new Thickness(0)
                            };
                            newIconDisplay = iconText;
                        }

                        // Replace the old content with new content
                        // Update the visual using the same logic as card creation
                        var updatedIconDisplay = CreateIconDisplay(shortcut);
                        iconBorder.Child = updatedIconDisplay;
                        System.Diagnostics.Debug.WriteLine($"Updated icon display to: {iconType} = {iconValue}");
                    }

                    // Enable save button
                    EnableSaveButtonForCard(shortcutId);
                }

                System.Diagnostics.Debug.WriteLine($"Icon selection complete for '{shortcut.Label}'");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnIconSelected: {ex.Message}");
                MessageBox.Show($"更新图标失败：{ex.Message}", "错误",
                               MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        /// <summary>
        /// Helper to find icon border in a card
        /// </summary>
        private Border FindIconBorderInCard(Border card)
        {
            // The icon border is the first Border child in the card's grid
            var grid = card.Child as Grid;
            if (grid?.Children.Count > 0 && grid.Children[0] is Border iconBorder)
            {
                return iconBorder;
            }
            return null;
        }

        /// <summary>
        /// Enable save button for a specific card
        /// </summary>
        private void EnableSaveButtonForCard(string shortcutId)
        {
            if (_shortcutCards.ContainsKey(shortcutId))
            {
                var card = _shortcutCards[shortcutId];
                var saveButton = FindSaveButtonInCard(card);
                if (saveButton != null)
                {
                    saveButton.IsEnabled = true;
                    saveButton.Background = new SolidColorBrush(Color.FromRgb(40, 167, 69));
                }
            }
        }

        /// <summary>
        /// Helper to find save button in a card
        /// </summary>
        private Button FindSaveButtonInCard(Border card)
        {
            // Walk the visual tree to find the save button (has disk emoji)
            return FindChildByContent<Button>(card, "💾");
        }

        /// <summary>
        /// Find child by content
        /// </summary>
        private T FindChildByContent<T>(DependencyObject parent, string content) where T : ContentControl
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T element && element.Content?.ToString() == content)
                    return element;

                var found = FindChildByContent<T>(child, content);
                if (found != null)
                    return found;
            }
            return null;
        }
        /// <summary>
        /// Handle save button click
        /// </summary>
        private void SaveShortcut_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button?.Tag == null) return;

            var shortcutId = button.Tag.ToString();
            var shortcut = _shortcutsData.Shortcuts.FirstOrDefault(s => s.Id == shortcutId);
            if (shortcut == null) return;

            try
            {
                var card = _shortcutCards.ContainsKey(shortcutId) ? _shortcutCards[shortcutId] : null;
                if (card != null)
                {
                    // Find text boxes by walking the visual tree
                    var labelTextBox = FindTextBoxInCard(card, 0); // First text box (label)
                    var pathTextBox = FindTextBoxInCard(card, 1);  // Second text box (path)

                    if (labelTextBox != null && pathTextBox != null)
                    {
                        shortcut.Label = labelTextBox.Text?.Trim() ?? "";
                        shortcut.Path = pathTextBox.Text?.Trim() ?? "";
                        shortcut.Type = ShortcutItem.DetectType(shortcut.Path); // Re-detect type

                        // Validate the shortcut
                        string validationError;

                        if (!ValidateShortcutData(shortcut, out validationError))
                        {
                            MessageBox.Show(validationError, "校验失败",
                                           MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        // Save to storage
                        SharedResourceManager.Instance.QueueSave("shortcuts", _shortcutsData);

                        // Disable save button
                        button.IsEnabled = false;
                        button.Background = new SolidColorBrush(Color.FromRgb(108, 117, 125)); // Gray when disabled
                        ShortcutsChanged?.Invoke();
                        System.Diagnostics.Debug.WriteLine($"Saved shortcut: {shortcut.Label} -> {shortcut.Path}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving shortcut: {ex.Message}");
                MessageBox.Show($"保存快捷方式失败：{ex.Message}", "错误",
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Handle delete button click
        /// </summary>
        private void DeleteShortcut_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button?.Tag == null) return;

            var shortcutId = button.Tag.ToString();
            var shortcut = _shortcutsData.Shortcuts.FirstOrDefault(s => s.Id == shortcutId);
            if (shortcut == null) return;

            // Confirmation dialog
            var result = MessageBox.Show(
                $"确定要删除快捷方式“{shortcut.Label}”吗？",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // Remove from data
                    _shortcutsData.RemoveShortcut(shortcutId);

                    // Save to storage
                    SharedResourceManager.Instance.QueueSave("shortcuts", _shortcutsData);

                    // Remove from UI
                    if (_shortcutCards.ContainsKey(shortcutId))
                    {
                        ShortcutsContainer.Children.Remove(_shortcutCards[shortcutId]);
                        _shortcutCards.Remove(shortcutId);
                    }
                    ShortcutsChanged?.Invoke();

                    System.Diagnostics.Debug.WriteLine($"Deleted shortcut: {shortcut.Label}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error deleting shortcut: {ex.Message}");
                    MessageBox.Show($"删除快捷方式失败：{ex.Message}", "错误",
                                   MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Handle add new shortcut button click
        /// </summary>
        private void AddShortcutButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Create a new shortcut with default values
                var newShortcut = _shortcutsData.AddShortcut("新建快捷方式", "calc", "builtin", "📁");

                // Save to storage
                SharedResourceManager.Instance.QueueSave("shortcuts", _shortcutsData);

                // Add to UI
                var card = CreateShortcutCard(newShortcut);
                ShortcutsContainer.Children.Add(card);
                _shortcutCards[newShortcut.Id] = card;

                ShortcutsChanged?.Invoke();

                // Focus on the label text box for immediate editing
                var labelTextBox = FindTextBoxInCard(card, 0); // First text box
                labelTextBox?.Focus();
                labelTextBox?.SelectAll();

                System.Diagnostics.Debug.WriteLine($"Added new shortcut with ID: {newShortcut.Id}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding shortcut: {ex.Message}");
                MessageBox.Show($"添加快捷方式失败：{ex.Message}", "错误",
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Helper method to find child controls by type and tag
        /// </summary>
        private T FindChildByTag<T>(DependencyObject parent, string tag) where T : FrameworkElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T element && element.Tag?.ToString() == tag)
                    return element;

                var found = FindChildByTag<T>(child, tag);
                if (found != null)
                    return found;
            }
            return null;
        }

  
        
        


        private SettingsData CloneSettings(SettingsData settings)
        {
            return new SettingsData
            {
                Window = new WindowSettings
                {
                    Position = settings.Window.Position,
                    Width = settings.Window.Width,
                    MarginTop = settings.Window.MarginTop,
                    MarginBottom = settings.Window.MarginBottom,
                    MarginSide = settings.Window.MarginSide,
                    AutoHideOnFocusLost = settings.Window.AutoHideOnFocusLost,
                    AutoHideDelayMs = settings.Window.AutoHideDelayMs,
                    AutoHidePeekPx = settings.Window.AutoHidePeekPx,
                    AutoHideEdgeTopRatio = settings.Window.AutoHideEdgeTopRatio,
                    AutoHideEdgeBottomRatio = settings.Window.AutoHideEdgeBottomRatio,
                    UseFloatingBall = settings.Window.UseFloatingBall,
                    FloatingBallY = settings.Window.FloatingBallY,
                    FloatingBallSize = settings.Window.FloatingBallSize
                },
                SystemMonitor = new SystemMonitorSettings
                {
                    CpuThreshold = settings.SystemMonitor.CpuThreshold,
                    RamThreshold = settings.SystemMonitor.RamThreshold,
                    PingThreshold = settings.SystemMonitor.PingThreshold,
                    UpdateFrequencySeconds = settings.SystemMonitor.UpdateFrequencySeconds
                },
                // 组件状态必须一并克隆，否则编辑期间的改动会丢掉已有的启用/排序信息
                Widgets = new WidgetSettings
                {
                    Items = (settings.Widgets?.Items ?? new List<WidgetState>())
                        .Select(i => new WidgetState
                        {
                            Name = i.Name,
                            Enabled = i.Enabled,
                            Order = i.Order
                        })
                        .ToList()
                },
                Version = settings.Version
            };
        }

        #region System Monitor Settings

        private void SystemMonitorHeader_Click(object sender, MouseButtonEventArgs e)
        {
            // Toggle section visibility
            if (SystemMonitorContent.Visibility == Visibility.Visible)
            {
                SystemMonitorContent.Visibility = Visibility.Collapsed;
                SystemMonitorToggle.Text = "▶";
            }
            else
            {
                SystemMonitorContent.Visibility = Visibility.Visible;
                SystemMonitorToggle.Text = "▼";
            }
        }

        private void CpuThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (CpuThresholdValue != null)
            {
                CpuThresholdValue.Text = $"{(int)e.NewValue}%";
            }
        }

        private void RamThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (RamThresholdValue != null)
            {
                RamThresholdValue.Text = $"{(int)e.NewValue}%";
            }
        }

        private void PingThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (PingThresholdValue != null)
            {
                PingThresholdValue.Text = $"{(int)e.NewValue}ms";
            }
        }

        private void UpdateFrequencyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Nothing special needed here - just for UI feedback
        }

        #endregion

        private void LoadSettingsToUI()
        {
            // Position
            RightPositionRadio.IsChecked = _currentSettings.Window.Position == SidebarPosition.Right;
            LeftPositionRadio.IsChecked = _currentSettings.Window.Position == SidebarPosition.Left;

            // Width
            WidthSlider.Value = _currentSettings.Window.Width;
            WidthValueText.Text = $"{_currentSettings.Window.Width:F0} px";

            // Margins
            MarginTopSlider.Value = _currentSettings.Window.MarginTop;
            MarginTopValueText.Text = $"{_currentSettings.Window.MarginTop:F0} px";

            MarginBottomSlider.Value = _currentSettings.Window.MarginBottom;
            MarginBottomValueText.Text = $"{_currentSettings.Window.MarginBottom:F0} px";

            MarginSideSlider.Value = _currentSettings.Window.MarginSide;
            MarginSideValueText.Text = $"{_currentSettings.Window.MarginSide:F0} px";

            // 自动隐藏设置
            AutoHideCheckBox.IsChecked = _currentSettings.Window.AutoHideOnFocusLost;
            AutoHideDelaySlider.Value = _currentSettings.Window.AutoHideDelayMs;
            AutoHideDelayValueText.Text = $"{(int)_currentSettings.Window.AutoHideDelayMs} ms";

            AutoHideEdgeTopSlider.Value = _currentSettings.Window.AutoHideEdgeTopRatio * 100;
            AutoHideEdgeTopValueText.Text = $"{(int)(_currentSettings.Window.AutoHideEdgeTopRatio * 100)}%";

            AutoHideEdgeBottomSlider.Value = _currentSettings.Window.AutoHideEdgeBottomRatio * 100;
            AutoHideEdgeBottomValueText.Text = $"{(int)(_currentSettings.Window.AutoHideEdgeBottomRatio * 100)}%";

            // 收起方式：悬浮球
            if (FloatingBallCheckBox != null)
            {
                FloatingBallCheckBox.IsChecked = _currentSettings.Window.UseFloatingBall;
            }

            UpdateAutoHideControlsEnabled();

            LoadSystemMonitorSettingsToUI();

            // 组件（插件）管理列表
            RefreshWidgetsList();
        }

        /// <summary>
        /// 未启用自动隐藏时，相关滑块置灰
        /// </summary>
        private void UpdateAutoHideControlsEnabled()
        {
            var enabled = AutoHideCheckBox.IsChecked == true;
            if (AutoHideDelaySlider != null) AutoHideDelaySlider.IsEnabled = enabled;
            if (AutoHideEdgeTopSlider != null) AutoHideEdgeTopSlider.IsEnabled = enabled;
            if (AutoHideEdgeBottomSlider != null) AutoHideEdgeBottomSlider.IsEnabled = enabled;
        }

        private void AutoHideCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateAutoHideControlsEnabled();
        }

        /// <summary>
        /// 切换收起方式：悬浮球 / 传统贴边。
        /// 同时联动贴边相关滑块的可编辑性——悬浮球模式下它们仍用于计算球的默认位置，
        /// 所以保持可用，不做置灰。
        /// </summary>
        private void FloatingBallCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateAutoHideControlsEnabled();
        }

        private void AutoHideDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (AutoHideDelayValueText != null)
            {
                AutoHideDelayValueText.Text = $"{(int)e.NewValue} ms";
            }
        }

        private void AutoHideEdgeTopSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (AutoHideEdgeTopValueText != null)
            {
                AutoHideEdgeTopValueText.Text = $"{(int)e.NewValue}%";
            }

            // 顶 + 底留白不能把感应区挤没（至少留 20%）
            if (AutoHideEdgeBottomSlider != null &&
                e.NewValue + AutoHideEdgeBottomSlider.Value > 80)
            {
                AutoHideEdgeBottomSlider.Value = Math.Max(0, 80 - e.NewValue);
            }
        }

        private void AutoHideEdgeBottomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (AutoHideEdgeBottomValueText != null)
            {
                AutoHideEdgeBottomValueText.Text = $"{(int)e.NewValue}%";
            }

            if (AutoHideEdgeTopSlider != null &&
                e.NewValue + AutoHideEdgeTopSlider.Value > 80)
            {
                AutoHideEdgeTopSlider.Value = Math.Max(0, 80 - e.NewValue);
            }
        }

        private void LoadSystemMonitorSettingsToUI()
        {
            try
            {
                // Load System Monitor settings (only if the controls exist)
                if (CpuThresholdSlider != null)
                {
                    CpuThresholdSlider.Value = _currentSettings.SystemMonitor.CpuThreshold;
                    RamThresholdSlider.Value = _currentSettings.SystemMonitor.RamThreshold;
                    PingThresholdSlider.Value = _currentSettings.SystemMonitor.PingThreshold;

                    // Set update frequency
                    foreach (ComboBoxItem item in UpdateFrequencyComboBox.Items)
                    {
                        if (item.Tag.ToString() == _currentSettings.SystemMonitor.UpdateFrequencySeconds.ToString())
                        {
                            UpdateFrequencyComboBox.SelectedItem = item;
                            break;
                        }
                    }

                    System.Diagnostics.Debug.WriteLine($"Settings: Loaded System Monitor settings - CPU: {_currentSettings.SystemMonitor.CpuThreshold}%, RAM: {_currentSettings.SystemMonitor.RamThreshold}%, Ping: {_currentSettings.SystemMonitor.PingThreshold}ms, Freq: {_currentSettings.SystemMonitor.UpdateFrequencySeconds}s");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Settings: Error loading System Monitor settings: {ex.Message}");
            }
        }

        private void UpdateCurrentSettings()
        {
            // Position
            _currentSettings.Window.Position = RightPositionRadio.IsChecked == true
                ? SidebarPosition.Right
                : SidebarPosition.Left;

            // Width
            _currentSettings.Window.Width = WidthSlider.Value;

            // Margins
            _currentSettings.Window.MarginTop = MarginTopSlider.Value;
            _currentSettings.Window.MarginBottom = MarginBottomSlider.Value;
            _currentSettings.Window.MarginSide = MarginSideSlider.Value;

            // 自动隐藏
            _currentSettings.Window.AutoHideOnFocusLost = AutoHideCheckBox.IsChecked == true;
            _currentSettings.Window.AutoHideDelayMs = (int)AutoHideDelaySlider.Value;
            _currentSettings.Window.AutoHideEdgeTopRatio = AutoHideEdgeTopSlider.Value / 100.0;
            _currentSettings.Window.AutoHideEdgeBottomRatio = AutoHideEdgeBottomSlider.Value / 100.0;

            UpdateSystemMonitorSettings();

        }
        private void UpdateSystemMonitorSettings()
        {
            try
            {
                if (CpuThresholdSlider != null)
                {
                    // Save threshold values
                    _currentSettings.SystemMonitor.CpuThreshold = (int)CpuThresholdSlider.Value;
                    _currentSettings.SystemMonitor.RamThreshold = (int)RamThresholdSlider.Value;
                    _currentSettings.SystemMonitor.PingThreshold = (int)PingThresholdSlider.Value;

                    // Save update frequency
                    if (UpdateFrequencyComboBox.SelectedItem is ComboBoxItem selectedItem)
                    {
                        _currentSettings.SystemMonitor.UpdateFrequencySeconds = int.Parse(selectedItem.Tag.ToString());
                    }

                    System.Diagnostics.Debug.WriteLine($"Settings: Updated System Monitor settings - CPU: {_currentSettings.SystemMonitor.CpuThreshold}%, RAM: {_currentSettings.SystemMonitor.RamThreshold}%, Ping: {_currentSettings.SystemMonitor.PingThreshold}ms, Freq: {_currentSettings.SystemMonitor.UpdateFrequencySeconds}s");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Settings: Error updating System Monitor settings: {ex.Message}");
            }
        }

        private void WidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (WidthValueText != null)
            {
                WidthValueText.Text = $"{e.NewValue:F0} px";
                ValidateCurrentValues();
            }
        }

        private void MarginTopSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (MarginTopValueText != null)
            {
                MarginTopValueText.Text = $"{e.NewValue:F0} px";
                ValidateCurrentValues();
            }
        }

        private void MarginBottomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (MarginBottomValueText != null)
            {
                MarginBottomValueText.Text = $"{e.NewValue:F0} px";
                ValidateCurrentValues();
            }
        }

        private void MarginSideSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (MarginSideValueText != null)
            {
                MarginSideValueText.Text = $"{e.NewValue:F0} px";
                ValidateCurrentValues();
            }
        }

        private void ValidateCurrentValues()
        {
            // Ensure all sliders are initialized
            if (WidthSlider == null || MarginTopSlider == null ||
                MarginBottomSlider == null || MarginSideSlider == null ||
                ValidationText == null || ApplyButton == null)
                return;

            var hasErrors = false;
            var errorMessage = "";

            // Check width
            if (WidthSlider.Value < 100 || WidthSlider.Value > 200)
            {
                hasErrors = true;
                errorMessage = "宽度必须在 100-200 像素之间";
            }

            // Check margins
            if (MarginTopSlider.Value < 0 || MarginTopSlider.Value > 50 ||
                MarginBottomSlider.Value < 0 || MarginBottomSlider.Value > 50 ||
                MarginSideSlider.Value < 0 || MarginSideSlider.Value > 50)
            {
                hasErrors = true;
                errorMessage = "边距必须在 0-50 像素之间";
            }

            if (hasErrors)
            {
                ValidationText.Text = errorMessage;
                ValidationText.Visibility = Visibility.Visible;
                ApplyButton.IsEnabled = false;
            }
            else
            {
                ValidationText.Visibility = Visibility.Collapsed;
                ApplyButton.IsEnabled = true;
            }
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateCurrentSettings();

            // Validate settings
            if (!SettingsStorage.ValidateSettings(_currentSettings))
            {
                MessageBox.Show("设置无效，请检查填写的内容。", "校验失败",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Apply settings via SharedResourceManager (this will save to file and notify subscribers)
            SettingsApplied?.Invoke(_currentSettings);

            // TRIGGER SHORTCUTS REFRESH TOO
            ShortcutsChanged?.Invoke();

            // Remove direct SystemMonitorSettingsChanged call since it's now handled by SharedResourceManager
            // SystemMonitorSettingsChanged?.Invoke(); // REMOVE THIS LINE

            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CancelButton_Click(sender, e);
        }
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }
        /// <summary>
        /// Helper to find text boxes in order within a card
        /// </summary>
        private TextBox FindTextBoxInCard(DependencyObject parent, int index)
        {
            var textBoxes = new List<TextBox>();
            FindTextBoxesRecursive(parent, textBoxes);
            return index < textBoxes.Count ? textBoxes[index] : null;
        }

        private void FindTextBoxesRecursive(DependencyObject parent, List<TextBox> textBoxes)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is TextBox textBox)
                    textBoxes.Add(textBox);

                FindTextBoxesRecursive(child, textBoxes);
            }
        }
        private void WindowLayoutHeader_Click(object sender, MouseButtonEventArgs e)
        {
            if (WindowLayoutContent.Visibility == Visibility.Visible)
            {
                WindowLayoutContent.Visibility = Visibility.Collapsed;
                WindowLayoutArrow.Text = "▶";
            }
            else
            {
                WindowLayoutContent.Visibility = Visibility.Visible;
                WindowLayoutArrow.Text = "▼";
            }
        }
    }
}