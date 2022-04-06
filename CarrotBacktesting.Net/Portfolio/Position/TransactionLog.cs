using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Portfolio.Position
{
    /// <summary>
    /// 交易记录
    /// </summary>
    public class TransactionLog
    {
        /// <summary>
        /// 交易ID
        /// </summary>
        public int TransactionId { get; set; }
        /// <summary>
        /// 交易时间
        /// </summary>
        public DateTime TransactionTime { get; set; }
        /// <summary>
        /// 交易所信息
        /// </summary>
        public string ExchangeName { get; set; }
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
        public double Price { get; set; }

        /// <summary>
        /// 头寸信息转交易记录构造函数
        /// </summary>
        /// <param name="transactionId"></param>
        /// <param name="transactionTime"></param>
        /// <param name="position"></param>
        public TransactionLog(int transactionId, DateTime transactionTime, GeneralPosition position)
        {
            TransactionId = transactionId;
            TransactionTime = transactionTime;
            ExchangeName = position.ExchangeName;
            ShareName = position.ShareName;
            Size = position.Size;
            Price = position.Cost;
        }
    }
}
