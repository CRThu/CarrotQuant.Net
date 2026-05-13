using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Strategy;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace CarrotBacktesting.NET.Examples
{
    public class MyMarketData
    {
        [Display(Name = "全市场上涨比例")]
        public double UpRatio { get; set; }
    }

    public class MarketTrendStrategy : ISignalStrategy, IMarketStrategy<MyMarketData>
    {
        public string Name => "简单联动策略示例";

        public MarketResult<MyMarketData> CheckMarket(MarketStrategyContext context)
        {
            double ratio = 0;
            if (context.Frame != null)
            {
                int upCount = context.Frame.PrimaryData.Count(f => f.HasValue && f.Value.Close > f.Value.Open);
                int totalCount = context.Frame.PrimaryData.Count(f => f.HasValue);
                ratio = totalCount > 0 ? (double)upCount / totalCount : 0;
            }

            return new MarketResult<MyMarketData>
            {
                SkipAlpha = ratio < 0.2, 
                State = new MyMarketData { UpRatio = ratio }
            };
        }

        public SignalResult? CheckSignal(SignalStrategyContext context)
        {
            var myData = context.MarketState<MyMarketData>();
            if (myData == null) return null;

            if (myData.UpRatio < 0.5) return null;

            double? close = context.GetClose(0);
            double? open = context.GetOpen(0);
            if (close / open > 1.03)
            {
                return new SignalResult("大盘好且个股强", "默认分组");
            }

            return null;
        }
    }
}
