using CarrotBacktesting.NET.Common;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.DataFeed
{
    public class ShareData
    {
        /// <summary>
        /// key为数据列名(例如OHLC), Value为对应数据
        /// </summary>
        public Dictionary<string, double[]> Data { get; set; }

        public string TimeDisplayName { get; set; }
        public DateTime[] Time { get; set; }

        public int Count => Time.Length;
        public double[] this[string key] => Data[key];
        public string[] Keys => Data.Keys.ToArray();

        public ShareData()
        {
            Data = new Dictionary<string, double[]>();
            Time = Array.Empty<DateTime>();
        }

        public ShareData(DataTable dataTable, string timeColName, string[] dataColNames)
        {
            DataTable2ShareData(dataTable, timeColName, dataColNames);
        }

        /// <summary>
        /// 本实现仅支持DataTable内数据均为System.String类型, 且Time为正序排列
        /// </summary>
        /// <param name="dataTable"></param>
        /// <param name="timeColName"></param>
        /// <param name="dataColNames"></param>
        /// <returns></returns>
        public static void DataTable2ShareData(ShareData shareData, DataTable dataTable, string timeColName, string[] dataColNames)
        {
#if DEBUG
            Debug.WriteLine($"ShareData.DataTable2ShareData(...) called.");
            Stopwatch sw = new();
            sw.Start();
#endif
            var timeArray = DataTableMisc.GetColumn<string>(dataTable, timeColName);
            shareData.Time = timeArray.Select(t => DateTimeMisc.Parse(t)).ToArray();

            shareData.Data = new Dictionary<string, double[]>();
            foreach (var dataColName in dataColNames)
                shareData.Data.Add(dataColName, DataTableMisc.GetColumn<string>(dataTable, dataColName)
                    .Select(s => Convert.ToDouble(s)).ToArray());
            shareData.TimeDisplayName = timeColName;
#if DEBUG
            sw.Stop();
            Debug.WriteLine($"Elapsed time: {sw.ElapsedMilliseconds} ms.");
#endif
        }

        public void DataTable2ShareData(DataTable dataTable, string timeColName, string[] dataColNames)
        {
            DataTable2ShareData(this, dataTable, timeColName, dataColNames);
        }

        /// <summary>
        /// 获取时间对应的索引号, 若没有匹配到精确日期,则向后匹配最近的日期, ShareData中存储的时间必须为正序排列.
        /// </summary>
        /// <param name="dateTime"></param>
        /// <returns></returns>
        public int GetTimeIndex(DateTime dateTime)
        {
            int index = Array.BinarySearch(Time, dateTime);
            if (index >= 0)
                return index;
            else
            {
                return (~index == Time.Length) ? (~index - 1) : (~index);
            }
        }

        public int GetTimeIndex(int year, int month, int day)
        {
            return GetTimeIndex(new DateTime(year, month, day));
        }

        public int GetTimeIndex(int year, int month, int day, int hour, int minute, int second)
        {
            return GetTimeIndex(new DateTime(year, month, day, hour, minute, second));
        }

        public double GetPrice(DateTime datetime, string key)
        {
            int index = GetTimeIndex(datetime);
            return GetPrice(index, key);
        }

        public double GetPrice(int index, string key)
        {
            return Data[key][index];
        }

        public double[] GetData(DateTime start, DateTime end, string key)
        {
            int startIndex = GetTimeIndex(start);
            int endIndex = GetTimeIndex(end);
            //TODO
            return null;
        }
    }
}
