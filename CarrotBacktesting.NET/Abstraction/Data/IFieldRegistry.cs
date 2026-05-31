namespace CarrotBacktesting.NET.Abstraction.Data;

/// <summary>
/// 字段注册表契约。
/// 管理已加载到内存中的所有字段元信息，包括内置字段（如 OHLCV）和自定义指标字段。
/// </summary>
public interface IFieldRegistry
{
    /// <summary>
    /// 获取所有已加载的字段名称集合。
    /// 例如："open"、"high"、"low"、"close"、"volume"、"custom_indicator"。
    /// </summary>
    /// <returns>字段名称的枚举序列（惰性求值，不分配额外内存）。</returns>
    IEnumerable<string> GetAvailableFields();

    /// <summary>
    /// 获取指定字段的详细元数据，包括精度、单位、是否为自定义字段等信息。
    /// </summary>
    /// <param name="fieldName">字段名称。</param>
    /// <returns>字段元数据记录对象。</returns>
    /// <exception cref="KeyNotFoundException">当字段不存在时抛出。</exception>
    FieldInfo GetFieldInfo(string fieldName);

    /// <summary>
    /// 注册一个字段。
    /// </summary>
    void RegisterField(string name, Type dataType, bool isCustom = false);

    /// <summary>
    /// 检查字段是否存在。
    /// </summary>
    bool FieldExists(string fieldName);

    /// <summary>
    /// 获取指定字段的 CLR 数据类型。
    /// </summary>
    Type GetFieldType(string fieldName);
}

/// <summary>
/// 字段元数据记录。
/// 描述单个数据字段的基础属性信息。
/// </summary>
/// <param name="Name">字段名称（唯一标识）。</param>
/// <param name="DataType">字段的 CLR 数据类型（例如 typeof(double)、typeof(float)）。</param>
/// <param name="IsCustom">标记是否为用户自定义字段（true）或系统内置行情字段（false）。</param>
public record FieldInfo(string Name, Type DataType, bool IsCustom);
