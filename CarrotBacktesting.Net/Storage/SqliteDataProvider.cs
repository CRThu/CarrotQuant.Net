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

        public MarketData GetShareHistoryData(string shareCode, string timeCol, string[]? dataCols = null, string[]? strCols = null, DateTime? startTime = null, DateTime? endTime = null)
        {
            // 所需所有字段
            string[] cols = ArrayMisc.ArrayCombine(new string[] { timeCol }, dataCols, strCols);

            DataTable table = new();
            if (startTime is null && endTime is null)
            {
                table = sqliteHelper.GetTable(shareCode, cols);
            }
            else
            {
                table = sqliteHelper.GetTable(shareCode, cols, (timeCol, startTime.ToString(), endTime.ToString(), FilterCondition.BigEqualAndSmallEqual)!);
            }

            // TODO
            // DATATABLE 2 SHAREDATA
            return new MarketData();
        }
    }
}
