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
        public string[] Time { get; set; }

        /// <summary>
        /// 本实现仅支持DataTable内数据均为System.String类型, 且Time为正序排列
        /// </summary>
        /// <param name="dataTable"></param>
        /// <param name="timeColName"></param>
        /// <param name="dataColNames"></param>
        public ShareData(DataTable dataTable, string timeColName, string[] dataColNames)
        {
            Time = DataTableMisc.GetColumn<string>(dataTable, timeColName).ToArray();
            Data = new Dictionary<string, double[]>();
            foreach (var dataColName in dataColNames)
                Data.Add(dataColName, DataTableMisc.GetColumn<string>(dataTable, dataColName)
                    .Select(s => Convert.ToDouble(s)).ToArray());
        }
    }
}
