using System;
using System.Collections.Generic;

namespace CarrotBacktesting.NET.Abstraction.Data
{
    /// <summary>
    /// 通用动态数据行抽象契约（领域中立，统一承载时态事件与静态维表数据）。
    /// 提供标的主键、可选时间戳、列名序号映射与强类型转换方法。
    /// </summary>
    public interface IDataRow
    {
        /// <summary>
        /// 标的主键标识符（如代码 "sh.600000"；全局广播事件为空字符串）。
        /// </summary>
        string Key { get; }

        /// <summary>
        /// 数据行时间戳（时态事件包含具体日期，静态维表则为 null）。
        /// </summary>
        DateTime? Timestamp { get; }

        /// <summary>
        /// 获取所有可用列名列表。
        /// </summary>
        IReadOnlyCollection<string> Columns { get; }

        /// <summary>
        /// 获取字段数量。
        /// </summary>
        int Count { get; }

        /// <summary>
        /// 列名索引器。若列不存在则抛出 KeyNotFoundException。
        /// </summary>
        string this[string columnName] { get; }

        /// <summary>
        /// 下标索引器。
        /// </summary>
        string this[int index] { get; }

        /// <summary>
        /// 安全获取指定列的字符串值。
        /// </summary>
        string GetString(string columnName, string defaultValue = "");

        /// <summary>
        /// 获取双精度浮点数值。
        /// </summary>
        double GetDouble(string columnName, double defaultValue = 0.0);

        /// <summary>
        /// 获取单精度浮点数值。
        /// </summary>
        float GetFloat(string columnName, float defaultValue = 0.0f);

        /// <summary>
        /// 获取高精度十进制浮点数值（用于金额、成交量及除权分红比例等）。
        /// </summary>
        decimal GetDecimal(string columnName, decimal defaultValue = 0m);

        /// <summary>
        /// 获取 32 位整型值。
        /// </summary>
        int GetInt(string columnName, int defaultValue = 0);

        /// <summary>
        /// 获取 64 位长整型值。
        /// </summary>
        long GetLong(string columnName, long defaultValue = 0);

        /// <summary>
        /// 获取布尔值。
        /// </summary>
        bool GetBoolean(string columnName, bool defaultValue = false);

        /// <summary>
        /// 获取日期时间值。遵循双时间轴契约：优先按 Unix 毫秒时间戳解析，其次按标准日期格式文本解析。
        /// </summary>
        DateTime GetDateTime(string columnName, DateTime defaultValue = default);

        /// <summary>
        /// 尝试获取列值。
        /// </summary>
        bool TryGetValue(string columnName, out string? value);

        /// <summary>
        /// 判断是否包含指定列。
        /// </summary>
        bool ContainsColumn(string columnName);
    }
}
