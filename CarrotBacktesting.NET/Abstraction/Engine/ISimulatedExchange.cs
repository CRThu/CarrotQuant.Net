using System;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 仅供回测仿真交易所实现的专有驱动契约。
/// 负责在回测离散时间步推进时，根据最新行情批量撮合挂单簿中的未成交限价单。
/// 实盘网关（如 CtpGateway）严禁实现此接口。
/// </summary>
public interface ISimulatedExchange : IExchangeGateway
{
    /// <summary>
    /// 获取当前仿真交易所中指定标的的最新基准行情价。
    /// </summary>
    /// <param name="symbol">交易标的代码。</param>
    /// <returns>最新价格；若无记录则返回 0.0。</returns>
    double GetMarketPrice(string symbol);

    /// <summary>
    /// 设置当前仿真交易所的物理时间戳（由外部引擎或经纪商在时间步驱动时同步）。
    /// </summary>
    /// <param name="currentTime">当前全局物理时间戳。</param>
    void SetCurrentTime(DateTime currentTime);

    /// <summary>
    /// 回测时间步时钟推进。
    /// 根据当前时间步的最新行情数据，驱动内部挂单簿批量撮合判定。
    /// </summary>
    /// <param name="currentTime">当前时间步的全局物理时间戳。</param>
    /// <param name="timeseries">全局时序注册表，用于获取当前时间步的标的价格与行情数据。</param>
    /// <param name="currentIndex">当前时间步在时序注册表中的行索引。</param>
    void Step(DateTime currentTime, ITimeseriesRegistry timeseries, int currentIndex);

    /// <summary>
    /// 更新指定标的的最新市场基准价与行情数据行（供单步微结构测试或精确撮合使用）。
    /// </summary>
    /// <param name="symbol">交易标的代码。</param>
    /// <param name="price">最新基准价格。</param>
    /// <param name="bar">当前时间步行情数据行（可选，供流动性与涨跌停拦截校验）。</param>
    void UpdateMarketPrice(string symbol, double price, IDataRow? bar = null);
}
