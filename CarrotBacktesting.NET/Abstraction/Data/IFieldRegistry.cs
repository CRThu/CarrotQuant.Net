using System.Collections.Generic;

namespace CarrotBacktesting.NET.Abstraction.Data
{
    /// <summary>
    /// 静态标的属性与维表注册中心契约。
    /// 统一管理板块、概念、行业及标的静态属性维表，
    /// 提供统一的维表索引器 Fields["industry"][symbol][col] 与强类型投影流 Fields["industry"].As&lt;T&gt;()。
    /// </summary>
    public interface IFieldRegistry
    {
        /// <summary>
        /// 维表索引器：Fields["industry"] -> IFieldTable。
        /// </summary>
        IFieldTable this[string name] { get; }

        /// <summary>
        /// 获取静态维表。
        /// </summary>
        IFieldTable GetTable(string name);

        /// <summary>
        /// 检查指定名称的维表是否存在。
        /// </summary>
        bool HasTable(string name);

        /// <summary>
        /// 注册静态维表。
        /// </summary>
        void Register(string name, IFieldTable table);

        /// <summary>
        /// 获取所有已加载的维表名称列表。
        /// </summary>
        IReadOnlyList<string> GetTableNames();
    }
}
