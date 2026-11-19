using System;
using System.Collections;
using System.Collections.Generic;

namespace CarrotBacktesting.NET.Abstraction.Data
{
    /// <summary>
    /// 时态事件流表契约，支持日期截面索引、二维主键检索与强类型实体流映射。
    /// </summary>
    public interface IEventTable : IEnumerable<KeyValuePair<MarketTime, IReadOnlyDictionary<string, IReadOnlyList<IDataRow>>>>
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
        IEnumerable<MarketTime> Dates { get; }

        /// <summary>
        /// 截面日期索引器：table[date] -> 当日全市场标的只读事件字典。
        /// </summary>
        IReadOnlyDictionary<string, IReadOnlyList<IDataRow>> this[MarketTime date] { get; }

        /// <summary>
        /// 二维复合坐标索引器：table[date, symbol] -> 当日该标的首条事件记录。
        /// </summary>
        IDataRow this[MarketTime date, string key] { get; }

        /// <summary>
        /// 尝试获取指定日期的全市场事件标的快照字典。
        /// </summary>
        bool TryGetDay(MarketTime date, out IReadOnlyDictionary<string, IReadOnlyList<IDataRow>>? dayMap);

        /// <summary>
        /// 获取指定时间戳、主体的事件记录列表。若无记录则返回空列表。
        /// </summary>
        IReadOnlyList<IDataRow> GetRows(MarketTime timestamp, string key);

        /// <summary>
        /// 尝试获取指定时间戳、主体的事件记录列表。
        /// </summary>
        bool TryGet(MarketTime timestamp, string key, out IReadOnlyList<IDataRow> rows);

        /// <summary>
        /// 获取指定时间戳、主体的首条事件记录。
        /// </summary>
        bool TryGetFirst(MarketTime timestamp, string key, out IDataRow? row);

        /// <summary>
        /// 获取指定时间戳下的全域广播事件列表（不挂钩特定标的代码）。
        /// </summary>
        bool TryGetGlobal(MarketTime timestamp, out IReadOnlyList<IDataRow> globalRows);

        /// <summary>
        /// 检查指定日期是否存在事件。
        /// </summary>
        bool ContainsDate(MarketTime date);

        /// <summary>
        /// 获取指定时间戳、标的的首条事件强类型实体模型。
        /// </summary>
        T Get<T>(MarketTime timestamp, string key) where T : class;

        /// <summary>
        /// 尝试获取指定时间戳、标的的强类型实体模型。
        /// </summary>
        bool TryGet<T>(MarketTime timestamp, string key, out T? value) where T : class;

        /// <summary>
        /// 将指定日期当日全市场的所有事件记录投影为强类型实体流。
        /// </summary>
        IEnumerable<T> As<T>(MarketTime timestamp) where T : class;

        /// <summary>
        /// 将指定标的跨越全周期的历史事件流投影为强类型实体流。
        /// </summary>
        IEnumerable<T> As<T>(string key) where T : class;

        /// <summary>
        /// 将全量历史的所有事件记录投影为强类型实体流。
        /// </summary>
        IEnumerable<T> As<T>() where T : class;
    }
}
