using CarrotBacktesting.Net.DataModel;
using CarrotBacktesting.Net.Engine;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Storage
{
    public class DataFeed
    {
        /// <summary>
        /// 市场数据构造器
        /// </summary>
        private MarketDataBuilder MarketDataBuilder { get; set; }
        /// <summary>
        /// 数据源接口
        /// </summary>
        private IDataProvider IDataProvider { get; set; }

        /// <summary>
        /// 市场数据存储类访问器
        /// </summary>
        private MarketData MarketData => MarketDataBuilder.ToMarketData();

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="options"></param>
        /// <exception cref="NotImplementedException"></exception>
        public DataFeed(BackTestingSimulationOptions options)
        {
            if (!options.IsSqliteDataFeed)
                throw new NotImplementedException("暂不支持该格式数据源");

            MarketDataBuilder = new();
            IDataProvider = new SqliteDataProvider(options.SqliteDatabasePath, options.FieldsMapper);
        }

        public void AddShareData(string shareCode, string timeColName, string[] dataColNames, string[] stringColNames)
        {

        }

        //public DataTable GetShareDataTable(string shareCode)
        //public (DateTime start, DateTime end) GetDateTimeRange()

        //public (int index, bool isPrecise) GetTimeIndex(string shareName, DateTime dateTime)
        //public double GetPrice(string shareName, int index, string key)
        //public string GetStringData(string shareName, int index, string key)
        //public double GetData(string shareName, int index, string key)
    }
}
