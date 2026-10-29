using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using gtt_sidebar.Core.Interfaces;
using gtt_sidebar.Core.Managers;
using gtt_sidebar.Core.Settings;

namespace gtt_sidebar.Widgets.SystemInfo
{
    /// <summary>
    /// 系统信息卡片：实时负载（CPU / 内存 / 延迟）+ 本机信息（运行时长 / 磁盘 / 网络 / 进程数）。
    ///
    /// 其中 CPU / 内存 / 延迟 原属独立的「系统监控」组件，现合并到本卡片：
    /// - 告警阈值与刷新频率沿用设置窗口「系统监控」区的配置
    /// - 数值超过阈值时标红
    /// - 悬停可查看当前值 / 5 分钟均值 / 1 小时均值
    /// </summary>
    public partial class SystemInfoWidget : UserControl, IWidget
    {
        private DispatcherTimer _timer;

        // ---------- 实时负载（原系统监控逻辑）----------
        private PerformanceCounter _cpuCounter;
        private PerformanceCounter _ramCounter;
        private SystemMonitorSettings _settings;

        private double _currentCpu = 0.0;
        private double _currentRam = 0.0;
        private double _currentPing = 0.0;
        private double _totalRamGB = 0.0;

        // 历史数据（用于均值）
        private readonly Queue<double> _cpuHistory = new Queue<double>();
        private readonly Queue<double> _ramHistory = new Queue<double>();
        private readonly Queue<double> _pingHistory = new Queue<double>();

        private const int FIVE_MINUTE_SAMPLES = 150;  // 5 分钟 ÷ 2 秒 = 150 个样本
        private const int ONE_HOUR_SAMPLES = 1800;    // 60 分钟 ÷ 2 秒 = 1800 个样本

        private static readonly Brush NormalBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
        private static readonly Brush AlertBrush = Brushes.Red;
        private static readonly Brush DisabledBrush = Brushes.Gray;

        public new string Name => "System Info";

        public string DisplayName => "系统信息";

        public string Description => "CPU、内存、延迟、磁盘与网络状态";

        public SystemInfoWidget()
        {
            InitializeComponent();
        }

        public UserControl GetControl() => this;

        public async Task InitializeAsync()
        {
            LoadSettings();
            SharedResourceManager.Instance.SettingsChanged += OnSettingsChanged;

            InitializePerformanceCounters();

            // 实时负载需要更快的刷新节奏，本机信息跟着一起更新（开销都很小）
            StartTimer();

            Refresh();
            await Task.CompletedTask;
        }

        private void InitializePerformanceCounters()
        {
            // CPU 计数器
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue();   // 首次调用返回 0，先初始化
                System.Diagnostics.Debug.WriteLine("SystemInfo: CPU 计数器初始化成功");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SystemInfo: CPU 计数器初始化失败 - {ex.Message}");
                _cpuCounter = null;
            }

            // 内存计数器
            try
            {
                _ramCounter = new PerformanceCounter("Memory", "Available MBytes");
                var computerInfo = new Microsoft.VisualBasic.Devices.ComputerInfo();
                _totalRamGB = computerInfo.TotalPhysicalMemory / (1024.0 * 1024.0 * 1024.0);
                System.Diagnostics.Debug.WriteLine($"SystemInfo: 内存计数器初始化成功，总内存 {_totalRamGB:F1} GB");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SystemInfo: 内存计数器初始化失败 - {ex.Message}");
                _ramCounter = null;
            }
        }

        private void StartTimer()
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= Timer_Tick;
                _timer = null;
            }

            var seconds = _settings?.UpdateFrequencySeconds ?? 2;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void OnSettingsChanged(SettingsData newSettings)
        {
            _settings = newSettings.SystemMonitor;
            StartTimer();
            Refresh();
        }

        private void LoadSettings()
        {
            try
            {
                _settings = SharedResourceManager.Instance.Settings.SystemMonitor;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SystemInfo: 读取设置失败，使用默认值 - {ex.Message}");
                _settings = new SystemMonitorSettings();
            }
        }

        private async void Timer_Tick(object sender, EventArgs e)
        {
            // 实时负载
            ReadCpu();
            ReadRam();
            await ReadPingAsync();
            StoreHistory();

            Refresh();
        }

        // ---------- 刷新界面 ----------

        private void Refresh()
        {
            try
            {
                // 实时负载
                UpdateLoadDisplay();

                // 本机信息（相对静态，但一起刷新简单可靠）
                UptimeValue.Text = GetUptime();
                DiskValue.Text = GetDiskFree();
                NetworkValue.Text = GetNetworkStatus();
                ProcessValue.Text = GetProcessCount();

                UpdateTimeText.Text = DateTime.Now.ToString("HH:mm");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SystemInfo: 刷新失败 - {ex.Message}");
            }
        }

        private void UpdateLoadDisplay()
        {
            // CPU
            CpuValue.Text = _currentCpu >= 0 ? $"{_currentCpu:F0}%" : "不可用";
            CpuValue.Foreground = _currentCpu < 0
                ? DisabledBrush
                : (_currentCpu > _settings.CpuThreshold ? AlertBrush : NormalBrush);
            CpuValue.ToolTip = _currentCpu >= 0
                ? BuildAverageTooltip("CPU", _currentCpu, "%", _cpuHistory)
                : "CPU 监控不可用";

            // 内存
            RamValue.Text = _currentRam >= 0 ? $"{_currentRam:F0}%" : "不可用";
            RamValue.Foreground = _currentRam < 0
                ? DisabledBrush
                : (_currentRam > _settings.RamThreshold ? AlertBrush : NormalBrush);
            RamValue.ToolTip = _currentRam >= 0
                ? BuildAverageTooltip("内存", _currentRam, "%", _ramHistory)
                : "内存监控不可用";

            // 延迟
            PingValue.Text = _currentPing >= 0 ? $"{_currentPing:F0}ms" : "不可用";
            PingValue.Foreground = _currentPing < 0
                ? DisabledBrush
                : (_currentPing > _settings.PingThreshold ? AlertBrush : NormalBrush);
            PingValue.ToolTip = _currentPing >= 0
                ? BuildAverageTooltip("延迟", _currentPing, "ms", _pingHistory)
                : "网络延迟不可用";
        }

        private string BuildAverageTooltip(string label, double current, string unit, Queue<double> history)
        {
            var avg5 = CalculateAverage(history, FIVE_MINUTE_SAMPLES);
            var avg1h = CalculateAverage(history, ONE_HOUR_SAMPLES);

            var text = $"当前：{current:F1}{unit}";

            text += avg5 >= 0
                ? $"\n5 分钟均值：{avg5:F1}{unit}"
                : "\n5 分钟均值：（正在采集数据…）";

            text += avg1h >= 0
                ? $"\n1 小时均值：{avg1h:F1}{unit}"
                : "\n1 小时均值：（正在采集数据…）";

            return text;
        }

        // ---------- 实时负载数据读取 ----------

        private void ReadCpu()
        {
            try
            {
                _currentCpu = _cpuCounter != null ? _cpuCounter.NextValue() : -1;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SystemInfo: 读取 CPU 失败 - {ex.Message}");
                _currentCpu = -1;
            }
        }

        private void ReadRam()
        {
            try
            {
                if (_ramCounter != null && _totalRamGB > 0)
                {
                    var availableRamMB = _ramCounter.NextValue();
                    var usedRamGB = _totalRamGB - (availableRamMB / 1024.0);
                    _currentRam = (usedRamGB / _totalRamGB) * 100.0;
                }
                else
                {
                    _currentRam = -1;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SystemInfo: 读取内存失败 - {ex.Message}");
                _currentRam = -1;
            }
        }

        private async Task ReadPingAsync()
        {
            try
            {
                using (var ping = new Ping())
                {
                    var reply = await ping.SendPingAsync("8.8.8.8", 1000);

                    if (reply.Status == IPStatus.Success)
                    {
                        _currentPing = reply.RoundtripTime;
                    }
                    else
                    {
                        _currentPing = -1;
                        System.Diagnostics.Debug.WriteLine($"SystemInfo: Ping 失败 - {reply.Status}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SystemInfo: Ping 异常 - {ex.Message}");
                _currentPing = -1;
            }
        }

        private void StoreHistory()
        {
            Store(_cpuHistory, _currentCpu);
            Store(_ramHistory, _currentRam);
            Store(_pingHistory, _currentPing);
        }

        private void Store(Queue<double> history, double value)
        {
            if (value < 0) return;   // 只记录有效读数

            history.Enqueue(value);
            while (history.Count > ONE_HOUR_SAMPLES)
            {
                history.Dequeue();
            }
        }

        private double CalculateAverage(Queue<double> data, int sampleCount)
        {
            if (data.Count == 0) return -1;

            var samplesToUse = Math.Min(sampleCount, data.Count);
            return data.Skip(data.Count - samplesToUse).Average();
        }

        // ---------- 本机信息 ----------

        /// <summary>
        /// 系统运行时长，输出如 "3天 5小时" / "5小时 12分"
        /// </summary>
        private string GetUptime()
        {
            // .NET Framework 4.7.2 没有 Environment.TickCount64，
            // 用 GetTickCount64 取 64 位开机时长，避免 TickCount 的 49 天溢出问题
            var uptime = TimeSpan.FromMilliseconds(GetTickCount64());

            if (uptime.TotalDays >= 1)
            {
                return $"{(int)uptime.TotalDays}天 {uptime.Hours}小时";
            }
            if (uptime.TotalHours >= 1)
            {
                return $"{uptime.Hours}小时 {uptime.Minutes}分";
            }
            return $"{uptime.Minutes}分 {uptime.Seconds}秒";
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern ulong GetTickCount64();

        /// <summary>
        /// 系统盘（C:）剩余空间，输出如 "128 / 476 GB"
        /// </summary>
        private string GetDiskFree()
        {
            try
            {
                var drive = System.IO.DriveInfo.GetDrives()
                    .FirstOrDefault(d => d.IsReady && d.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase));

                if (drive == null) return "--";

                var freeGb = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
                var totalGb = drive.TotalSize / 1024.0 / 1024.0 / 1024.0;

                return $"{freeGb:F0} / {totalGb:F0} GB";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SystemInfo: 读取磁盘失败 - {ex.Message}");
                return "--";
            }
        }

        /// <summary>
        /// 网络状态：优先显示已连接的物理网卡类型与速率，否则显示"未连接"
        /// </summary>
        private string GetNetworkStatus()
        {
            try
            {
                var active = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up)
                    .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    // 排除虚拟网卡
                    .Where(n => !n.Description.ToLowerInvariant().Contains("virtual"))
                    .Where(n => !n.Description.ToLowerInvariant().Contains("vmware"))
                    .Where(n => !n.Description.ToLowerInvariant().Contains("hyper-v"))
                    .OrderByDescending(n => n.Speed)
                    .FirstOrDefault();

                if (active == null) return "未连接";

                var speedMbps = active.Speed > 0 ? active.Speed / 1_000_000.0 : 0;

                var isWireless = active.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                var kind = isWireless ? "无线" : "有线";

                return speedMbps >= 1000
                    ? $"{kind} {speedMbps / 1000:F0}Gbps"
                    : $"{kind} {speedMbps:F0}Mbps";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SystemInfo: 读取网络状态失败 - {ex.Message}");
                return "--";
            }
        }

        /// <summary>
        /// 当前进程数
        /// </summary>
        private string GetProcessCount()
        {
            try
            {
                return $"{Process.GetProcesses().Length} 个";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SystemInfo: 读取进程数失败 - {ex.Message}");
                return "--";
            }
        }

        public void Dispose()
        {
            SharedResourceManager.Instance.SettingsChanged -= OnSettingsChanged;

            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= Timer_Tick;
                _timer = null;
            }

            if (_cpuCounter != null)
            {
                _cpuCounter.Dispose();
                _cpuCounter = null;
            }

            if (_ramCounter != null)
            {
                _ramCounter.Dispose();
                _ramCounter = null;
            }
        }
    }
}
