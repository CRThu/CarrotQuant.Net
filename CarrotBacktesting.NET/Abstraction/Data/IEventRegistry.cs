using System.Collections.Generic;

namespace CarrotBacktesting.NET.Abstraction.Data
{
    /// <summary>
    /// 时态事件表注册中心契约。
    /// 统一管理时态事件表，提供 Events["adjustments"][date, symbol] 索引访问与流式投影。
    /// </summary>
    public interface IEventRegistry
    {
        /// <summary>
        /// 事件表索引器：Events["adjustments"] -> IEventTable。
        /// </summary>
        IEventTable this[string name] { get; }

        /// <summary>
        /// 获取事件表。
        /// </summary>
        IEventTable GetTable(string name);

        /// <summary>
        /// 检查指定名称的事件表是否存在。
        /// </summary>
        bool HasTable(string name);

        /// <summary>
        /// 注册事件表。
        /// </summary>
        void Register(string name, IEventTable table);

        /// <summary>
        /// 获取所有已加载的事件表名称列表。
        /// </summary>
        IReadOnlyList<string> GetTableNames();

        /// <summary>
        /// 检查指定时间戳（日期或逻辑时间点）在所有已注册事件表中（或指定事件表中）是否存在待处理的新事件。
        /// 供策略在 OnUpdate 中按需轻量探测，规避无事件时的无效遍历与开销。
        /// 默认实现返回 false。
        /// </summary>
        /// <param name="time">要检查的逻辑时间戳。</param>
        /// <param name="tableName">可选的事件表名。缺省为空时探测所有已注册的事件表。</param>
        /// <returns>若存在事件则返回 true，否则返回 false。</returns>
        bool HasNewEvents(DateTime time, string? tableName = null) => false;
    }
}
