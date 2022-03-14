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

        public static string Format(DateTime dateTime, bool isDisplayTime = false)
        {
            return dateTime.ToString(isDisplayTime ? "yyyy-MM-dd HH:mm:ss" : "yyyy-MM-dd");
        }
    }
}
