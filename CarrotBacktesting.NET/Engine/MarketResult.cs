using MessagePack;
using System;

namespace CarrotBacktesting.NET.Engine
{
    /// <summary>
    /// 市场决策结果
    /// </summary>
    [MessagePackObject]
    public class MarketResult
    {
        /// <summary>
        /// 是否跳过 Alpha (个股信号) 计算
        /// </summary>
        [Key(0)]
        public bool SkipAlpha { get; set; } = false;

        /// <summary>
        /// 市场偏向
        /// </summary>
        [Key(1)]
        public MarketBias Bias { get; set; } = MarketBias.Neutral;

        /// <summary>
        /// 获取原始状态对象 (用于快照捕获)
        /// </summary>
        public virtual object? GetStateRaw() => null;
    }

    /// <summary>
    /// 泛型市场决策结果，支持携带自定义状态
    /// </summary>
    /// <typeparam name="T">自定义状态类型</typeparam>
    [MessagePackObject]
    public class MarketResult<T> : MarketResult where T : class, new()
    {
        /// <summary>
        /// 自定义状态数据
        /// </summary>
        [Key(2)]
        public T State { get; set; } = new();

        /// <summary>
        /// 重写以返回泛型状态对象
        /// </summary>
        public override object? GetStateRaw() => State;
    }
}
