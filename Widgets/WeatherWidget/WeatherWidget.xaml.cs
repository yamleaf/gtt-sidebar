using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Newtonsoft.Json.Linq;
using gtt_sidebar.Core.Interfaces;
using gtt_sidebar.Core.Managers;

namespace gtt_sidebar.Widgets.WeatherWidget
{
    /// <summary>
    /// 天气组件：数据源为 Open-Meteo（完全免费、无需 API Key、支持中文城市名）。
    /// 先通过地理编码接口把城市名解析为经纬度，再查询当前天气与 3 天预报。
    /// </summary>
    public partial class WeatherWidget : UserControl, IWidget, ITimerSubscriber
    {
        // 默认城市（可用中文或英文，地理编码接口均支持）
        private string _currentCity = "杭州";
        private double? _latitude;
        private double? _longitude;

        private int _retryCount = 0;
        private const int MAX_IMMEDIATE_RETRIES = 3;

        public new string Name => "Weather";

        public string DisplayName => "天气";

        public string Description => "实时天气与三日预报";
        public TimeSpan UpdateInterval => TimeSpan.FromMinutes(30);

        public WeatherWidget()
        {
            InitializeComponent();
        }

        public UserControl GetControl() => this;

        public async Task InitializeAsync()
        {
            SharedResourceManager.Instance.Subscribe(this);

            LocationText.Text = _currentCity;

            await OnTimerTickAsync();
        }

        public async Task OnTimerTickAsync()
        {
            await UpdateWeatherAsync();
        }

        private async Task UpdateWeatherAsync()
        {
            try
            {
                await EnsureCoordinatesAsync();

                if (!_latitude.HasValue || !_longitude.HasValue)
                {
                    SetErrorState("未找到该城市");
                    return;
                }

                var lat = _latitude.Value.ToString("F4", CultureInfo.InvariantCulture);
                var lon = _longitude.Value.ToString("F4", CultureInfo.InvariantCulture);

                // 当前天气 + 4 天预报（第一天用于对齐"今天"）
                var url = "https://api.open-meteo.com/v1/forecast" +
                          $"?latitude={lat}&longitude={lon}" +
                          "&current=temperature_2m,weather_code" +
                          "&daily=weather_code,temperature_2m_max,temperature_2m_min" +
                          "&timezone=auto&forecast_days=4";

                var response = await SharedResourceManager.Instance.HttpClient.GetStringAsync(url);
                var data = JObject.Parse(response);

                UpdateCurrentWeather(data);
                UpdateForecast(data);

                _retryCount = 0;
                System.Diagnostics.Debug.WriteLine("Weather updated successfully (Open-Meteo)");
            }
            catch (Exception ex)
            {
                _retryCount++;
                System.Diagnostics.Debug.WriteLine($"Weather update error (attempt {_retryCount}): {ex.Message}");

                SetErrorState(_retryCount <= MAX_IMMEDIATE_RETRIES
                    ? $"加载失败（第 {_retryCount} 次重试）"
                    : "网络连接异常");
            }
        }

        /// <summary>
        /// 通过地理编码接口把城市名解析为经纬度（中文名同样支持）
        /// </summary>
        private async Task EnsureCoordinatesAsync()
        {
            if (_latitude.HasValue && _longitude.HasValue) return;

            var url = "https://geocoding-api.open-meteo.com/v1/search" +
                      $"?name={Uri.EscapeDataString(_currentCity)}&count=1&language=zh&format=json";

            var response = await SharedResourceManager.Instance.HttpClient.GetStringAsync(url);
            var data = JObject.Parse(response);

            var results = data["results"] as JArray;
            if (results == null || results.Count == 0)
            {
                _latitude = null;
                _longitude = null;
                return;
            }

            var first = results[0];
            _latitude = (double)first["latitude"];
            _longitude = (double)first["longitude"];

            // 用接口返回的规范中文名刷新显示（例如输入 "hangzhou" 显示 "杭州"）
            var name = (string)first["name"];
            if (!string.IsNullOrWhiteSpace(name))
            {
                LocationText.Text = name;
            }

            System.Diagnostics.Debug.WriteLine($"Geocoded {_currentCity} → {_latitude}, {_longitude} ({name})");
        }

        private void UpdateCurrentWeather(JObject data)
        {
            var current = data["current"];
            if (current == null) return;

            var temp = Math.Round((double)current["temperature_2m"]);
            var code = (int)current["weather_code"];

            Temperature.Text = $"{temp}°C";
            WeatherDescription.Text = GetWeatherDescription(code);
            WeatherIcon.Text = GetWeatherIcon(code);
        }

        /// <summary>
        /// 3 天预报：跳过索引 0（今天），取接下来三天
        /// </summary>
        private void UpdateForecast(JObject data)
        {
            var daily = data["daily"];
            if (daily == null) return;

            var codes = daily["weather_code"] as JArray;
            var maxTemps = daily["temperature_2m_max"] as JArray;
            var minTemps = daily["temperature_2m_min"] as JArray;

            if (codes == null || maxTemps == null || minTemps == null) return;

            var icons = new TextBlock[] { Day1Icon, Day2Icon, Day3Icon };
            var labels = new TextBlock[] { Day1Temp, Day2Temp, Day3Temp };

            // 从第 1 项开始（第 0 项是今天）
            for (int i = 0; i < 3; i++)
            {
                var idx = i + 1;
                if (idx >= codes.Count)
                {
                    icons[i].Text = "?";
                    labels[i].Text = "--°";
                    continue;
                }

                var code = (int)codes[idx];
                var max = Math.Round((double)maxTemps[idx]);
                var min = Math.Round((double)minTemps[idx]);

                icons[i].Text = GetWeatherIcon(code);
                labels[i].Text = $"{max}°/{min}°";
            }
        }

        private void SetErrorState(string message)
        {
            WeatherDescription.Text = message;
            Temperature.Text = "--°C";

            Day1Icon.Text = "?";
            Day1Temp.Text = "--°";
            Day2Icon.Text = "?";
            Day2Temp.Text = "--°";
            Day3Icon.Text = "?";
            Day3Temp.Text = "--°";
        }

        /// <summary>
        /// WMO 天气代码 → 中文描述
        /// </summary>
        private string GetWeatherDescription(int code)
        {
            if (code == 0) return "晴";
            if (code == 1) return "晴间多云";
            if (code == 2) return "多云";
            if (code == 3) return "阴";
            if (code == 45 || code == 48) return "雾";
            if (code >= 51 && code <= 57) return "毛毛雨";
            if (code >= 61 && code <= 67) return "雨";
            if (code >= 71 && code <= 77) return "雪";
            if (code >= 80 && code <= 82) return "阵雨";
            if (code >= 85 && code <= 86) return "阵雪";
            if (code >= 95 && code <= 99) return "雷暴";
            return "未知";
        }

        /// <summary>
        /// WMO 天气代码 → 图标
        /// </summary>
        private string GetWeatherIcon(int code)
        {
            // 晴 / 晴间多云 / 多云 / 阴
            if (code == 0) return "☀️";
            if (code == 1) return "🌤️";
            if (code == 2) return "⛅";
            if (code == 3) return "☁️";

            // 雾
            if (code == 45 || code == 48) return "🌫️";

            // 毛毛雨
            if (code >= 51 && code <= 57) return "🌦️";

            // 雨（含冻雨）
            if (code >= 61 && code <= 67) return "🌧️";

            // 雪
            if (code >= 71 && code <= 77) return "❄️";

            // 阵雨
            if (code >= 80 && code <= 82) return "🌧️";

            // 阵雪
            if (code >= 85 && code <= 86) return "❄️";

            // 雷暴（含冰雹）
            if (code >= 95 && code <= 99) return "⛈️";

            return "🌡️";
        }

        private void LocationText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var newCity = Microsoft.VisualBasic.Interaction.InputBox(
                "请输入城市名称（支持中文，例如：杭州、上海、北京）：",
                "更改定位",
                _currentCity);

            if (!string.IsNullOrWhiteSpace(newCity))
            {
                _currentCity = newCity.Trim();

                // 城市变了，需要重新解析经纬度
                _latitude = null;
                _longitude = null;
                _retryCount = 0;

                LocationText.Text = _currentCity;
                _ = UpdateWeatherAsync();
            }
        }

        public void Dispose()
        {
            SharedResourceManager.Instance.Unsubscribe(this);
        }
    }
}
