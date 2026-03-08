using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Strategy;
using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace CarrotBacktesting.NET.Examples
{
    /// <summary>
    /// 1. 定义你想追踪的市场指标（POCO类）
    /// ExcelExporter 会通过反射自动识别此类中的公开属性，并将其导出为 Excel 中的独立列。
    /// </summary>
    public class MyMarketTrace
    {
        [Display(Name = "上涨家数占比")]
        public double UpRatio { get; set; }
    }

    /// <summary>
    /// 2. 编写市场策略
    /// 宏观市场策略在所有个股扫描之前执行，其结果 (Trace) 会被回测引擎捕获并存储在买入信号对应的 Trade 对象中。
    /// </summary>
    public class MarketHeatStrategy : IMarketStrategy<MyMarketTrace>
    {
        public string Name => "热度策略";

        /// <summary>
        /// 每日执行一次，计算市场宏观状态
        /// </summary>
        public MarketResult<MyMarketTrace> CheckMarket(MarketStrategyContext context)
        {
            double ratio = 0;

            if (context.Frame != null)
            {
                // 计算当日上涨家数占比 (收盘价 > 开盘价)
                int upCount = context.Frame.PrimaryData.Count(f => f.HasValue && f.Value.Close > f.Value.Open);
                int totalCount = context.Frame.PrimaryData.Count(f => f.HasValue && f.Value.Status == TradeStatus.Active);
                
                ratio = totalCount > 0 ? (double)upCount / totalCount : 0;
            }

            return new MarketResult<MyMarketTrace>
            {
                // 宏观决策：如果上涨家数占比超过 60%，设为积极看多 (Up)
                Bias = ratio > 0.6 ? MarketBias.Up : MarketBias.Neutral,
                
                // 宏观剪枝：如果上涨家数占比低于 20%，可以跳过当日所有个股的信号计算以规避风险
                SkipAlpha = ratio < 0.2,

                // 填充 Trace：这个状态对象会被持久化，最终出现在 Excel 的全量流水表中
                State = new MyMarketTrace 
                { 
                    UpRatio = ratio 
                }
            };
        }
    }
}
