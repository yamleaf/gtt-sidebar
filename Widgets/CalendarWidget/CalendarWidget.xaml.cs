using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using gtt_sidebar.Core.Interfaces;
using gtt_sidebar.Core.Managers;

namespace gtt_sidebar.Widgets.CalendarWidget
{
    /// <summary>
    /// 月历卡片：显示当月日历网格（今日高亮、放假/调休日色块标记），
    /// 并在下方显示下一个节假日及其倒计时。
    /// 节假日数据低频刷新（启动一次 + 每 24 小时一次）。
    /// </summary>
    public partial class CalendarWidget : UserControl, IWidget
    {
        private DispatcherTimer _calendarTimer;   // 日历重绘：每小时检查跨天/跨月
        private DispatcherTimer _holidayTimer;    // 节假日低频刷新：每 24 小时

        private HolidayYear _currentYearData;
        private HolidayYear _nextYearData;        // 次年数据：本年度假期用完后继续倒计时
        private DateTime _renderedMonth = DateTime.MinValue;
        private bool _isLoadingHolidays;

        // 配色常量
        private static readonly Brush TextNormal = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
        private static readonly Brush TextMuted = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
        private static readonly Brush TextWeekend = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        private static readonly Brush TodayBackground = new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xE2));
        private static readonly Brush TodayForeground = Brushes.White;
        private static readonly Brush OffDayBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0x53, 0x4F));
        private static readonly Brush WorkdayBrush = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));

        public new string Name => "Calendar";

        public string DisplayName => "日历";

        public string Description => "月历视图，含节假日与倒计时";

        public CalendarWidget()
        {
            InitializeComponent();
        }

        public UserControl GetControl() => this;

        public async Task InitializeAsync()
        {
            BuildWeekHeader();

            // 先用无节假日的状态立即渲染，界面不空白
            RenderCalendar();

            // 每小时检查一次跨天/跨月（若月份变了就重绘）
            _calendarTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
            _calendarTimer.Tick += (s, e) =>
            {
                if (DateTime.Now.Month != _renderedMonth.Month || DateTime.Now.Year != _renderedMonth.Year)
                {
                    RenderCalendar();
                    _ = RefreshHolidaysAsync();
                }
                else
                {
                    RenderCalendar();   // 刷新"今天"高亮
                }
            };
            _calendarTimer.Start();

            // 节假日：启动时拉一次
            await RefreshHolidaysAsync();

            // 之后每 24 小时刷新一次（低频）
            _holidayTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(24) };
            _holidayTimer.Tick += async (s, e) => await RefreshHolidaysAsync();
            _holidayTimer.Start();
        }

        /// <summary>
        /// 拉取并应用节假日数据（带并发保护，避免定时器叠加触发）
        /// </summary>
        private async Task RefreshHolidaysAsync()
        {
            if (_isLoadingHolidays) return;
            _isLoadingHolidays = true;

            try
            {
                var year = DateTime.Now.Year;
                var data = await HolidayService.GetYearAsync(year);

                _currentYearData = data;
                SourceText.Text = data != null && data.Days.Count > 0
                    ? $"节假日数据：{year} 年（{data.FetchedAt:MM-dd HH:mm} 更新）"
                    : "节假日数据暂不可用";

                // 本年度已无剩余假期时，顺带拉取次年数据用于倒计时
                if (HolidayService.FindNextHoliday(data, DateTime.Today) == null)
                {
                    _nextYearData = await HolidayService.GetYearAsync(year + 1);
                }

                await Dispatcher.InvokeAsync(() => RenderCalendar());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CalendarWidget: 刷新节假日失败 - {ex.Message}");
                SourceText.Text = "节假日数据加载失败";
            }
            finally
            {
                _isLoadingHolidays = false;
            }
        }

        /// <summary>
        /// 星期表头：一 二 三 四 五 六 日
        /// </summary>
        private void BuildWeekHeader()
        {
            WeekHeaderGrid.Children.Clear();
            var names = new[] { "一", "二", "三", "四", "五", "六", "日" };

            // 周一为一周起点
            for (int i = 0; i < names.Length; i++)
            {
                var isWeekend = i >= 5;
                WeekHeaderGrid.Children.Add(new TextBlock
                {
                    Text = names[i],
                    FontSize = 7,
                    Foreground = isWeekend ? OffDayBrush : TextMuted,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
        }

        /// <summary>
        /// 渲染当月日历网格
        /// </summary>
        private void RenderCalendar()
        {
            try
            {
                var today = DateTime.Today;
                var firstOfMonth = new DateTime(today.Year, today.Month, 1);

                MonthTitleText.Text = $"{today.Year}年{today.Month}月";
                TodayTagText.Text = "今天";

                DaysGrid.Children.Clear();

                // 计算当月第一天是周几（以周一为 0）
                var offset = ((int)firstOfMonth.DayOfWeek + 6) % 7;
                var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);

                // 上月末尾的补位
                for (int i = 0; i < offset; i++)
                {
                    DaysGrid.Children.Add(CreateEmptyCell());
                }

                // 当月每一天
                for (int day = 1; day <= daysInMonth; day++)
                {
                    var date = new DateTime(today.Year, today.Month, day);
                    var holiday = HolidayService.FindDay(_currentYearData, date);
                    DaysGrid.Children.Add(CreateDayCell(date, today, holiday));
                }

                // 补齐最后一行，保证网格整齐
                var totalCells = offset + daysInMonth;
                var trailing = (7 - totalCells % 7) % 7;
                for (int i = 0; i < trailing; i++)
                {
                    DaysGrid.Children.Add(CreateEmptyCell());
                }

                _renderedMonth = firstOfMonth;

                UpdateNextHoliday();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CalendarWidget: 渲染日历失败 - {ex.Message}");
            }
        }

        private FrameworkElement CreateEmptyCell()
        {
            return new Border { Height = 13 };
        }

        /// <summary>
        /// 单个日期格：今日高亮，放假/调休日加标记
        /// </summary>
        private FrameworkElement CreateDayCell(DateTime date, DateTime today, HolidayDay holiday)
        {
            var isToday = date.Date == today;
            var isWeekend = date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;

            var cell = new Border
            {
                Height = 13,
                CornerRadius = new CornerRadius(2),
                Background = isToday ? TodayBackground : Brushes.Transparent,
                ToolTip = BuildDayTooltip(date, holiday)
            };

            var grid = new Grid();

            // 日期数字
            var dayText = new TextBlock
            {
                Text = date.Day.ToString(),
                FontSize = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (isToday)
            {
                dayText.Foreground = TodayForeground;
                dayText.FontWeight = FontWeights.Bold;
            }
            else if (isWeekend)
            {
                dayText.Foreground = TextWeekend;
            }
            else
            {
                dayText.Foreground = TextNormal;
            }

            grid.Children.Add(dayText);

            // 节假日角标：左上角小圆点（放假=红，调休=灰）
            if (holiday != null)
            {
                var dot = new System.Windows.Shapes.Ellipse
                {
                    Width = 3,
                    Height = 3,
                    Fill = holiday.IsOffDay ? OffDayBrush : WorkdayBrush,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(1, 1, 0, 0)
                };
                grid.Children.Add(dot);

                // 非今日的放假日，数字用红色强调
                if (!isToday && holiday.IsOffDay)
                {
                    dayText.Foreground = OffDayBrush;
                }
            }

            cell.Child = grid;
            return cell;
        }

        private string BuildDayTooltip(DateTime date, HolidayDay holiday)
        {
            if (holiday == null)
            {
                return $"{date:yyyy-MM-dd}";
            }

            return holiday.IsOffDay
                ? $"{date:yyyy-MM-dd}\n{holiday.Name}（放假）"
                : $"{date:yyyy-MM-dd}\n{holiday.Name}（调休上班）";
        }

        /// <summary>
        /// 显示下一个节假日及倒计时（本年度没有剩余假期时，尝试次年数据）
        /// </summary>
        private void UpdateNextHoliday()
        {
            if (_currentYearData == null || _currentYearData.Days.Count == 0)
            {
                NextHolidayText.Text = "节假日数据加载中…";
                CountdownText.Text = "";
                return;
            }

            var today = DateTime.Today;
            var next = HolidayService.FindNextHoliday(_currentYearData, today);

            // 本年度已无剩余假期（如 10 月中旬之后），用次年的数据继续倒计时
            if (next == null && _nextYearData != null)
            {
                next = HolidayService.FindNextHoliday(_nextYearData, today);
            }

            if (next == null)
            {
                NextHolidayText.Text = "暂无更多假期";
                CountdownText.Text = "";
                return;
            }

            var daysLeft = (next.Date - today).Days;

            NextHolidayText.Text = $"{next.Name} {next.Date:M月d日}";

            if (daysLeft == 0)
            {
                CountdownText.Text = "就是今天";
            }
            else if (daysLeft == 1)
            {
                CountdownText.Text = "明天";
            }
            else
            {
                CountdownText.Text = $"{daysLeft} 天后";
            }
        }

        public void Dispose()
        {
            if (_calendarTimer != null)
            {
                _calendarTimer.Stop();
                _calendarTimer = null;
            }

            if (_holidayTimer != null)
            {
                _holidayTimer.Stop();
                _holidayTimer = null;
            }
        }
    }
}
