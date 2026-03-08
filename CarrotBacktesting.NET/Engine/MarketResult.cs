namespace CarrotBacktesting.NET.Engine
{
    /// <summary>
    /// 市场决策结果
    /// </summary>
    public class MarketResult
    {
        /// <summary>
        /// 是否跳过 Alpha (个股信号) 计算
        /// </summary>
        public bool SkipAlpha { get; set; } = false;

        /// <summary>
        /// 市场偏向
        /// </summary>
        public MarketBias Bias { get; set; } = MarketBias.Neutral;
    }

    /// <summary>
    /// 泛型市场决策结果，支持携带自定义状态
    /// </summary>
    /// <typeparam name="T">自定义状态类型</typeparam>
    public class MarketResult<T> : MarketResult where T : class, new()
    {
        /// <summary>
        /// 自定义状态数据
        /// </summary>
        public T State { get; set; } = new();
    }
}
