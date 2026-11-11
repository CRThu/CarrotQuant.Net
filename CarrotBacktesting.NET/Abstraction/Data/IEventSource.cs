namespace CarrotBacktesting.NET.Abstraction.Data;

/// <summary>
/// 异构事件市场数据源契约（底层物理格式抽取层）。
/// 负责针对指定事件表，从底层存储文件（CSV、Parquet 等）中解析出强类型事件实体并构建为事件表容器。
/// </summary>
public interface IEventSource
{
    /// <summary>
    /// 从物理文件中读取并映射构建强类型事件表。
    /// </summary>
    /// <typeparam name="T">事件数据实体类型，必须为引用类型（class）。</typeparam>
    /// <param name="resolver">存储路径与格式调度器。</param>
    /// <param name="tableId">事件表标识符。</param>
    /// <returns>已加载完成的只读事件表容器。</returns>
    IEventTable<T> LoadTable<T>(IStorageResolver resolver, string tableId) where T : class;
}
