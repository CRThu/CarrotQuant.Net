using CarrotBacktesting.Net.Portfolio.Order;
using CarrotBacktesting.Net.Portfolio.Position;
using CarrotBacktesting.Net.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Portfolio.Analyzer
{
    /// <summary>
    /// 交易记录器
    /// </summary>
    public class TransactionLogger
    {
        public List<TransactionLog> Logs { get; set; } = new();

        /// <summary>
        /// 交易记录自增Id生成
        /// </summary>
        private int transactionId;

        /// <summary>
        /// 交易记录自增Id生成
        /// </summary>
        public int TransactionId
        {
            get
            {
                return transactionId++;
            }
        }

        /// <summary>
        /// 添加设置现金记录
        /// </summary>
        /// <param name="cash"></param>
        public void SetCash(DateTime transactionTime, double cash)
        {
            Logs.Add(new TransactionLog(transactionTime, "$CASH$", cash, 0, OrderDirection.Buy)
            {
                TransactionId = TransactionId
            });
        }

        /// <summary>
        /// 添加交易记录
        /// </summary>
        /// <param name="time"></param>
        /// <param name="shareName"></param>
        /// <param name="cost"></param>
        /// <param name="size"></param>
        /// <param name="direction"></param>
        public void AddTransaction(DateTime time, string shareName, double cost, double size, OrderDirection direction)
        {
            Logs.Add(new TransactionLog(time, shareName, cost, size, direction)
            {
                TransactionId = TransactionId
            });
        }

        public override string ToString()
        {
            return ClassFormatter.Formatter(Logs);
        }
    }
}
