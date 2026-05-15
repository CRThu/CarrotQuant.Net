using System;
using System.Collections.Generic;

namespace CarrotBacktesting.NET.Abstraction.Data;

/// <summary>
/// 事件提供器接口。
/// 用于获取特定类型（如复权因子、分红等）的异构 KV 数据。
/// </summary>
/// <typeparam name="T">事件数据的类型。</typeparam>
public interface IEventProvider<T> where T : class
{
    /// <summary>
    /// 获取指定日期、指定品种的单个事件。
    /// </summary>
    /// <param name="date">日期。</param>
    /// <param name="symbol">品种代码。</param>
    /// <param name="value">获取到的事件数据，如果不存在则为 null。</param>
    /// <returns>如果找到事件则返回 true，否则返回 false。</returns>
    bool TryGet(DateTime date, string symbol, out T? value);

    /// <summary>
    /// 获取指定日期下的全市场快照（如果该流支持全市场维度）。
    /// </summary>
    /// <param name="date">日期。</param>
    /// <param name="values">包含所有品种代码及其对应事件数据的字典。</param>
    /// <returns>如果成功获取到快照则返回 true，否则返回 false。</returns>
    bool TryGetDaily(DateTime date, out IReadOnlyDictionary<string, T> values);
}
