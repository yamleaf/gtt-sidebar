using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using gtt_sidebar.Core.Managers;

namespace gtt_sidebar.Widgets.CalendarWidget
{
    /// <summary>
    /// 单日节假日信息
    /// </summary>
    public class HolidayDay
    {
        /// <summary>日期（仅日期部分）</summary>
        public DateTime Date { get; set; }

        /// <summary>节日/调休名称，如"春节"、"国庆后补班"</summary>
        public string Name { get; set; }

        /// <summary>true=放假日，false=调休上班日</summary>
        public bool IsOffDay { get; set; }
    }

    /// <summary>
    /// 某一年份的节假日数据
    /// </summary>
    public class HolidayYear
    {
        public int Year { get; set; }
        public List<HolidayDay> Days { get; set; } = new List<HolidayDay>();

        /// <summary>本次数据的抓取时间（用于判断缓存是否过期）</summary>
        public DateTime FetchedAt { get; set; }
    }

    /// <summary>
    /// 节假日数据服务：负责拉取、缓存并提供查询。
    ///
    /// 数据源为「低频率请求」场景：
    /// - 应用启动时拉取一次（本地缓存优先，缺失或过期才走网络）
    /// - 之后每 24 小时刷新一次
    /// - 多级降级：jsdelivr(主) → fastly.jsdelivr(备) → timor.tech(三) → 本地缓存
    /// </summary>
    public static class HolidayService
    {
        private static readonly string _appDataPath = gtt_sidebar.Core.AppPaths.AppDataPath;

        private static readonly string _cacheFilePath = Path.Combine(_appDataPath, "holidays.json");

        /// <summary>缓存有效期：24 小时</summary>
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);

        // 按年份缓存已加载的数据，避免重复解析
        private static readonly Dictionary<int, HolidayYear> _memoryCache = new Dictionary<int, HolidayYear>();
        private static readonly object _lock = new object();

        /// <summary>
        /// 数据源列表（按优先级）。均为无版权限制的公开数据。
        /// holiday-cn 数据由官方放假通知整理，附 gov.cn 出处。
        /// </summary>
        private static readonly string[] _sourceUrls =
        {
            "https://cdn.jsdelivr.net/gh/NateScarlet/holiday-cn@master/{0}.json",
            "https://fastly.jsdelivr.net/gh/NateScarlet/holiday-cn@master/{0}.json",
            "https://timor.tech/api/holiday/year/{0}"
        };

        /// <summary>
        /// 获取指定年份的节假日数据（内存缓存 → 本地文件缓存 → 网络）
        /// </summary>
        public static async Task<HolidayYear> GetYearAsync(int year)
        {
            lock (_lock)
            {
                HolidayYear cached;
                if (_memoryCache.TryGetValue(year, out cached))
                {
                    // 内存缓存且未过期
                    if (DateTime.Now - cached.FetchedAt < CacheLifetime && cached.Days.Count > 0)
                    {
                        return cached;
                    }
                }
            }

            // 尝试本地文件缓存
            var fileCached = LoadFromFile(year);
            if (fileCached != null &&
                DateTime.Now - fileCached.FetchedAt < CacheLifetime &&
                fileCached.Days.Count > 0)
            {
                lock (_lock) { _memoryCache[year] = fileCached; }
                return fileCached;
            }

            // 走网络
            var fetched = await FetchFromNetworkAsync(year);
            if (fetched != null && fetched.Days.Count > 0)
            {
                fetched.FetchedAt = DateTime.Now;
                lock (_lock) { _memoryCache[year] = fetched; }
                SaveToFile(year, fetched);
                return fetched;
            }

            // 网络全部失败：退回过期缓存（有总比没有好）
            if (fileCached != null)
            {
                lock (_lock) { _memoryCache[year] = fileCached; }
                return fileCached;
            }

            // 彻底没有数据
            return new HolidayYear { Year = year, FetchedAt = DateTime.MinValue };
        }

        /// <summary>
        /// 依次尝试各数据源，任一成功即返回
        /// </summary>
        private static async Task<HolidayYear> FetchFromNetworkAsync(int year)
        {
            foreach (var template in _sourceUrls)
            {
                var url = string.Format(template, year);
                try
                {
                    var json = await SharedResourceManager.Instance.HttpClient.GetStringAsync(url);
                    var parsed = Parse(json, year);

                    if (parsed != null && parsed.Days.Count > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"HolidayService: 数据源成功 {url}（{parsed.Days.Count} 天）");
                        return parsed;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"HolidayService: 数据源失败 {url} - {ex.Message}");
                }
            }

            System.Diagnostics.Debug.WriteLine($"HolidayService: {year} 年所有数据源均失败");
            return null;
        }

        /// <summary>
        /// 兼容两种数据结构：
        /// 1) holiday-cn: { "year":2026, "days":[ {"name":"元旦","date":"2026-01-01","isOffDay":true} ] }
        /// 2) timor.tech: { "code":0, "holiday": { "01-01": {"holiday":true,"name":"元旦","date":"2026-01-01"} } }
        /// </summary>
        private static HolidayYear Parse(string json, int year)
        {
            var root = JObject.Parse(json);
            var result = new HolidayYear { Year = year };

            // 结构 1：holiday-cn
            var days = root["days"] as JArray;
            if (days != null)
            {
                foreach (var item in days)
                {
                    var day = ParseHolidayCnDay(item);
                    if (day != null) result.Days.Add(day);
                }
                return result;
            }

            // 结构 2：timor.tech
            var holiday = root["holiday"] as JObject;
            if (holiday != null)
            {
                foreach (var prop in holiday.Properties())
                {
                    var value = prop.Value;
                    var day = ParseTimorDay(value);
                    if (day != null) result.Days.Add(day);
                }
                return result;
            }

            return null;
        }

        private static HolidayDay ParseHolidayCnDay(JToken item)
        {
            try
            {
                var dateStr = (string)item["date"];
                if (string.IsNullOrWhiteSpace(dateStr)) return null;

                DateTime date;
                if (!DateTime.TryParse(dateStr, out date)) return null;

                return new HolidayDay
                {
                    Date = date.Date,
                    Name = (string)item["name"] ?? "",
                    IsOffDay = item["isOffDay"] != null && (bool)item["isOffDay"]
                };
            }
            catch
            {
                return null;
            }
        }

        private static HolidayDay ParseTimorDay(JToken value)
        {
            try
            {
                var dateStr = (string)value["date"];
                if (string.IsNullOrWhiteSpace(dateStr)) return null;

                DateTime date;
                if (!DateTime.TryParse(dateStr, out date)) return null;

                return new HolidayDay
                {
                    Date = date.Date,
                    Name = (string)value["name"] ?? "",
                    IsOffDay = value["holiday"] != null && (bool)value["holiday"]
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 查询某一天是否为节假日/调休
        /// </summary>
        public static HolidayDay FindDay(HolidayYear year, DateTime date)
        {
            if (year?.Days == null) return null;
            var target = date.Date;
            return year.Days.FirstOrDefault(d => d.Date == target);
        }

        /// <summary>
        /// 找出从指定日期起、下一个放假节日（跳过周末与已过去的日期）
        /// </summary>
        public static HolidayDay FindNextHoliday(HolidayYear year, DateTime from)
        {
            if (year?.Days == null) return null;

            var today = from.Date;
            return year.Days
                .Where(d => d.IsOffDay && d.Date >= today)
                .OrderBy(d => d.Date)
                .FirstOrDefault();
        }

        #region 本地缓存读写

        private static HolidayYear LoadFromFile(int year)
        {
            try
            {
                if (!File.Exists(_cacheFilePath)) return null;

                var json = File.ReadAllText(_cacheFilePath);
                if (string.IsNullOrWhiteSpace(json)) return null;

                var all = JsonConvert.DeserializeObject<List<HolidayYear>>(json);
                return all?.FirstOrDefault(y => y.Year == year);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HolidayService: 读取缓存失败 - {ex.Message}");
                return null;
            }
        }

        private static void SaveToFile(int year, HolidayYear data)
        {
            try
            {
                Directory.CreateDirectory(_appDataPath);

                // 读取已有内容，替换同一年份后写回（保留其它年份的缓存）
                List<HolidayYear> all = null;
                if (File.Exists(_cacheFilePath))
                {
                    try
                    {
                        var json = File.ReadAllText(_cacheFilePath);
                        all = JsonConvert.DeserializeObject<List<HolidayYear>>(json);
                    }
                    catch { /* 缓存损坏则重建 */ }
                }

                if (all == null) all = new List<HolidayYear>();

                all.RemoveAll(y => y.Year == year);
                all.Add(data);

                File.WriteAllText(_cacheFilePath, JsonConvert.SerializeObject(all, Formatting.Indented));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HolidayService: 写入缓存失败 - {ex.Message}");
            }
        }

        #endregion
    }
}
