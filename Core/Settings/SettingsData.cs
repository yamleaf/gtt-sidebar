using System;
using System.Collections.Generic;
using System.Linq;

namespace gtt_sidebar.Core.Settings
{
    /// <summary>
    /// Main settings data container
    /// </summary>
    public class SettingsData
    {
        public WindowSettings Window { get; set; } = new WindowSettings();
        public string Version { get; set; } = "1.0.0"; // For future migration support
        public SystemMonitorSettings SystemMonitor { get; set; } = new SystemMonitorSettings();

        /// <summary>
        /// 组件（插件）启用状态与顺序。
        /// </summary>
        public WidgetSettings Widgets { get; set; } = new WidgetSettings();

        /// <summary>
        /// Validates and corrects all settings
        /// </summary>
        public void ValidateAndCorrect()
        {
            Window?.Validate();
            SystemMonitor?.ValidateAndCorrect();
            Widgets?.ValidateAndCorrect();
        }
    }

    /// <summary>
    /// 组件启用状态与排序配置。
    ///
    /// 设计要点：
    /// - 只存"被管理过"的条目，未出现过的组件视为**启用**（默认全开），
    ///   这样新增组件时用户无需手动开启即可看到。
    /// - <see cref="Order"/> 记录用户自定义的先后顺序，值越小越靠上；
    ///   不在列表中的组件排在后面，并按内置默认顺序兜底。
    /// </summary>
    public class WidgetSettings
    {
        public List<WidgetState> Items { get; set; } = new List<WidgetState>();

        /// <summary>
        /// 查询某组件是否启用；未记录时默认启用
        /// </summary>
        public bool IsEnabled(string widgetName)
        {
            var item = Items?.FirstOrDefault(i =>
                string.Equals(i.Name, widgetName, StringComparison.OrdinalIgnoreCase));
            return item == null || item.Enabled;
        }

        /// <summary>
        /// 查询某组件的自定义顺序；未记录时返回 int.MaxValue（排到已知项之后）
        /// </summary>
        public int GetOrder(string widgetName)
        {
            var item = Items?.FirstOrDefault(i =>
                string.Equals(i.Name, widgetName, StringComparison.OrdinalIgnoreCase));
            return item?.Order ?? int.MaxValue;
        }

        /// <summary>
        /// 写入/更新某组件的启用状态
        /// </summary>
        public void SetEnabled(string widgetName, bool enabled)
        {
            var item = GetOrCreate(widgetName);
            item.Enabled = enabled;
        }

        /// <summary>
        /// 按排序结果重写全部条目的 Order 字段
        /// </summary>
        public void ApplyOrder(IList<string> orderedNames)
        {
            for (int i = 0; i < orderedNames.Count; i++)
            {
                GetOrCreate(orderedNames[i]).Order = i;
            }
        }

        private WidgetState GetOrCreate(string widgetName)
        {
            if (Items == null) Items = new List<WidgetState>();

            var item = Items.FirstOrDefault(i =>
                string.Equals(i.Name, widgetName, StringComparison.OrdinalIgnoreCase));
            if (item == null)
            {
                item = new WidgetState { Name = widgetName, Enabled = true, Order = int.MaxValue };
                Items.Add(item);
            }
            return item;
        }

        public void ValidateAndCorrect()
        {
            if (Items == null) Items = new List<WidgetState>();
            Items.RemoveAll(i => i == null || string.IsNullOrWhiteSpace(i.Name));
        }
    }

    /// <summary>
    /// 单个组件的持久化状态
    /// </summary>
    public class WidgetState
    {
        /// <summary>组件标识（对应 IWidget.Name）</summary>
        public string Name { get; set; }

        /// <summary>是否启用</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>排序权重，越小越靠上</summary>
        public int Order { get; set; } = int.MaxValue;
    }

    /// <summary>
    /// Window positioning and sizing settings
    /// </summary>
    public class WindowSettings
    {
        public SidebarPosition Position { get; set; } = SidebarPosition.Right;
        public double Width { get; set; } = 144;
        public double MarginTop { get; set; } = 5;
        public double MarginBottom { get; set; } = 5;
        public double MarginSide { get; set; } = 5;

        /// <summary>
        /// 失去焦点后自动贴边隐藏
        /// </summary>
        public bool AutoHideOnFocusLost { get; set; } = true;

        /// <summary>
        /// 失去焦点后延迟多少毫秒才开始隐藏（避免误触发）
        /// </summary>
        public int AutoHideDelayMs { get; set; } = 600;

        /// <summary>
        /// 隐藏后仍然露在屏幕内、用于鼠标感应的像素宽度
        /// </summary>
        public double AutoHidePeekPx { get; set; } = 3;

        /// <summary>
        /// 边缘感应区顶部留白（占屏幕高度比例，0~0.5）。
        /// 用于避开窗口右上角的关闭/最小化按钮，避免唤回时把按钮挡住。
        /// </summary>
        public double AutoHideEdgeTopRatio { get; set; } = 0.25;

        /// <summary>
        /// 边缘感应区底部留白（占屏幕高度比例，0~0.5）。
        /// 用于避开任务栏托盘区域。
        /// </summary>
        public double AutoHideEdgeBottomRatio { get; set; } = 0.15;

        /// <summary>
        /// 收起方式：true = 收缩为悬浮球（点击/悬停展开）；
        /// false = 传统贴边滑出（仅留 Peek 像素）
        /// </summary>
        public bool UseFloatingBall { get; set; } = true;

        /// <summary>
        /// 悬浮球的垂直中心位置（屏幕坐标，WPF 逻辑像素）。
        /// NaN 表示尚未定位，此时按边缘感应区中点自动计算。
        /// </summary>
        public double FloatingBallY { get; set; } = double.NaN;

        /// <summary>
        /// 悬浮球直径（像素）。实际渲染时窗口比球大 4px，用于容纳投影与悬停光环。
        /// </summary>
        public double FloatingBallSize { get; set; } = 40;

        /// <summary>
        /// Validates that all settings are within reasonable bounds
        /// </summary>
        public bool IsValid()
        {
            return Width >= 100 && Width <= 200 &&
                   MarginTop >= 0 && MarginTop <= 50 &&
                   MarginBottom >= 0 && MarginBottom <= 50 &&
                   MarginSide >= 0 && MarginSide <= 50 &&
                   AutoHideDelayMs >= 0 && AutoHideDelayMs <= 3000 &&
                   AutoHidePeekPx >= 0 && AutoHidePeekPx <= 20 &&
                   AutoHideEdgeTopRatio >= 0 && AutoHideEdgeTopRatio <= 0.5 &&
                   AutoHideEdgeBottomRatio >= 0 && AutoHideEdgeBottomRatio <= 0.5 &&
                   FloatingBallSize >= 28 && FloatingBallSize <= 80;
        }

        /// <summary>
        /// Ensures values are within valid ranges, correcting if necessary
        /// </summary>
        public void Validate()
        {
            Width = Math.Max(100, Math.Min(200, Width));
            MarginTop = Math.Max(0, Math.Min(50, MarginTop));
            MarginBottom = Math.Max(0, Math.Min(50, MarginBottom));
            MarginSide = Math.Max(0, Math.Min(50, MarginSide));
            AutoHideDelayMs = Math.Max(0, Math.Min(3000, AutoHideDelayMs));
            AutoHidePeekPx = Math.Max(0, Math.Min(20, AutoHidePeekPx));
            AutoHideEdgeTopRatio = Math.Max(0, Math.Min(0.5, AutoHideEdgeTopRatio));
            AutoHideEdgeBottomRatio = Math.Max(0, Math.Min(0.5, AutoHideEdgeBottomRatio));
            FloatingBallSize = Math.Max(28, Math.Min(80, FloatingBallSize));

            // 顶+底留白不能把感应区挤没了（至少保留 20% 高度可触发）
            if (AutoHideEdgeTopRatio + AutoHideEdgeBottomRatio > 0.8)
            {
                AutoHideEdgeBottomRatio = Math.Max(0, 0.8 - AutoHideEdgeTopRatio);
            }
        }

        /// <summary>
        /// Checks if the window would fit on screen with current settings
        /// </summary>
        public bool FitsOnScreen()
        {
            var screenHeight = System.Windows.SystemParameters.PrimaryScreenHeight;
            var taskbarHeight = System.Windows.SystemParameters.PrimaryScreenHeight -
                              System.Windows.SystemParameters.WorkArea.Height;
            var requiredHeight = MarginTop + MarginBottom + 100; // Minimum content height
            var availableHeight = screenHeight - taskbarHeight;
            return requiredHeight <= availableHeight;
        }
    }

    public class SystemMonitorSettings
    {
        public int CpuThreshold { get; set; } = 85;        // Default 85%
        public int RamThreshold { get; set; } = 85;        // Default 85%
        public int PingThreshold { get; set; } = 100;      // Default 100ms
        public int UpdateFrequencySeconds { get; set; } = 2; // Default 2 seconds

        /// <summary>
        /// Validation method following existing pattern
        /// </summary>
        public void ValidateAndCorrect()
        {
            // CPU threshold: 50-95%
            if (CpuThreshold < 50) CpuThreshold = 50;
            if (CpuThreshold > 95) CpuThreshold = 95;

            // RAM threshold: 50-95%  
            if (RamThreshold < 50) RamThreshold = 50;
            if (RamThreshold > 95) RamThreshold = 95;

            // Ping threshold: 25-500ms
            if (PingThreshold < 25) PingThreshold = 25;
            if (PingThreshold > 500) PingThreshold = 500;

            // Update frequency: 1-10 seconds
            if (UpdateFrequencySeconds < 1) UpdateFrequencySeconds = 1;
            if (UpdateFrequencySeconds > 10) UpdateFrequencySeconds = 10;
        }
    }

    /// <summary>
    /// Sidebar position options
    /// </summary>
    public enum SidebarPosition
    {
        Right,
        Left
    }
}