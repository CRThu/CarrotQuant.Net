using CarrotBacktesting.Net.Common;
using CarrotBacktesting.Net.DataModel;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Storage
{
    /// <summary>
    /// Sqlite数据库历史数据获取接口<br/>
    /// 要求数据结构:<br/>
    /// 表名为股票代码<br/>
    /// 字段类型全部为TEXT<br/>
    /// 日期字段格式为 2021-01-01 / 20210101<br/>
    /// 时间字段格式为 2021-01-01 01:02:03.456 / 20050104095500000<br/>
    /// 表每条数据按时间正序排列
    /// </summary>
    public class SqliteDataProvider : IDataProvider
    {
        private SqliteHelper sqliteHelper = new();

        public SqliteDataProvider(string dbPath)
        {
            sqliteHelper.Open(dbPath);
        }

        public MarketData GetShareHistoryData(string stockCode, string[] fields, DateTime? startTime = null, DateTime? endTime = null)
        {
            IEnumerable<IDictionary<string, object>> table;
            if (startTime is null && endTime is null)
            {
                table = sqliteHelper.GetTable(stockCode, fields);
            }
            else
            {
                table = sqliteHelper.GetTable(stockCode, fields, ("DateTime", startTime.ToString(), endTime.ToString(), FilterCondition.BigEqualAndSmallEqual)!);
            }

            MarketDataBuilder marketDataBuilder = new();
            marketDataBuilder.AddRange(stockCode, table);
            return marketDataBuilder.ToMarketData();
        }
    }
}
