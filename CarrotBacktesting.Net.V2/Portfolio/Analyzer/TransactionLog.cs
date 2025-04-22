using CarrotBacktesting.Net.Common;
using CarrotBacktesting.Net.Portfolio.Order;
using CarrotBacktesting.Net.Portfolio.Position;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Portfolio.Analyzer
{
    /// <summary>
    /// 交易记录
    /// </summary>
    public class TransactionLog
    {
        /// <summary>
        /// 交易ID(自动生成)
        /// </summary>
        public int Id { get; init; }

        /// <summary>
        /// 交易时间
        /// </summary>
        public DateTime Time { get; init; }

        /// <summary>
        /// 股票代码
        /// </summary>
        public string StockCode { get; init; }

        /// <summary>
        /// 交易头寸数量, 正值为买入, 负值为卖出
        /// </summary>
        public double Size { get; init; }

        /// <summary>
        /// 交易成本
        /// </summary>
        public double Cost { get; init; }

        /// <summary>
        /// Id生成器
        /// </summary>
        private static readonly IncrementIdGenerator<int> IdGenerator = new();

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="time">交易时间</param>
        /// <param name="stockCode">股票代码</param>
        /// <param name="cost">交易成本</param>
        /// <param name="size">头寸数量</param>
        public TransactionLog(DateTime time, string stockCode, double cost, double size)
        {
            Id = IdGenerator.GetId();
            Time = time;
            StockCode = stockCode;
            Size = size;
            Cost = cost;
        }
    }
}
