using System;

namespace CarrotBacktesting.NET.Abstraction.Data;

/// <summary>
/// 事件数据源驱动契约。
/// 负责从底层物理存储中加载事件数据并构建 IEventTable 实例。
/// </summary>
public interface IEventSource
{
    /// <summary>
    /// 读取并构建事件表，支持按时间区间进行分区剪枝与配置允许空值字段。
    /// </summary>
    IEventTable LoadTable(
        IStorageResolver resolver, 
        string tableId, 
        DateTime? startDate = null, 
        DateTime? endDate = null,
        IReadOnlySet<string>? nullableFields = null);
}
