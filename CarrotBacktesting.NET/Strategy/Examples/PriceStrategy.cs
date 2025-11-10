using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Result;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Strategy.Examples
{
    /// <summary>
    /// 一个简单的交易策略示例:收盘价比昨天高，就买入，持有期达到5天就卖出
    /// </summary>
    public class PriceStrategy : ITradeStrategy
    {
        public string Name => "简单交易策略(持5天卖)";

        public string? CheckEntry(SignalStrategyContext context)
        {
            // 简单逻辑：如果收盘价比昨天高，就买入
            double? close0 = context.GetClose(0);
            double? close1 = context.GetClose(-1);

            if (close0.HasValue && close1.HasValue && close0 > close1)
            {
                return "PriceUp";
            }
            return null;
        }

        public string? CheckExit(SignalStrategyContext context, Trade trade)
        {
            // 简单逻辑：持有期达到5天就卖出
            if (trade.HoldingPeriod >= 5)
            {
                return "FixedPeriodExit";
            }
            return null;
        }
    }
}
