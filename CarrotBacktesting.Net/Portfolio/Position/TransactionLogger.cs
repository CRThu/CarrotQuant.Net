using CarrotBacktesting.Net.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Portfolio.Position
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
            Logs.Add(new TransactionLog(TransactionId, transactionTime, new GeneralPosition("DBNULL", "DBNULL", cash, 0)));
        }

        /// <summary>
        /// 添加交易记录
        /// </summary>
        /// <param name="transactionTime"></param>
        /// <param name="position"></param>
        public void AddTransaction(DateTime transactionTime, GeneralPosition position)
        {
            Logs.Add(new TransactionLog(TransactionId, transactionTime, position));
        }

        public override string ToString()
        {
            return Serializer.Serialize(this);
        }
    }
}
