using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.DataFeed
{
    public class DataFeed
    {
        /// <summary>
        /// MarketCache存放所需的股票行情数据
        /// key为stock名, value指向个股数据
        /// value的字典的key为数据列名(例如OHLC), Value为对应数据
        /// </summary>
        private Dictionary<string, ShareData> MarketCache { get; set; } = new();

        /// <summary>
        /// 设置一支股票的行情数据
        /// </summary>
        public void SetShareData(string shareCode, ShareData shareData)
        {
            if (!MarketCache.ContainsKey(shareCode))
                MarketCache[shareCode] = shareData;
        }

        public DataTable GetShareDataTable(string shareCode)
        {
            ShareData shareData = MarketCache[shareCode];
            DataTable dataTable = new();

            // Add Columns
            dataTable.Columns.Add(shareData.TimeDisplayName);
            foreach (var key in shareData.Data.Keys)
                dataTable.Columns.Add(key);

            // Add Rows
            dataTable.Rows.Add(new object[] { 1, 2, 3 });
            dataTable.Rows.Add(new object[] { 4, 5, 6 });
            dataTable.Rows.Add(new object[] { 7, 8, 9 });
            return dataTable;
        }
    }
}
