using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Common
{
    public static class DateTimeMisc
    {
        // ---------- NEW METHOD START ----------
        /// <summary>
        /// 日期时间字符串解析为DateTime<br/>
        /// 标准解析格式如下:<br/>
        /// "yyyy-MM-dd" : "2022-01-01"<br/>
        /// "yyyy-MM-dd HH:mm:ss" : "2022-01-01 23:59:59"<br/>
        /// "yyyy-MM-dd HH:mm:ss.FFF" : "2022-01-01 23:59:59.999"<br/>
        /// "yyyyMMdd" : "20220101"<br/>
        /// "yyyyMMddHHmmss" : "20220101235959"<br/>
        /// "yyyyMMddHHmmssFFF" : "20220101235959999"<br/>
        /// 格式化字符串见：<seealso cref="FormatDateTime(DateTime, bool)"/>
        /// </summary>
        /// <param name="dateTimeString">待解析的日期时间字符串</param>
        /// <returns>解析的日期时间</returns>
        public static DateTime ParseDateTime(this string dateTimeString)
        {
            // 常用转换, 例如 2021-01-01 / 2021-01-01 01:02:03.456
            if (DateTime.TryParse(dateTimeString, out DateTime dateTime))
                return dateTime;

            // 常用转换, 例如 20210101 20050104095500000
            string[] formats = { "yyyyMMdd", "yyyyMMddHHmmssFFF" };
            var provider = CultureInfo.InvariantCulture;
            if (DateTime.TryParseExact(dateTimeString, formats, provider, DateTimeStyles.AllowWhiteSpaces, out dateTime))
                return dateTime;

            throw new ArgumentException("该字符串未被识别为有效的 DateTime");
        }

        /// <summary>
        /// DateTime格式化为字符串<br/>
        /// 格式化格式如下:<br/>
        /// <c>isDisplayTime = true</c> : "yyyy-MM-dd HH:mm:ss" : "2022-01-01 23:59:59"<br/>
        /// <c>isDisplayTime = false</c> : "yyyy-MM-dd" : "2022-01-01"<br/>
        /// 解析字符串见：<seealso cref="ParseDateTime(string)"/>
        /// </summary>
        /// <param name="dateTime">待格式化的日期时间</param>
        /// <param name="isDisplayTime">是否格式化时间</param>
        /// <returns>格式化的日期与时间字符串</returns>
        public static string FormatDateTime(this DateTime dateTime, bool isDisplayTime = true)
        {
            return dateTime.ToString(isDisplayTime ? "yyyy-MM-dd HH:mm:ss" : "yyyy-MM-dd");
        }

        /// <summary>
        /// 二分法搜索<see cref="DateTime"/>数组内所在或向前最近的索引号<br/>
        /// 数组内存储的时间必须为正序排列
        /// </summary>
        /// <param name="dateTimes">DateTime数组, 数组内存储的时间必须为正序排列</param>
        /// <param name="dateTime">查找的时间</param>
        /// <param name="isExist">输出是否存在此DateTime</param>
        /// <returns>数组内所在或向前最近的索引号</returns>
        public static int GetTimeIndex(this DateTime[] dateTimes, in DateTime dateTime, out bool isExist)
        {
            int index = Array.BinarySearch(dateTimes, dateTime);
            if (index >= 0)
            {
                // 匹配精确日期
                isExist = true;
                return index;
            }
            else
            {
                isExist = false;
                // 未匹配精确日期, 返回大于value的第一个元素, 若为最大则返回最大索引
                //return (~index == dateTimes.Length) ? (~index - 1) : (~index);
                // 未匹配精确日期, 返回小于value的第一个元素, 若为最小则返回最小索引
                return (~index == 0) ? (~index) : (~index - 1);
            }
        }

        /// <summary>
        /// 二分法搜索<see cref="DateTime"/>数组内所在或向前最近的时间<br/>
        /// 数组内存储的时间必须为正序排列
        /// </summary>
        /// <param name="dateTimes">DateTime数组, 数组内存储的时间必须为正序排列</param>
        /// <param name="dateTime">查找的时间</param>
        /// <returns>数组内所在或向前最近的时间</returns>
        public static ref DateTime GetTimeNearby(this DateTime[] dateTimes, in DateTime dateTime)
        {
            return ref dateTimes[dateTimes.GetTimeIndex(dateTime, out _)];
        }

        // ---------- NEW METHOD END ----------

        // ---------- TO BE MODIFY START ----------
        #region TO BE MODIFY START

        /// <summary>
        /// 获取时间对应的索引号, 若没有匹配到精确日期, 则向前匹配最近的日期, ShareData中存储的时间必须为正序排列.
        /// </summary>
        /// <param name="dateTimes"></param>
        /// <param name="dateTime"></param>
        /// <returns>返回精确或模糊索引与精确/模糊标志位</returns>
        public static (int index, bool isPrecise) GetTimeIndex(DateTime[] dateTimes, DateTime dateTime)
        {
            int index = Array.BinarySearch(dateTimes, dateTime);
            if (index >= 0)
                // 匹配精确日期
                return (index, true);
            else
            {
                // 未匹配精确日期, 返回大于value的第一个元素, 若为最大则返回最大索引
                //return (~index == dateTimes.Length) ? (~index - 1) : (~index);
                // 未匹配精确日期, 返回小于value的第一个元素, 若为最小则返回最小索引
                return ((~index == 0) ? (~index) : (~index - 1), false);
            }
        }

        /// <summary>
        /// 获取start-end(包括start和end)的日期跨度
        /// 例如start: 2022-1-20, end:2022-3-10, span:Month
        /// 返回: [2022-1-1, 2022-2-1, 2022-3-1]
        /// </summary>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <param name="span"></param>
        /// <returns></returns>
        public static DateTime[] GetDateTimeSpan(DateTime start, DateTime end, DateSpan span)
        {
            List<DateTime> dateTimes = new();
            start = span switch
            {
                DateSpan.Day => start.ToString("yyyy-MM-dd").ParseDateTime(),
                DateSpan.Month => start.ToString("yyyy-MM-01").ParseDateTime(),
                DateSpan.Year => start.ToString("yyyy-01-01").ParseDateTime(),
                _ => throw new ArgumentOutOfRangeException(),
            };
            while (start <= end)
            {
                dateTimes.Add(start);

                start = span switch
                {
                    DateSpan.Day => start.AddDays(1),
                    DateSpan.Month => start.AddMonths(1),
                    DateSpan.Year => start.AddYears(1),
                    _ => throw new ArgumentOutOfRangeException(),
                };
            }
            return dateTimes.ToArray();
        }

        #endregion
        // ---------- TO BE MODIFY END ----------
    }
}
