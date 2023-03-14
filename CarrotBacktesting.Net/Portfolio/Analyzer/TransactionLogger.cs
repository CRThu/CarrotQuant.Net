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
        public List<TransactionLog> Logs { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public TransactionLogger()
        {
            Logs = new();
        }

        ///// <summary>
        ///// 添加设置现金记录
        ///// </summary>
        ///// <param name="time">时间</param>
        ///// <param name="cash">金额</param>
        //public void SetCash(DateTime time, double cash)
        //{
        //    Logs.Add(new TransactionLog(time, "$CASH$", 1, cash));
        //}

        /// <summary>
        /// 添加交易记录
        /// </summary>
        /// <param name="time">交易时间</param>
        /// <param name="stockCode">股票代码</param>
        /// <param name="cost">交易成本</param>
        /// <param name="size">头寸数量</param>
        public void AddTransaction(DateTime time, string stockCode, double cost, double size)
        {
            Logs.Add(new TransactionLog(time, stockCode, cost, size));
        }

        public override string ToString()
        {
            return ClassFormatter.Formatter(Logs);
        }
    }
}
