using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using CarrotBacktesting.NET.Engine;

namespace CarrotBacktesting.NET.Result
{
    public class BacktestingResult
    {
        /// <summary>
        /// 本次回测生成的所有已完成的交易列表。
        /// </summary>
        public List<Trade> Trades { get; }

        /// <summary>
        /// 每日市场宏观结果记录
        /// </summary>
        public Dictionary<DateTime, MarketResult>? DailyMarketResults { get; set; }

        /// <summary>
        /// 构造函数。
        /// </summary>
        public BacktestingResult()
        {
            Trades = new List<Trade>();
        }

        [JsonConstructor]
        public BacktestingResult(List<Trade> trades, Dictionary<DateTime, MarketResult>? dailyMarketResults)
        {
            Trades = trades;
            DailyMarketResults = dailyMarketResults;
        }
    }
}
