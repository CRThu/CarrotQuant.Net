using CarrotBacktesting.Net.Common;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.DataFeed
{
    public class ShareData
    {
        /// <summary>
        /// key为数据列名(例如OHLC), Value为对应数据
        /// </summary>
        public Dictionary<string, double[]> Data { get; set; } = new();

        /// <summary>
        /// key为数据列名(例如交易状态/是否ST), Value为对应数据, 不应将数据放入本字典
        /// </summary>
        public Dictionary<string, string[]> StringData { get; set; } = new();

        /// <summary>
        /// 时间显示列名
        /// </summary>
        public string TimeDisplayName { get; set; }
        /// <summary>
        /// 交易时间数组
        /// </summary>
        public DateTime[] Time { get; set; } = Array.Empty<DateTime>();

        public int Count => Time.Length;
        public double[] this[string key] => Data[key];
        public string[] Keys => Data.Keys.ToArray();

        public DateTime StartTime => Time.First();
        public DateTime EndTime => Time.Last();

        public ShareData()
        {
        }

        public ShareData(DataTable dataTable, string timeColName, string[] dataColNames)
        {
            DataTable2ShareData(dataTable, timeColName, dataColNames);
        }

        /// <summary>
        /// 本实现仅支持DataTable内数据均为System.String类型, 且Time为正序排列
        /// </summary>
        /// <param name="shareData"></param>
        /// <param name="dataTable"></param>
        /// <param name="timeColName">数据库表中时间列列名</param>
        /// <param name="dataColNames">数据库表中数据列列名数组(例如OHLC)</param>
        /// <param name="stringColNames">数据库表中字符串列列名数组(例如停牌/ST)</param>
        public static void DataTable2ShareData(ShareData shareData, DataTable dataTable, string timeColName, string[] dataColNames, string[] stringColNames = null)
        {
            Console.WriteLine($"ShareData.DataTable2ShareData(...) called.");
            Stopwatch sw = new();
            sw.Start();

            // 导入时间数据
            var timeArray = DataTableMisc.GetColumn<string>(dataTable, timeColName);
            shareData.Time = timeArray.Select(t => DateTimeMisc.Parse(t)).ToArray();
            shareData.TimeDisplayName = timeColName;

            // 导入股价数据
            shareData.Data = new();
            foreach (var dataColName in dataColNames)
                shareData.Data.Add(dataColName, DataTableMisc.GetColumn<string>(dataTable, dataColName)
                    .Select(s => Convert.ToDouble(s)).ToArray());

            // 导入字符串数据
            if (stringColNames != null)
            {
                shareData.StringData = new();
                foreach (var stringColName in stringColNames)
                    shareData.StringData.Add(stringColName, DataTableMisc.GetColumn<string>(dataTable, stringColName).ToArray());
            }

            sw.Stop();
            Console.WriteLine($"Elapsed time: {sw.ElapsedMilliseconds} ms.");
        }

        public void DataTable2ShareData(DataTable dataTable, string timeColName, string[] dataColNames, string[] stringColNames = null)
        {
            DataTable2ShareData(this, dataTable, timeColName, dataColNames, stringColNames);
        }

        public (double price, bool isActive) GetPrice(DateTime datetime, string key)
        {
            (int index, bool isPrecise) = GetTimeIndex(datetime);
            return (GetPrice(index, key), isPrecise);
        }

        public double GetPrice(int index, string key)
        {
            return Data[key][index];
        }

        public string GetStringData(int index, string key)
        {
            return StringData[key][index];
        }

        public double[] GetPrices(DateTime start, DateTime end, string key)
        {
            (int startIndex, _) = GetTimeIndex(start);
            (int endIndex, _) = GetTimeIndex(end);
            return Data[key][startIndex..(endIndex + 1)];
        }

        // Get DateTimeMisc
        public (int index, bool isPrecise) GetTimeIndex(DateTime dateTime) => DateTimeMisc.GetTimeIndex(Time, dateTime);
        public (int index, bool isPrecise) GetTimeIndex(int year, int month, int day) => DateTimeMisc.GetTimeIndex(Time, year, month, day);
        public (int index, bool isPrecise) GetTimeIndex(int year, int month, int day, int hour, int minute, int second) => DateTimeMisc.GetTimeIndex(Time, year, month, day, hour, minute, second);
        public DateTime GetTime(DateTime first, int indexOffset) => DateTimeMisc.GetTime(Time, first, indexOffset);
        public DateTime[] GetTimes(DateTime first, int startIndexOffset, int endIndexOffset) => DateTimeMisc.GetTimes(Time, first, startIndexOffset, endIndexOffset);
        public DateTime[] GetTimes(DateTime start, DateTime end) => DateTimeMisc.GetTimes(Time, start, end);
    }
}
