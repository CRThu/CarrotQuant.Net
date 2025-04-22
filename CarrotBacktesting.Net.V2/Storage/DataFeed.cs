using CarrotBacktesting.Net.DataModel;
using CarrotBacktesting.Net.Engine;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CarrotBacktesting.Net.Common.Enums;


namespace CarrotBacktesting.Net.Storage
{
    public class DataFeed
    {
        /// <summary>
        /// 市场数据存储类访问器
        /// </summary>
        public MarketData MarketData { get; init; }

        /// <summary>
        /// 回测配置类
        /// </summary>
        public SimulationOptions Options { get; set; }

        /// <summary>
        /// 数据存储类开始日期
        /// </summary>
        public DateTime StartTime => MarketData.StartTime;
        /// <summary>
        /// 数据存储类结束日期
        /// </summary>
        public DateTime EndTime => MarketData.EndTime;

        /// <summary>
        /// 时间帧数量
        /// </summary>
        public int Count => MarketData.Count;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="options"></param>
        /// <exception cref="NotImplementedException"></exception>
        public DataFeed(SimulationOptions options)
        {
            Options = options;
            MarketDataBuilder MarketDataBuilder = new();

            IDataProvider IDataProvider = Options.DataFeedSource switch {
                DataFeedSource.Csv => new CsvDataProvider(Options),
                _ => throw new NotImplementedException("暂不支持该格式数据源"),
            };

            // 若未传入被测股票代码则获取所有股票代码
            if (Options.StockCodes is null)
                Options.StockCodes = IDataProvider.GetAllStockCode();

            // TODO 待增加Cache
            var frames = IDataProvider.GetShareData(Options.StockCodes);
            MarketDataBuilder.AddRange(frames);

            MarketData = MarketDataBuilder.ToMarketData();
        }

        /// <summary>
        /// 获取此时间对应或相近的市场信息帧
        /// </summary>
        /// <param name="dateTime">时间</param>
        /// <param name="frame">返回帧, 若此时间帧不存在则返回向前搜索最近的时间帧, 若向前搜索不存在帧则返回最早时间</param>
        /// <returns>是否为此时间对应市场信息帧</returns>
        public bool GetMarketData(DateTime dateTime, out MarketFrame frame)
        {
            return MarketData.TryGetNearby(dateTime, out frame);
        }

        /// <summary>
        /// 获取此时间此股票对应或相近的股票信息帧
        /// </summary>
        /// <param name="dateTime">时间</param>
        /// <param name="stockCode">股票代码</param>
        /// <param name="frame">返回帧, 若此时间帧不存在则返回向前搜索最近的时间帧, 若向前搜索不存在帧则返回最早时间, 若帧不存在股票信息则返回null</param>
        /// <returns>是否为此时间此股票对应市场信息帧</returns>
        public bool GetShareData(DateTime dateTime, string stockCode, out ShareFrame? frame)
        {
            bool isExist = MarketData.TryGetNearby(dateTime, out MarketFrame marketFrame);
            if (marketFrame[stockCode] is not null)
            {
                frame = marketFrame[stockCode];
                return isExist;
            }
            else
            {
                // 若相近日期市场帧不存在该股票代码则返回null
                frame = null;
                return false;
            }
        }
    }
}
