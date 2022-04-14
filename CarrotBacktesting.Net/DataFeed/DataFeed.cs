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
                MarketCache.Add(shareCode, shareData);
            else
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
            foreach (var key in shareData.StringData.Keys)
                dataTable.Columns.Add(key);

            // Add Rows
            for (int i = 0; i < shareData.Count; i++)
            {
                object[] row = new object[shareData.Keys.Length + shareData.StringData.Keys.Count + 1];
                string[] keysArray = shareData.Keys;
                row[0] = shareData.Time[i];
                for (int j = 0; j < keysArray.Length; j++)
                    row[j + 1] = shareData[keysArray[j]][i];

                // Add StringData
                string[] stringKeysArray = shareData.StringData.Keys.ToArray();
                for (int j = 0; j < stringKeysArray.Length; j++)
                    row[keysArray.Length + j + 1] = shareData.StringData[stringKeysArray[j]][i];

                dataTable.Rows.Add(row);
            }
            return dataTable;
        }

        /// <summary>
        /// 获取数据源时间范围
        /// </summary>
        /// <returns></returns>
        public (DateTime start, DateTime end) GetDateTimeRange()
        {
            DateTime minStartDateTime = DateTime.MinValue;
            DateTime maxEndDateTime = DateTime.MaxValue;
            foreach (var shareData in MarketCache.Values)
            {
                if (minStartDateTime > shareData.StartTime)
                    minStartDateTime = shareData.StartTime;
                if (maxEndDateTime < shareData.EndTime)
                    maxEndDateTime = shareData.EndTime;
            }
            return (minStartDateTime, maxEndDateTime);
        }

        // Raw From ShareData Method
        public (int index, bool isPrecise) GetTimeIndex(string shareName, DateTime dateTime) => MarketCache[shareName].GetTimeIndex(dateTime);
        public double GetPrice(string shareName, int index, string key) => MarketCache[shareName].GetPrice(index, key);
        public string GetStringData(string shareName, int index, string key) => MarketCache[shareName].GetStringData(index, key);
        public double GetData(string shareName, int index, string key) => MarketCache[shareName].GetData(index, key);
    }
}
