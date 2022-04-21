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
    public struct TransactionLog
    {
        /// <summary>
        /// 交易ID
        /// </summary>
        public int TransactionId { get; set; } = 0;
        /// <summary>
        /// 交易时间
        /// </summary>
        public DateTime TransactionTime { get; set; }
        /// <summary>
        /// 股票信息
        /// </summary>
        public string ShareName { get; set; }
        /// <summary>
        /// 交易股份数量, 正值为买入, 负值为卖出
        /// </summary>
        public double Size { get; set; }
        /// <summary>
        /// 交易成本
        /// </summary>
        public double Cost { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="transactionId"></param>
        /// <param name="transactionTime"></param>
        /// <param name="shareName"></param>
        /// <param name="cost"></param>
        /// <param name="size"></param>
        /// <param name="direction"></param>
        public TransactionLog(DateTime transactionTime, string shareName, double cost, double size, OrderDirection direction)
        {
            TransactionTime = transactionTime;
            ShareName = shareName;
            Size = direction == OrderDirection.Long ? size : -size;
            Cost = cost;
        }
    }
}
