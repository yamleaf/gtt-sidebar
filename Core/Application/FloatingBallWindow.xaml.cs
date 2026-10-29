using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using gtt_sidebar.Core.Settings;

namespace gtt_sidebar.Core.Application
{
    /// <summary>
    /// 收起后的悬浮球。
    ///
    /// 设计意图：替代原先「滑出屏幕只留 3px」的隐藏方式——3px 太窄，用户既看不见也难命中。
    /// 现在收起后是一个明确的圆形小窗，一眼能看到、随手能点到。
    ///
    /// 交互约定（与 AutoHideManager 配合）：
    /// - 鼠标**进入**球体 → 立即展开侧边栏（悬停展开）
    /// - 鼠标**左键点击** → 同样展开（兜底：悬停没触发时可用）
    /// - 按住**拖动** → 沿屏幕垂直方向移动，松手后记住位置
    /// - **右键** → 弹出菜单（设置 / 退出），避免球太小不好操作
    ///
    /// 注意：本窗口 Topmost 且不抢焦点，避免展开时打断用户在全屏应用里的输入。
    /// </summary>
    public partial class FloatingBallWindow : Window
    {
        private WindowSettings _settings;

        // 拖动状态
        private bool _isDragging;
        private Point _dragStartMouse;       // 按下时的鼠标屏幕坐标
        private double _dragStartTop;        // 按下时的窗口 Top
        private bool _dragMoved;             // 是否真正发生了位移（用于区分"点击"与"拖动"）

        // 悬停展开的延迟计时器：
        // 必须延迟，否则鼠标一进入球体就立刻展开，用户根本没有机会按住拖动。
        private DispatcherTimer _hoverTimer;
        private bool _pointerInsideBall;

        // 点击/拖动判定的容差：位移小于该值视为点击
        private const double DRAG_THRESHOLD_PX = 3.0;

        // 悬停多久才展开（毫秒）。
        // 太短会导致来不及按住拖动（用户实测 280ms 仍然手忙脚乱），
        // 太长则显得迟钝。600ms 给拖动留出充裕的反应时间。
        private const int HOVER_EXPAND_DELAY_MS = 600;

        /// <summary>鼠标进入球体，请求展开侧边栏</summary>
        public event Action ExpandRequested;

        /// <summary>用户拖动结束，位置需要持久化（参数为屏幕坐标 Y）</summary>
        public event Action<double> PositionChanged;

        /// <summary>请求打开设置窗口</summary>
        public event Action SettingsRequested;

        /// <summary>请求退出程序</summary>
        public event Action ExitRequested;

        public FloatingBallWindow()
        {
            InitializeComponent();

            // 不抢焦点：展开侧边栏时不应把前台窗口切走
            ShowActivated = false;

            _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(HOVER_EXPAND_DELAY_MS) };
            _hoverTimer.Tick += HoverTimer_Tick;

            MouseEnter += OnBallMouseEnter;
            MouseLeave += OnBallMouseLeave;

            // 双保险：本窗口是 ShowActivated=false 的置顶小窗，实测 MouseEnter 可能不触发。
            // 用低频轮询光标位置作为可靠替代方案，两条路径谁先到都算数。
            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            _pollTimer.Tick += PollTimer_Tick;
            _pollTimer.Start();
        }

        private DispatcherTimer _pollTimer;

        /// <summary>
        /// 轮询光标是否落在球体圆内。
        ///
        /// 为什么需要这个而不是只靠 MouseEnter：本窗口 ShowActivated=false，
        /// 实测在部分环境下 Window 级 MouseEnter 根本不会触发，
        /// 但 Windows 侧的 WindowFromPoint 是正常的。轮询能稳定兜住这种情况。
        /// </summary>
        private void PollTimer_Tick(object sender, EventArgs e)
        {
            if (!IsVisible) return;

            // 拖动中不处理
            if (_isDragging) return;

            var mouse = GetCursorScreenPoint();
            var inside = IsPointInPill(mouse);

            if (inside == _pointerInsideBall) return;

            _pointerInsideBall = inside;

            if (inside)
            {
                _hoverTimer.Stop();
                _hoverTimer.Start();
                SetHover(true);
            }
            else
            {
                _hoverTimer.Stop();
                SetHover(false);
            }
        }

        /// <summary>
        /// 判断屏幕坐标是否落在胶囊把手范围内。
        ///
        /// 胶囊是贴边的小矩形（宽 18 / 高 64），窗口本身已按胶囊尺寸设定，
        /// 因此直接用窗口矩形判定即可，无需再做圆形距离计算。
        /// </summary>
        private bool IsPointInPill(Point screenPoint)
        {
            return screenPoint.X >= this.Left
                && screenPoint.X <= this.Left + this.Width
                && screenPoint.Y >= this.Top
                && screenPoint.Y <= this.Top + this.Height;
        }

        /// <summary>光标屏幕坐标（物理像素 → WPF 逻辑像素）</summary>
        private Point GetCursorScreenPoint()
        {
            var pos = new POINT();
            if (!GetCursorPos(out pos)) return new Point(-9999, -9999);

            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                return source.CompositionTarget.TransformFromDevice.Transform(new Point(pos.X, pos.Y));
            }
            return new Point(pos.X, pos.Y);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [System.Runtime.InteropServices.StructLayout(
            System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        /// <summary>
        /// 鼠标进入球体：不立即展开，而是启动延迟计时。
        /// 这段时间内用户若按下并拖动，就取消展开，改为拖动。
        /// </summary>
        private void OnBallMouseEnter(object sender, MouseEventArgs e)
        {
            _pointerInsideBall = true;

            if (_isDragging) return;

            _hoverTimer.Stop();
            _hoverTimer.Start();

            SetHover(true);
        }

        private void OnBallMouseLeave(object sender, MouseEventArgs e)
        {
            _pointerInsideBall = false;

            // 拖动中鼠标可能移出球体，此时不能停计时器或改变外观
            if (_isDragging) return;

            _hoverTimer.Stop();
            SetHover(false);
        }

        private void HoverTimer_Tick(object sender, EventArgs e)
        {
            _hoverTimer.Stop();

            // 到点时鼠标仍停留在球上，且没在拖动 → 展开
            if (_isDragging || !_pointerInsideBall) return;

            ExpandRequested?.Invoke();
        }

        /// <summary>
        /// 取消待执行的悬停展开（开始拖动时调用）
        /// </summary>
        private void CancelHoverExpand()
        {
            _hoverTimer?.Stop();
        }

        /// <summary>
        /// 胶囊把手尺寸：宽度（伸出屏幕的厚度）与高度（竖向长度）。
        ///
        /// 设计成竖向把手而非圆形球：占用横向空间更小，贴着屏幕边缘时
        /// 几乎不遮挡内容，纵向又足够长便于命中。
        ///
        /// 窗口比胶囊本身大一圈，用于容纳投影扩散与悬停光环，
        /// 同时让胶囊与屏幕边缘保持间距，能完整看出圆角轮廓。
        /// </summary>
        private const double PILL_WIDTH = 20;
        private const double PILL_HEIGHT = 62;

        /// <summary>胶囊与屏幕边缘的间距（像素）</summary>
        private const double EDGE_GAP = 6;

        /// <summary>窗口在胶囊四周额外留出的边距（用于投影与光环）</summary>
        private const double SHADOW_PAD = 5;

        /// <summary>
        /// 应用设置：尺寸、位置、贴边方向
        /// </summary>
        public void ApplySettings(WindowSettings settings)
        {
            if (settings == null) return;
            _settings = settings;

            // 高度允许按 FloatingBallSize 微调（28~80 映射为长度），宽度固定
            var scale = Math.Max(28, Math.Min(80, settings.FloatingBallSize)) / 40.0;
            var pillHeight = Math.Round(PILL_HEIGHT * Math.Max(0.8, Math.Min(1.4, scale)));

            // 窗口 = 胶囊 + 左右投影留白；底部多留 2px 让投影有落点
            this.Width = PILL_WIDTH + SHADOW_PAD * 2;
            this.Height = pillHeight + SHADOW_PAD + SHADOW_PAD + 2;

            Reposition();
        }

        /// <summary>
        /// 按当前设置计算并应用位置：贴边（水平方向留间距），垂直方向用记住的值或默认中点
        /// </summary>
        private void Reposition()
        {
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            var winSize = this.Height;

            // 水平：与屏幕边缘留出 EDGE_GAP，让胶囊的圆角和投影都能完整显示。
            // 窗口本身含 SHADOW_PAD，所以再减去这部分，保证"胶囊边缘"到屏幕的距离恒定。
            if (_settings.Position == SidebarPosition.Right)
            {
                this.Left = screenWidth - this.Width - (EDGE_GAP - SHADOW_PAD);
            }
            else
            {
                this.Left = EDGE_GAP - SHADOW_PAD;
            }

            // 垂直：优先用记住的位置
            double top;
            if (double.IsNaN(_settings.FloatingBallY))
            {
                top = CalculateDefaultTop(screenHeight, winSize);
            }
            else
            {
                top = _settings.FloatingBallY;
            }

            this.Top = ClampTop(top, screenHeight, winSize);
        }

        /// <summary>
        /// 默认位置：放在边缘感应区（上下留白之间）的垂直中点
        /// </summary>
        private double CalculateDefaultTop(double screenHeight, double winSize)
        {
            var topLimit = screenHeight * (_settings?.AutoHideEdgeTopRatio ?? 0.25);
            var bottomLimit = screenHeight * (1.0 - (_settings?.AutoHideEdgeBottomRatio ?? 0.15));
            var centerY = (topLimit + bottomLimit) / 2.0;
            return centerY - winSize / 2.0;
        }

        /// <summary>
        /// 限制在屏幕内（含任务栏区域之外的安全边界）
        /// </summary>
        private double ClampTop(double top, double screenHeight, double size)
        {
            var maxTop = screenHeight - size - 4;
            if (top < 4) top = 4;
            if (top > maxTop) top = maxTop;
            return top;
        }

        #region 拖动

        private void Ball_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                // 关键：按下即取消悬停展开，给拖动让路
                CancelHoverExpand();

                _isDragging = true;
                _dragMoved = false;
                _dragStartMouse = GetMouseScreenPosition();
                _dragStartTop = this.Top;
                CaptureMouse();

                SetDragging(true);
            }
        }

        private void Ball_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;

            _isDragging = false;
            ReleaseMouseCapture();
            SetDragging(false);

            if (_dragMoved)
            {
                // 拖动结束：记住位置
                if (_settings != null)
                {
                    _settings.FloatingBallY = this.Top;
                }
                PositionChanged?.Invoke(this.Top);

                // 拖动结束后鼠标还在球上，不应立刻又触发展开
                _hoverTimer.Stop();
            }
            else
            {
                // 位移不足 → 视为点击，直接展开
                ExpandRequested?.Invoke();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (!_isDragging) return;

            var current = GetMouseScreenPosition();
            var deltaY = current.Y - _dragStartMouse.Y;

            if (!_dragMoved && Math.Abs(deltaY) >= DRAG_THRESHOLD_PX)
            {
                _dragMoved = true;
            }

            if (_dragMoved)
            {
                var size = this.Width;
                this.Top = ClampTop(_dragStartTop + deltaY, SystemParameters.PrimaryScreenHeight, size);
            }
        }

        /// <summary>
        /// 鼠标屏幕坐标（WPF 逻辑像素，适配 DPI 缩放）
        /// </summary>
        private Point GetMouseScreenPosition()
        {
            return GetCursorScreenPoint();
        }

        #endregion

        #region 右键菜单

        private void Ball_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;

            var menu = new System.Windows.Controls.ContextMenu();

            var settingsItem = new System.Windows.Controls.MenuItem { Header = "设置…" };
            settingsItem.Click += (s, args) => SettingsRequested?.Invoke();
            menu.Items.Add(settingsItem);

            menu.Items.Add(new System.Windows.Controls.Separator());

            var exitItem = new System.Windows.Controls.MenuItem { Header = "退出" };
            exitItem.Click += (s, args) => ExitRequested?.Invoke();
            menu.Items.Add(exitItem);

            menu.PlacementTarget = this;
            menu.IsOpen = true;
        }

        #endregion

        /// <summary>
        /// 悬停高亮：淡入蓝色光环 + 轻微放大，给出"可点击"的反馈。
        /// 用动画而不是直接改属性，操作手感更顺。
        /// </summary>
        private void SetHover(bool on)
        {
            try
            {
                var to = on ? 1.0 : 0.0;
                var animation = new DoubleAnimation
                {
                    To = to,
                    Duration = TimeSpan.FromMilliseconds(140),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.Stop
                };

                HoverRing.BeginAnimation(UIElement.OpacityProperty, animation);
                HoverRing.Opacity = to;

                if (!on)
                {
                    // 离开时取消拖动态的缩放，避免残留
                    PillBody.RenderTransform = Transform.Identity;
                }
            }
            catch
            {
                // 视觉反馈失败不影响功能
            }
        }

        /// <summary>
        /// 拖动态：胶囊轻微放大，暗示"被抓住"
        /// </summary>
        private void SetDragging(bool on)
        {
            try
            {
                // 竖向把手横向很窄，放大比例取小一些，避免贴边时视觉外溢
                var scale = on ? 1.05 : 1.0;
                var transform = new ScaleTransform(scale, scale);
                PillBody.RenderTransform = transform;
                PillBody.RenderTransformOrigin = new Point(0.5, 0.5);

                // 拖动时保持高亮可见
                SetHover(on || _pointerInsideBall);
            }
            catch
            {
                // 忽略
            }
        }

        public void Dispose()
        {
            _hoverTimer?.Stop();
            _pollTimer?.Stop();
        }
    }
}
