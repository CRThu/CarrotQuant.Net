using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Strategy;

namespace CarrotBacktesting.NET.Strategy;

/// <summary>
/// 通用量化交易策略抽象基类。
/// 消除常规策略开发时针对 <see cref="IStrategy"/> 空白生命周期方法与默认元数据的样板代码。
/// 开发者仅需覆写 <see cref="OnUpdate(IEngineContext)"/> 即可快速实现横向截面选股、时序追踪或复杂组合逻辑。
/// </summary>
public abstract class StrategyBase : IStrategy
{
    /// <inheritdoc />
    public virtual string Name => GetType().Name;

    /// <inheritdoc />
    public virtual int Lookback => 200;

    /// <inheritdoc />
    public virtual EnginePhase UpdatePhase => EnginePhase.Close;

    /// <inheritdoc />
    public virtual void OnStart(IEngineContext context) { }

    /// <inheritdoc />
    public abstract void OnUpdate(IEngineContext context);

    /// <inheritdoc />
    public virtual void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events) { }

    /// <inheritdoc />
    public virtual void OnStop(IEngineContext context) { }

    /// <inheritdoc />
    public virtual void OnOrderStatusChanged(IEngineContext context, long orderId, OrderStatus status) { }

    /// <inheritdoc />
    public virtual void OnTrade(IEngineContext context, ExecutionReport e) { }

    /// <inheritdoc />
    public virtual void OnPositionChanged(IEngineContext context, PositionEvent e) { }
}
