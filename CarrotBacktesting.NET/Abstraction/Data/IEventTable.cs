using System;
using System.Collections;
using System.Collections.Generic;

namespace CarrotBacktesting.NET.Abstraction.Data
{
    /// <summary>
    /// 时态事件流表契约（单一权威内核，同时支持免定义动态点查、原生截面字典遍历、强类型即时投影与流式 LINQ）。
    /// </summary>
    public interface IEventTable : IEnumerable<KeyValuePair<DateTime, IReadOnlyDictionary<string, IReadOnlyList<IDataRow>>>>
    {
        /// <summary>
        /// 事件流名称。
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 事件记录覆盖的日期总数。
        /// </summary>
        int Count { get; }

        /// <summary>
        /// 包含事件的所有日期集合（按时间严格单调递增排序）。
        /// </summary>
        IEnumerable<DateTime> Dates { get; }

        /// <summary>
        /// 截面日期索引器：table[date] -> 当日全市场标的只读事件字典。
        /// </summary>
        IReadOnlyDictionary<string, IReadOnlyList<IDataRow>> this[DateTime date] { get; }

        /// <summary>
        /// 二维复合坐标索引器：table[date, symbol] -> 当日该标的首条事件记录。
        /// </summary>
        IDataRow this[DateTime date, string key] { get; }

        /// <summary>
        /// 尝试获取指定日期的全市场事件标的快照字典。
        /// </summary>
        bool TryGetDay(DateTime date, out IReadOnlyDictionary<string, IReadOnlyList<IDataRow>>? dayMap);

        /// <summary>
        /// 【1:N 动态多值查询】获取指定时间戳、主体的事件记录列表。若无记录则返回空列表。
        /// </summary>
        IReadOnlyList<IDataRow> GetRows(DateTime timestamp, string key);

        /// <summary>
        /// 【1:N 动态多值查询】尝试获取指定时间戳、主体的事件记录列表。
        /// </summary>
        bool TryGet(DateTime timestamp, string key, out IReadOnlyList<IDataRow> rows);

        /// <summary>
        /// 【1:1 动态单值便捷点查】获取指定时间戳、主体的首条事件记录。
        /// </summary>
        bool TryGetFirst(DateTime timestamp, string key, out IDataRow? row);

        /// <summary>
        /// 【N:1 全局广播】获取指定时间戳下的全域事件列表（不挂钩特定标的代码）。
        /// </summary>
        bool TryGetGlobal(DateTime timestamp, out IReadOnlyList<IDataRow> globalRows);

        /// <summary>
        /// 检查指定日期是否存在事件。
        /// </summary>
        bool ContainsDate(DateTime date);

        /// <summary>
        /// 【强类型即时点查】获取指定时间戳、标的的首条事件强类型实体模型。
        /// </summary>
        T Get<T>(DateTime timestamp, string key) where T : class;

        /// <summary>
        /// 【强类型安全尝试点查】尝试获取指定时间戳、标的的强类型实体模型。
        /// </summary>
        bool TryGet<T>(DateTime timestamp, string key, out T? value) where T : class;

        /// <summary>
        /// 【当日截面流式 LINQ】将指定日期当日全市场的所有事件记录惰性投影为强类型实体流。
        /// </summary>
        IEnumerable<T> As<T>(DateTime timestamp) where T : class;

        /// <summary>
        /// 【单标的历史流式 LINQ】将指定标的跨越全周期的历史事件流按需惰性投影为强类型实体流。
        /// </summary>
        IEnumerable<T> As<T>(string key) where T : class;

        /// <summary>
        /// 【全域事件流式 LINQ】将全量历史的所有事件记录惰性投影为强类型实体流。
        /// </summary>
        IEnumerable<T> As<T>() where T : class;
    }
}
