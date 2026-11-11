using System;
using System.Collections.Generic;

namespace CarrotBacktesting.NET.Abstraction.Data;

/// <summary>
/// 异构事件表数据容器契约。
/// 用于管理并随机点查特定类型（如复权因子、分红、公告等）的异构稀疏 KV 数据。
/// </summary>
/// <typeparam name="T">事件实体类型，必须为引用类型（class）。</typeparam>
public interface IEventTable<T> where T : class
{
    /// <summary>
    /// 获取指定日期、指定标的的单个事件实体。
    /// </summary>
    /// <param name="date">交易日期。</param>
    /// <param name="symbol">品种代码。</param>
    /// <param name="value">获取到的事件数据，如果不存在则为 null。</param>
    /// <returns>如果找到事件则返回 true，否则返回 false。</returns>
    bool TryGet(DateTime date, string symbol, out T? value);

    /// <summary>
    /// 获取指定日期下的全市场快照（按股票代码分组的只读字典）。
    /// </summary>
    /// <param name="date">交易日期。</param>
    /// <param name="values">包含所有品种代码及其对应事件数据的字典。</param>
    /// <returns>如果成功获取到快照则返回 true，否则返回 false。</returns>
    bool TryGetDaily(DateTime date, out IReadOnlyDictionary<string, T> values);
}
