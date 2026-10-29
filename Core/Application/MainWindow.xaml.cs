using System;
using System.Drawing;
using System.IO;
using System.Resources;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using gtt_sidebar.Core.Managers;
using gtt_sidebar.Core.Settings;
using gtt_sidebar.Widgets.ClockWidget;
using gtt_sidebar.Widgets.WeatherWidget;

namespace gtt_sidebar.Core.Application
{
    public partial class MainWindow : Window
    {
        private WidgetManager _widgetManager;
        private SettingsData _currentSettings;
        private SettingsWindow _settingsWindow;
        private NotifyIcon _trayIcon;
        private bool _isClosing = false;
        private AutoHideManager _autoHide;
        private ToolStripMenuItem _autoHideMenuItem;

        public MainWindow()
        {
            InitializeComponent();

            // Load settings and apply to window
            _currentSettings = SettingsStorage.LoadSettings();

            // 自动隐藏管理器必须先于 ApplySettings 创建，否则无法记录显示位置
            _autoHide = new AutoHideManager(this);
            // 悬浮球右键菜单需要回调到主窗口
            _autoHide.SetBallCallbacks(
                () => SettingsButton_Click(null, null),
                () => ExitApplication());
            ApplySettings(_currentSettings);
            _autoHide.ApplySettings(_currentSettings);

            var resourceManager = SharedResourceManager.Instance;

            _widgetManager = new WidgetManager();
            LoadWidgets();

            // 失去焦点贴边隐藏
            this.Activated += MainWindow_Activated;
            this.Deactivated += MainWindow_Deactivated;

            // Create tray icon AFTER window is shown
            Dispatcher.BeginInvoke(new Action(CreateTrayIcon), DispatcherPriority.Background);

        }

        private void MainWindow_Activated(object sender, EventArgs e)
        {
            _autoHide?.OnActivated();
        }

        private void MainWindow_Deactivated(object sender, EventArgs e)
        {
            // 设置窗口打开时不隐藏侧边栏，避免设置到一半侧边栏跑掉
            if (_settingsWindow != null && _settingsWindow.IsVisible) return;

            _autoHide?.OnDeactivated();
        }

        /// <summary>
        /// 供子窗口（备注弹窗、图标选择器等）在打开期间抑制自动隐藏
        /// </summary>
        public void SuppressAutoHide()
        {
            _autoHide?.SuppressAutoHide();
        }

        /// <summary>
        /// 解除自动隐藏抑制
        /// </summary>
        public void ResumeAutoHide()
        {
            _autoHide?.ResumeAutoHide();
        }

        private void CreateTrayIcon()
        {
            _trayIcon = new NotifyIcon();
            try
            {
                // Load icon from embedded resource
                var iconUri = new Uri("pack://application:,,,/gtt-sidebar-icon.ico");
                var resourceStream = System.Windows.Application.GetResourceStream(iconUri);
                if (resourceStream != null)
                {
                    _trayIcon.Icon = new System.Drawing.Icon(resourceStream.Stream);
                }
                else
                {
                    _trayIcon.Icon = SystemIcons.Application;
                }
            }
            catch (Exception)
            {
                _trayIcon.Icon = SystemIcons.Application;
            }
            _trayIcon.Text = "桌面侧边栏";
            _trayIcon.Visible = true;

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("显示", null, (s, e) => ShowWindow());
            contextMenu.Items.Add("隐藏", null, (s, e) => HideWindow());
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("设置…", null, (s, e) => SettingsButton_Click(null, null));
            var autoHideItem = contextMenu.Items.Add("失去焦点自动隐藏", null, (s, e) => ToggleAutoHide());
            _autoHideMenuItem = (ToolStripMenuItem)autoHideItem;
            _autoHideMenuItem.Checked = _currentSettings.Window.AutoHideOnFocusLost;
            var alwaysOnTopItem = contextMenu.Items.Add("窗口置顶", null, (s, e) => ToggleAlwaysOnTop());
            ((ToolStripMenuItem)alwaysOnTopItem).Checked = this.Topmost;
            var runAtStartupItem = contextMenu.Items.Add("开机自启动", null, (s, e) => ToggleRunAtStartup());
            ((ToolStripMenuItem)runAtStartupItem).Checked = StartupManager.IsStartupEnabled();
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("退出", null, (s, e) => ExitApplication());

            _trayIcon.ContextMenuStrip = contextMenu;
            _trayIcon.DoubleClick += (s, e) => ToggleWindow();
        }

        /// <summary>
        /// 切换自动隐藏并持久化
        /// </summary>
        private void ToggleAutoHide()
        {
            _currentSettings.Window.AutoHideOnFocusLost = !_currentSettings.Window.AutoHideOnFocusLost;
            SharedResourceManager.Instance.UpdateSettings(_currentSettings);
            _autoHide?.ApplySettings(_currentSettings);
            UpdateTrayMenuChecks();
        }

        private void ToggleAlwaysOnTop()
        {
            this.Topmost = !this.Topmost;
            UpdateTrayMenuChecks();
        }



        private void UpdateTrayMenuChecks()
        {
            if (_trayIcon?.ContextMenuStrip?.Items == null)
                return;

            for (int i = 0; i < _trayIcon.ContextMenuStrip.Items.Count; i++)
            {
                var item = _trayIcon.ContextMenuStrip.Items[i] as ToolStripMenuItem;
                if (item == null) continue;

                if (item.Text == "窗口置顶")
                {
                    item.Checked = this.Topmost;
                }
                else if (item.Text == "开机自启动")
                {
                    item.Checked = StartupManager.IsStartupEnabled();
                }
                else if (item.Text == "失去焦点自动隐藏")
                {
                    item.Checked = _currentSettings.Window.AutoHideOnFocusLost;
                }
            }
        }

        private void ShowWindow()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            // 唤回时若处于收起状态（悬浮球或贴边隐藏），统一走 AutoHideManager 恢复
            _autoHide?.ShowSidebar(true);
            this.Activate();
        }

        private void HideWindow()
        {
            // 统一走 CollapseManually：它不受自动隐藏开关限制，
            // 且在悬浮球模式下会正确弹出球体（而不是留下一个看不见的隐藏窗口）
            if (_autoHide != null && _currentSettings.Window.AutoHideOnFocusLost)
            {
                _autoHide.CollapseManually();
            }
            else
            {
                // 自动隐藏关闭时，托盘的"隐藏"就是纯粹隐藏窗口、不留球
                _autoHide?.ShowSidebar(false);
                this.Hide();
            }
        }

        private void ToggleWindow()
        {
            if (this.IsVisible && !(_autoHide?.IsHidden ?? false))
                HideWindow();
            else
                ShowWindow();
        }

        private void ExitApplication()
        {
            _isClosing = true;
            System.Windows.Application.Current.Shutdown();
        }

        private void ApplySettings(SettingsData settings)
        {
            this.Width = settings.Window.Width;
            WindowPositioner.PositionSidebarWindow(this, settings.Window);
            // 记录显示位置，供自动隐藏滑回使用
            _autoHide?.CaptureShownPosition();
        }

        private void LoadWidgets()
        {
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] LoadWidgets START");

            WidgetContainer.Children.Clear();

            // 按用户的启用状态与自定义顺序装载组件
            var widgets = _widgetManager.LoadWidgets(_currentSettings?.Widgets);
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Widget discovery complete");

            // Add UI elements immediately 
            foreach (var widget in widgets)
            {
                WidgetContainer.Children.Add(widget.GetControl());
            }
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] UI elements added - WINDOW SHOULD SHOW NOW");

            // Initialize widgets in background with delay to let UI render
            Dispatcher.BeginInvoke(new Action(async () => {
                System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Starting background initialization");
                await _widgetManager.InitializeWidgetsAsync();
                System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Background initialization complete");
            }), DispatcherPriority.Background);

            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] LoadWidgets END");
        }

        /// <summary>
        /// 供设置窗口调用：组件启用状态或顺序变更后重建组件区，无需重启程序。
        /// </summary>
        public void ReloadWidgets()
        {
            // 重新读取一次设置，确保用的是最新持久化结果
            _currentSettings = SettingsStorage.LoadSettings();
            LoadWidgets();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_settingsWindow != null && _settingsWindow.IsVisible)
            {
                _settingsWindow.Activate();
                return;
            }

            // Reload current settings before opening window
            _currentSettings = SettingsStorage.LoadSettings();

            _settingsWindow = new SettingsWindow(_currentSettings);
            _settingsWindow.SettingsApplied += OnSettingsApplied;
            _settingsWindow.ShortcutsChanged += OnShortcutsChanged;
            _settingsWindow.WidgetsChanged += OnWidgetsChanged;
            _settingsWindow.Left = this.Left - _settingsWindow.Width - 10;
            _settingsWindow.Top = this.Top;

            if (_settingsWindow.Left < 0)
            {
                _settingsWindow.Left = this.Left + this.Width + 10;
            }

            // 设置窗口打开期间抑制自动隐藏（含图标选择器）
            _autoHide?.SuppressAutoHide();
            _settingsWindow.Closed += (closedSender, closedArgs) => _autoHide?.ResumeAutoHide();

            _settingsWindow.Show();
        }



        private void OnShortcutsChanged()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("OnShortcutsChanged: Starting widget refresh...");

                // Find the shortcuts widget and refresh it
                var widgets = _widgetManager.GetLoadedWidgets();
                System.Diagnostics.Debug.WriteLine($"Found {widgets.Count} total widgets");

                foreach (var widget in widgets)
                {
                    System.Diagnostics.Debug.WriteLine($"  Widget: {widget.Name}");
                    if (widget.Name == "Shortcuts" && widget is gtt_sidebar.Widgets.Shortcuts.ShortcutsWidget shortcutsWidget)
                    {
                        System.Diagnostics.Debug.WriteLine("Found shortcuts widget, calling RefreshShortcuts()");
                        shortcutsWidget.RefreshShortcuts();
                        System.Diagnostics.Debug.WriteLine("Refreshed shortcuts widget from settings change");
                        return;
                    }
                }

                System.Diagnostics.Debug.WriteLine("ERROR: Shortcuts widget not found!");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error refreshing shortcuts widget: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置在"组件"区切换启用/调整顺序后调用：立即重建组件区，无需重启。
        /// 直接使用传入的最新配置，避免回读磁盘拿到旧数据。
        /// </summary>
        private void OnWidgetsChanged(WidgetSettings widgetSettings)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("OnWidgetsChanged: 重建组件区");

                if (_currentSettings == null)
                {
                    _currentSettings = SettingsStorage.LoadSettings();
                }

                // 用设置窗口传回的内存对象替换，保证本次改动立刻反映到界面
                _currentSettings.Widgets = widgetSettings;

                LoadWidgets();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reloading widgets: {ex.Message}");
            }
        }
        private void OnSettingsApplied(SettingsData newSettings)
        {
            _currentSettings = newSettings;

            // Update SharedResourceManager cache instead of direct file save
            SharedResourceManager.Instance.UpdateSettings(_currentSettings);

            ApplySettings(_currentSettings);
            _autoHide?.ApplySettings(_currentSettings);
            UpdateTrayMenuChecks();
        }

        private void ToggleRunAtStartup()
        {
            var currentStatus = StartupManager.IsStartupEnabled();
            var newStatus = !currentStatus;

            if (StartupManager.SetStartupEnabled(newStatus))
            {
                System.Diagnostics.Debug.WriteLine($"Startup status changed: {newStatus}");
                UpdateTrayMenuChecks(); // Update both checkmarks
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Failed to change startup status");
                // Optionally show a message box to the user
                System.Windows.MessageBox.Show("修改开机自启动设置失败，请检查权限。",
                               "开机自启动",
                               MessageBoxButton.OK,
                               MessageBoxImage.Warning);
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            this.ResizeMode = ResizeMode.NoResize;
        }

        protected override void OnClosed(EventArgs e)
        {
            _autoHide?.Dispose();
            _trayIcon?.Dispose();
            base.OnClosed(e);
            _widgetManager?.DisposeWidgets();

            if (_settingsWindow != null)
            {
                _settingsWindow.SettingsApplied -= OnSettingsApplied;
                _settingsWindow.ShortcutsChanged -= OnShortcutsChanged;
                

                if (_settingsWindow.IsVisible)
                {
                    _settingsWindow.Close();
                }
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_isClosing)
            {
                e.Cancel = true;

                // 已处于收起状态（悬浮球模式窗口是 Hide 的）就直接跳过，
                // 否则 HideWindow 会先 ShowSidebar 把窗口显示出来，造成一次闪烁。
                if (!(_autoHide?.IsHidden ?? false))
                {
                    HideWindow();
                }
            }
            base.OnClosing(e);
        }
    }
}
