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
    }
}
