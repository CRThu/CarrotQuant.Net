using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 本地撮合引擎接口（驱动层）。
/// 仅面向回测引擎 (BacktestEngine) 暴露，包含时间驱动逻辑。
/// 实盘环境不存在此接口的调用。
/// </summary>
public interface IMatchingEngine
{
    /// <summary>
    /// 驱动撮合逻辑。
    /// 回测引擎在每个时间步 (Tick/Bar) 数据更新后调用此方法，评估挂单是否可以成交。
    /// </summary>
    /// <param name="data">全局数据提供器，用于获取最新价格</param>
    /// <param name="currentIndex">当前时间索引</param>
    void Match(IDataProvider data, int currentIndex);
}
