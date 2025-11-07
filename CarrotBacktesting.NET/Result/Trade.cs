using CarrotBacktesting.NET.Engine;
using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Result
{
    /// <summary>
    /// 代表一笔从开仓到平仓的完整交易。
    /// </summary>
    [MessagePackObject]
    public class Trade
    {
        // --- 核心属性 ---
        [Key(0)] public string StockCode { get; }
        [Key(1)] public string EntryReason { get; }
        [Key(2)] public DateTime EntryDate { get; }
        [Key(3)] public double EntryPrice { get; }
        [Key(4)] public string? ExitReason { get; private set; }
        [Key(5)] public DateTime? ExitDate { get; private set; }
        [Key(6)] public double? ExitPrice { get; private set; }

        // --- 状态与统计属性 ---
        /// <summary>
        /// 交易是否已平仓
        /// </summary>
        [Key(7)] public bool IsClosed { get; private set; } = false;
        /// <summary>
        /// 持仓天数 (K线数量)
        /// </summary>
        [Key(8)] public int HoldingPeriod { get; private set; } = 0;
        /// <summary>
        /// 收益率
        /// </summary>
        [JsonIgnore][IgnoreMember] public double? Return => IsClosed ? (ExitPrice - EntryPrice) / EntryPrice : null;
        /// <summary>
        /// 持仓期间遇到的最高收盘价
        /// </summary>
        [Key(9)] public double HighestPriceSinceEntry { get; private set; }

        [JsonConstructor]
        [SerializationConstructor]
        public Trade(string stockCode, string entryReason, DateTime entryDate, double entryPrice, string? exitReason, DateTime? exitDate, double? exitPrice, bool isClosed, int holdingPeriod, double highestPriceSinceEntry)
        {
            StockCode = stockCode;
            EntryReason = entryReason;
            EntryDate = entryDate;
            EntryPrice = entryPrice;
            ExitReason = exitReason;
            ExitDate = exitDate;
            ExitPrice = exitPrice;
            IsClosed = isClosed;
            HoldingPeriod = holdingPeriod;
            HighestPriceSinceEntry = highestPriceSinceEntry;
        }

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
