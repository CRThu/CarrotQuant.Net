using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Common
{
    public static class DateTimeMisc
    {
        /// <summary>
        /// 字符串解析为DateTime
        /// </summary>
        /// <param name="dateTimeString"></param>
        /// <returns></returns>
        /// <exception cref="FormatException"></exception>
        public static DateTime Parse(string dateTimeString)
        {
            DateTime dateTime = new();

            // 常用转换, 例如 2021-01-01 / 2021-01-01 01:02:03.456
            if (DateTime.TryParse(dateTimeString, out dateTime))
                return dateTime;

            // 常用转换, 例如 20210101 20050104095500000
            string[] formats = { "yyyyMMdd", "yyyyMMddHHmmssFFF" };
            var provider = CultureInfo.InvariantCulture;
            if (DateTime.TryParseExact(dateTimeString, formats, provider, DateTimeStyles.AllowWhiteSpaces, out dateTime))
                return dateTime;

            throw new FormatException("该字符串未被识别为有效的 DateTime");
        }

        /// <summary>
        /// DateTime格式化为字符串
        /// </summary>
        /// <param name="dateTime"></param>
        /// <param name="isDisplayTime"></param>
        /// <returns></returns>
        public static string Format(DateTime dateTime, bool isDisplayTime = false)
        {
            return dateTime.ToString(isDisplayTime ? "yyyy-MM-dd HH:mm:ss" : "yyyy-MM-dd");
        }

        /// <summary>
        /// 获取时间对应的索引号, 若没有匹配到精确日期,则向后匹配最近的日期, ShareData中存储的时间必须为正序排列.
        /// </summary>
        /// <param name="dateTime"></param>
        /// <returns></returns>
        public static int GetTimeIndex(DateTime[] dateTimes, DateTime dateTime)
        {
            int index = Array.BinarySearch(dateTimes, dateTime);
            if (index >= 0)
                return index;
            else
            {
                return (~index == dateTimes.Length) ? (~index - 1) : (~index);
            }
        }

        public static int GetTimeIndex(DateTime[] dateTimes, int year, int month, int day)
        {
            return GetTimeIndex(dateTimes, new DateTime(year, month, day));
        }

        public static int GetTimeIndex(DateTime[] dateTimes, int year, int month, int day, int hour, int minute, int second)
        {
            return GetTimeIndex(dateTimes, new DateTime(year, month, day, hour, minute, second));
        }

        public static DateTime GetTime(DateTime[] dateTimes, DateTime first, int indexOffset)
        {
            int firstIndex = GetTimeIndex(dateTimes, first);
            return dateTimes[firstIndex + indexOffset];
        }

        public static DateTime[] GetTimes(DateTime[] dateTimes, DateTime first, int startIndexOffset, int endIndexOffset)
        {
            int firstIndex = GetTimeIndex(dateTimes, first);
            return dateTimes[(firstIndex + startIndexOffset)..(firstIndex + endIndexOffset + 1)];
        }

        public static DateTime[] GetTimes(DateTime[] dateTimes, DateTime start, DateTime end)
        {
            int startIndex = GetTimeIndex(dateTimes, start);
            int endIndex = GetTimeIndex(dateTimes, end);
            return dateTimes[startIndex..(endIndex + 1)];
        }
    }
}
