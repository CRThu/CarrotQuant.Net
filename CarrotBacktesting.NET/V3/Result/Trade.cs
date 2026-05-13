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
        /// <summary>
        /// 股票代码
        /// </summary>
        [Key(0)] public string StockCode { get; }
        /// <summary>
        /// 开仓组
        /// </summary>
        [Key(1)] public string EntryGroup { get; }
        /// <summary>
        /// 开仓原因，由策略的 CheckEntry 方法返回。
        /// </summary>
        [Key(2)] public string EntryReason { get; }
        /// <summary>
        /// 开仓日期
        /// </summary>
        [Key(3)] public DateTime EntryDate { get; }
        /// <summary>
        /// 开仓价格
        /// </summary>
        [Key(4)] public double EntryPrice { get; }
        /// <summary>
        /// 平仓组。
        /// </summary>
        [Key(5)] public string? ExitGroup { get; private set; }
        /// <summary>
        /// 平仓原因，由策略的 CheckExit 方法返回。
        /// </summary>
        [Key(6)] public string? ExitReason { get; private set; }
        /// <summary>
        /// 平仓日期
        /// </summary>
        [Key(7)] public DateTime? ExitDate { get; private set; }
        /// <summary>
        /// 平仓价格
        /// </summary>
        [Key(8)] public double? ExitPrice { get; private set; }

        // --- 状态与统计属性 ---
        /// <summary>
        /// 交易是否已平仓
        /// </summary>
        [Key(9)] public bool IsClosed { get; private set; } = false;
        /// <summary>
        /// 持仓天数 (K线数量)
        /// </summary>
        [Key(10)] public int HoldingPeriod { get; private set; } = 0;
        /// <summary>
        /// 收益率
        /// </summary>
        [JsonIgnore][IgnoreMember] public double? Return => IsClosed ? (ExitPrice - EntryPrice) / EntryPrice : null;
        /// <summary>
        /// 持仓期间遇到的最高收盘价
        /// </summary>
        [Key(11)] public double HighestPriceSinceEntry { get; private set; }
        /// <summary>
        /// 市场快照数据 (Trace 数据)
        /// </summary>
        [Key(12)] public object? MarketSnapshot { get; set; }

        /// <summary>
        /// 对最终盈利的交易计算平仓效率 (Trade Efficiency)。
        /// 公式: (实际利润) / (最大潜在利润)
        /// 衡量卖点捕获潜在利润的能力。
        /// </summary>
        [JsonIgnore, IgnoreMember]
        public double? TradeEfficiency
        {
            get
            {
                if (!IsClosed || !Return.HasValue) return null;

                // 1. 如果最终没有盈利，则效率指标不适用
                if (Return.Value <= 0) return null;

                // 2. 计算最大潜在利润
                double maxPotentialProfit = HighestPriceSinceEntry - EntryPrice;

                // 3. 只有当最大潜在利润为正时，比率才有意义
                if (maxPotentialProfit > 0)
                {
                    double actualProfit = ExitPrice.GetValueOrDefault() - EntryPrice;
                    // 确保结果不会因为浮点数精度问题略大于1
                    return Math.Min(1.0, actualProfit / maxPotentialProfit);
                }

                return null;
            }
        }

        [JsonConstructor]
        [SerializationConstructor]
        public Trade(string stockCode, string entryGroup, string entryReason, DateTime entryDate, double entryPrice, string? exitGroup, string? exitReason, DateTime? exitDate, double? exitPrice, bool isClosed, int holdingPeriod, double highestPriceSinceEntry, object? marketSnapshot)
        {
            StockCode = stockCode;
            EntryGroup = entryGroup;
            EntryReason = entryReason;
            EntryDate = entryDate;
            EntryPrice = entryPrice;
            ExitGroup = exitGroup;
            ExitReason = exitReason;
            ExitDate = exitDate;
            ExitPrice = exitPrice;
            IsClosed = isClosed;
            HoldingPeriod = holdingPeriod;
            HighestPriceSinceEntry = highestPriceSinceEntry;
            MarketSnapshot = marketSnapshot;
        }

        /// <summary>
        /// 构造一笔新的、未平仓的交易
        /// </summary>
        public Trade(string stockCode, string entryGroup, string entryReason, DateTime entryDate, double entryPrice)
        {
            StockCode = stockCode;
            EntryGroup = entryGroup;
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
        public void Close(string exitGroup, string exitReason, DateTime exitDate, double exitPrice)
        {
            if (IsClosed) return;

            ExitGroup = exitGroup;
            ExitReason = exitReason;
            ExitDate = exitDate;
            ExitPrice = exitPrice;
            IsClosed = true;
        }
    }
}
