using System;
using System.Collections;
using System.Collections.Generic;

namespace CarrotBacktesting.NET.Abstraction.Data
{
    /// <summary>
    /// 静态标的维表契约（单一内核，原生同时支持动态行点查、强类型即时投影、流式 LINQ 与双向倒排选股）。
    /// </summary>
    public interface IFieldTable : IEnumerable<KeyValuePair<string, IReadOnlyList<IDataRow>>>
    {
        /// <summary>
        /// 维表名称。
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 维表包含的列名集合。
        /// </summary>
        IReadOnlyCollection<string> Columns { get; }

        /// <summary>
        /// 维表包含的标的主键总数。
        /// </summary>
        int Count { get; }

        /// <summary>
        /// 维表中所有标的主键集合。
        /// </summary>
        IEnumerable<string> Keys { get; }

        /// <summary>
        /// 标的主键动态索引器：table[symbol] -> IDataRow。
        /// </summary>
        IDataRow this[string key] { get; }

        /// <summary>
        /// 【1:1 动态单值点查】尝试获取主体的首条维表行记录。
        /// </summary>
        bool TryGetRow(string key, out IDataRow? row);

        /// <summary>
        /// 【1:N 动态多值查询】尝试获取主体的多条维表行记录（如归属多个概念）。
        /// </summary>
        bool TryGetRows(string key, out IReadOnlyList<IDataRow> rows);

        /// <summary>
        /// 【1:N 动态多值查询】获取主体的多条维表行记录。若主体不存在则返回空列表。
        /// </summary>
        IReadOnlyList<IDataRow> GetRows(string key);

        /// <summary>
        /// 【1:N 便捷多值提取】提取指定标的在目标列上的所有属性值列表（自动聚合多行）。
        /// </summary>
        IReadOnlyList<string> GetValues(string key, string column);

        /// <summary>
        /// 检查指定主体是否存在。
        /// </summary>
        bool ContainsKey(string key);

        /// <summary>
        /// 【动态倒排选股】根据属性值（与可选列名）反向圈选符合条件的所有标的主键池。
        /// 若 column 为 null，则在全表所有列中检索并返回标的并集；若指定 column，则仅在对应列中反查。
        /// </summary>
        IReadOnlyList<string> GetKeys(string value, string? column = null);

        /// <summary>
        /// 获取全表只读映射快照。
        /// </summary>
        IReadOnlyDictionary<string, IReadOnlyList<IDataRow>> GetAll();

        /// <summary>
        /// 【强类型即时点查】将指定标的的首行数据按需投影转换为强类型实体模型（零全表拷贝）。
        /// </summary>
        T Get<T>(string key) where T : class;

        /// <summary>
        /// 【强类型安全尝试点查】尝试获取指定标的的强类型实体模型。
        /// </summary>
        bool TryGet<T>(string key, out T? value) where T : class;

        /// <summary>
        /// 【强类型流式 LINQ】按需将全表记录惰性投影为强类型实体流，原生支持 Where / OrderBy / Select。
        /// 具备短路优化（如 Take / First），零多余对象分配。
        /// </summary>
        IEnumerable<T> As<T>() where T : class;

        /// <summary>
        /// 【强类型键值对 LINQ】按需将全表标的代码与首实体投影为键值对流。
        /// </summary>
        IEnumerable<KeyValuePair<string, T>> AsPairs<T>() where T : class;

        /// <summary>
        /// 【强类型谓词倒排选股】通过强类型 Lambda 表达式即时圈选符合条件的标的主键池。
        /// </summary>
        IReadOnlyList<string> GetKeys<T>(Func<T, bool> predicate) where T : class;
    }
}
