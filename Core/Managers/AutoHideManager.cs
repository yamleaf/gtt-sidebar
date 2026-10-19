using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using gtt_sidebar.Core.Settings;

namespace gtt_sidebar.Core.Managers
{
    /// <summary>
    /// 负责“失去焦点贴边隐藏”行为：
    /// 侧边栏失去焦点后延迟滑出屏幕外，只保留一条窄边用于鼠标感应；
    /// 鼠标碰到屏幕边缘时自动滑回，托盘唤回时也会取消隐藏状态。
    ///
    /// 设计要点：
    /// 1) 不依赖一次性的 Deactivated 事件来决定隐藏，而是用低频监视计时器
    ///    持续评估“当前是否应该可见”，这样任意途径唤回后行为都自洽。
    /// 2) 边缘唤回是“一次性触发”的：隐藏时布防，触发后立即解除，
    ///    必须等鼠标离开屏幕边缘才重新布防，否则会与隐藏逻辑形成
    ///    “隐藏→边缘唤回→延迟→隐藏”的死循环跳变。
    /// </summary>
    public class AutoHideManager : IDisposable
    {
        private readonly Window _window;
        private DispatcherTimer _watchTimer;     // 唯一的决策计时器：判断隐藏/唤回
        private WindowSettings _settings;

        private bool _isHidden;                  // 当前是否处于隐藏（滑出屏幕）状态
        private bool _isAnimating;               // 动画进行中，避免互相打断
        private double _shownLeft;               // 显示状态下的 Left
        private int _suppressCount;              // 抑制计数（弹窗/设置窗口打开期间不隐藏）
        private DateTime _pointerLeftAt;         // 鼠标离开“边缘+窗口”区域的时刻
        private bool _pointerInside;             // 上一轮鼠标是否在窗口内
        private bool _pointerInEdgeZone;         // 上一轮鼠标是否处于边缘感应区
        private DateTime _shownAt;               // 本次滑回到位的时刻（用于宽限期）
        private readonly DateTime _startedAt;    // 管理器创建时刻（用于启动宽限期）

        // 边缘感应区宽度（像素）：鼠标进入该区域即视为“想唤回/正在操作边缘”
        private const double EDGE_ZONE_PX = 2;

        // 滑回到位后的宽限期（毫秒）：这段时间内禁止隐藏，留出时间让鼠标移进窗口
        private const int SHOWN_GRACE_MS = 600;

        // 启动宽限期（毫秒）：程序启动/系统解锁后的这段时间内一律不隐藏。
        // 因为开机自启时窗口从一开始就是显示状态，尚未发生任何用户交互，
        // 此时若立刻按“指针不在窗口内”判定隐藏，用户会看到侧边栏自己收起来。
        private const int STARTUP_GRACE_MS = 3000;

        // 边缘感应区在垂直方向上的留白比例（相对屏幕高度），取自设置：
        // 顶部留白避开窗口右上角的关闭/最小化按钮，
        // 底部留白避开任务栏托盘区域。
        // 兜底默认值与 SettingsData 中的默认值保持一致。
        private const double DEFAULT_EDGE_TOP_RATIO = 0.25;
        private const double DEFAULT_EDGE_BOTTOM_RATIO = 0.15;

        /// <summary>
        /// 窗口是否处于隐藏状态
        /// </summary>
        public bool IsHidden => _isHidden;

        /// <summary>
        /// 临时抑制自动隐藏（例如备注弹窗、设置窗口打开期间），支持嵌套
        /// </summary>
        public void SuppressAutoHide()
        {
            _suppressCount++;

            // 若已经隐藏，立刻滑回，避免操作目标消失
            if (_isHidden && !_isAnimating) ShowSidebar(true);
        }

        /// <summary>
        /// 解除抑制
        /// </summary>
        public void ResumeAutoHide()
        {
            if (_suppressCount > 0) _suppressCount--;
        }

        /// <summary>
        /// 隐藏状态变化通知（true=已隐藏）
        /// </summary>
        public event Action<bool> HiddenStateChanged;

        public AutoHideManager(Window window)
        {
            _window = window;
            _settings = SettingsStorage.LoadSettings().Window;

            // 关键：启动时窗口本来就是“已显示”状态，不会走 ShowSidebar()，
            // 因此这两个时间戳必须在构造时初始化，否则会停留在 DateTime.MinValue，
            // 导致宽限期判断 (Now - _shownAt) 永远是个极大值 → 宽限期永久失效。
            // 表现为：开机后侧边栏一直显示（靠 IsActive 兜住），一旦鼠标进入再离开就立刻隐藏。
            var now = DateTime.Now;
            _pointerLeftAt = now;
            _shownAt = now;
            _startedAt = now;

            // 单一决策计时器（120ms 一次，开销极低）：
            // 统一负责“该隐藏 / 该唤回”的全部判断，避免多个计时器语义重叠
            _watchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _watchTimer.Tick += WatchTimer_Tick;
            _watchTimer.Start();
        }

        /// <summary>
        /// 应用最新设置（开关、延迟）
        /// </summary>
        public void ApplySettings(SettingsData settings)
        {
            if (settings?.Window == null) return;
            _settings = settings.Window;

            if (!_settings.AutoHideOnFocusLost)
            {
                // 关闭特性时立即恢复显示
                if (_isHidden && !_isAnimating) ShowSidebar(false);
            }
        }

        /// <summary>
        /// 记录显示状态下的位置，作为滑回的目标。
        /// 动画结束后位置已被固化为属性值，因此直接读取 Left 即真实位置。
        /// </summary>
        public void CaptureShownPosition()
        {
            if (_isHidden || _isAnimating) return;

            _shownLeft = _window.Left;
        }

        /// <summary>
        /// 窗口获得焦点：立即滑回
        /// </summary>
        public void OnActivated()
        {
            if (_isHidden && !_isAnimating)
            {
                ShowSidebar(true);
            }
        }

        /// <summary>
        /// 窗口失去焦点：不再直接触发隐藏。
        /// 隐藏由 WatchTimer 统一评估，这里仅记录时间点，避免重复逻辑。
        /// </summary>
        public void OnDeactivated()
        {
            _pointerLeftAt = DateTime.Now;
        }

        /// <summary>
        /// 显示侧边栏（从屏幕外滑回）
        /// </summary>
        public void ShowSidebar(bool animate = true)
        {
            if (!_isHidden && !_isAnimating)
            {
                return;
            }

            // 打断可能正在进行的隐藏动画，防止旧动画回调把窗口又"锁"回屏幕外
            _window.BeginAnimation(Window.LeftProperty, null);
            _isHidden = false;
            _isAnimating = false;

            // 唤回后重置计时（指针状态在窗口复位后于动画回调中重新计算）
            _pointerLeftAt = DateTime.Now;

            var targetLeft = _shownLeft;
            if (animate)
            {
                AnimateLeft(targetLeft, () =>
                {
                    _isAnimating = false;
                    // 动画结束后清除动画，避免 HoldEnd 把 Left 钉死在动画值上
                    _window.BeginAnimation(Window.LeftProperty, null);
                    _window.Left = targetLeft;
                    // 位置已复位，此时再准确判断指针是否在窗口内
                    _pointerInside = IsPointerInsideWindow();
                    _pointerLeftAt = DateTime.Now;
                    _shownAt = DateTime.Now;                 // 启动宽限期，避免立刻又被收回
                    HiddenStateChanged?.Invoke(false);
                });
            }
            else
            {
                _window.BeginAnimation(Window.LeftProperty, null);
                _window.Left = targetLeft;
                _isAnimating = false;
                _pointerInside = IsPointerInsideWindow();
                _pointerLeftAt = DateTime.Now;
                _shownAt = DateTime.Now;
                HiddenStateChanged?.Invoke(false);
            }
        }

        /// <summary>
        /// 立即隐藏侧边栏（滑出屏幕）
        /// </summary>
        public void HideSidebar()
        {
            if (!_settings.AutoHideOnFocusLost) return;
            if (_isHidden || _isAnimating) return;

            // 记录滑回目标（动画结束后位置已固化，直接读 Left 即真实值）
            _shownLeft = _window.Left;

            _isHidden = true;

            var targetLeft = GetHiddenLeft();
            AnimateLeft(targetLeft, () =>
            {
                _isAnimating = false;
                // 动画结束后固化位置并清除动画，避免后续读取到被钉死的动画值
                _window.BeginAnimation(Window.LeftProperty, null);
                _window.Left = targetLeft;
                // 隐藏完成后解除边缘区标记，允许下一次触碰边缘重新唤回
                _pointerInEdgeZone = false;
                HiddenStateChanged?.Invoke(true);
            });
        }

        /// <summary>
        /// 计算隐藏后窗口的 Left：大部分移出屏幕，仅保留 Peek 像素
        /// </summary>
        private double GetHiddenLeft()
        {
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var peek = Math.Max(0, _settings.AutoHidePeekPx);

            return IsDockedRight()
                ? screenWidth - peek                 // 右侧：几乎全部移出右边缘
                : peek - _window.Width;              // 左侧：几乎全部移出左边缘
        }

        /// <summary>
        /// 判断侧边栏是贴右还是贴左，以显示位置的中心点为准
        /// </summary>
        private bool IsDockedRight()
        {
            // 优先按设置里的停靠方向判断，避免 _shownLeft 尚未采集时误判
            if (_shownLeft <= double.Epsilon || _isHidden)
            {
                return _settings.Position == SidebarPosition.Right;
            }

            var screenWidth = SystemParameters.PrimaryScreenWidth;
            return (_shownLeft + _window.Width / 2.0) >= screenWidth / 2.0;
        }

        /// 核心评估逻辑：每 120ms 判断一次“当前应该显示还是隐藏”。
        ///
        /// 交互模型（避免鼠标停在屏幕边缘时来回跳变）：
        /// - 隐藏状态下，鼠标进入边缘感应区 → 滑回显示；
        /// - 显示状态下，只要鼠标还在边缘感应区或侧边栏窗口内，
        ///   就视为“用户正在边缘操作”，保持展开、不启动隐藏倒计时；
        /// - 只有当鼠标离开边缘区与窗口、且窗口未获得焦点时，
        ///   才开始按设定的延迟倒计时，倒计时结束后隐藏。
        /// </summary>
        private void WatchTimer_Tick(object sender, EventArgs e)
        {
            // 动画进行中不做决策，等动画结束后的下一轮再说
            if (_isAnimating) return;

            var pointerInside = IsPointerInsideWindow();
            if (pointerInside != _pointerInside)
            {
                _pointerInside = pointerInside;
            }

            // 鼠标是否处于“贴近/操作边缘”的状态
            var nearEdge = IsPointerInEdgeZone();

            if (_isHidden)
            {
                // 已隐藏：由该状态机自身负责唤回（不再依赖单独的 EdgeTimer，
                // 避免两个计时器各判一次导致语义分散、重复触发）
                if (nearEdge && !_pointerInEdgeZone)
                {
                    _pointerInEdgeZone = true;
                    ShowSidebar(true);
                    return;
                }
                if (!nearEdge)
                {
                    _pointerInEdgeZone = false;
                }
                return;
            }

            // 走到这里说明当前处于显示状态
            if (nearEdge)
            {
                // 鼠标仍贴着边缘 → 视为正在唤回/操作边缘，保持展开，重置倒计时
                _pointerInEdgeZone = true;
                _pointerLeftAt = DateTime.Now;
                return;
            }

            _pointerInEdgeZone = false;

            // 鼠标在侧边栏窗口内 → 正在使用，不隐藏
            if (pointerInside) return;

            // 以下判断“是否可以开始隐藏倒计时”
            if (!_settings.AutoHideOnFocusLost) return;
            if (_suppressCount > 0) return;                       // 设置窗口/弹窗打开期间不隐藏
            if (_window.WindowState != WindowState.Normal) return;
            if (!_window.IsVisible) return;

            // 焦点仍在窗口上（例如刚点完侧边栏按钮）→ 不隐藏
            if (_window.IsActive) return;

            // 滑回到位后的宽限期：留出时间让用户把鼠标移进窗口
            var sinceShownMs = (DateTime.Now - _shownAt).TotalMilliseconds;
            if (sinceShownMs < SHOWN_GRACE_MS) return;

            // 启动宽限期：刚启动/刚解锁时窗口本就在屏幕上且无任何交互，
            // 此时不应立刻隐藏（否则用户会看到侧边栏自己收起来）
            var sinceStartMs = (DateTime.Now - _startedAt).TotalMilliseconds;
            if (sinceStartMs < STARTUP_GRACE_MS)
            {
                // 宽限期内持续刷新计时基准，让宽限期结束后重新走完整的隐藏延迟
                _pointerLeftAt = DateTime.Now;
                return;
            }

            // 离开边缘/窗口后，按设定延迟倒计时
            var idleMs = (DateTime.Now - _pointerLeftAt).TotalMilliseconds;
            if (idleMs >= Math.Max(0, _settings.AutoHideDelayMs))
            {
                HideSidebar();
            }
        }

        /// <summary>
        /// 判断鼠标当前是否落在侧边栏窗口内（用于避免“指针还在窗口上就被收走”）
        /// </summary>
        private bool IsPointerInsideWindow()
        {
            if (!_window.IsVisible) return false;

            var mouse = GetCursorPosition();
            var left = _window.Left;
            var top = _window.Top;
            var width = _window.ActualWidth > 0 ? _window.ActualWidth : _window.Width;
            var height = _window.ActualHeight > 0 ? _window.ActualHeight : _window.Height;

            return mouse.X >= left && mouse.X <= left + width &&
                   mouse.Y >= top && mouse.Y <= top + height;
        }

        /// <summary>
        /// 鼠标是否处于贴边那一侧的边缘感应区（用于唤回、以及保持展开避免误收）。
        ///
        /// 感应区为「屏幕边缘的一条垂直带」，但**顶、底各留出一段空白**：
        /// - 顶部留白：避开窗口右上角的关闭 / 最小化按钮，
        ///   否则鼠标去点关闭按钮时会误触发侧边栏滑出，反把按钮挡住；
        /// - 底部留白：避开任务栏右侧的托盘区域。
        /// </summary>
        private bool IsPointerInEdgeZone()
        {
            var mouse = GetCursorPosition();
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;

            // 先判断是否贴边（横向）
            var atSide = IsDockedRight()
                ? mouse.X >= screenWidth - EDGE_ZONE_PX
                : mouse.X <= EDGE_ZONE_PX;

            if (!atSide) return false;

            // 再判断是否落在垂直方向的有效区间内（排除顶部与底部留白）
            var topRatio = _settings?.AutoHideEdgeTopRatio ?? DEFAULT_EDGE_TOP_RATIO;
            var bottomRatio = _settings?.AutoHideEdgeBottomRatio ?? DEFAULT_EDGE_BOTTOM_RATIO;

            var topLimit = screenHeight * topRatio;
            var bottomLimit = screenHeight * (1.0 - bottomRatio);

            return mouse.Y >= topLimit && mouse.Y <= bottomLimit;
        }

        private void AnimateLeft(double targetLeft, Action onCompleted)
        {
            _isAnimating = true;

            var animation = new DoubleAnimation
            {
                To = targetLeft,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            };

            animation.Completed += (s, e) =>
            {
                onCompleted?.Invoke();
            };

            try
            {
                _window.BeginAnimation(Window.LeftProperty, animation);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AutoHide: 动画失败，直接定位 - {ex.Message}");
                _window.BeginAnimation(Window.LeftProperty, null);
                _window.Left = targetLeft;
                onCompleted?.Invoke();
            }
        }

        /// <summary>
        /// 获取当前鼠标的屏幕坐标（物理像素 → WPF 逻辑像素）
        /// </summary>
        private Point GetCursorPosition()
        {
            var pos = new POINT();
            if (!GetCursorPos(out pos)) return new Point(0, 0);

            // 多显示器/缩放场景下，按主屏 DPI 换算成 WPF 逻辑坐标
            var source = PresentationSource.FromVisual(_window);
            if (source?.CompositionTarget != null)
            {
                var transform = source.CompositionTarget.TransformFromDevice;
                return transform.Transform(new Point(pos.X, pos.Y));
            }

            return new Point(pos.X, pos.Y);
        }

        public void Dispose()
        {
            if (_watchTimer != null)
            {
                _watchTimer.Stop();
                _watchTimer.Tick -= WatchTimer_Tick;
                _watchTimer = null;
            }
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }
    }
}
