using System.Collections.Generic;

namespace CarrotBacktesting.NET.Abstraction.Data;

/// <summary>
/// 时序注册中心契约，统筹管理主时序表与辅助时序子表。
/// 注册中心不承载具体时序数据，具体矩阵与字段由 ITimeseriesTable 提供。
/// </summary>
public interface ITimeseriesRegistry
{
    /// <summary>
    /// 获取主时钟时序表。
    /// </summary>
    ITimeseriesTable Primary { get; }

    /// <summary>
    /// 获取指定名称的时序表。若不存在则抛出 KeyNotFoundException。
    /// </summary>
    ITimeseriesTable this[string tableName] { get; }

    /// <summary>
    /// 获取指定名称的时序表。若不存在则抛出 KeyNotFoundException。
    /// </summary>
    ITimeseriesTable GetTable(string tableName);

    /// <summary>
    /// 检查指定名称的时序表是否存在。
    /// </summary>
    bool HasTable(string tableName);

    /// <summary>
    /// 获取所有已注册的时序表名称列表。
    /// </summary>
    IReadOnlyList<string> GetTableNames();

    /// <summary>
    /// 检查指定序号的时序表在当前时间步是否触发了更新。
    /// </summary>
    bool IsUpdated(int tableIndex);

    /// <summary>
    /// 检查指定名称的时序表在当前时间步是否触发了更新。
    /// </summary>
    bool IsUpdated(string tableName);

    /// <summary>
    /// 获取指定时序表的注册序号（0-63），未注册则返回 -1。
    /// </summary>
    int GetTableIndex(string tableName);
}
