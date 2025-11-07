using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Result
{
    [MessagePackObject]
    public class BacktestingResult
    {
        /// <summary>
        /// 本次回测生成的所有已完成的交易列表。
        /// </summary>
        [Key(0)]
        public List<Trade> Trades { get; }

        /// <summary>
        /// 构造函数。
        /// </summary>
        public BacktestingResult()
        {
            Trades = new List<Trade>();
        }

        [JsonConstructor]
        [SerializationConstructor]
        public BacktestingResult(List<Trade> trades)
        {
            Trades = trades;
        }
    }
}
