using CarrotBacktesting.NET.Data;

namespace CarrotBacktesting.NET.Engine
{
    /// <summary>
    /// 宏观市场策略扫描上下文
    /// </summary>
    public class MarketStrategyContext
    {
        public DateTime CurrentDate { get; }
        public MarketFrame? Frame { get; }
        public IDataStorage FullData { get; }

        public MarketStrategyContext(DateTime date, MarketFrame? frame, IDataStorage fullData)
        {
            CurrentDate = date;
            Frame = frame;
            FullData = fullData;
        }
    }
}
