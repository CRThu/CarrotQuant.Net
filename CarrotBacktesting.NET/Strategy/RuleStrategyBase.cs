using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Strategy;

namespace CarrotBacktesting.NET.Strategy;

/// <summary>
/// 规则交易便利模板基类（Template Method Pattern）。
/// 专为经典单/多品种“无仓开仓、有仓止盈止损”策略与快速单元测试提供的脚手架。
/// 设计意图：将单标的循环与二分持仓状态机收敛在基类内部，不污染通用顶层 <see cref="IStrategy"/> 抽象。
/// </summary>
public abstract class RuleStrategyBase : IStrategy
{
    /// <inheritdoc />
    public virtual string Name => GetType().Name;

    /// <inheritdoc />
    public virtual int Lookback => 200;

    /// <summary>
    /// 策略启动生命周期回调。
    /// </summary>
    public virtual void OnStart(IEngineContext context) { }

    /// <summary>
    /// 时间步核心驱动回调。
    /// 默认实现：遍历目标标的池，根据标的持仓状态（pos == 0 或 pos != 0）分发至入场与出场检查。
    /// </summary>
    /// <param name="context">引擎运行上下文。</param>
    public virtual void OnUpdate(IEngineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var symbols = GetSymbols(context);
        if (symbols == null) return;

        foreach (var symbol in symbols)
        {
            if (string.IsNullOrWhiteSpace(symbol)) continue;

            var pos = context.Broker.GetPosition(symbol);
            if (pos == 0m)
            {
                CheckEntry(context, symbol);
            }
            else
            {
                CheckExit(context, symbol, pos);
            }
        }
    }

    /// <summary>
    /// 外部异构市场事件批量推送回调。
    /// </summary>
    public virtual void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events) { }

    /// <summary>
    /// 策略停止生命周期回调。
    /// </summary>
    public virtual void OnStop(IEngineContext context) { }

    /// <summary>
    /// 订单状态跃迁通知。
    /// </summary>
    public virtual void OnOrderStatusChanged(IEngineContext context, string orderId, OrderStatus status) { }

    /// <summary>
    /// 订单成交回报通知。
    /// </summary>
    public virtual void OnTrade(IEngineContext context, ExecutionReport e) { }

    /// <summary>
    /// 持仓变动通知。
    /// </summary>
    public virtual void OnPositionChanged(IEngineContext context, PositionEvent e) { }

    /// <summary>
    /// 获取当前策略监控的目标标的池枚举。
    /// </summary>
    /// <param name="context">引擎运行上下文。</param>
    /// <returns>标的代码集合。</returns>
    protected abstract IEnumerable<string> GetSymbols(IEngineContext context);

    /// <summary>
    /// 检查并执行无持仓时的开仓信号与逻辑。
    /// </summary>
    /// <param name="context">引擎运行上下文。</param>
    /// <param name="symbol">目标标的代码。</param>
    protected abstract void CheckEntry(IEngineContext context, string symbol);

    /// <summary>
    /// 检查并执行已有持仓时的止盈、止损或调仓逻辑。
    /// </summary>
    /// <param name="context">引擎运行上下文。</param>
    /// <param name="symbol">目标标的代码。</param>
    /// <param name="position">当前多/空持仓数量。</param>
    protected abstract void CheckExit(IEngineContext context, string symbol, decimal position);
}
