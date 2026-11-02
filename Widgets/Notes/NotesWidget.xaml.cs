using gtt_sidebar.Core.Interfaces;
using gtt_sidebar.Core.Managers;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace gtt_sidebar.Widgets.Notes
{
    public partial class NotesWidget : UserControl, IWidget
    {
        private NotesData _notesData;
        private NotesPopup _popup;

        /// <summary>抑制自动隐藏时使用的持有者名（与 AutoHideManager 中的具名持有对应）</summary>
        private const string AutoHideHolder = "notes-popup";

        /// <summary>本地幂等标志：只在 true→false / false→true 时真正通知主窗口</summary>
        private bool _autoHideSuppressed;

        public new string Name => "Notes";

        public string DisplayName => "备注";

        public string Description => "多标签便签，点击展开编辑";

        public NotesWidget()
        {
            InitializeComponent();

            // Make the entire widget clickable
            this.MouseLeftButtonDown += NotesWidget_MouseLeftButtonDown;
            this.Background = Brushes.Transparent; // Ensure whole area is clickable
        }

        public UserControl GetControl() => this;

        public async Task InitializeAsync()
        {
            try
            {
                // Load notes data from storage
                System.Diagnostics.Debug.WriteLine("NotesWidget: Loading notes from storage...");
                _notesData = NotesStorage.LoadNotes();

                if (_notesData == null)
                {
                    System.Diagnostics.Debug.WriteLine("NotesWidget: LoadNotes returned null!");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"NotesWidget: Loaded {_notesData.Tabs?.Count ?? 0} tabs");
                }

                UpdatePreview();

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NotesWidget InitializeAsync error: {ex.Message}");
                // Create emergency fallback
                _notesData = new NotesData();
                _notesData.EnsureDefaultTab("你好，这里是你的记事本。");
            }
        }

        private void UpdatePreview()
        {
            try
            {
                if (_notesData?.Tabs?.Count > 0)
                {
                    // Show preview of the active tab
                    var activeTab = _notesData.GetActiveTab();
                    PreviewText.Text = activeTab?.GetPreview() ?? "点击此处开始记录…";

                    // Update tab count
                    var tabCount = _notesData.Tabs.Count;
                    TabCountText.Text = tabCount == 1 ? "1 个标签" : $"{tabCount} 个标签";
                }
                else
                {
                    PreviewText.Text = "点击此处开始记录…";
                    TabCountText.Text = "0 个标签";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdatePreview error: {ex.Message}");
                PreviewText.Text = "点击此处开始记录…";
                TabCountText.Text = "0 个标签";
            }
        }

        private void NotesWidget_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            OpenNotesPopup();
        }

        private void OpenNotesPopup()
        {
            try
            {
                // Ensure we have valid notes data
                if (_notesData == null)
                {
                    System.Diagnostics.Debug.WriteLine("NotesWidget: _notesData is null in OpenNotesPopup, initializing...");
                    // This might happen if InitializeAsync wasn't called or failed
                    _notesData = NotesStorage.LoadNotes();
                    if (_notesData == null)
                    {
                        System.Diagnostics.Debug.WriteLine("NotesWidget: LoadNotes still returned null, creating emergency default");
                        _notesData = new NotesData();
                        _notesData.EnsureDefaultTab("你好，这里是你的记事本。");
                    }
                    UpdatePreview();
                }

                // Debug: Log current state
                System.Diagnostics.Debug.WriteLine($"NotesWidget: Opening popup with {_notesData.Tabs.Count} tabs, active: {_notesData.LastActiveTabIndex}");

                // Get main window reference once
                var mainWindow = Application.Current.MainWindow;

                // 弹窗打开期间抑制侧边栏自动隐藏，避免操作到一半侧边栏消失。
                // 幂等：重复点击备注卡片不会累积，只有真正隐藏/关闭弹窗才释放。
                SetAutoHideSuppressed(mainWindow, true);

                // If popup exists but is hidden, just show it
                if (_popup != null)
                {
                    // Update popup with current data (in case it was modified elsewhere)
                    _popup.UpdateNotesData(_notesData);

                    // Position and show
                    if (mainWindow != null)
                    {
                        _popup.PositionRelativeToSidebar(mainWindow);
                    }

                    _popup.ShowAndActivate();
                    return;
                }

                // Create popup for the first time
                _popup = new NotesPopup(_notesData);
                _popup.NotesChanged += OnNotesChanged;

                // 弹窗每次真正隐藏都要释放抑制；关闭则彻底释放并解引用。
                // 注意：NotesPopup 失焦时是 Hide() 而不是 Close()，旧代码只挂 Closed，
                // 导致"弹窗失焦自隐藏 → 抑制永不释放 → 侧边栏再也不自动收起"。
                _popup.Hidden += OnPopupHidden;

                // Handle popup closing to set it to null
                _popup.Closed += (s, e) =>
                {
                    if (_popup != null)
                    {
                        _popup.NotesChanged -= OnNotesChanged;
                        _popup.Hidden -= OnPopupHidden;
                        _popup = null;
                    }
                    // 弹窗关闭后恢复自动隐藏（幂等，重复释放无副作用）
                    SetAutoHideSuppressed(mainWindow, false);
                };

                // Position popup relative to sidebar
                if (mainWindow != null)
                {
                    _popup.PositionRelativeToSidebar(mainWindow);
                }

                // Show popup
                _popup.ShowAndActivate();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening notes popup: {ex.Message}");
                // 异常路径同样要释放抑制，否则一次失败就会让自动隐藏永久失效
                SetAutoHideSuppressed(Application.Current.MainWindow, false);
                MessageBox.Show($"打开备注失败：{ex.Message}", "备注错误",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>弹窗隐藏（失焦自隐藏 / ESC）时释放自动隐藏抑制</summary>
        private void OnPopupHidden()
        {
            SetAutoHideSuppressed(Application.Current.MainWindow, false);
        }

        private void OnNotesChanged(NotesData updatedNotesData)
        {
            _notesData = updatedNotesData;
            UpdatePreview();

            // Save to storage
            SharedResourceManager.Instance.QueueSave("notes", _notesData);
        }

        /// <summary>
        /// 设置/解除自动隐藏抑制（本地幂等，避免重复调用把计数带偏）
        /// </summary>
        private void SetAutoHideSuppressed(Window mainWindow, bool suppressed)
        {
            if (_autoHideSuppressed == suppressed) return;

            var host = mainWindow as gtt_sidebar.Core.Application.MainWindow;
            if (host == null) return;

            if (suppressed)
            {
                host.SuppressAutoHide(AutoHideHolder);
                _autoHideSuppressed = true;
            }
            else
            {
                host.ReleaseAutoHide(AutoHideHolder);
                _autoHideSuppressed = false;
            }
        }

        public void Dispose()
        {
            try
            {
                // Save any pending changes
                if (_notesData != null)
                {
                    NotesStorage.SaveNotes(_notesData);
                }

                // Clean up popup properly
                if (_popup != null)
                {
                    _popup.NotesChanged -= OnNotesChanged;
                    _popup.Hidden -= OnPopupHidden;

                    // Force close popup if it's still open
                    if (_popup.IsVisible)
                    {
                        _popup.Hide();
                    }

                    _popup.Close();
                    _popup = null;
                }

                // 兜底：组件被重建/销毁时不留下悬挂的抑制持有
                SetAutoHideSuppressed(Application.Current.MainWindow, false);

                // Clear references
                _notesData = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing NotesWidget: {ex.Message}");
            }
        }
    }
}