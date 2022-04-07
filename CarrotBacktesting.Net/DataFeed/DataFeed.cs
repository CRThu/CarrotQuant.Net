using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.DataFeed
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
            foreach (var key in shareData.Keys)
                dataTable.Columns.Add(key);

            // Add Rows
            for (int i = 0; i < shareData.Count; i++)
            {
                object[] row = new object[shareData.Keys.Length + 1];
                string[] keysArray = shareData.Keys;
                row[0] = shareData.Time[i];
                for (int j = 0; j < shareData.Keys.Length; j++)
                    row[j + 1] = shareData[keysArray[j]][i];

                dataTable.Rows.Add(row);
            }
            return dataTable;
        }
    }
}
