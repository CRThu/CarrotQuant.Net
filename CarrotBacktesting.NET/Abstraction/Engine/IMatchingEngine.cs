using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 定义本地撮合引擎接口。
/// 该接口属于驱动层，通常仅在回测环境中使用，负责模拟交易所的撮合逻辑。
/// </summary>
public interface IMatchingEngine
{
    /// <summary>
    /// 执行撮合逻辑。
    /// 回测引擎在每个时间步（如 Tick 或 Bar）更新后调用此方法，根据当前市场价格评估挂单是否可以成交。
    /// </summary>
    /// <param name="data">全局数据提供器，用于获取当前最新的价格数据。</param>
    /// <param name="currentIndex">当前的时间步索引（对应 IDataProvider 的行索引）。</param>
    void Match(IDataProvider data, int currentIndex);
}
