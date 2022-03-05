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
        public Dictionary<string, DateTime> Time { get; set; }

        public ShareData(DataTable dataTable, string TimeColName, params string[] DataColName)
        {
            var ids = dataTable.AsEnumerable().Select(r => r.Field<int>("id")).ToList();
            // TODO
        }
    }
}
