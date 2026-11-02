using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace gtt_sidebar.Widgets.Notes
{
    public partial class NotesPopup : Window
    {
        private NotesData _notesData;
        private DispatcherTimer _autoSaveTimer;
        private bool _contentChanged = false;
        private int _maxTabs = 8;
        private bool _isInitializing = false;
        private string _currentlyEditingTabId; // Track which tab we're actually editing

        public event Action<NotesData> NotesChanged;

        /// <summary>
        /// 弹窗被隐藏（失焦自隐藏 / ESC）时触发。
        ///
        /// 之所以需要这个事件：本弹窗在失焦时是 Hide() 而不是 Close()（为了下次秒开），
        /// 所以 Closed 只在进程退出时才会来。外部（NotesWidget）如果只监听 Closed，
        /// 就会漏掉"弹窗已不可见"这个事实，导致抑制自动隐藏的持有永远不释放。
        /// </summary>
        public event Action Hidden;

        /// <summary>
        /// 打开后忽略失焦的时间窗（毫秒）。
        ///
        /// 侧边栏是 Topmost + 常驻激活的窗口，点击备注卡片弹窗的瞬间，
        /// 鼠标点击/焦点回填会把刚 Show 出来的弹窗挤成 Deactivated，
        /// 于是 Window_Deactivated 立刻 Hide()，表现为"点了没反应"。
        /// 这里给一个短暂宽限，等焦点真正稳定下来再让失焦逻辑接管。
        /// </summary>
        private const int DEACTIVATE_GUARD_MS = 350;

        private DateTime _ignoreDeactivateUntil = DateTime.MinValue;

        public NotesPopup(NotesData notesData)
        {
            InitializeComponent();
            UpdateNotesData(notesData);
            InitializeAutoSave();
            RestoreWindowPosition();
        }

        /// <summary>
        /// Updates the popup with new notes data (for reuse)
        /// </summary>
        public void UpdateNotesData(NotesData notesData)
        {
            _isInitializing = true;

            // Ensure we have valid data
            if (notesData == null)
            {
                System.Diagnostics.Debug.WriteLine("NotesPopup: Received null notesData, creating default");
                notesData = NotesStorage.LoadNotes(); // This should never return null
            }

            _notesData = notesData;

            // Additional safety check
            if (_notesData?.Tabs == null)
            {
                System.Diagnostics.Debug.WriteLine("NotesPopup: _notesData.Tabs is null, ensuring default tab");
                _notesData = NotesStorage.LoadNotes();
            }

            LoadTabs();
            SetActiveTab(_notesData.LastActiveTabIndex);

            _isInitializing = false;
        }

        private void InitializeAutoSave()
        {
            _autoSaveTimer = new DispatcherTimer();
            _autoSaveTimer.Interval = TimeSpan.FromSeconds(15);
            _autoSaveTimer.Tick += (s, e) => SaveIfChanged();
            _autoSaveTimer.Start();
        }

        private void LoadTabs()
        {
            TabsContainer.Children.Clear();

            // Safety check
            if (_notesData?.Tabs == null)
            {
                System.Diagnostics.Debug.WriteLine("LoadTabs: _notesData or Tabs is null");
                AddTabButton.IsEnabled = false;
                return;
            }

            System.Diagnostics.Debug.WriteLine($"LoadTabs: Creating UI for {_notesData.Tabs.Count} tabs:");

            for (int i = 0; i < _notesData.Tabs.Count; i++)
            {
                var tab = _notesData.Tabs[i];
                System.Diagnostics.Debug.WriteLine($"  [{i}] ID: {tab.Id}, IsDefault: {tab.IsDefault}, Title: '{tab.Title}', HasCloseButton: {!tab.IsDefault}");

                var tabButton = CreateTabButton(tab, i);
                TabsContainer.Children.Add(tabButton);
            }

            // Update add button state
            AddTabButton.IsEnabled = _notesData.Tabs.Count < _maxTabs;

            System.Diagnostics.Debug.WriteLine($"LoadTabs: Complete. Active tab index: {_notesData.LastActiveTabIndex}");
        }

        private Button CreateTabButton(NoteTab tab, int index)
        {
            var tabButton = new Button
            {
                Height = 22,
                MinWidth = 30,
                Margin = new Thickness(1, 0, 1, 0),
                Background = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                BorderThickness = new Thickness(1),
                FontSize = 9,
                Tag = index // Store index for tab switching
            };

            // Create content with title and close button
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var titleText = new TextBlock
            {
                Text = string.IsNullOrEmpty(tab.Title) ? (tab.IsDefault ? "📄" : "📝") : tab.Title,
                FontSize = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0)
            };

            Grid.SetColumn(titleText, 0);
            grid.Children.Add(titleText);

            // Add close button for non-default tabs
            if (!tab.IsDefault)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var closeButton = new Button
                {
                    Content = "×",
                    Width = 14,
                    Height = 14,
                    FontSize = 8,
                    FontWeight = FontWeights.Bold,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                    Margin = new Thickness(0, 0, 2, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = tab.Id
                };

                closeButton.Click += CloseTabButton_Click;
                closeButton.MouseEnter += (s, e) => closeButton.Foreground = Brushes.Red;
                closeButton.MouseLeave += (s, e) => closeButton.Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128));

                Grid.SetColumn(closeButton, 1);
                grid.Children.Add(closeButton);
            }

            tabButton.Content = grid;
            tabButton.Click += (s, e) => SetActiveTab(index);

            return tabButton;
        }

        private void SetActiveTab(int index)
        {
            // Safety check
            if (_notesData?.Tabs == null || _notesData.Tabs.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("SetActiveTab: No tabs available");
                return;
            }

            // Save current tab content first (using the tab ID we were editing, not the index)
            if (!_isInitializing && !string.IsNullOrEmpty(_currentlyEditingTabId))
            {
                SaveCurrentTabContentById(_currentlyEditingTabId);
            }

            // Validate index
            if (index < 0 || index >= _notesData.Tabs.Count)
            {
                index = 0; // Default to first tab
            }

            _notesData.LastActiveTabIndex = index;
            var activeTab = _notesData.Tabs[index];

            // Track which tab we're now editing
            _currentlyEditingTabId = activeTab.Id;

            System.Diagnostics.Debug.WriteLine($"SetActiveTab: Switching to tab {index} (ID: {activeTab.Id}, Title: '{activeTab.Title}')");
            System.Diagnostics.Debug.WriteLine($"SetActiveTab: Loading content: '{(activeTab.Content ?? "").Substring(0, Math.Min(30, (activeTab.Content ?? "").Length))}...'");

            // Update content - simple text assignment
            ContentTextBox.Text = activeTab.Content ?? "";

            if (!_isInitializing)
            {
                _contentChanged = false;
            }

            // Update tab visual states
            UpdateTabVisualStates();

            // Focus content area
            if (!_isInitializing)
            {
                ContentTextBox.Focus();
            }
        }

        // Removed markdown processing methods - using plain text for now

        private void UpdateTabVisualStates()
        {
            for (int i = 0; i < TabsContainer.Children.Count; i++)
            {
                var tabButton = (Button)TabsContainer.Children[i];
                if (i == _notesData.LastActiveTabIndex)
                {
                    tabButton.Background = new SolidColorBrush(Color.FromRgb(230, 240, 255));
                    tabButton.BorderBrush = new SolidColorBrush(Color.FromRgb(100, 149, 237));
                }
                else
                {
                    tabButton.Background = new SolidColorBrush(Colors.White);
                    tabButton.BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200));
                }
            }
        }

        private void SaveCurrentTabContent()
        {
            if (!string.IsNullOrEmpty(_currentlyEditingTabId))
            {
                SaveCurrentTabContentById(_currentlyEditingTabId);
            }
        }

        private void SaveCurrentTabContentById(string tabId)
        {
            if (string.IsNullOrEmpty(tabId))
            {
                System.Diagnostics.Debug.WriteLine("SaveCurrentTabContentById: No tab ID provided");
                return;
            }

            var tab = _notesData.Tabs.FirstOrDefault(t => t.Id == tabId);
            if (tab == null)
            {
                System.Diagnostics.Debug.WriteLine($"SaveCurrentTabContentById: Tab {tabId} not found");
                return;
            }

            var currentContent = ContentTextBox.Text ?? "";
            System.Diagnostics.Debug.WriteLine($"SaveCurrentTabContentById: Saving content to tab {tabId} ('{tab.Title}'): '{currentContent.Substring(0, Math.Min(20, currentContent.Length))}...'");

            tab.Content = currentContent;
            _contentChanged = true;
        }

        private void SaveIfChanged()
        {
            if (_contentChanged && !_isInitializing)
            {
                SaveCurrentTabContent(); // This now uses ID-based saving
                SaveWindowPosition();
                NotesChanged?.Invoke(_notesData);
                _contentChanged = false;
                System.Diagnostics.Debug.WriteLine("Auto-saved notes");
            }
        }

        private void SaveWindowPosition()
        {
            _notesData.PopupPosition.Left = this.Left;
            _notesData.PopupPosition.Top = this.Top;
        }

        private void RestoreWindowPosition()
        {
            if (_notesData.PopupPosition.IsValid)
            {
                // Ensure the window is still on screen
                if (_notesData.PopupPosition.Left >= 0 &&
                    _notesData.PopupPosition.Top >= 0 &&
                    _notesData.PopupPosition.Left + this.Width <= SystemParameters.PrimaryScreenWidth &&
                    _notesData.PopupPosition.Top + this.Height <= SystemParameters.PrimaryScreenHeight)
                {
                    this.Left = _notesData.PopupPosition.Left;
                    this.Top = _notesData.PopupPosition.Top;
                    this.WindowStartupLocation = WindowStartupLocation.Manual;
                }
            }
        }

        private void AddTabButton_Click(object sender, RoutedEventArgs e)
        {
            // Save current content before creating new tab
            SaveCurrentTabContent();

            var newTab = _notesData.AddNewTab(_maxTabs);
            if (newTab != null)
            {
                System.Diagnostics.Debug.WriteLine($"AddTabButton_Click: Created new tab with ID {newTab.Id}");
                LoadTabs();
                SetActiveTab(_notesData.Tabs.Count - 1); // Switch to new tab
                NotesChanged?.Invoke(_notesData);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"AddTabButton_Click: Failed to create new tab (max {_maxTabs} reached)");
            }
        }

        private void CloseTabButton_Click(object sender, RoutedEventArgs e)
        {
            var tabId = ((Button)sender).Tag as string;

            if (string.IsNullOrEmpty(tabId))
            {
                System.Diagnostics.Debug.WriteLine("CloseTabButton_Click: No tab ID found");
                return;
            }

            // Find the tab to delete
            var tabToDelete = _notesData.Tabs.FirstOrDefault(t => t.Id == tabId);
            if (tabToDelete == null)
            {
                System.Diagnostics.Debug.WriteLine($"CloseTabButton_Click: Tab with ID {tabId} not found");
                return;
            }

            if (tabToDelete.IsDefault)
            {
                System.Diagnostics.Debug.WriteLine($"CloseTabButton_Click: Attempted to delete default tab {tabId} - BLOCKED");
                return;
            }

            // ADD THIS CONFIRMATION DIALOG:
            var result = System.Windows.MessageBox.Show(
                "确定要删除这条备注吗？",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                System.Diagnostics.Debug.WriteLine("CloseTabButton_Click: User cancelled deletion");
                return; // User clicked No or closed dialog, don't delete
            }

            var tabIndexToDelete = _notesData.Tabs.IndexOf(tabToDelete);
            System.Diagnostics.Debug.WriteLine($"CloseTabButton_Click: Deleting tab '{tabToDelete.Title}' (ID: {tabId}, Index: {tabIndexToDelete}, IsDefault: {tabToDelete.IsDefault})");

            // CRITICAL: Only save content if we're currently editing the tab we're about to delete
            if (_currentlyEditingTabId == tabId)
            {
                System.Diagnostics.Debug.WriteLine($"CloseTabButton_Click: Saving content for tab being deleted ({tabId})");
                SaveCurrentTabContentById(tabId);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"CloseTabButton_Click: NOT saving content - deleting {tabId} but editing {_currentlyEditingTabId}");
            }

            // Remove the tab
            if (_notesData.RemoveTab(tabId))
            {
                // Always switch to default tab after deletion
                var defaultTab = _notesData.Tabs.FirstOrDefault(t => t.IsDefault);
                if (defaultTab != null)
                {
                    _notesData.LastActiveTabIndex = _notesData.Tabs.IndexOf(defaultTab);
                    System.Diagnostics.Debug.WriteLine($"CloseTabButton_Click: Switching to default tab (index {_notesData.LastActiveTabIndex})");
                }
                else
                {
                    _notesData.LastActiveTabIndex = 0; // Fallback to first tab
                }

                // Reload the tab UI
                LoadTabs();
                SetActiveTab(_notesData.LastActiveTabIndex);
                NotesChanged?.Invoke(_notesData);

                System.Diagnostics.Debug.WriteLine($"CloseTabButton_Click: Successfully deleted tab. Active tab now: {_notesData.LastActiveTabIndex} (ID: {_currentlyEditingTabId})");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"CloseTabButton_Click: Failed to remove tab {tabId}");
            }
        }

        private void ContentTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing)
                return;

            _contentChanged = true;

            // Update tab title if this is the active tab
            if (!string.IsNullOrEmpty(_currentlyEditingTabId))
            {
                var activeTab = _notesData.Tabs.FirstOrDefault(t => t.Id == _currentlyEditingTabId);
                if (activeTab != null)
                {
                    var oldTitle = activeTab.Title;

                    // Temporarily update content to get new title
                    activeTab.Content = ContentTextBox.Text ?? "";
                    var newTitle = activeTab.Title;

                    if (oldTitle != newTitle)
                    {
                        // Find the tab button for this tab ID and update its text
                        var tabIndex = _notesData.Tabs.IndexOf(activeTab);
                        if (tabIndex >= 0 && tabIndex < TabsContainer.Children.Count)
                        {
                            var tabButton = (Button)TabsContainer.Children[tabIndex];
                            var grid = (Grid)tabButton.Content;
                            var titleText = (TextBlock)grid.Children[0];
                            titleText.Text = string.IsNullOrEmpty(newTitle) ? (activeTab.IsDefault ? "📄" : "📝") : newTitle;
                        }
                    }
                }
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // ESC to close
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                HidePopup("ESC");
                return;
            }

            // Ctrl+Tab to cycle through tabs
            if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                var nextIndex = (_notesData.LastActiveTabIndex + 1) % _notesData.Tabs.Count;
                System.Diagnostics.Debug.WriteLine($"Window_KeyDown (Ctrl+Tab): Switching from tab {_notesData.LastActiveTabIndex} to {nextIndex}");
                SetActiveTab(nextIndex);
            }
        }

        // Simplified dragging - only from the drag handle
        private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try
                {
                    this.DragMove();
                    SaveWindowPosition();
                    NotesChanged?.Invoke(_notesData);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Drag error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 显示并激活弹窗（外部统一入口，替代直接调用 Show()+Activate()）。
        ///
        /// 关键点：
        /// 1) 先设置失焦宽限，再 Show，避免"刚显示就 Deactivated → 自己把自己藏起来"；
        /// 2) Activate 失败（例如主窗口抢焦点）不作为错误处理，下一轮点击仍能正常打开。
        /// </summary>
        public void ShowAndActivate()
        {
            _ignoreDeactivateUntil = DateTime.Now.AddMilliseconds(DEACTIVATE_GUARD_MS);

            if (!this.IsVisible)
            {
                this.Show();
            }

            // 置顶并激活，确保输入框能直接拿到键盘焦点
            this.Topmost = true;
            try
            {
                this.Activate();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NotesPopup.ShowAndActivate: Activate 失败 - {ex.Message}");
            }

            // 让光标直接落在正文区，符合"点开就能写"的预期
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    ContentTextBox.Focus();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"NotesPopup.ShowAndActivate: Focus 失败 - {ex.Message}");
                }
            }), DispatcherPriority.Input);
        }

        /// <summary>
        /// 统一隐藏入口：保存 → 通知外部（释放自动隐藏抑制）→ 隐藏
        /// </summary>
        private void HidePopup(string reason)
        {
            System.Diagnostics.Debug.WriteLine($"NotesPopup.HidePopup({reason}): Saving content for tab {_currentlyEditingTabId}");

            SaveIfChanged();
            NotesChanged?.Invoke(_notesData);

            this.Hide();

            // 通知外部：弹窗已不可见，可以恢复自动隐藏
            Hidden?.Invoke();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            // 刚打开的瞬间不响应失焦：此时主窗口/悬浮球抢焦点属于正常现象，
            // 若在此刻 Hide()，用户会看到弹窗一闪而过（甚至完全看不到）。
            if (DateTime.Now < _ignoreDeactivateUntil)
            {
                System.Diagnostics.Debug.WriteLine("NotesPopup: 失焦发生在打开宽限期内，忽略");
                return;
            }

            // 焦点回到侧边栏主窗口本身时不算"用户离开备注"：
            // 用户可能正在点侧边栏上的其它组件，弹窗保持打开更符合预期。
            var mainWindow = System.Windows.Application.Current?.MainWindow;
            if (mainWindow != null && mainWindow.IsActive)
            {
                System.Diagnostics.Debug.WriteLine("NotesPopup: 焦点回到侧边栏，保持打开");
                return;
            }

            HidePopup("Deactivated");
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Save before closing
            SaveIfChanged();

            if (_autoSaveTimer != null)
            {
                _autoSaveTimer.Stop();
                _autoSaveTimer.Tick -= (sender, args) => SaveIfChanged();
                _autoSaveTimer = null;
            }

            base.OnClosing(e);
        }

        public void PositionRelativeToSidebar(Window sidebar)
        {
            // Only position relative to sidebar if no saved position exists
            if (_notesData.PopupPosition.IsValid) return;
            if (sidebar == null) return;

            // 侧边栏可能是 Hide() 状态（悬浮球模式）或正在播放滑出动画，
            // 此时 sidebar.Left 可能是屏幕外的坐标，不能直接拿来算弹窗位置。
            var sidebarLeft = sidebar.Left;
            var sidebarTop = sidebar.Top;
            var sidebarWidth = sidebar.ActualWidth > 0 ? sidebar.ActualWidth : sidebar.Width;

            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;

            // 侧边栏不在可见区域内（Left 为负或超过屏幕）→ 使用一个安全的默认落点
            var sidebarVisible = sidebarLeft >= 0 && sidebarLeft < screenWidth;
            if (!sidebarVisible)
            {
                // 靠右停靠的侧边栏收起后 Left≈屏宽，弹窗自动放到屏幕右侧内侧
                sidebarLeft = screenWidth - sidebarWidth;
            }

            // Position 5px to the left of the sidebar
            var targetLeft = sidebarLeft - this.Width - 5;
            var targetTop = sidebarTop + 50; // Offset from top to align with notes widget

            // Show on right if no room on left
            if (targetLeft < 0)
            {
                targetLeft = sidebarLeft + sidebarWidth + 5;
            }

            // 最后兜底：夹回屏幕内，避免弹窗跑到屏幕外看不见
            targetLeft = Math.Max(0, Math.Min(targetLeft, screenWidth - this.Width));
            if (double.IsNaN(targetTop) || targetTop < 0) targetTop = 0;
            if (targetTop + this.Height > screenHeight) targetTop = Math.Max(0, screenHeight - this.Height);

            this.WindowStartupLocation = WindowStartupLocation.Manual;
            this.Left = targetLeft;
            this.Top = targetTop;
        }
    }
}