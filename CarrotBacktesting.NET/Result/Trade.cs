using CarrotBacktesting.NET.Engine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Result
{
    /// <summary>
    /// 代表一笔从开仓到平仓的完整交易。
    /// </summary>
    public class Trade
    {
        // --- 核心属性 ---
        public string StockCode { get; }
        public string EntryReason { get; }
        public DateTime EntryDate { get; }
        public double EntryPrice { get; }
        public string? ExitReason { get; private set; }
        public DateTime? ExitDate { get; private set; }
        public double? ExitPrice { get; private set; }

        // --- 状态与统计属性 ---
        /// <summary>
        /// 交易是否已平仓
        /// </summary>
        public bool IsClosed { get; private set; } = false;
        /// <summary>
        /// 持仓天数 (K线数量)
        /// </summary>
        public int HoldingPeriod { get; private set; } = 0;
        /// <summary>
        /// 收益率
        /// </summary>
        public double? Return => IsClosed ? (ExitPrice - EntryPrice) / EntryPrice : null;
        /// <summary>
        /// 持仓期间遇到的最高收盘价
        /// </summary>
        public double HighestPriceSinceEntry { get; private set; }

        /// <summary>
        /// 构造一笔新的、未平仓的交易
        /// </summary>
        public Trade(string stockCode, string entryReason, DateTime entryDate, double entryPrice)
        {
            StockCode = stockCode;
            EntryReason = entryReason;
            EntryDate = entryDate;
            EntryPrice = entryPrice;
            HighestPriceSinceEntry = entryPrice;
        }

        /// <summary>
        /// 在每个新的K线上调用，用于更新交易的内部状态。
        /// </summary>
        public void UpdateOnNewBar(SignalStrategyContext context)
        {
            if (IsClosed) return;

            HoldingPeriod++;
            double currentClose = context.GetClose(0) ?? EntryPrice;
            if (currentClose > HighestPriceSinceEntry)
            {
                HighestPriceSinceEntry = currentClose;
            }
        }

        /// <summary>
        /// 平仓
        /// </summary>
        public void Close(string exitReason, DateTime exitDate, double exitPrice)
        {
            if (IsClosed) return;

            ExitReason = exitReason;
            ExitDate = exitDate;
            ExitPrice = exitPrice;
            IsClosed = true;
        }
    }
}
