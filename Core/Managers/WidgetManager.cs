using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using gtt_sidebar.Core.Interfaces;
using gtt_sidebar.Core.Settings;

namespace gtt_sidebar.Core.Managers
{
    /// <summary>
    /// 组件（插件）的描述信息，供设置界面展示与排序使用
    /// </summary>
    public class WidgetDescriptor
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }

        /// <summary>
        /// 创建实例（每次刷新界面时新建，避免复用已 Dispose 的组件）
        /// </summary>
        public Func<IWidget> Factory { get; set; }

        /// <summary>
        /// 内置默认顺序，用于用户从未自定义过排序时的兜底
        /// </summary>
        public int DefaultOrder { get; set; }
    }

    /// <summary>
    /// 全局共享的组件注册表。
    ///
    /// 设置窗口需要展示"有哪些组件可用"，但组件实例归主窗口所有。
    /// 这里只维护**可用组件清单**（Descriptor，含工厂函数），不持有实例，
    /// 因此主窗口反复重载组件不会与设置窗口产生生命周期冲突。
    /// </summary>
    public sealed class WidgetRegistry
    {
        private static readonly Lazy<WidgetRegistry> _instance =
            new Lazy<WidgetRegistry>(() => new WidgetRegistry());

        public static WidgetRegistry Instance => _instance.Value;

        private readonly WidgetManager _manager = new WidgetManager();
        private bool _discovered;

        private WidgetRegistry() { }

        /// <summary>
        /// 可用组件清单（首次访问时扫描程序集）
        /// </summary>
        public List<WidgetDescriptor> AvailableWidgets
        {
            get
            {
                if (!_discovered)
                {
                    _manager.DiscoverAvailableWidgets();
                    _discovered = true;
                }
                return _manager.AvailableWidgets;
            }
        }
    }

    /// <summary>
    /// 组件（插件）发现、装载与生命周期管理。
    ///
    /// 插件化机制：任何实现 <see cref="IWidget"/> 且具备无参构造函数的类
    /// 都会被反射自动发现，成为可用组件。是否显示、显示在第几位
    /// 完全由 <see cref="WidgetSettings"/> 决定，主窗口代码无需改动。
    /// </summary>
    public class WidgetManager
    {
        private List<IWidget> _loadedWidgets = new List<IWidget>();

        /// <summary>
        /// 所有已发现的可用组件（无论启用与否）
        /// </summary>
        public List<WidgetDescriptor> AvailableWidgets { get; private set; } = new List<WidgetDescriptor>();

        /// <summary>
        /// 反射扫描程序集，收集所有可用的组件类型
        /// </summary>
        public void DiscoverAvailableWidgets()
        {
            AvailableWidgets = new List<WidgetDescriptor>();

            try
            {
                var widgetTypes = Assembly.GetExecutingAssembly()
                    .GetTypes()
                    .Where(t => typeof(IWidget).IsAssignableFrom(t) &&
                                !t.IsInterface &&
                                !t.IsAbstract &&
                                t.GetConstructor(Type.EmptyTypes) != null);

                foreach (var widgetType in widgetTypes)
                {
                    try
                    {
                        // 用一个临时实例读取元数据
                        var probe = (IWidget)Activator.CreateInstance(widgetType);

                        AvailableWidgets.Add(new WidgetDescriptor
                        {
                            Name = probe.Name,
                            DisplayName = string.IsNullOrWhiteSpace(probe.DisplayName) ? probe.Name : probe.DisplayName,
                            Description = probe.Description ?? string.Empty,
                            DefaultOrder = GetDefaultOrder(probe.Name),
                            Factory = () => (IWidget)Activator.CreateInstance(widgetType)
                        });

                        // 探测实例不用于显示，立即释放它可能持有的资源
                        try { probe.Dispose(); } catch { }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"Failed to inspect widget {widgetType.Name}: {ex.Message}");
                    }
                }

                AvailableWidgets = AvailableWidgets.OrderBy(w => w.DefaultOrder).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during widget discovery: {ex.Message}");
            }
        }

        /// <summary>
        /// 按配置装载组件：只创建已启用的，并按用户自定义顺序排列。
        /// </summary>
        /// <param name="widgetSettings">用户配置；为 null 时按默认（全启用 + 内置顺序）</param>
        public List<IWidget> LoadWidgets(WidgetSettings widgetSettings)
        {
            if (AvailableWidgets == null || AvailableWidgets.Count == 0)
            {
                DiscoverAvailableWidgets();
            }

            DisposeWidgets();

            // 过滤出启用的组件
            var enabled = AvailableWidgets
                .Where(d => widgetSettings == null || widgetSettings.IsEnabled(d.Name))
                .ToList();

            // 排序：用户排序优先，未设置过的按内置默认顺序
            enabled = enabled
                .OrderBy(d =>
                {
                    var custom = widgetSettings?.GetOrder(d.Name) ?? int.MaxValue;
                    return custom == int.MaxValue ? d.DefaultOrder : custom;
                })
                .ThenBy(d => d.DefaultOrder)
                .ToList();

            foreach (var descriptor in enabled)
            {
                try
                {
                    var widget = descriptor.Factory();
                    _loadedWidgets.Add(widget);
                    System.Diagnostics.Debug.WriteLine($"Loaded widget: {widget.Name}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Failed to load widget {descriptor.Name}: {ex.Message}");
                }
            }

            return _loadedWidgets;
        }

        /// <summary>
        /// 内置默认顺序：决定用户从未调整过顺序时的初始排列
        /// </summary>
        private static int GetDefaultOrder(string widgetName)
        {
            switch (widgetName)
            {
                case "Clock":       return 1;
                case "Calendar":    return 2;   // 日历紧跟时钟，日期信息相邻
                case "Weather":     return 3;
                case "Notes":       return 4;
                case "Shortcuts":   return 5;
                case "System Info": return 7;
                default:            return 50;  // 未知组件排在已知项之后
            }
        }

        /// <summary>
        /// 初始化组件。
        /// 本地组件串行初始化，依赖网络的组件并行初始化，兼顾启动速度与顺序稳定性。
        /// </summary>
        public async Task InitializeWidgetsAsync()
        {
            // 依赖网络的组件（可能较慢），并行处理
            var networkBound = new[] { "Calendar", "Weather", "System Info" };

            var localWidgets = _loadedWidgets
                .Where(w => !networkBound.Contains(w.Name)).ToList();
            var apiWidgets = _loadedWidgets
                .Where(w => networkBound.Contains(w.Name)).ToList();

            foreach (var widget in localWidgets)
            {
                try { await widget.InitializeAsync(); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Init failed for {widget.Name}: {ex.Message}");
                }
            }

            var apiTasks = apiWidgets.Select(async widget =>
            {
                try { await widget.InitializeAsync(); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Init failed for {widget.Name}: {ex.Message}");
                }
            }).ToArray();

            await Task.WhenAll(apiTasks);
        }

        /// <summary>
        /// Disposes all loaded widgets
        /// </summary>
        public void DisposeWidgets()
        {
            foreach (var widget in _loadedWidgets)
            {
                try
                {
                    widget.Dispose();
                    System.Diagnostics.Debug.WriteLine($"Disposed widget: {widget.Name}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error disposing widget {widget.Name}: {ex.Message}");
                }
            }
            _loadedWidgets.Clear();
        }

        /// <summary>
        /// Gets all currently loaded widgets
        /// </summary>
        public List<IWidget> GetLoadedWidgets()
        {
            return new List<IWidget>(_loadedWidgets);
        }
    }
}
