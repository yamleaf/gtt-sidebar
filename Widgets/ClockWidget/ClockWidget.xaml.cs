using System;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;
using gtt_sidebar.Core.Interfaces;

namespace gtt_sidebar.Widgets.ClockWidget
{
    public partial class ClockWidget : UserControl, IWidget
    {
        private DispatcherTimer _timer;

        public new string Name => "Clock";  // Add 'new' keyword

        public string DisplayName => "时钟";

        public string Description => "显示当前时间与日期";
        public ClockWidget()
        {
            InitializeComponent();
        }

        public UserControl GetControl() => this;

        public Task InitializeAsync()
        {
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _timer.Start();

            UpdateDisplay(); // Update immediately
            return Task.CompletedTask;
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            var now = DateTime.Now;
            TimeDisplay.Text = now.ToString("HH:mm:ss");

            // 中文日期：2025年1月1日 星期三
            var weekNames = new[] { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };
            DateDisplay.Text = $"{now:yyyy年M月d日} {weekNames[(int)now.DayOfWeek]}";
        }

        public void Dispose()
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= Timer_Tick;
                _timer = null;
            }
        }
    }
}