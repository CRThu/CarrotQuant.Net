using CarrotBacktesting.NET.Common;
using System;
using System.Collections.Generic;
using System.Data;
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
        public string[] Time { get; set; }

        public int Count => Time.Length;
        public double[] this[string key] => Data[key];
        public string[] Keys => Data.Keys.ToArray();

        public ShareData()
        {
            Data = new Dictionary<string, double[]>();
            Time = Array.Empty<string>();
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
            shareData.Time = DataTableMisc.GetColumn<string>(dataTable, timeColName).ToArray();
            shareData.Data = new Dictionary<string, double[]>();
            foreach (var dataColName in dataColNames)
                shareData.Data.Add(dataColName, DataTableMisc.GetColumn<string>(dataTable, dataColName)
                    .Select(s => Convert.ToDouble(s)).ToArray());
            shareData.TimeDisplayName = timeColName;
        }

        public void DataTable2ShareData(DataTable dataTable, string timeColName, string[] dataColNames)
        {
            DataTable2ShareData(this, dataTable, timeColName, dataColNames);
        }
    }
}
